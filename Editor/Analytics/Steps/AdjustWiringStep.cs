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
    /// Ties Adjust to Metica analytics where the game starts Adjust: one shared user id, sent
    /// to Adjust as the "muid" callback parameter (and its external device id), and Adjust's
    /// own id handed to the genre's analytics asset once Adjust reports it.
    ///
    /// <para>GD SDK: AdjustAnalyticsNetwork.cs, whose one <c>Adjust.InitSdk(adjustConfig);</c> is
    /// the anchor in every release from 5.0.0 to 6.0.1. Custom SDK: the file that calls
    /// <c>Adjust.InitSdk(…)</c>, found by scanning Assets — picked from a list when there are
    /// several, written by hand from the shown code when there is none.</para>
    /// </summary>
    public sealed class AdjustWiringStep : MeticaStep
    {
        public const string StepTitle = "Connect Adjust to Metica";

        private const string BeforeMarker = "Metica analytics: Adjust and Metica share one user id";
        private const string AfterMarker = "Metica analytics: hand the Adjust ID to the genre";
        internal const string EventsMarker = "Metica analytics: install (first launch) and sessionStart, once Adjust has its ID";

        private static readonly Regex InitSdk = new Regex(@"\bAdjust\.InitSdk\s*\(\s*(\w+)\s*\)\s*;");
        private static readonly Regex AdjustV4Start = new Regex(@"\bAdjust\.start\s*\(");

        public override string Title => StepTitle;

        public override string Summary => MeticaPaths.HasGDSdk
            ? "Send the Metica user id to Adjust (muid), hand Adjust's id to the genre asset, then send install and sessionStart."
            : "Send the Metica user id to Adjust (muid) and hand Adjust's id to the genre asset.";

        public override string Why =>
            "Right before Adjust starts: its external device id and a \"muid\" global callback parameter are set " +
            "to SystemInfo.deviceUniqueIdentifier — the same id the genre asset uses for Metica when its User Id " +
            "is empty. Right after: the genre asset is loaded from Resources and, once Adjust reports its id, " +
            "UpdateAdjustInfo(adid, app token) is called on it. In a GD SDK project this is AdjustAnalyticsNetwork.cs, " +
            "and the same Adjust ID callback then sends the install event (first launch only, PlayerPrefs " +
            $"{AnalyticsStartupStep.InstallLoggedKey}) and sessionStart — unless the game's own code already does; " +
            "a project wired by an earlier version gets just those lines. Otherwise it's the file that calls " +
            "Adjust.InitSdk. A file inside an assembly definition also gets a " +
            $"reference to {GenreCreatorPackage.RuntimeAssembly}. Backed up first.";

        public override string ActionLabel
        {
            get
            {
                var target = Target(out _);
                if (target == null) return null;
                if (!Wired(target)) return $"Wire Adjust in {Path.GetFileName(target)}";
                return EventsMissing(target) ? "Add install and sessionStart" : null;
            }
        }

        public override IEnumerable<string> TouchedPaths
        {
            get
            {
                var target = Target(out _);
                if (target == null) return Array.Empty<string>();
                var asmdef = AsmdefFor(target);
                return asmdef == null ? new[] { target } : new[] { target, asmdef };
            }
        }

        public override string ReviewHint => "A few lines around Adjust.InitSdk — nothing else in the file.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var game = GenreFilesStep.GameName;
            if (GenreFilesStep.NameError(game) != null || !MeticaPaths.FileExists(GenreFilesStep.AssetPath(game)))
                return result.Problem("Create the genre files first.").Seal();

            // Wired already — by this step, or by hand (any file in the game).
            var wired = WiredFile();
            if (wired != null)
            {
                result.Note($"Wired in {wired}");
                var asmdef = AsmdefFor(wired);
                if (asmdef != null && !References(asmdef))
                    result.Problem($"{Path.GetFileName(asmdef)} needs a reference to {GenreCreatorPackage.RuntimeAssembly}.");

                if (EventsMissing(wired))
                    result.Problem("install and sessionStart aren't sent yet — they go in the Adjust ID callback.");
                else if (MeticaPaths.HasGDSdk)
                {
                    var own = AnalyticsStartupStep.GameCodeEvents();
                    result.Note(own.Count > 0
                        ? $"install / sessionStart sent by the game's code: {string.Join("; ", own)}"
                        : "install (first launch) and sessionStart sent once Adjust has its ID");
                }

                return result.Seal();
            }

            var target = Target(out var why);
            if (target == null)
            {
                result.Problem(why);
                return result.Seal();
            }

            result.Problem($"Not wired yet — {target}.");
            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            var game = GenreFilesStep.GameName;
            var wired = WiredFile();

            // Wired before: only the reference, or (GD SDK, an earlier version) the events, are missing.
            if (wired != null)
            {
                AddReference(AsmdefFor(wired));
                if (EventsMissing(wired))
                {
                    MeticaIntegrationLog.Record(Title, AddEvents(wired));
                    AssetDatabase.ImportAsset(wired);
                }
                return;
            }

            var target = Target(out var why);
            if (target == null)
            {
                MeticaIntegrationLog.Record(Title, why);
                return;
            }

            var log = Patch(target, game);

            var asmdef = AsmdefFor(target);
            if (asmdef != null && !References(asmdef)) AddReference(asmdef);

            MeticaIntegrationLog.Record(Title, log);
            AssetDatabase.ImportAsset(target);
        }

        /// <summary>
        /// The edit itself, around the file's Adjust.InitSdk call — through SourcePatcher only,
        /// so it runs on disk or in memory alike.
        /// </summary>
        internal static List<string> Patch(string target, string game)
        {
            var lines = SourcePatcher.ReadAll(target).Replace("\r\n", "\n").Split('\n');
            var code = MeticaV1Code.WithoutComments(lines);
            var index = Array.FindIndex(code, line => InitSdk.IsMatch(line));
            if (index < 0) return new List<string> { $"Adjust.InitSdk not found in {target}." };

            var config = InitSdk.Match(code[index]).Groups[1].Value;
            var indent = lines[index].Substring(0, lines[index].Length - lines[index].TrimStart().Length);
            var setsDeviceId = code.Any(line => line.Contains("ExternalDeviceId"));
            var events = MeticaPaths.HasGDSdk && AnalyticsStartupStep.GameCodeEvents().Count == 0;

            return new List<string>
            {
                SourcePatcher.Describe(
                    SourcePatcher.InsertBeforeLine(target, BeforeMarker, lines[index], Before(indent, config, setsDeviceId)),
                    target, "set the muid before Adjust.InitSdk"),
                SourcePatcher.Describe(
                    SourcePatcher.InsertAfterLine(target, AfterMarker, lines[index], After(indent, config, game, events)),
                    target, events
                        ? "hand the Adjust ID to the genre asset after Adjust.InitSdk, then send install and sessionStart"
                        : "hand the Adjust ID to the genre asset after Adjust.InitSdk")
            };
        }

        private static string Before(string i, string config, bool setsDeviceId) =>
            $"{i}// {BeforeMarker} (muid).\n" +
            (setsDeviceId ? "" : $"{i}{config}.ExternalDeviceId = UnityEngine.SystemInfo.deviceUniqueIdentifier;\n") +
            $"{i}Adjust.AddGlobalCallbackParameter(\"muid\", UnityEngine.SystemInfo.deviceUniqueIdentifier);";

        private static string After(string i, string config, string game, bool events) =>
            $"{i}// {AfterMarker}'s analytics asset.\n" +
            $"{i}var meticaAnalytics = UnityEngine.Resources.Load<GameDistrict.MeticaAnalytics.GDMeticaAnalytics>(\"{game}Analytics\");\n" +
            $"{i}Adjust.GetAdid(adid =>\n" +
            $"{i}{{\n" +
            $"{i}    if (meticaAnalytics != null && !string.IsNullOrEmpty(adid))\n" +
            $"{i}        meticaAnalytics.UpdateAdjustInfo(adid, {config}.AppToken);\n" +
            (events ? Events(i + "    ") : "") +
            $"{i}}});";

        /// <summary>
        /// The install event on the first launch only, and sessionStart on every launch — inside
        /// the Adjust ID callback, so both carry the Adjust ID. Adjust runs that callback on
        /// Unity's main thread, so PlayerPrefs is safe there.
        /// </summary>
        private static string Events(string i) =>
            $"{i}// {EventsMarker}.\n" +
            $"{i}if (meticaAnalytics != null)\n" +
            $"{i}{{\n" +
            $"{i}    if (UnityEngine.PlayerPrefs.GetInt(\"{AnalyticsStartupStep.InstallLoggedKey}\", 0) == 0)\n" +
            $"{i}    {{\n" +
            $"{i}        meticaAnalytics.LogInstallEvent(null);\n" +
            $"{i}        UnityEngine.PlayerPrefs.SetInt(\"{AnalyticsStartupStep.InstallLoggedKey}\", 1);\n" +
            $"{i}        UnityEngine.PlayerPrefs.Save();\n" +
            $"{i}    }}\n" +
            $"{i}    meticaAnalytics.LogSessionStartEvent(null);\n" +
            $"{i}}}\n";

        /// <summary>
        /// GD SDK: the events are not in the wired file, and the game's own code doesn't send
        /// them either. Custom SDKs get them from the startup script instead.
        /// </summary>
        internal static bool EventsMissing(string path) =>
            MeticaPaths.HasGDSdk
            && !SourcePatcher.Contains(path, "LogSessionStartEvent(")
            && AnalyticsStartupStep.GameCodeEvents().Count == 0;

        /// <summary>For a file wired by an earlier version: the events, right after its UpdateAdjustInfo line.</summary>
        private static List<string> AddEvents(string path)
        {
            var lines = SourcePatcher.ReadAll(path).Replace("\r\n", "\n").Split('\n');
            var index = Array.FindIndex(lines, line => line.Contains("UpdateAdjustInfo("));
            if (index < 0) return new List<string> { $"UpdateAdjustInfo not found in {path} — add install and sessionStart by hand." };

            // The call sits under an un-braced if inside the callback: one level up is the callback's own.
            var updateIndent = lines[index].Substring(0, lines[index].Length - lines[index].TrimStart().Length);
            var callbackIndent = index > 0 && lines[index - 1].TrimStart().StartsWith("if ")
                ? lines[index - 1].Substring(0, lines[index - 1].Length - lines[index - 1].TrimStart().Length)
                : updateIndent;

            return new List<string>
            {
                SourcePatcher.Describe(
                    SourcePatcher.InsertAfterLine(path, EventsMarker, lines[index], Events(callbackIndent).TrimEnd('\n')),
                    path, "send install and sessionStart in the Adjust ID callback")
            };
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (WiredFile() != null || MeticaPaths.HasGDSdk) yield break;

            var candidates = Candidates();
            if (candidates.Count > 1)
                yield return new StepChoice("Adjust starts in", candidates.ToArray(),
                    Math.Max(0, candidates.IndexOf(ChosenFile)), index => ChosenFile = candidates[index]);

            if (candidates.Count == 0)
                yield return new StepButton("Copy the code", () =>
                {
                    var game = GenreFilesStep.GameName;
                    EditorGUIUtility.systemCopyBuffer =
                        "// Before Adjust.InitSdk(config):\n" + Before("", "config", false) +
                        "\n\n// After Adjust.InitSdk(config):\n" + After("", "config", game, false);
                }, icon: StepIcon.File);
        }

        // ── Reading the project ────────────────────────────────────────────────

        /// <summary>The file to wire, or null with the reason.</summary>
        private static string Target(out string why)
        {
            why = null;

            if (MeticaPaths.HasGDSdk)
            {
                var path = MeticaPaths.AdjustAnalyticsNetwork;
                if (MeticaPaths.FileExists(path)) return path;
                why = $"{path} not found.";
                return null;
            }

            var candidates = Candidates();
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count > 1) return candidates.Contains(ChosenFile) ? ChosenFile : candidates[0];

            why = Scan().Any(file => file.Value.Any(line => AdjustV4Start.IsMatch(line)))
                ? "This game uses Adjust v4 (Adjust.start) — add the code by hand where Adjust starts."
                : "No Adjust.InitSdk call found in Assets — add the code by hand where Adjust starts.";
            return null;
        }

        /// <summary>Files that call Adjust.InitSdk.</summary>
        private static List<string> Candidates() =>
            Scan().Where(file => file.Value.Any(line => InitSdk.IsMatch(line))).Select(file => file.Key)
                .OrderBy(path => path, StringComparer.Ordinal).ToList();

        /// <summary>The first file that already sets the muid and hands over the Adjust ID.</summary>
        private static string WiredFile()
        {
            if (MeticaPaths.HasGDSdk)
            {
                var path = MeticaPaths.AdjustAnalyticsNetwork;
                return MeticaPaths.FileExists(path) && Wired(path) ? path : null;
            }

            return Scan().Where(file => IsWired(file.Value)).Select(file => file.Key).FirstOrDefault();
        }

        internal static bool Wired(string path) =>
            IsWired(MeticaV1Code.WithoutComments(SourcePatcher.ReadAll(path).Replace("\r\n", "\n").Split('\n')));

        /// <summary>
        /// The muid set and the Adjust ID handed over. Read from comment-stripped code, where a
        /// string's contents are blanked — "muid" shows as four spaces.
        /// </summary>
        private static bool IsWired(string[] code) =>
            code.Any(line => MuidParameter.IsMatch(line)) && code.Any(line => line.Contains("UpdateAdjustInfo("));

        private static readonly Regex MuidParameter = new Regex(@"AddGlobalCallbackParameter\(\s*"" {4}""");

        private static string ChosenFile
        {
            get => EditorPrefs.GetString(ChosenKey, "");
            set => EditorPrefs.SetString(ChosenKey, value ?? "");
        }

        private static string ChosenKey =>
            $"GameDistrict.MeticaIntegrationTools.Analytics.AdjustFile.{Application.dataPath.GetHashCode():X8}";

        // ── Scanning Assets ────────────────────────────────────────────────────

        /// <summary>Folders that are SDKs, not game code.</summary>
        private static readonly string[] Skipped =
            { "Assets/Adjust/", MeticaPaths.MeticaSdkRoot + "/", MeticaPaths.GenresRoot + "/", MeticaPaths.StandaloneRoot + "/" };

        private static readonly Dictionary<string, (DateTime written, string[] code)> Cache =
            new Dictionary<string, (DateTime, string[])>();

        /// <summary>Game scripts that mention Adjust, comments stripped, by project path.</summary>
        private static Dictionary<string, string[]> Scan()
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
                    cached = (written, text.IndexOf("Adjust", StringComparison.Ordinal) < 0
                        ? null
                        : MeticaV1Code.WithoutComments(text.Replace("\r\n", "\n").Split('\n')));
                    Cache[file] = cached;
                }

                if (cached.code != null) found[relative] = cached.code;
            }

            return found;
        }

        // ── Assembly definitions ───────────────────────────────────────────────

        /// <summary>The .asmdef that compiles <paramref name="path"/>, or null (Assembly-CSharp).</summary>
        internal static string AsmdefFor(string path)
        {
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) && folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                var absolute = MeticaPaths.ToAbsolute(folder);
                if (Directory.Exists(absolute))
                {
                    var asmdef = Directory.GetFiles(absolute, "*.asmdef").FirstOrDefault();
                    if (asmdef != null) return folder + "/" + Path.GetFileName(asmdef);
                }

                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }

            return null;
        }

        internal static bool References(string asmdef)
        {
            var text = SourcePatcher.ReadAll(asmdef);
            if (text.Contains("\"" + GenreCreatorPackage.RuntimeAssembly + "\"")) return true;

            var guid = AssetDatabase.FindAssets(GenreCreatorPackage.RuntimeAssembly + " t:AssemblyDefinitionAsset")
                .FirstOrDefault();
            return guid != null && text.Contains("GUID:" + guid);
        }

        internal static void AddReference(string asmdef)
        {
            if (asmdef == null || References(asmdef)) return;

            var name = $"\"{GenreCreatorPackage.RuntimeAssembly}\"";
            var text = SourcePatcher.ReadAll(asmdef);
            var empty = Regex.Match(text, @"""references""\s*:\s*\[\s*\]");
            var list = Regex.Match(text, @"""references""\s*:\s*\[");

            if (empty.Success) text = text.Remove(empty.Index, empty.Length).Insert(empty.Index, $"\"references\": [ {name} ]");
            else if (list.Success) text = text.Insert(list.Index + list.Length, $"\n        {name},");
            else text = text.Insert(text.IndexOf('{') + 1, $"\n    \"references\": [ {name} ],");

            SourcePatcher.WriteWithBackup(asmdef, text);
            MeticaIntegrationLog.Record(StepTitle, $"Added {name} to {asmdef}'s references");
            AssetDatabase.ImportAsset(asmdef);
        }
    }
}
