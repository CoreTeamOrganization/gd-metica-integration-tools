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
    /// Starts Metica analytics without touching the game's code: one script in the genre folder
    /// that runs before the first scene loads, initializes the genre's analytics asset, and sends
    /// the install event (first launch only) and the sessionStart event (every launch).
    ///
    /// <para>A game that already does any of this itself (Pistols Fight does it in its splash
    /// screen) keeps its own: the step finds the calls and writes nothing, so nothing is sent
    /// twice.</para>
    /// </summary>
    public sealed class AnalyticsStartupStep : MeticaStep
    {
        public const string StepTitle = "Start Metica analytics";

        public static string ScriptPath => MeticaPaths.GenresRoot + "/MeticaAnalyticsStartup.cs";

        /// <summary>The PlayerPrefs key that marks the install event as sent on this device.</summary>
        public const string InstallLoggedKey = "Metica_InstallLogged";

        public override string Title => StepTitle;

        public override string Summary =>
            "Initialize analytics at app start, and send the install (first launch) and sessionStart events.";

        public override string Why =>
            $"Writes {ScriptPath}: before the first scene loads it loads <Game>Analytics from Resources, calls " +
            "Initialize(), sends LogInstallEvent once per install (PlayerPrefs " + InstallLoggedKey + ") and " +
            "LogSessionStartEvent on every launch. Metica buffers early events, so they are sent at start-up; the " +
            "Adjust ID reaches the asset a moment later, so the first ones may go without it. If the game already " +
            "calls Initialize, LogInstallEvent or LogSessionStartEvent itself, nothing is written.";

        public override string ActionLabel
        {
            get
            {
                var game = GenreFilesStep.GameName;
                if (!GenreReady(game) || GameCodeCalls().Count > 0) return null;
                return Written(game) ? null : "Add the startup script";
            }
        }

        public override IEnumerable<string> TouchedPaths => new[] { ScriptPath };

        public override string ReviewHint => "One new script in Assets/MeticaGenres — nothing in the game's own code.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var game = GenreFilesStep.GameName;
            if (!GenreReady(game)) return result.Problem("Create the genre files first.").Seal();

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
