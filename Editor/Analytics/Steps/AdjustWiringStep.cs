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
    /// own id kept for the genre's analytics asset.
    ///
    /// <para>GD SDK: AdjustAnalyticsNetwork.cs gets only what GD SDK 6.2.x has in that file,
    /// line for line — the AppToken / AdId statics, the muid, and Adjust.GetAdid → AdId. Nothing
    /// in it names the genre: <see cref="AnalyticsStartupStep"/>'s MeticaGenreAnalytics.cs reads
    /// the statics and hands them to the genre. An upgrade to 6.2.0+ replaces the file with the
    /// same code, so nothing is lost.</para>
    ///
    /// <para>Custom SDK: the file that calls <c>Adjust.InitSdk(…)</c>, found by scanning Assets —
    /// picked from a list when there are several, written by hand from the shown code when there
    /// is none. There the genre asset gets the Adjust ID straight from the Adjust ID callback.</para>
    /// </summary>
    public sealed class AdjustWiringStep : MeticaStep
    {
        public const string StepTitle = "Connect Adjust to Metica";

        // Custom SDK markers.
        private const string BeforeMarker = "Metica analytics: Adjust and Metica share one user id";
        internal const string AfterMarker = "Metica analytics: hand the Adjust ID to the genre";

        private static readonly Regex InitSdk = new Regex(@"\bAdjust\.InitSdk\s*\(\s*(\w+)\s*\)\s*;");
        private static readonly Regex AdjustV4Start = new Regex(@"\bAdjust\.start\s*\(");

        public override string Title => StepTitle;

        public override string Summary => MeticaPaths.HasGDSdk
            ? "Add GD SDK 6.2's Adjust lines: the muid, and the Adjust ID and app token kept for Metica."
            : "Send the Metica user id to Adjust (muid) and hand Adjust's id to the genre asset.";

        public override string Why => MeticaPaths.HasGDSdk
            ? "Adds to AdjustAnalyticsNetwork.cs exactly what GD SDK 6.2.x has there: the AppToken and AdId " +
              "statics, AppToken set from AdjustConfiguration, ExternalDeviceId and the \"muid\" global callback " +
              "parameter set to SystemInfo.deviceUniqueIdentifier before Adjust starts, and Adjust.GetAdid → AdId " +
              "after. Nothing about the genre goes in this file — MeticaGenreAnalytics.cs (Start Metica analytics) " +
              "reads AdId / AppToken and hands them to the genre. Upgrading the SDK to 6.2.0+ replaces the file " +
              "with the same lines, so nothing is lost. Only missing lines are added. Backed up first."
            : "Right before Adjust starts: its external device id and a \"muid\" global callback parameter are set " +
              "to SystemInfo.deviceUniqueIdentifier — the same id the genre asset uses for Metica when its User Id " +
              "is empty. Right after: the genre asset is loaded from Resources and, once Adjust reports its id, " +
              "UpdateAdjustInfo(adid, app token) is called on it. This is the file that calls Adjust.InitSdk. A file " +
              $"inside an assembly definition also gets a reference to {GenreCreatorPackage.RuntimeAssembly}. Backed up first.";

        public override string ActionLabel
        {
            get
            {
                if (MeticaPaths.HasGDSdk)
                {
                    var path = MeticaPaths.AdjustAnalyticsNetwork;
                    if (!MeticaPaths.FileExists(path) || AnalyticsStartupStep.EarlierIntegration != null) return null;
                    return GdMissing(path).Count > 0 ? "Add the Adjust lines" : null;
                }

                var target = Target(out _);
                if (target == null) return null;
                return Wired(target) ? null : $"Wire Adjust in {Path.GetFileName(target)}";
            }
        }

        public override IEnumerable<string> TouchedPaths
        {
            get
            {
                if (MeticaPaths.HasGDSdk) return new[] { MeticaPaths.AdjustAnalyticsNetwork };

                var target = Target(out _);
                if (target == null) return Array.Empty<string>();
                var asmdef = AsmdefFor(target);
                return asmdef == null ? new[] { target } : new[] { target, asmdef };
            }
        }

        public override string ReviewHint => MeticaPaths.HasGDSdk
            ? "Only GD SDK 6.2.x's own lines — two statics, AppToken, the muid, GetAdid → AdId."
            : "A few lines around Adjust.InitSdk — nothing else in the file.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var game = GenreFilesStep.GameName;
            if (GenreFilesStep.NameError(game) != null || !MeticaPaths.FileExists(GenreFilesStep.AssetPath(game)))
                return result.Problem("Create the genre files first.").Seal();

            return MeticaPaths.HasGDSdk ? VerifyGdSdk(result) : VerifyCustom(result);
        }

        private static VerifyResult VerifyGdSdk(VerifyResult result)
        {
            var path = MeticaPaths.AdjustAnalyticsNetwork;
            if (!MeticaPaths.FileExists(path)) return result.Problem($"{path} not found.").Seal();

            var earlier = AnalyticsStartupStep.EarlierIntegration;
            if (earlier != null)
            {
                result.Note($"Integrated by an earlier version of the tool ({earlier}) — left as it is.");
                return result.Seal();
            }

            var missing = GdMissing(path);
            if (missing.Count == 0)
            {
                result.Note($"{Path.GetFileName(path)} — AppToken, AdId (from Adjust.GetAdid) and the muid, as GD SDK 6.2.x");
                return result.Seal();
            }

            result.Problem($"Missing in {Path.GetFileName(path)}: {string.Join("; ", missing)}.");
            return result.Seal();
        }

        private static VerifyResult VerifyCustom(VerifyResult result)
        {
            // Wired already — by this step, or by hand (any file in the game).
            var wired = WiredFile();
            if (wired != null)
            {
                result.Note($"Wired in {wired}");
                var asmdef = AsmdefFor(wired);
                if (asmdef != null && !References(asmdef))
                    result.Problem($"{Path.GetFileName(asmdef)} needs a reference to {GenreCreatorPackage.RuntimeAssembly}.");
                return result.Seal();
            }

            var target = Target(out var why);
            result.Problem(target == null ? why : $"Not wired yet — {target}.");
            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            if (MeticaPaths.HasGDSdk)
            {
                var path = MeticaPaths.AdjustAnalyticsNetwork;
                if (!MeticaPaths.FileExists(path) || AnalyticsStartupStep.EarlierIntegration != null) return;

                MeticaIntegrationLog.Record(Title, PatchGdSdk(path));
                AssetDatabase.ImportAsset(path);
                return;
            }

            var game = GenreFilesStep.GameName;
            var wired = WiredFile();
            if (wired != null)
            {
                AddReference(AsmdefFor(wired));
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

        // ── GD SDK: GD SDK 6.2.x's own lines ───────────────────────────────────

        internal const string AppTokenDeclaration = "internal static string AppToken { get; private set; } = string.Empty;";
        internal const string AdIdDeclaration = "internal static string AdId { get; private set; } = string.Empty;";

        private static readonly Regex AppTokenStatic = new Regex(@"\bstatic\s+string\s+AppToken\b");
        private static readonly Regex AdIdStatic = new Regex(@"\bstatic\s+string\s+AdId\b");
        private static readonly Regex ConfigLoad = new Regex(@"\b(\w+)\s*=\s*Resources\.Load<\s*AdjustConfiguration\s*>");
        private static readonly Regex AppTokenSet = new Regex(@"(?<![\w.])AppToken\s*=\s*\w+\.AppToken\b");
        private static readonly Regex AdIdSet = new Regex(@"(?<![\w.])AdId\s*=\s*\w+\s*;");
        private static readonly Regex GetAdid = new Regex(@"\bAdjust\.GetAdid\s*\(");

        /// <summary>What AdjustAnalyticsNetwork still lacks of GD SDK 6.2.x's lines; empty when complete.</summary>
        internal static List<string> GdMissing(string path)
        {
            var code = Code(path);
            var missing = new List<string>();
            if (!code.Any(line => AppTokenStatic.IsMatch(line))) missing.Add("the AppToken static");
            if (!code.Any(line => AdIdStatic.IsMatch(line))) missing.Add("the AdId static");
            if (!code.Any(line => AppTokenSet.IsMatch(line))) missing.Add("AppToken = <config>.AppToken");
            if (!code.Any(line => line.Contains("ExternalDeviceId"))) missing.Add("ExternalDeviceId");
            if (!code.Any(line => MuidParameter.IsMatch(line))) missing.Add("the muid parameter");
            if (!code.Any(line => GetAdid.IsMatch(line)) || !code.Any(line => AdIdSet.IsMatch(line)))
                missing.Add("Adjust.GetAdid → AdId");
            return missing;
        }

        /// <summary>
        /// GD SDK 6.2.x's lines, where 6.2.x has them — only the missing ones. 6.2.x takes the user
        /// id as Initialize's meticaUserId (MeticaInitializer.UserId); older SDKs call Initialize()
        /// with none, so the same value, SystemInfo.deviceUniqueIdentifier, is used directly.
        /// </summary>
        internal static List<string> PatchGdSdk(string path)
        {
            var log = new List<string>();

            // 1. The two statics, after IsInitialized.
            var statics = new List<string>();
            var code = Code(path);
            if (!code.Any(line => AppTokenStatic.IsMatch(line))) statics.Add(AppTokenDeclaration);
            if (!code.Any(line => AdIdStatic.IsMatch(line))) statics.Add(AdIdDeclaration);
            if (statics.Count > 0)
            {
                // A blank line on both sides, as in 6.2.x.
                var blankAfter = false;
                log.Add(InsertAt(path, lines =>
                    {
                        var index = Array.FindIndex(lines, line => line.Contains("bool IsInitialized"));
                        blankAfter = index >= 0 && index + 1 < lines.Length && lines[index + 1].Trim().Length > 0;
                        return index;
                    }, true,
                    i => "\n" + string.Join("\n", statics.Select(line => i + line)) + (blankAfter ? "\n" : ""),
                    "the AppToken / AdId statics after IsInitialized"));
            }

            // 2. AppToken, right after the AdjustConfiguration is loaded.
            if (!Code(path).Any(line => AppTokenSet.IsMatch(line)))
            {
                var config = Code(path).Select(line => ConfigLoad.Match(line)).FirstOrDefault(match => match.Success);
                log.Add(config == null
                    ? $"No Resources.Load<AdjustConfiguration> in {path} — add AppToken = <config>.AppToken; by hand."
                    : InsertAt(path, lines => Array.FindIndex(lines, line => ConfigLoad.IsMatch(line)), true,
                        i => $"{i}AppToken = {config.Groups[1].Value}.AppToken;",
                        "AppToken from AdjustConfiguration"));
            }

            // 3. The muid, right before Adjust.InitSdk.
            code = Code(path);
            var init = Array.FindIndex(code, line => InitSdk.IsMatch(line));
            if (init < 0)
            {
                log.Add($"Adjust.InitSdk not found in {path} — add the muid and GetAdid by hand.");
                return log;
            }

            var adjustConfig = InitSdk.Match(code[init]).Groups[1].Value;
            var muid = new List<string>();
            if (!code.Any(line => line.Contains("ExternalDeviceId")))
                muid.Add($"{adjustConfig}.ExternalDeviceId = SystemInfo.deviceUniqueIdentifier;");
            if (!code.Any(line => MuidParameter.IsMatch(line)))
                muid.Add("Adjust.AddGlobalCallbackParameter(\"muid\", SystemInfo.deviceUniqueIdentifier);");
            if (muid.Count > 0)
            {
                SourcePatcher.EnsureUsing(path, "using UnityEngine;");
                log.Add(InsertAt(path, lines => Array.FindIndex(lines, line => InitSdk.IsMatch(line)), false,
                    i => string.Join("\n", muid.Select(line => i + line)) + "\n",
                    "the muid before Adjust.InitSdk"));
            }

            // 4. Adjust.GetAdid → AdId, after "IsInitialized = true;" (or Adjust.InitSdk when there is none).
            code = Code(path);
            if (!code.Any(line => GetAdid.IsMatch(line)) || !code.Any(line => AdIdSet.IsMatch(line)))
                log.Add(InsertAt(path, AfterInit, true,
                    i => $"\n{i}Adjust.GetAdid(adid =>\n" +
                         $"{i}{{\n" +
                         $"{i}    if (!string.IsNullOrEmpty(adid))\n" +
                         $"{i}        AdId = adid;\n" +
                         $"{i}}});",
                    "Adjust.GetAdid → AdId after Adjust starts"));

            return log;
        }

        /// <summary>"IsInitialized = true;" after Adjust.InitSdk, else Adjust.InitSdk itself.</summary>
        private static int AfterInit(string[] code)
        {
            var init = Array.FindIndex(code, line => InitSdk.IsMatch(line));
            if (init < 0) return -1;

            for (var i = init + 1; i < code.Length && i <= init + 4; i++)
                if (Regex.IsMatch(code[i], @"\bIsInitialized\s*=\s*true\s*;")) return i;
            return init;
        }

        /// <summary>
        /// Inserts text before or after the line <paramref name="find"/> picks (from the
        /// comment-stripped code), indented like that line. Backed up first.
        /// </summary>
        internal static string InsertAt(string path, Func<string[], int> find, bool after, Func<string, string> text, string what)
        {
            var raw = SourcePatcher.ReadAll(path);
            var newline = raw.Contains("\r\n") ? "\r\n" : "\n";
            var lines = raw.Replace("\r\n", "\n").Split('\n').ToList();

            var index = find(MeticaV1Code.WithoutComments(lines.ToArray()));
            if (index < 0) return $"Couldn't find where to add {what} in {path} — add it by hand.";

            var indent = lines[index].Substring(0, lines[index].Length - lines[index].TrimStart().Length);
            lines.InsertRange(after ? index + 1 : index, text(indent).Split('\n'));
            SourcePatcher.WriteWithBackup(path, string.Join(newline, lines));
            return $"{path}: {what}";
        }

        private static string[] Code(string path) =>
            MeticaV1Code.WithoutComments(SourcePatcher.ReadAll(path).Replace("\r\n", "\n").Split('\n'));

        // ── Custom SDK ─────────────────────────────────────────────────────────

        /// <summary>
        /// The custom-SDK edit, around the file's Adjust.InitSdk call — through SourcePatcher only,
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

            return new List<string>
            {
                SourcePatcher.Describe(
                    SourcePatcher.InsertBeforeLine(target, BeforeMarker, lines[index], Before(indent, config, setsDeviceId)),
                    target, "set the muid before Adjust.InitSdk"),
                SourcePatcher.Describe(
                    SourcePatcher.InsertAfterLine(target, AfterMarker, lines[index], After(indent, config, game)),
                    target, "hand the Adjust ID to the genre asset after Adjust.InitSdk")
            };
        }

        private static string Before(string i, string config, bool setsDeviceId) =>
            $"{i}// {BeforeMarker} (muid).\n" +
            (setsDeviceId ? "" : $"{i}{config}.ExternalDeviceId = UnityEngine.SystemInfo.deviceUniqueIdentifier;\n") +
            $"{i}Adjust.AddGlobalCallbackParameter(\"muid\", UnityEngine.SystemInfo.deviceUniqueIdentifier);";

        private static string After(string i, string config, string game) =>
            $"{i}// {AfterMarker}'s analytics asset.\n" +
            $"{i}var meticaAnalytics = UnityEngine.Resources.Load<GameDistrict.MeticaAnalytics.GDMeticaAnalytics>(\"{game}Analytics\");\n" +
            $"{i}Adjust.GetAdid(adid =>\n" +
            $"{i}{{\n" +
            $"{i}    if (meticaAnalytics != null && !string.IsNullOrEmpty(adid))\n" +
            $"{i}        meticaAnalytics.UpdateAdjustInfo(adid, {config}.AppToken);\n" +
            $"{i}}});";

        // ── UI ─────────────────────────────────────────────────────────────────

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (MeticaPaths.HasGDSdk || WiredFile() != null) yield break;

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
                        "\n\n// After Adjust.InitSdk(config):\n" + After("", "config", game);
                }, icon: StepIcon.File);
        }

        // ── Reading the project (custom SDK) ───────────────────────────────────

        /// <summary>The file to wire, or null with the reason.</summary>
        private static string Target(out string why)
        {
            why = null;

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
        private static string WiredFile() =>
            Scan().Where(file => IsWired(file.Value)).Select(file => file.Key).FirstOrDefault();

        internal static bool Wired(string path) => IsWired(Code(path));

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
