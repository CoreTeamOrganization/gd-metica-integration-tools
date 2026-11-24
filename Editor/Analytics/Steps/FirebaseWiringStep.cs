using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The Firebase app instance id, as GD SDK 6.2.6 sends it: once Firebase's dependencies
    /// resolve, read it, send it to Adjust as the "firebase_app_instance_id" global callback
    /// parameter (so the data team can join the Adjust ID to the Firebase ID), and hand it to the
    /// genre asset with UpdateFirebaseInfo — every Metica event then carries it as
    /// firebaseAppInstanceId (genre creator 2.1.0+), the way UpdateAdjustInfo gives them adid
    /// and appToken.
    ///
    /// <para>GD SDK: FirebaseAnalyticsNetwork.cs gets only what GD SDK 6.2.6 has in that file,
    /// line for line — the AppInstanceId static and SendInstanceIdToAdjust(), called once the
    /// dependencies are available. Nothing in it names the genre: MeticaGenreAnalytics.cs
    /// (<see cref="AnalyticsStartupStep"/>) reads AppInstanceId and calls UpdateFirebaseInfo.
    /// An upgrade to 6.2.6+ replaces the file with the same code.</para>
    ///
    /// <para>Custom SDK: the file that checks <c>FirebaseApp.CheckAndFixDependenciesAsync()</c>.
    /// The lines, UpdateFirebaseInfo included, go first in its
    /// <c>if (… == DependencyStatus.Available) {</c> block.</para>
    /// </summary>
    public sealed class FirebaseWiringStep : MeticaStep
    {
        public const string StepTitle = "Send the Firebase instance ID";

        internal const string Marker = "Metica analytics: the Firebase app instance id";

        private static readonly Regex DependenciesCheck = new Regex(@"\bCheckAndFixDependenciesAsync\s*\(\s*\)");
        private static readonly Regex AvailableIf = new Regex(@"\bif\s*\(.*\bDependencyStatus\.Available\b");

        public override string Title => StepTitle;

        public override string Summary =>
            "Once Firebase is ready, send its app instance ID to Adjust and to every Metica event.";

        public override string Why => MeticaPaths.HasGDSdk
            ? "Adds to FirebaseAnalyticsNetwork.cs exactly what GD SDK 6.2.6 has there: the AppInstanceId static " +
              "and SendInstanceIdToAdjust() — GetAnalyticsInstanceIdAsync(), keep it in AppInstanceId, and " +
              "Adjust.AddGlobalCallbackParameter(\"firebase_app_instance_id\", id) like the muid — called once " +
              "Firebase's dependencies are available (5.3+: after DependenciesAvailable = true; 5.0 – 5.2: first in " +
              "the dependencies-available block). Nothing about the genre goes in this file — MeticaGenreAnalytics.cs " +
              "(Start Metica analytics) hands AppInstanceId to the genre with UpdateFirebaseInfo, so every Metica " +
              "event carries firebaseAppInstanceId. Upgrading the SDK to 6.2.6+ replaces the file with the same lines " +
              $"(6.2.0 – 6.2.5 don't have them). Needs genre creator {GenreCreatorPackage.MinimumVersion}+. A game " +
              "without Firebase Analytics passes with a note. Only missing lines are added. Backed up first."
            : "Right where the game learns Firebase's dependencies are available: " +
            "FirebaseAnalytics.GetAnalyticsInstanceIdAsync(), then Adjust.AddGlobalCallbackParameter(" +
            "\"firebase_app_instance_id\", id) — like the muid — and UpdateFirebaseInfo(id) on the genre asset, so " +
            "every Metica event carries firebaseAppInstanceId (empty until Firebase answers). The same as GD SDK " +
            $"6.2.6's FirebaseAnalyticsNetwork. Needs genre creator {GenreCreatorPackage.MinimumVersion}+. In a GD SDK " +
            "project this is FirebaseAnalyticsNetwork.cs; otherwise the file that calls " +
            "FirebaseApp.CheckAndFixDependenciesAsync(). A game without Firebase Analytics passes with a note. " +
            "Backed up first.";

        public override string ActionLabel
        {
            get
            {
                if (!HasFirebaseAnalytics) return null;
                if (MeticaPaths.HasGDSdk)
                    return AnalyticsStartupStep.EarlierIntegration == null && GdMissing().Count > 0
                        ? "Add the Firebase lines"
                        : null;

                if (WiredFile() != null) return null;
                var target = Target(out _);
                return target == null ? null : $"Wire Firebase in {Path.GetFileName(target)}";
            }
        }

        public override IEnumerable<string> TouchedPaths
        {
            get
            {
                if (MeticaPaths.HasGDSdk) return new[] { GdFirebaseFile };

                var target = WiredFile() ?? Target(out _);
                if (target == null) return Array.Empty<string>();
                var asmdef = AdjustWiringStep.AsmdefFor(target);
                return asmdef == null ? new[] { target } : new[] { target, asmdef };
            }
        }

        public override string ReviewHint => MeticaPaths.HasGDSdk
            ? "Only GD SDK 6.2.6's own lines — a static, one method, one call, a using."
            : "A few lines at the top of the \"dependencies available\" block, and a using — nothing else in the file.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var game = GenreFilesStep.GameName;
            if (GenreFilesStep.NameError(game) != null || !MeticaPaths.FileExists(GenreFilesStep.AssetPath(game)))
                return result.Problem("Create the genre files first.").Seal();

            if (GenreCreatorPackage.IsOutdated)
                return result.Problem($"Genre creator {GenreCreatorPackage.InstalledVersion} has no UpdateFirebaseInfo — " +
                                      $"update it to {GenreCreatorPackage.Version} first (Add Genre Creator step).").Seal();

            if (MeticaPaths.HasGDSdk) return VerifyGdSdk(result);

            var wired = WiredFile();
            if (wired != null)
            {
                result.Note($"Wired in {wired}");
                var asmdef = AdjustWiringStep.AsmdefFor(wired);
                if (asmdef != null && !AdjustWiringStep.References(asmdef))
                    result.Problem($"{Path.GetFileName(asmdef)} needs a reference to {GenreCreatorPackage.RuntimeAssembly}.");
                return result.Seal();
            }

            if (!HasFirebaseAnalytics)
            {
                result.Note("No Firebase Analytics in this project — firebaseAppInstanceId stays empty.");
                return result.Seal();
            }

            var target = Target(out var why);
            result.Problem(target == null ? why : $"Not wired yet — {target}.");
            return result.Seal();
        }

        private static VerifyResult VerifyGdSdk(VerifyResult result)
        {
            if (!HasFirebaseAnalytics)
            {
                result.Note("No Firebase Analytics in this project — firebaseAppInstanceId stays empty.");
                return result.Seal();
            }

            var earlier = AnalyticsStartupStep.EarlierIntegration;
            if (earlier != null)
            {
                result.Note($"Integrated by an earlier version of the tool ({earlier}) — left as it is.");
                return result.Seal();
            }

            var missing = GdMissing();
            if (missing.Count == 0)
                result.Note($"{Path.GetFileName(GdFirebaseFile)} — AppInstanceId and firebase_app_instance_id, as GD SDK 6.2.6");
            else
                result.Problem($"Missing in {Path.GetFileName(GdFirebaseFile)}: {string.Join("; ", missing)}.");
            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            if (MeticaPaths.HasGDSdk)
            {
                if (!HasFirebaseAnalytics || AnalyticsStartupStep.EarlierIntegration != null) return;
                MeticaIntegrationLog.Record(Title, PatchGdSdk(GdFirebaseFile));
                AssetDatabase.ImportAsset(GdFirebaseFile);
                return;
            }

            var wired = WiredFile();
            if (wired != null)
            {
                AdjustWiringStep.AddReference(AdjustWiringStep.AsmdefFor(wired));
                return;
            }

            var target = Target(out var why);
            if (target == null)
            {
                MeticaIntegrationLog.Record(Title, why);
                return;
            }

            MeticaIntegrationLog.Record(Title, Patch(target, GenreFilesStep.GameName));
            AdjustWiringStep.AddReference(AdjustWiringStep.AsmdefFor(target));
            AssetDatabase.ImportAsset(target);
        }

        // ── GD SDK: GD SDK 6.2.6's own lines ───────────────────────────────────

        private static readonly Regex AppInstanceIdStatic = new Regex(@"\bstatic\s+string\s+AppInstanceId\b");
        private static readonly Regex SendMethod = new Regex(@"\bvoid\s+SendInstanceIdToAdjust\s*\(");
        private static readonly Regex SendCall = new Regex(@"(?<!void\s)\bSendInstanceIdToAdjust\s*\(\s*\)\s*;");
        private static readonly Regex InitializeMethod = new Regex(@"\bvoid\s+Initialize\s*\(\s*\)");
        private static readonly Regex AvailableSet = new Regex(@"\bDependenciesAvailable\s*=\s*true\s*;");

        /// <summary>What FirebaseAnalyticsNetwork still lacks of GD SDK 6.2.6's lines; empty when complete.</summary>
        internal static List<string> GdMissing()
        {
            var path = GdFirebaseFile;
            if (!MeticaPaths.FileExists(path)) return new List<string> { $"{path} not found" };

            var text = SourcePatcher.ReadAll(path);
            var code = Code(path);
            var missing = new List<string>();
            if (!text.Contains("using AdjustSdk;")) missing.Add("using AdjustSdk");
            if (!code.Any(line => AppInstanceIdStatic.IsMatch(line))) missing.Add("the AppInstanceId static");
            if (!code.Any(line => SendMethod.IsMatch(line)) || !text.Contains("\"firebase_app_instance_id\""))
                missing.Add("SendInstanceIdToAdjust()");
            if (!code.Any(line => SendCall.IsMatch(line))) missing.Add("the SendInstanceIdToAdjust() call");
            return missing;
        }

        /// <summary>GD SDK 6.2.6's lines, where 6.2.6 has them — only the missing ones.</summary>
        internal static List<string> PatchGdSdk(string path)
        {
            var log = new List<string>();

            SourcePatcher.EnsureUsing(path, "using Firebase.Extensions;");
            if (!SourcePatcher.Contains(path, "using AdjustSdk;"))
            {
                SourcePatcher.EnsureUsing(path, "using AdjustSdk;");
                log.Add($"{path}: using AdjustSdk");
            }

            // 1. The static, after DependenciesAvailable (5.3+) or IsInitialized (5.0 – 5.2).
            if (!Code(path).Any(line => AppInstanceIdStatic.IsMatch(line)))
                log.Add(AdjustWiringStep.InsertAt(path, lines =>
                    {
                        var available = Array.FindIndex(lines, line => Regex.IsMatch(line, @"\bstatic\s+bool\s+DependenciesAvailable\b"));
                        return available >= 0 ? available : Array.FindIndex(lines, line => line.Contains("bool IsInitialized"));
                    }, true,
                    i => $"{i}internal static string AppInstanceId {{ get; private set; }} = string.Empty;",
                    "the AppInstanceId static"));

            // 2. The method, right before Initialize() — where 6.2.6 has it.
            if (!Code(path).Any(line => SendMethod.IsMatch(line)))
                log.Add(AdjustWiringStep.InsertAt(path, lines => Array.FindIndex(lines, line => InitializeMethod.IsMatch(line)), false,
                    SendInstanceIdToAdjust, "SendInstanceIdToAdjust() before Initialize()"));

            // 3. The call, once the dependencies are available.
            if (!Code(path).Any(line => SendCall.IsMatch(line)))
            {
                if (Code(path).Any(line => AvailableSet.IsMatch(line)))
                    log.Add(AdjustWiringStep.InsertAt(path, lines => Array.FindIndex(lines, line => AvailableSet.IsMatch(line)), true,
                        i => i + "SendInstanceIdToAdjust();", "SendInstanceIdToAdjust() after DependenciesAvailable = true"));
                else
                    log.Add(AdjustWiringStep.InsertAt(path, BlockOpening, true,
                        i => i + "    SendInstanceIdToAdjust();", "SendInstanceIdToAdjust() first in the dependencies-available block"));
            }

            return log;
        }

        /// <summary>GD SDK 6.2.6's method, verbatim, then a blank line.</summary>
        private static string SendInstanceIdToAdjust(string i) =>
            $"{i}// Lets the data team map Adjust ID -> Firebase ID via Adjust publisher_parameters.\n" +
            $"{i}private static void SendInstanceIdToAdjust()\n" +
            $"{i}{{\n" +
            $"{i}    FirebaseAnalytics.GetAnalyticsInstanceIdAsync().ContinueWithOnMainThread(task =>\n" +
            $"{i}    {{\n" +
            $"{i}        if (task.IsFaulted || task.IsCanceled || string.IsNullOrEmpty(task.Result)) return;\n" +
            $"{i}        AppInstanceId = task.Result;\n" +
            $"{i}        Message.Log(Logger.Tag.Firebase, $\"firebase_app_instance_id: {{task.Result}}\");\n" +
            $"{i}        Adjust.AddGlobalCallbackParameter(\"firebase_app_instance_id\", task.Result);\n" +
            $"{i}    }});\n" +
            $"{i}}}\n";

        private static string[] Code(string path) =>
            MeticaV1Code.WithoutComments(SourcePatcher.ReadAll(path).Replace("\r\n", "\n").Split('\n'));

        // ── Custom SDK ─────────────────────────────────────────────────────────

        /// <summary>The lines, first in the dependencies-available block. Backed up first.</summary>
        private static string Patch(string target, string game)
        {
            var text = SourcePatcher.ReadAll(target);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            var brace = BlockOpening(lines.ToArray());
            if (brace < 0)
                return $"Couldn't find the \"if (… == DependencyStatus.Available) {{\" block in {target} — " +
                       "add the code by hand (Copy the code).";

            var indent = lines[brace].Substring(0, lines[brace].Length - lines[brace].TrimStart().Length) + "    ";
            lines.InsertRange(brace + 1, Code(indent, game).Split('\n'));
            SourcePatcher.WriteWithBackup(target, string.Join(newline, lines));
            SourcePatcher.EnsureUsing(target, "using Firebase.Extensions;");

            return $"{target}: sends the Firebase app instance id to Adjust and the genre asset once Firebase is ready";
        }

        /// <summary>
        /// The line holding the opening brace of the first <c>if (… DependencyStatus.Available)</c>
        /// block, in comment-stripped code; -1 when there is none, or the if has no braces.
        /// </summary>
        private static int BlockOpening(string[] lines)
        {
            var code = MeticaV1Code.WithoutComments(lines);
            var index = Array.FindIndex(code, line => AvailableIf.IsMatch(line));
            if (index < 0) return -1;
            if (code[index].TrimEnd().EndsWith("{")) return index;

            for (var next = index + 1; next < code.Length; next++)
            {
                var trimmed = code[next].Trim();
                if (trimmed.Length == 0) continue;
                return trimmed == "{" ? next : -1;
            }
            return -1;
        }

        private static string Code(string i, string game) =>
            $"{i}// {Marker}: to Adjust (like the muid) and to every Metica event.\n" +
            $"{i}Firebase.Analytics.FirebaseAnalytics.GetAnalyticsInstanceIdAsync().ContinueWithOnMainThread(firebaseIdTask =>\n" +
            $"{i}{{\n" +
            $"{i}    if (firebaseIdTask.IsFaulted || firebaseIdTask.IsCanceled || string.IsNullOrEmpty(firebaseIdTask.Result)) return;\n" +
            $"{i}    AdjustSdk.Adjust.AddGlobalCallbackParameter(\"firebase_app_instance_id\", firebaseIdTask.Result);\n" +
            $"{i}    var meticaGenreAsset = UnityEngine.Resources.Load<GameDistrict.MeticaAnalytics.GDMeticaAnalytics>(\"{game}Analytics\");\n" +
            $"{i}    if (meticaGenreAsset != null) meticaGenreAsset.UpdateFirebaseInfo(firebaseIdTask.Result);\n" +
            $"{i}}});";

        // ── UI ─────────────────────────────────────────────────────────────────

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (MeticaPaths.HasGDSdk || WiredFile() != null || !HasFirebaseAnalytics) yield break;

            var candidates = Candidates();
            if (candidates.Count > 1)
                yield return new StepChoice("Firebase starts in", candidates.ToArray(),
                    Math.Max(0, candidates.IndexOf(ChosenFile)), index => ChosenFile = candidates[index]);

            yield return new StepButton("Copy the code", () =>
            {
                EditorGUIUtility.systemCopyBuffer =
                    "// using Firebase.Extensions;\n" +
                    "// First thing in: if (task.Result == DependencyStatus.Available) {\n" +
                    Code("", GenreFilesStep.GameName);
                MeticaIntegrationLog.Record(StepTitle, "Code copied — paste it first in the block that runs once Firebase's dependencies are available.");
            }, icon: StepIcon.File);
        }

        // ── Reading the project ────────────────────────────────────────────────

        internal static string GdFirebaseFile => MeticaPaths.Combine(MeticaPaths.RuntimeScripts, "Analytics/Services/FirebaseAnalyticsNetwork.cs");

        /// <summary>Firebase Analytics is in the project (its plugin, or a package).</summary>
        internal static bool HasFirebaseAnalytics =>
            MeticaPaths.HasGDSdk
                ? MeticaPaths.FileExists(GdFirebaseFile)
                : AssetDatabase.FindAssets("Firebase.Analytics").Any() || TypeLoaded("Firebase.Analytics.FirebaseAnalytics");

        private static bool TypeLoaded(string fullName) => TemplateWriter.TypeIsLoaded(fullName);

        /// <summary>The file to wire, or null with the reason.</summary>
        private static string Target(out string why)
        {
            why = null;
            var candidates = Candidates();
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count > 1) return candidates.Contains(ChosenFile) ? ChosenFile : candidates[0];

            why = "No FirebaseApp.CheckAndFixDependenciesAsync() call found in Assets — add the code by hand where " +
                  "Firebase reports its dependencies available (Copy the code).";
            return null;
        }

        /// <summary>Game files that call CheckAndFixDependenciesAsync().</summary>
        private static List<string> Candidates() =>
            GameScripts().Where(file => file.Value.Any(line => DependenciesCheck.IsMatch(line)))
                .Select(file => file.Key).OrderBy(path => path, StringComparer.Ordinal).ToList();

        /// <summary>The file that already hands the id to the genre asset and to Adjust (custom SDK).</summary>
        private static string WiredFile() => GameScripts().Keys.FirstOrDefault(Wired);

        /// <summary>UpdateFirebaseInfo called in code, and the Adjust parameter sent.</summary>
        private static bool Wired(string path)
        {
            var text = SourcePatcher.ReadAll(path);
            if (!text.Contains("firebase_app_instance_id")) return false;
            return MeticaV1Code.WithoutComments(text.Replace("\r\n", "\n").Split('\n'))
                .Any(line => line.Contains("UpdateFirebaseInfo("));
        }

        private static string ChosenFile
        {
            get => EditorPrefs.GetString(ChosenKey, "");
            set => EditorPrefs.SetString(ChosenKey, value ?? "");
        }

        private static string ChosenKey =>
            $"GameDistrict.MeticaIntegrationTools.Analytics.FirebaseFile.{Application.dataPath.GetHashCode():X8}";

        // ── Scanning Assets ────────────────────────────────────────────────────

        private static readonly string[] Skipped =
            { "Assets/Firebase/", "Assets/ExternalDependencyManager/", MeticaPaths.MeticaSdkRoot + "/", MeticaPaths.GenresRoot + "/", MeticaPaths.StandaloneRoot + "/" };

        private static readonly Dictionary<string, (DateTime written, string[] code)> Cache =
            new Dictionary<string, (DateTime, string[])>();

        /// <summary>Game scripts that mention Firebase, comments stripped, by project path.</summary>
        private static Dictionary<string, string[]> GameScripts()
        {
            var found = new Dictionary<string, string[]>();
            var assets = Application.dataPath;

            foreach (var file in Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories))
            {
                var relative = "Assets/" + file.Substring(assets.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (Skipped.Any(folder => relative.StartsWith(folder, StringComparison.Ordinal))) continue;

                var written = File.GetLastWriteTimeUtc(file);
                if (!Cache.TryGetValue(file, out var cached) || cached.written != written)
                {
                    var text = File.ReadAllText(file);
                    cached = (written, text.IndexOf("Firebase", StringComparison.Ordinal) < 0
                        ? null
                        : MeticaV1Code.WithoutComments(text.Replace("\r\n", "\n").Split('\n')));
                    Cache[file] = cached;
                }

                if (cached.code != null) found[relative] = cached.code;
            }

            return found;
        }
    }
}
