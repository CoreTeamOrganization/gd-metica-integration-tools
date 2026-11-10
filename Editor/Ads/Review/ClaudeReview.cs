using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Runs the review: installs the metica-integration-review skill into the project's
    /// .claude/skills, then runs the Claude Code CLI headless (claude -p) in the project
    /// folder. Claude may read anything and run read-only git, but write only inside
    /// MeticaIntegrationReview/ — the review reports, it never edits the game.
    ///
    /// <para>The run is a separate process: it survives a domain reload (its id is kept in
    /// SessionState), and the skill writes result.json itself, so only the captured console
    /// output is lost when Unity reloads mid-run.</para>
    /// </summary>
    internal static class ClaudeReview
    {
        public const string SkillName = "metica-integration-review";

        private const string RunningPidKey = "GameDistrict.MeticaIntegrationTools.ReviewPid";
        private const string RunningBundleKey = "GameDistrict.MeticaIntegrationTools.ReviewBundle";
        private const string StartedKey = "GameDistrict.MeticaIntegrationTools.ReviewStarted";
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

        private static Process _process;
        private static readonly StringBuilder Output = new StringBuilder();
        private static bool _watching;

        // ── Prompt ─────────────────────────────────────────────────────────────

        /// <summary>
        /// What Claude is asked. No quotes or shell characters: it goes through cmd.exe as-is
        /// for an npm-installed claude.cmd.
        /// </summary>
        public static string Prompt(string bundle) =>
            $"Use the {SkillName} skill to review the Metica integration bundle in {bundle}. " +
            $"Write {ReviewBundle.ResultFile} in that folder as the skill describes, then reply with a short summary.";

        // ── Skill ──────────────────────────────────────────────────────────────

        public static string SkillSource =>
            MeticaPaths.ToolRoot == null ? null : MeticaPaths.ToAbsolute(MeticaPaths.ToolRoot + "/Skills~/" + SkillName);

        public static string SkillTargetRelative => ".claude/skills/" + SkillName;

        private static string SkillTarget => Path.Combine(ReviewBundle.ProjectDir, SkillTargetRelative).Replace('\\', '/');

        /// <summary>Copies the packaged skill into the project, replacing an older copy. Returns a problem or null.</summary>
        public static string InstallSkill()
        {
            var source = SkillSource;
            if (source == null || !Directory.Exists(source))
                return "The review skill is missing from this tool (Skills~/" + SkillName + ").";

            try
            {
                if (Directory.Exists(SkillTarget)) Directory.Delete(SkillTarget, true);
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(source.Length).TrimStart('/', '\\');
                    var target = Path.Combine(SkillTarget, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? SkillTarget);
                    File.Copy(file, target, true);
                }
                return null;
            }
            catch (Exception e)
            {
                return $"Couldn't install the review skill into {SkillTargetRelative}: {e.Message}";
            }
        }

        /// <summary>True when the project's copy matches the packaged skill file for file.</summary>
        public static bool SkillIsCurrent()
        {
            var source = SkillSource;
            if (source == null || !Directory.Exists(source) || !Directory.Exists(SkillTarget)) return false;

            try
            {
                return Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).All(file =>
                {
                    var target = Path.Combine(SkillTarget, file.Substring(source.Length).TrimStart('/', '\\'));
                    return File.Exists(target) && FileText.Same(File.ReadAllText(file), File.ReadAllText(target));
                });
            }
            catch (IOException)
            {
                return false;
            }
        }

        // ── CLI ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The claude executable: on PATH, or where its installers put it (Unity started from
        /// the Hub on macOS gets a short PATH, so the usual places are looked at directly).
        /// </summary>
        public static string FindCli()
        {
            var windows = Application.platform == RuntimePlatform.WindowsEditor;
            var names = windows ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(f => f.Trim().Length > 0)
                .ToList();
            folders.Add(Path.Combine(home, ".local", "bin"));
            folders.Add(Path.Combine(home, ".claude", "local"));
            if (windows)
                folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            else
                folders.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(home, ".npm-global", "bin") });

            foreach (var folder in folders)
            foreach (var name in names)
            {
                try
                {
                    var path = Path.Combine(folder.Trim().Trim('"'), name);
                    if (File.Exists(path)) return path;
                }
                catch (ArgumentException)
                {
                    // a malformed PATH entry
                }
            }

            return null;
        }

        // ── Run ────────────────────────────────────────────────────────────────

        public static bool IsRunning
        {
            get
            {
                Watch();
                if (_process != null) return !_process.HasExited;

                var pid = SessionState.GetInt(RunningPidKey, 0);
                if (pid == 0) return false;
                try
                {
                    using (var process = Process.GetProcessById(pid))
                        return !process.HasExited;
                }
                catch
                {
                    Finish(null);
                    return false;
                }
            }
        }

        public static string RunningBundle => SessionState.GetString(RunningBundleKey, null);

        public static TimeSpan Elapsed
        {
            get
            {
                var started = SessionState.GetString(StartedKey, null);
                return started != null && long.TryParse(started, out var ticks)
                    ? DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)
                    : TimeSpan.Zero;
            }
        }

        /// <summary>Starts the review of <paramref name="bundle"/>. Returns a problem, or null when it started.</summary>
        public static string Start(string bundle)
        {
            if (IsRunning) return "A review is already running.";

            var cli = FindCli();
            if (cli == null) return "Claude Code CLI not found. Install it, or use Copy prompt and paste it into Claude Code in this project's folder.";

            var skill = InstallSkill();
            if (skill != null) return skill;

            // Edit(path) rules cover every file-writing tool, Write included.
            var reviewFolder = "./" + ReviewBundle.RootFolder + "/**";
            var args = new List<string>
            {
                "-p", Prompt(bundle),
                "--allowedTools", "Read", "Grep", "Glob", "Skill",
                $"Edit({reviewFolder})",
                "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)", "Bash(git blame:*)"
            };

            var info = new ProcessStartInfo
            {
                WorkingDirectory = ReviewBundle.ProjectDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (cli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                info.Arguments = "/d /s /c \"" + Quote(cli) + " " + string.Join(" ", args.Select(Quote)) + "\"";
            }
            else
            {
                info.FileName = cli;
                info.Arguments = string.Join(" ", args.Select(Quote));
            }

            // An npm-installed claude needs node on PATH; the Hub's PATH on macOS may lack it.
            var extra = new List<string> { Path.GetDirectoryName(cli) };
            if (Application.platform != RuntimePlatform.WindowsEditor) extra.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin" });
            info.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator.ToString(),
                extra.Concat(new[] { info.EnvironmentVariables["PATH"] }).Where(p => !string.IsNullOrEmpty(p)));

            try
            {
                Output.Clear();
                _process = new Process { StartInfo = info, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (Output) Output.AppendLine(e.Data); };
                _process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (Output) Output.AppendLine(e.Data); };
                if (!_process.Start()) return "Claude Code didn't start.";

                _process.StandardInput.Close();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                SessionState.SetInt(RunningPidKey, _process.Id);
                SessionState.SetString(RunningBundleKey, bundle);
                SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString());
                MeticaIntegrationLog.Record(IntegrationReviewStep.StepTitle, $"Claude Code is reviewing {bundle}");
                Watch();
                return null;
            }
            catch (Exception e)
            {
                _process = null;
                return "Claude Code didn't start: " + e.Message;
            }
        }

        public static void Cancel()
        {
            try
            {
                if (_process != null && !_process.HasExited) _process.Kill();
                else
                {
                    var pid = SessionState.GetInt(RunningPidKey, 0);
                    if (pid != 0)
                        using (var process = Process.GetProcessById(pid))
                            process.Kill();
                }
            }
            catch
            {
                // already gone
            }

            Finish("Review cancelled.");
        }

        /// <summary>Polls the run once per editor tick while one is going.</summary>
        private static void Watch()
        {
            if (_watching || (_process == null && SessionState.GetInt(RunningPidKey, 0) == 0)) return;
            _watching = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            bool exited;
            if (_process != null) exited = _process.HasExited;
            else
            {
                var pid = SessionState.GetInt(RunningPidKey, 0);
                try
                {
                    using (var process = Process.GetProcessById(pid))
                        exited = pid == 0 || process.HasExited;
                }
                catch
                {
                    exited = true;
                }
            }

            if (!exited && Elapsed > Timeout)
            {
                Cancel();
                MeticaIntegrationLog.Record(IntegrationReviewStep.StepTitle, $"Review stopped after {Timeout.TotalMinutes:0} minutes.");
                return;
            }

            if (exited) Finish(null);
        }

        private static void Finish(string message)
        {
            var bundle = RunningBundle;
            if (_watching)
            {
                EditorApplication.update -= Tick;
                _watching = false;
            }

            if (bundle != null)
            {
                string output;
                lock (Output) output = Output.ToString();
                if (_process != null)
                {
                    try { _process.WaitForExit(2000); } catch { /* gone */ }
                    lock (Output) output = Output.ToString();
                }

                try
                {
                    if (output.Length > 0)
                        File.WriteAllText(Path.Combine(ReviewBundle.Absolute(bundle), ReviewBundle.OutputFile), output);
                }
                catch (IOException e)
                {
                    Debug.LogWarning("[Metica Integration] Couldn't save Claude's output: " + e.Message);
                }

                MeticaIntegrationLog.Record(IntegrationReviewStep.StepTitle,
                    message ?? (File.Exists(Path.Combine(ReviewBundle.Absolute(bundle), ReviewBundle.ResultFile))
                        ? $"Review finished — {bundle}/{ReviewBundle.ResultFile}"
                        : $"Review ended without {ReviewBundle.ResultFile} — see {bundle}/{ReviewBundle.OutputFile}"));
            }

            _process?.Dispose();
            _process = null;
            SessionState.EraseInt(RunningPidKey);
            SessionState.EraseString(RunningBundleKey);
            SessionState.EraseString(StartedKey);
            MeticaIntegrationWindow.RefreshOpenWindows();
        }

        private static string Quote(string value) =>
            value.IndexOfAny(new[] { ' ', '"', '(', ')', '*', '\t' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
