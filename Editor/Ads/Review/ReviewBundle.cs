using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Everything the integration review needs, in one folder at the project root:
    /// MeticaIntegrationReview/&lt;time&gt;/. The facts that decided how the integration was
    /// done (which GD SDK, which base-class shape, which template each wrapper came from),
    /// every step's own check, the GD SDK's files against the original release, and the git
    /// diff since before the integration — so the review sees every change, addition and
    /// removal, whoever made it.
    ///
    /// <para>The review itself is the metica-integration-review skill, run by Claude Code in
    /// the project folder; it writes result.json next to these files. Reads the project
    /// only — the bundle folder is the one thing written.</para>
    /// </summary>
    internal static class ReviewBundle
    {
        public const string RootFolder = "MeticaIntegrationReview";
        public const string ResultFile = "result.json";
        public const string OutputFile = "claude-output.md";
        public const string IndexFile = "review.md";

        private const int GitTimeoutMs = 60000;

        public static string ProjectDir => StepCommit.ProjectDir;

        public static string RootAbsolute => Path.Combine(ProjectDir, RootFolder).Replace('\\', '/');

        /// <summary>The newest bundle, project-relative ("MeticaIntegrationReview/20261110-1530"); null when none.</summary>
        public static string Latest
        {
            get
            {
                if (!Directory.Exists(RootAbsolute)) return null;
                var newest = Directory.GetDirectories(RootAbsolute)
                    .Select(Path.GetFileName)
                    .Where(name => File.Exists(Path.Combine(RootAbsolute, name, IndexFile)))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .LastOrDefault();
                return newest == null ? null : RootFolder + "/" + newest;
            }
        }

        public static string Absolute(string bundle) => Path.Combine(ProjectDir, bundle).Replace('\\', '/');

        // ── Build ──────────────────────────────────────────────────────────────

        /// <summary>Writes a new bundle and returns its project-relative folder.</summary>
        /// <param name="gitBase">A commit to diff from; empty to find the one before the integration.</param>
        public static string Build(string gitBase, IReadOnlyList<MeticaStep> steps, MeticaStep reviewStep)
        {
            var name = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var bundle = RootFolder + "/" + name;
            var folder = Absolute(bundle);
            Directory.CreateDirectory(folder);

            // Machine output, like the backups: kept out of git unless someone wants it in.
            var ignore = Path.Combine(RootAbsolute, ".gitignore");
            if (!File.Exists(ignore)) File.WriteAllText(ignore, "# Written by Metica Integration Tools\n*\n");

            var facts = new Json();
            var md = new StringBuilder();

            md.AppendLine("# Metica integration review bundle");
            md.AppendLine();
            md.AppendLine($"Made {DateTime.Now:yyyy-MM-dd HH:mm} by Metica Integration Tools {ToolVersion()}. " +
                          "Review it with the `metica-integration-review` skill and write `result.json` in this folder. " +
                          "Paths are relative to the Unity project folder.");
            md.AppendLine();

            facts.Add("bundle", bundle);
            facts.Add("toolVersion", ToolVersion());
            facts.Add("unityVersion", Application.unityVersion);
            facts.Add("buildTarget", EditorUserBuildSettings.activeBuildTarget.ToString());

            var compare = MeticaPaths.HasGDSdk ? SafeCompare(true) : null;
            WriteScenario(md, facts, compare);
            if (compare != null) WriteGdSdk(md, facts, folder, compare);
            else WriteStandalone(md, facts);
            WriteSteps(md, facts, steps, reviewStep);
            WriteGit(md, facts, folder, gitBase);
            WriteLog(md, folder);

            File.WriteAllText(Path.Combine(folder, "facts.json"), facts.ToString());
            File.WriteAllText(Path.Combine(folder, IndexFile), md.ToString());
            return bundle;
        }

        // ── Scenario ───────────────────────────────────────────────────────────

        private static void WriteScenario(StringBuilder md, Json facts, SdkCompareResult compare)
        {
            md.AppendLine("## Scenario");
            md.AppendLine();
            md.AppendLine($"- Build target: {EditorUserBuildSettings.activeBuildTarget}");
            md.AppendLine($"- Unity: {Application.unityVersion}");

            if (!MeticaPaths.HasGDSdk)
            {
                facts.Add("flow", "standalone");
                facts.Add("scenario", "standalone");
                md.AppendLine("- Flow: **standalone** — no GD Monetization SDK; the tool wrote its own Metica runtime " +
                              $"in `{MeticaPaths.StandaloneRoot}` and patched the game's own ads code.");
                md.AppendLine("- Scenario: `standalone` → read `references/standalone.md`");
                md.AppendLine();
                return;
            }

            var known = compare.Version != null && !compare.ChosenByHand;
            var scenario = known ? ScenarioOf(compare.Version) : "unknown";

            facts.Add("flow", "gd-sdk");
            facts.Add("scenario", scenario);
            facts.Add("gdSdkDeclared", compare.Declared);
            facts.Add("gdSdkBaseVersion", compare.BaseVersion);
            facts.Add("gdSdkRelease", compare.Version);
            facts.Add("gdSdkReleaseChosenByHand", compare.ChosenByHand);
            facts.Add("gdSdkKnownRelease", known);

            md.AppendLine($"- Flow: **GD SDK** at `{MeticaPaths.GDRoot}`");
            md.AppendLine($"- GD SDK declares: `{compare.Declared ?? "(unreadable)"}`" +
                          (compare.BaseVersion != null ? $" (BaseVersion `{compare.BaseVersion}`)" : string.Empty));

            if (known)
                md.AppendLine($"- Matches the original release **{compare.Version}**");
            else
            {
                md.AppendLine("- **Unknown build** — no original release declares this version (a beta, a fork, or a " +
                              "removed release). The tool's patches and templates were written for released versions " +
                              "only: review everything against what this project's own code does, not against a release.");
                if (compare.Version != null)
                    md.AppendLine($"- Compared with **{compare.Version}**, picked by hand as the closest release.");
            }

            md.AppendLine($"- Scenario: `{scenario}` → read `references/{scenario}.md`" +
                          (known ? string.Empty : " and the scenario file of the closest release the code shape matches"));
            md.AppendLine();
        }

        /// <summary>The skill's scenario file for a release.</summary>
        internal static string ScenarioOf(string release)
        {
            var v = GdSdkVersion.Parse(release);
            if (v == null) return "unknown";
            if (v < new Version(5, 0, 2)) return "gd-5.0.0-5.0.1";
            if (v < new Version(5, 3, 0)) return "gd-5.0.2-5.2";
            if (v < new Version(6, 0, 0)) return "gd-5.3-5.5";
            if (v < new Version(6, 2, 1)) return "gd-6.0-6.2.0";
            return "gd-6.2.1-plus";
        }

        // ── GD SDK ─────────────────────────────────────────────────────────────

        private static void WriteGdSdk(StringBuilder md, Json facts, string folder, SdkCompareResult compare)
        {
            // Code shape: what picked the templates.
            var shape = WrapperFilesStep.ReadShape();
            md.AppendLine("## GD SDK code shape (what picked the templates)");
            md.AppendLine();
            md.AppendLine("| Fact | Value |");
            md.AppendLine("|---|---|");
            var shapeJson = new Json();
            foreach (var pair in shape)
            {
                md.AppendLine($"| {pair.Key} | {pair.Value} |");
                shapeJson.Add(pair.Key, pair.Value);
            }
            facts.Add("shape", shapeJson);
            md.AppendLine();

            // Wrapper files and the template each came from.
            var check = SafeFileCheck();
            var states = check?.Files.ToDictionary(f => f.Path, f => f.State) ?? new Dictionary<string, FileState>();

            md.AppendLine("## Files the tool wrote (wrapper)");
            md.AppendLine();
            md.AppendLine("| File | Template | State |");
            md.AppendLine("|---|---|---|");
            var wrappers = new List<Json>();
            foreach (var (template, target) in WrapperFilesStep.Files())
            {
                var state = states.TryGetValue(target, out var s) ? s.ToString() : MeticaPaths.FileExists(target) ? "?" : "Missing";
                md.AppendLine($"| `{target}` | `{template}` | {Explain(state)} |");
                wrappers.Add(new Json().Add("path", target).Add("template", template).Add("state", state));
            }
            facts.Add("wrapperFiles", wrappers);

            var skipped = WrapperFilesStep.InitializerSkipReason();
            if (skipped != null)
            {
                md.AppendLine();
                md.AppendLine($"Note: {skipped}");
                facts.Add("initializerSkipped", skipped);
            }
            md.AppendLine();

            // Patched GD SDK files.
            if (check != null)
            {
                md.AppendLine("## GD SDK files the tool patches");
                md.AppendLine();
                md.AppendLine("| File | State |");
                md.AppendLine("|---|---|");
                var patched = new List<Json>();
                foreach (var file in check.Files.Where(f => !f.IsWrapper))
                {
                    md.AppendLine($"| `{file.Path}` | {Explain(file.State.ToString())} |");
                    patched.Add(new Json().Add("path", file.Path).Add("state", file.State.ToString()));
                }
                facts.Add("patchedFiles", patched);
                md.AppendLine();
            }

            // Every file against the original release.
            md.AppendLine("## GD SDK against the original release");
            md.AppendLine();
            if (compare.Version == null)
            {
                md.AppendLine($"Not compared: {compare.Problem ?? "the release is unknown"}. Use `git-diff.patch` instead.");
                md.AppendLine();
                return;
            }

            md.AppendLine($"Against **{compare.Version}**: {compare.Compared} files, {compare.Identical} identical, " +
                          $"{compare.Changes.Count} differ. Each diff is in `sdk-diffs/`, each original in `sdk-original/`. " +
                          "\"By tool\" = exactly what this tool's patches or templates produce.");
            md.AppendLine();
            md.AppendLine("| File | Kind | By tool | Mentions Metica | +/- |");
            md.AppendLine("|---|---|---|---|---|");

            var stock = StockFiles.Packaged;
            var changes = new List<Json>();
            foreach (var change in compare.Changes)
            {
                var diffPath = "sdk-diffs/" + change.Path.Replace("../", "_up/") + ".diff";
                WriteText(folder, diffPath, UnifiedDiff(change, compare.Version));

                string originalPath = null;
                if (change.Kind != ChangeKind.Added && stock != null)
                {
                    originalPath = "sdk-original/" + change.Path.Replace("../", "_up/");
                    WriteText(folder, originalPath, stock.Get(compare.Version, change.Path));
                }

                md.AppendLine($"| `{change.ProjectPath}` | {change.Kind} | {(change.ByTool ? "yes" : "**no**")} | " +
                              $"{(change.Metica ? "yes" : "no")} | +{change.AddedLines} -{change.RemovedLines} |");
                changes.Add(new Json()
                    .Add("path", change.ProjectPath).Add("kind", change.Kind.ToString()).Add("byTool", change.ByTool)
                    .Add("metica", change.Metica).Add("added", change.AddedLines).Add("removed", change.RemovedLines)
                    .Add("diff", diffPath).Add("original", originalPath));
            }
            facts.Add("sdkChanges", changes);
            md.AppendLine();
        }

        private static string Explain(string state)
        {
            switch (state)
            {
                case "Stock": return "as the tool wrote it / original";
                case "Patched": return "original + the tool's patches only";
                case "Modified": return "**changed by hand** (or patched by an older tool)";
                case "Missing": return "**missing**";
                default: return state;
            }
        }

        private static string UnifiedDiff(SdkFileChange change, string release)
        {
            var text = new StringBuilder();
            text.AppendLine($"--- original {release}/{change.Path}");
            text.AppendLine($"+++ project {change.ProjectPath}");
            foreach (var hunk in LineDiff.Hunks(change.Diff))
            {
                text.AppendLine($"@@ -{hunk.OldStart} +{hunk.NewStart} @@");
                foreach (var line in hunk.Lines)
                    text.AppendLine((line.Op == LineDiff.Op.Added ? "+" : line.Op == LineDiff.Op.Removed ? "-" : " ") + line.Text);
            }
            return text.ToString();
        }

        // ── Standalone ─────────────────────────────────────────────────────────

        private static void WriteStandalone(StringBuilder md, Json facts)
        {
            var check = SafeFileCheck();
            md.AppendLine("## Files the tool wrote (standalone runtime)");
            md.AppendLine();
            md.AppendLine("| File | State |");
            md.AppendLine("|---|---|");
            var files = new List<Json>();
            foreach (var name in StandaloneRuntimeStep.Files)
            {
                var path = $"{MeticaPaths.StandaloneRoot}/{name}.cs";
                var state = check?.Files.FirstOrDefault(f => f.Path == path)?.State.ToString()
                            ?? (MeticaPaths.FileExists(path) ? "Stock" : "Missing");
                md.AppendLine($"| `{path}` | {Explain(state)} |");
                files.Add(new Json().Add("path", path).Add("state", state));
            }
            facts.Add("runtimeFiles", files);
            md.AppendLine();
            md.AppendLine("`MeticaAdsHooks.cs` is meant to be edited by the game. The game's own ads code was patched " +
                          "by the optional steps — see the steps table and `git-diff.patch`.");
            md.AppendLine();
        }

        // ── Steps ──────────────────────────────────────────────────────────────

        private static void WriteSteps(StringBuilder md, Json facts, IReadOnlyList<MeticaStep> steps, MeticaStep reviewStep)
        {
            md.AppendLine("## Steps and their own checks");
            md.AppendLine();
            md.AppendLine("| Step | Check | Problems / warnings | Files it touches |");
            md.AppendLine("|---|---|---|---|");

            var list = new List<Json>();
            foreach (var step in steps)
            {
                if (step == reviewStep || !SafeApplies(step)) continue;

                VerifyResult result;
                try { result = step.Verify(); }
                catch (Exception e) { result = VerifyResult.Fail("Check threw: " + e.Message); }

                var touched = SafeTouched(step).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                var remarks = result.Problems.Concat(result.Warnings).Select(OneLine).ToList();
                md.AppendLine($"| {step.Title}{(step.Optional ? " (optional)" : string.Empty)} | " +
                              $"{(result.Ok ? "passes" : result.Waiting ? "waiting for a compile" : "**fails**")} | " +
                              $"{(remarks.Count == 0 ? "—" : string.Join("<br>", remarks.Select(Cell)))} | " +
                              $"{(touched.Count == 0 ? "—" : string.Join("<br>", touched.Select(p => "`" + p + "`")))} |");

                list.Add(new Json()
                    .Add("id", step.Id).Add("title", step.Title).Add("optional", step.Optional).Add("ok", result.Ok)
                    .Add("problems", result.Problems).Add("warnings", result.Warnings).Add("notes", result.Notes)
                    .Add("touchedPaths", touched));
            }
            facts.Add("steps", list);
            md.AppendLine();
        }

        private static string OneLine(string text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

        private static string Cell(string text) => text.Replace("|", "\\|");

        // ── Git ────────────────────────────────────────────────────────────────

        private static void WriteGit(StringBuilder md, Json facts, string folder, string requestedBase)
        {
            md.AppendLine("## Git: every change since before the integration");
            md.AppendLine();

            var dir = ProjectDir;
            var inside = StepCommit.Git(dir, "rev-parse --is-inside-work-tree", GitTimeoutMs);
            if (inside.exit != 0)
            {
                md.AppendLine("Not a git repository (or git not found) — no history to diff. Review the project files directly.");
                md.AppendLine();
                facts.Add("git", false);
                return;
            }

            var (sha, how) = FindBase(dir, requestedBase);
            var git = new Json().Add("base", sha).Add("baseIs", how);

            if (sha == null)
            {
                md.AppendLine($"No base commit: {how}");
                md.AppendLine();
                facts.Add("git", git);
                return;
            }

            const string specs = "-- \"*.cs\" \"*.asmdef\" \"*AdUnitsSettings.asset\" \"*MeticaSettings.asset\" \"*AdsSettings*.asset\"";
            var stat = StepCommit.Git(dir, $"diff --stat=200 {sha} {specs}", GitTimeoutMs);
            var diff = StepCommit.Git(dir, $"diff -U8 {sha} {specs}", GitTimeoutMs * 2);
            var untracked = StepCommit.Git(dir, "ls-files --others --exclude-standard -- \"*.cs\"", GitTimeoutMs);
            var log = StepCommit.Git(dir, $"log --format=\"%h %ad %s\" --date=short {sha}..HEAD", GitTimeoutMs);

            WriteText(folder, "git-diff.patch", diff.exit == 0 ? diff.output : "git diff failed: " + diff.error);
            WriteText(folder, "git-log.txt", log.output);

            var untrackedFiles = Lines(untracked.output)
                .Where(p => !p.StartsWith(RootFolder + "/", StringComparison.Ordinal)).ToList();

            md.AppendLine($"Base: `{sha}` — {how}. `git-diff.patch` is the working tree (committed or not) against it, " +
                          "for C#, asmdefs and the ads settings assets; `git-log.txt` lists the commits since.");
            md.AppendLine();
            md.AppendLine("```");
            md.AppendLine(stat.output.TrimEnd());
            md.AppendLine("```");
            md.AppendLine();
            if (untrackedFiles.Count > 0)
            {
                md.AppendLine("New C# files not in git yet (not in the patch — read them directly):");
                md.AppendLine();
                foreach (var path in untrackedFiles) md.AppendLine($"- `{path}`");
                md.AppendLine();
            }

            git.Add("diff", "git-diff.patch").Add("untracked", untrackedFiles);
            facts.Add("git", git);
        }

        /// <summary>
        /// The commit to diff from: the one asked for; else the commit that installed this tool
        /// (its install commit says "Metica Integration Tools"); else the parent of the first
        /// commit that added a file the tool writes.
        /// </summary>
        private static (string sha, string how) FindBase(string dir, string requested)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                var resolved = StepCommit.Git(dir, $"rev-parse --verify --quiet \"{requested.Trim()}^{{commit}}\"", GitTimeoutMs);
                return resolved.exit == 0
                    ? (resolved.output.Trim(), $"`{requested.Trim()}`, picked in the review step")
                    : (null, $"`{requested.Trim()}` is not a commit here.");
            }

            var install = StepCommit.Git(dir, "log --format=%H --fixed-strings --grep=\"Metica Integration Tools\"", GitTimeoutMs);
            var installCommit = Lines(install.output).LastOrDefault();
            if (installCommit != null)
                return (installCommit, "the commit that installed Metica Integration Tools");

            var written = MeticaPaths.HasGDSdk
                ? WrapperFilesStep.Files().Select(f => f.target)
                : StandaloneRuntimeStep.Files.Select(n => $"{MeticaPaths.StandaloneRoot}/{n}.cs");
            var paths = string.Join(" ", written.Select(p => "\"" + p + "\""));
            var added = StepCommit.Git(dir, $"log --diff-filter=A --format=%H -- {paths}", GitTimeoutMs);
            var first = Lines(added.output).LastOrDefault();
            if (first != null)
            {
                var parent = StepCommit.Git(dir, $"rev-parse --verify --quiet {first}^", GitTimeoutMs);
                if (parent.exit == 0)
                    return (parent.output.Trim(), "the commit before the first one that added a tool-written file");
            }

            var head = StepCommit.Git(dir, "rev-parse --verify --quiet HEAD", GitTimeoutMs);
            return head.exit == 0
                ? (head.output.Trim(), "HEAD — no commit of the integration found, so only uncommitted changes show")
                : (null, "the repository has no commits.");
        }

        private static List<string> Lines(string text) =>
            (text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        // ── Log ────────────────────────────────────────────────────────────────

        private static void WriteLog(StringBuilder md, string folder)
        {
            var entries = MeticaIntegrationLog.All;
            md.AppendLine("## This session's integration log");
            md.AppendLine();
            if (entries.Count == 0)
            {
                md.AppendLine("Empty — the steps ran in an earlier session (the log does not survive a restart).");
            }
            else
            {
                WriteText(folder, "integration-log.txt", string.Join("\n", entries));
                md.AppendLine($"{entries.Count} entries in `integration-log.txt`.");
            }
            md.AppendLine();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void WriteText(string folder, string relative, string text)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? folder);
            File.WriteAllText(path, text ?? string.Empty);
        }

        private static SdkCompareResult SafeCompare(bool details)
        {
            try { return SdkCompare.Run(details); }
            catch (Exception e) { return new SdkCompareResult { Problem = "Couldn't compare: " + e.Message }; }
        }

        private static FileCheck SafeFileCheck()
        {
            try { return ModifiedFileCheck.Run(); }
            catch { return null; }
        }

        private static bool SafeApplies(MeticaStep step)
        {
            try { return step.Applies; }
            catch { return true; }
        }

        private static IEnumerable<string> SafeTouched(MeticaStep step)
        {
            try { return step.TouchedPaths.ToList(); }
            catch { return Array.Empty<string>(); }
        }

        internal static string ToolVersion()
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReviewBundle).Assembly)?.version ?? "?";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>Just enough JSON writing for facts.json — no package needed.</summary>
        private sealed class Json
        {
            private readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>();

            public Json Add(string key, object value)
            {
                _fields.Add(new KeyValuePair<string, object>(key, value));
                return this;
            }

            public override string ToString()
            {
                var text = new StringBuilder();
                Write(text, this, 0);
                return text.ToString();
            }

            private static void Write(StringBuilder text, object value, int depth)
            {
                var pad = new string(' ', (depth + 1) * 2);
                var close = new string(' ', depth * 2);
                switch (value)
                {
                    case null:
                        text.Append("null");
                        break;
                    case bool flag:
                        text.Append(flag ? "true" : "false");
                        break;
                    case int number:
                        text.Append(number.ToString(CultureInfo.InvariantCulture));
                        break;
                    case string s:
                        text.Append(Quote(s));
                        break;
                    case Json json:
                        text.Append("{\n");
                        for (var i = 0; i < json._fields.Count; i++)
                        {
                            text.Append(pad).Append(Quote(json._fields[i].Key)).Append(": ");
                            Write(text, json._fields[i].Value, depth + 1);
                            text.Append(i < json._fields.Count - 1 ? ",\n" : "\n");
                        }
                        text.Append(close).Append('}');
                        break;
                    case System.Collections.IEnumerable items:
                        var list = items.Cast<object>().ToList();
                        if (list.Count == 0)
                        {
                            text.Append("[]");
                            break;
                        }
                        text.Append("[\n");
                        for (var i = 0; i < list.Count; i++)
                        {
                            text.Append(pad);
                            Write(text, list[i], depth + 1);
                            text.Append(i < list.Count - 1 ? ",\n" : "\n");
                        }
                        text.Append(close).Append(']');
                        break;
                    default:
                        text.Append(Quote(value.ToString()));
                        break;
                }
            }

            private static string Quote(string s)
            {
                var text = new StringBuilder("\"");
                foreach (var c in s)
                {
                    switch (c)
                    {
                        case '"': text.Append("\\\""); break;
                        case '\\': text.Append("\\\\"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        case '\t': text.Append("\\t"); break;
                        default:
                            if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
                            else text.Append(c);
                            break;
                    }
                }
                return text.Append('"').ToString();
            }
        }
    }
}
