using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Commits one step's changes to the game's git repository, and nothing else.
    ///
    /// <para>A step's files are its <see cref="MeticaStep.TouchedPaths"/> (a folder means
    /// everything under it) plus their .meta files. Only the ones git reports as changed are
    /// committed, with <c>git commit --only</c>, so anything else the developer has staged
    /// stays staged and out of the commit. Never pushes, never skips hooks.</para>
    ///
    /// <para>The Unity project may sit in a subfolder of the repository, so every path is
    /// passed to git relative to the repository root.</para>
    /// </summary>
    internal static class StepCommit
    {
        internal sealed class Change
        {
            /// <summary>"new", "modified" or "deleted".</summary>
            public string Kind;

            /// <summary>Relative to the repository root — what git takes.</summary>
            public string RepoPath;

            /// <summary>Relative to the Unity project — what the window shows.</summary>
            public string ProjectPath;
        }

        private const int StatusTimeoutMs = 20000;
        private const int CommitTimeoutMs = 120000;

        /// <summary>
        /// Stands in for the Unity project folder, for exercising the git calls outside
        /// Unity; never set in normal use.
        /// </summary>
        internal static string ProjectDirOverride { get; set; }

        private static string ProjectDir => ProjectDirOverride ?? Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// The step's uncommitted changes. False, with the reason, when there is no git to
        /// ask — git missing, or the project is not in a repository.
        /// </summary>
        public static bool TryGetChanges(MeticaStep step, out List<Change> changes, out string unavailable)
        {
            changes = new List<Change>();

            if (!TryRepo(out var top, out var prefix, out unavailable)) return false;

            var specs = Specs(step, prefix);
            if (specs.Count == 0) return true;

            var args = "-c core.quotepath=off --literal-pathspecs status --porcelain=v1 -z --no-renames " +
                       "--untracked-files=all -- " + string.Join(" ", specs.Select(Quote));
            var status = Git(top, args, StatusTimeoutMs);
            if (status.exit != 0)
            {
                unavailable = "git status failed: " + FirstLine(status.error);
                return false;
            }

            foreach (var entry in status.output.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Length < 4) continue;

                var code = entry.Substring(0, 2);
                var repoPath = entry.Substring(3);
                changes.Add(new Change
                {
                    Kind = code == "??" || code.IndexOf('A') >= 0 ? "new" : code.IndexOf('D') >= 0 ? "deleted" : "modified",
                    RepoPath = repoPath,
                    ProjectPath = repoPath.StartsWith(prefix) ? repoPath.Substring(prefix.Length) : repoPath
                });
            }

            changes = changes.OrderBy(change => change.ProjectPath, StringComparer.Ordinal).ToList();
            return true;
        }

        /// <summary>
        /// Stages exactly <paramref name="changes"/> and commits only them. Returns the new
        /// commit's short hash, or null with <paramref name="error"/> set.
        /// </summary>
        public static string Commit(IReadOnlyList<Change> changes, string summary, string description, out string error)
        {
            error = null;
            if (changes == null || changes.Count == 0)
            {
                error = "Nothing to commit.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                error = "The commit summary is empty.";
                return null;
            }

            if (!TryRepo(out var top, out _, out error)) return null;

            // Paths and message go through files: no command-line length limit (an SDK import
            // is hundreds of files) and no quoting of the message.
            var pathFile = Path.Combine(Path.GetTempPath(), "metica-commit-paths.txt");
            var messageFile = Path.Combine(Path.GetTempPath(), "metica-commit-message.txt");
            var utf8 = new UTF8Encoding(false);

            try
            {
                File.WriteAllText(pathFile, string.Join("\0", changes.Select(change => change.RepoPath)) + "\0", utf8);

                var message = summary.Trim();
                if (!string.IsNullOrWhiteSpace(description)) message += "\n\n" + description.Trim();
                File.WriteAllText(messageFile, message.Replace("\r\n", "\n") + "\n", utf8);

                var pathspec = $"--pathspec-from-file={Quote(pathFile)} --pathspec-file-nul";

                var add = Git(top, $"--literal-pathspecs add -A {pathspec}", CommitTimeoutMs);
                if (add.exit != 0)
                {
                    error = "git add failed: " + FirstLine(add.error);
                    return null;
                }

                var commit = Git(top, $"--literal-pathspecs commit --only -F {Quote(messageFile)} {pathspec}",
                    CommitTimeoutMs);
                if (commit.exit != 0)
                {
                    // All of it: a hook's or a missing identity's explanation spans lines.
                    error = "git commit failed:\n" + Shorten(string.IsNullOrWhiteSpace(commit.error)
                        ? commit.output
                        : commit.error);
                    return null;
                }

                var head = Git(top, "rev-parse --short HEAD", StatusTimeoutMs);
                return head.exit == 0 ? head.output.Trim() : "HEAD";
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
            finally
            {
                TryDelete(pathFile);
                TryDelete(messageFile);
            }
        }

        // ── Repository ─────────────────────────────────────────────────────────

        private static string _cachedFor;
        private static string _top;
        private static string _prefix;

        /// <summary>
        /// The repository root, and the project's folder inside it ("" when the project is
        /// the root, "Game/" when it sits in a subfolder). Remembered once found.
        /// </summary>
        private static bool TryRepo(out string top, out string prefix, out string unavailable)
        {
            unavailable = null;
            if (_cachedFor == ProjectDir && _top != null)
            {
                top = _top;
                prefix = _prefix;
                return true;
            }

            top = prefix = null;

            var root = Git(ProjectDir, "rev-parse --show-toplevel", StatusTimeoutMs);
            if (root.exit != 0)
            {
                unavailable = root.missing ? "git isn't installed or isn't on PATH." : "This project isn't in a git repository.";
                return false;
            }

            var inside = Git(ProjectDir, "rev-parse --show-prefix", StatusTimeoutMs);
            top = root.output.Trim();
            prefix = inside.exit == 0 ? inside.output.Trim() : string.Empty;

            _cachedFor = ProjectDir;
            _top = top;
            _prefix = prefix;
            return true;
        }

        /// <summary>The step's paths and their .meta files, relative to the repository root.</summary>
        private static List<string> Specs(MeticaStep step, string prefix)
        {
            var specs = new List<string>();
            foreach (var raw in step.TouchedPaths)
            {
                if (string.IsNullOrEmpty(raw)) continue;

                var path = raw.Replace('\\', '/').TrimEnd('/');
                specs.Add(prefix + path);
                if (!path.EndsWith(".meta")) specs.Add(prefix + path + ".meta");
            }
            return specs.Distinct().ToList();
        }

        // ── Process ────────────────────────────────────────────────────────────

        private static (int exit, string output, string error, bool missing) Git(string workDir, string args, int timeoutMs)
        {
            var info = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            try
            {
                using (var process = Process.Start(info))
                {
                    if (process == null) return (-1, string.Empty, "git did not start", true);

                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { /* already gone */ }
                        return (-1, string.Empty, $"git {args.Split(' ')[0]} timed out", false);
                    }

                    process.WaitForExit();
                    return (process.ExitCode, output.Result, error.Result, false);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (-1, string.Empty, "git not found", true);
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static string FirstLine(string text)
        {
            var line = (text ?? string.Empty).Trim().Split('\n').FirstOrDefault(l => l.Trim().Length > 0);
            return string.IsNullOrEmpty(line) ? "no output" : line.Trim();
        }

        private static string Shorten(string text)
        {
            text = (text ?? string.Empty).Trim();
            return text.Length <= 800 ? text : text.Substring(0, 800) + "…";
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* temp file, harmless */ }
        }
    }
}
