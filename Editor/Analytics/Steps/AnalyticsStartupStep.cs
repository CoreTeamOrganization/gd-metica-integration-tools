using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Starts Metica analytics and sends the install (first launch) and sessionStart events.
    ///
    /// <para>GD SDK: no new script. The genre's analytics asset is initialized at the top of
    /// AnalyticsManager.Initialize(), before Adjust starts; the two events are sent from
    /// AdjustAnalyticsNetwork's Adjust ID callback, written by <see cref="AdjustWiringStep"/>.
    /// A startup script left by an earlier version is removed, so nothing runs twice.</para>
    ///
    /// <para>Custom SDK: one script in the genre folder that runs before the first scene loads,
    /// initializes the asset and sends both events.</para>
    ///
    /// <para>Either way, a game that already does any of this itself (Pistols Fight does it in
    /// its splash screen) keeps its own: the step finds the calls and writes nothing, so nothing
    /// is sent twice.</para>
    /// </summary>
    public sealed class AnalyticsStartupStep : MeticaStep
    {
        public const string StepTitle = "Start Metica analytics";

        public static string ScriptPath => MeticaPaths.GenresRoot + "/MeticaAnalyticsStartup.cs";

        /// <summary>The PlayerPrefs key that marks the install event as sent on this device.</summary>
        public const string InstallLoggedKey = "Metica_InstallLogged";

        private const string InitMarker = "Metica analytics: initialize the genre's analytics asset";

        public override string Title => StepTitle;

        public override string Summary => MeticaPaths.HasGDSdk
            ? "Initialize analytics in AnalyticsManager.Initialize(); install and sessionStart follow once Adjust has its ID."
            : "Initialize analytics at app start, and send the install (first launch) and sessionStart events.";

        public override string Why => MeticaPaths.HasGDSdk
            ? $"Adds a few lines at the top of {MeticaPaths.AnalyticsManager}'s Initialize(): load <Game>Analytics from " +
              "Resources and call Initialize() — before Adjust starts, so the asset is ready when Adjust reports its ID. " +
              "The install event (first launch only, PlayerPrefs " + InstallLoggedKey + ") and sessionStart are sent " +
              "from AdjustAnalyticsNetwork's Adjust ID callback (the Connect Adjust step), so both carry the Adjust ID; " +
              "on a first launch with no network they wait for it. No new script — one left by an earlier version is " +
              "deleted. If the game already initializes the asset itself, nothing is added. Backed up first."
            : $"Writes {ScriptPath}: before the first scene loads it loads <Game>Analytics from Resources, calls " +
              "Initialize(), sends LogInstallEvent once per install (PlayerPrefs " + InstallLoggedKey + ") and " +
              "LogSessionStartEvent on every launch. Metica buffers early events, so they are sent at start-up; the " +
              "Adjust ID reaches the asset a moment later, so the first ones may go without it. If the game already " +
              "calls Initialize, LogInstallEvent or LogSessionStartEvent itself, nothing is written.";

        public override string ActionLabel
        {
            get
            {
                var game = GenreFilesStep.GameName;
                if (!GenreReady(game)) return null;

                if (MeticaPaths.HasGDSdk)
                {
                    if (MeticaPaths.FileExists(ScriptPath)) return "Remove the old startup script";
                    return GameCodeInitializes() || InitPatched(game) ? null : "Initialize in AnalyticsManager";
                }

                if (GameCodeCalls().Count > 0) return null;
                return Written(game) ? null : "Add the startup script";
            }
        }

        public override IEnumerable<string> TouchedPaths => MeticaPaths.HasGDSdk
            ? new[] { MeticaPaths.AnalyticsManager, ScriptPath }
            : new[] { ScriptPath };

        public override string ReviewHint => MeticaPaths.HasGDSdk
            ? "A few lines at the top of AnalyticsManager.Initialize()."
            : "One new script in Assets/MeticaGenres — nothing in the game's own code.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var game = GenreFilesStep.GameName;
            if (!GenreReady(game)) return result.Problem("Create the genre files first.").Seal();

            return MeticaPaths.HasGDSdk ? VerifyGdSdk(result, game) : VerifyScript(result, game);
        }

        private static VerifyResult VerifyGdSdk(VerifyResult result, string game)
        {
            if (MeticaPaths.FileExists(ScriptPath))
            {
                result.Problem($"{ScriptPath} is from an earlier version — GD SDK games start analytics in " +
                               "AnalyticsManager and AdjustAnalyticsNetwork now, so it would run everything twice.");
                return result.Seal();
            }

            foreach (var call in GameCodeCalls()) result.Note($"Game code: {call}");

            if (!GameCodeInitializes())
            {
                if (!InitPatched(game))
                {
                    result.Problem(MeticaPaths.FileExists(MeticaPaths.AnalyticsManager)
                        ? "Not set up yet."
                        : $"{MeticaPaths.AnalyticsManager} not found.");
                    return result.Seal();
                }

                result.Note($"{Path.GetFileName(MeticaPaths.AnalyticsManager)} — {game}Analytics.Initialize()");

                var asmdef = AdjustWiringStep.AsmdefFor(MeticaPaths.AnalyticsManager);
                if (asmdef != null && !AdjustWiringStep.References(asmdef))
                    result.Problem($"{Path.GetFileName(asmdef)} needs a reference to {GenreCreatorPackage.RuntimeAssembly}.");
            }

            // The events belong to the Connect Adjust step; say where they are, or that they're not.
            var adjust = MeticaPaths.AdjustAnalyticsNetwork;
            if (MeticaPaths.FileExists(adjust) && AdjustWiringStep.EventsMissing(adjust))
                result.Warning("install and sessionStart aren't sent yet — run Connect Adjust to Metica again.");
            else if (GameCodeEvents().Count == 0)
                result.Note($"install and sessionStart — in {Path.GetFileName(adjust)}, once Adjust has its ID");

            return result.Seal();
        }

        private static VerifyResult VerifyScript(VerifyResult result, string game)
        {
            var calls = GameCodeCalls();
            var written = Written(game);

            if (calls.Count > 0)
            {
                foreach (var call in calls) result.Note($"Game code: {call}");
                if (written)
                    result.Warning($"The game already starts analytics itself, and {ScriptPath} does too — " +
                                   "events would be sent twice. Delete one.");
                return result.Seal();
            }

            if (!written)
            {
                result.Problem(MeticaPaths.FileExists(ScriptPath)
                    ? $"{ScriptPath} starts a different genre — rewrite it for {game}."
                    : "Not set up yet.");
                return result.Seal();
            }

            result.Note($"{ScriptPath} — {game}Analytics: Initialize, install (first launch), sessionStart");
            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            var game = GenreFilesStep.GameName;
            if (!GenreReady(game)) return;

            if (MeticaPaths.HasGDSdk) ApplyGdSdk(game);
            else WriteScript(game);
        }

        private void ApplyGdSdk(string game)
        {
            if (MeticaPaths.FileExists(ScriptPath))
            {
                AssetDatabase.DeleteAsset(ScriptPath);
                MeticaIntegrationLog.Record(Title, $"Deleted {ScriptPath} — GD SDK games start analytics in AnalyticsManager");
            }

            if (GameCodeInitializes() || InitPatched(game)) return;

            MeticaIntegrationLog.Record(Title, PatchInitialize(game));

            var asmdef = AdjustWiringStep.AsmdefFor(MeticaPaths.AnalyticsManager);
            if (asmdef != null && !AdjustWiringStep.References(asmdef)) AdjustWiringStep.AddReference(asmdef);

            AssetDatabase.ImportAsset(MeticaPaths.AnalyticsManager);
        }

        /// <summary>The init lines, first thing in AnalyticsManager.Initialize()'s body.</summary>
        internal static string PatchInitialize(string game)
        {
            var path = MeticaPaths.AnalyticsManager;
            if (!SourcePatcher.Exists(path)) return $"{path} not found.";

            var text = SourcePatcher.ReadAll(path);
            if (text.Contains(InitMarker)) return $"{path} already initializes Metica analytics";

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            var head = lines.FindIndex(line => line.Contains("static void Initialize()"));
            if (head < 0) return $"No Initialize() in {path} — add {game}Analytics.Initialize() by hand.";

            var open = lines.FindIndex(head, line => line.Contains("{"));
            if (open < 0) return $"Initialize() in {path} has no body — add {game}Analytics.Initialize() by hand.";

            var outer = lines[head].Substring(0, lines[head].Length - lines[head].TrimStart().Length);
            var i = outer + "    ";
            lines.InsertRange(open + 1, new[]
            {
                $"{i}// {InitMarker} — install and sessionStart follow in AdjustAnalyticsNetwork, once Adjust has its ID.",
                $"{i}var meticaAnalytics = UnityEngine.Resources.Load<GameDistrict.MeticaAnalytics.GDMeticaAnalytics>(\"{game}Analytics\");",
                $"{i}if (meticaAnalytics != null) meticaAnalytics.Initialize();",
                $"{i}else UnityEngine.Debug.LogWarning(\"[MeticaAnalytics] {game}Analytics not found in Resources — Metica analytics not started.\");",
                ""
            });

            SourcePatcher.WriteWithBackup(path, string.Join(newline, lines));
            return $"{path}: {game}Analytics.Initialize() at the top of Initialize()";
        }

        private void WriteScript(string game)
        {
            var path = MeticaPaths.ToAbsolute(ScriptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, Script(game), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ScriptPath);

            MeticaIntegrationLog.Record(Title, $"Wrote {ScriptPath} for {game}Analytics");
        }

        internal static string Script(string game) =>
            "// Written by the Metica Integration Tools (Analytics Integration → Start Metica analytics).\n" +
            "using UnityEngine;\n" +
            "\n" +
            "namespace GameDistrict.MeticaAnalytics\n" +
            "{\n" +
            "    /// <summary>\n" +
            "    /// Starts Metica analytics before the first scene loads: initializes the genre's analytics\n" +
            "    /// asset, then sends the install event (first launch only) and the sessionStart event.\n" +
            "    /// </summary>\n" +
            "    internal static class MeticaAnalyticsStartup\n" +
            "    {\n" +
            $"        private const string InstallLoggedKey = \"{InstallLoggedKey}\";\n" +
            "\n" +
            "        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]\n" +
            "        private static void Start()\n" +
            "        {\n" +
            $"            var analytics = Resources.Load<GDMeticaAnalytics>(\"{game}Analytics\");\n" +
            "            if (analytics == null)\n" +
            "            {\n" +
            $"                Debug.LogWarning(\"[MeticaAnalytics] {game}Analytics not found in Resources — analytics not started.\");\n" +
            "                return;\n" +
            "            }\n" +
            "\n" +
            "            analytics.Initialize();\n" +
            "\n" +
            "            if (PlayerPrefs.GetInt(InstallLoggedKey, 0) == 0)\n" +
            "            {\n" +
            "                analytics.LogInstallEvent(null);\n" +
            "                PlayerPrefs.SetInt(InstallLoggedKey, 1);\n" +
            "                PlayerPrefs.Save();\n" +
            "            }\n" +
            "\n" +
            "            analytics.LogSessionStartEvent(null);\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        // ── Reading the project ────────────────────────────────────────────────

        private static bool GenreReady(string game) =>
            GenreFilesStep.NameError(game) == null && MeticaPaths.FileExists(GenreFilesStep.AssetPath(game));

        /// <summary>The startup script is there, for this genre.</summary>
        private static bool Written(string game) =>
            MeticaPaths.FileExists(ScriptPath)
            && SourcePatcher.ReadAll(ScriptPath).Contains($"Resources.Load<GDMeticaAnalytics>(\"{game}Analytics\")");

        /// <summary>AnalyticsManager initializes this genre's asset.</summary>
        private static bool InitPatched(string game) =>
            MeticaPaths.FileExists(MeticaPaths.AnalyticsManager)
            && SourcePatcher.ReadAll(MeticaPaths.AnalyticsManager) is var text
            && text.Contains(InitMarker)
            && text.Contains($"GDMeticaAnalytics>(\"{game}Analytics\")");

        private static bool GameCodeInitializes() => GameCodeCalls().Any(call => call.Contains("Initialize"));

        /// <summary>Where the game's own code sends install or sessionStart — "file — what".</summary>
        internal static List<string> GameCodeEvents() =>
            GameCodeCalls().Where(call => call.Contains("install") || call.Contains("sessionStart")).ToList();

        private static readonly Dictionary<string, (DateTime written, string[] code)> Cache =
            new Dictionary<string, (DateTime, string[])>();

        /// <summary>
        /// Where the game's own code already starts analytics: "file — what" for every script
        /// outside the genre folder and the SDKs that calls Initialize on a genre analytics object,
        /// LogInstallEvent or LogSessionStartEvent.
        /// </summary>
        private static List<string> GameCodeCalls()
        {
            var found = new List<string>();
            var assets = Application.dataPath;
            var skipped = new[] { MeticaPaths.GenresRoot + "/", MeticaPaths.MeticaSdkRoot + "/", "Assets/GDMonetization/" };

            foreach (var file in Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories))
            {
                var relative = "Assets/" + file.Substring(assets.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (skipped.Any(folder => relative.StartsWith(folder, StringComparison.Ordinal))) continue;

                var stamp = File.GetLastWriteTimeUtc(file);
                if (!Cache.TryGetValue(file, out var cached) || cached.written != stamp)
                {
                    var text = File.ReadAllText(file);
                    cached = (stamp, text.IndexOf("Analytics", StringComparison.Ordinal) < 0
                        ? null
                        : MeticaV1Code.WithoutComments(text.Replace("\r\n", "\n").Split('\n')));
                    Cache[file] = cached;
                }
                if (cached.code == null) continue;

                var what = new List<string>();
                var usesGenre = cached.code.Any(line => line.Contains("Analytics") &&
                                                        (line.Contains("GDMeticaAnalytics") || IsGenreType(line)));
                // GameAnalytics.Initialize is GameAnalytics' own SDK, not Metica's.
                if (usesGenre && cached.code.Any(line => line.Contains(".Initialize(") && line.Contains("Analytics")
                                                         && !line.Contains("GameAnalytics.Initialize")))
                    what.Add("Initialize");
                if (cached.code.Any(line => line.Contains("LogInstallEvent("))) what.Add("install");
                if (cached.code.Any(line => line.Contains("LogSessionStartEvent("))) what.Add("sessionStart");

                if (what.Count > 0) found.Add($"{relative} — {string.Join(", ", what)}");
            }

            return found;
        }

        /// <summary>A line naming the project's genre analytics class.</summary>
        private static bool IsGenreType(string line)
        {
            var game = GenreFilesStep.GameName;
            return !string.IsNullOrEmpty(game) && line.Contains(game + "Analytics");
        }
    }
}
