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
    /// Writes MeticaGenreAnalytics.cs, the game's one place for Metica genre analytics. It lives
    /// in its own folder, outside the GD SDK and the genre folder, so an SDK upgrade never touches
    /// it. It:
    /// <list type="bullet">
    /// <item>holds the genre — <c>MeticaGenreAnalytics.Genre</c>, loaded from Resources — which game
    /// code logs every genre event through;</item>
    /// <item>initializes it before the first scene loads;</item>
    /// <item>sends install (first launch only) and sessionStart (once per launch) when the game
    /// leaves its first scene — the splash — guarded by a bool, so going back to the splash sends
    /// nothing again;</item>
    /// <item>GD SDK only: hands the genre AdjustAnalyticsNetwork.AdId / AppToken and
    /// FirebaseAnalyticsNetwork.AppInstanceId — the statics <see cref="AdjustWiringStep"/> and
    /// <see cref="FirebaseWiringStep"/> add, as GD SDK 6.2.x has them — as they become known.
    /// install and sessionStart may go out before the Adjust ID; later events carry it.</item>
    /// </list>
    ///
    /// <para>GD SDK files get no genre code at all, AnalyticsManager.cs included. Upgrading to
    /// GD SDK 6.2.6+ then means: remake the genre with the SDK's Genre Creator, and in this one file
    /// change the using and GenrePath and delete the lines marked "below GD SDK 6.2.0 only".</para>
    ///
    /// <para>For new integrations only: a project the tool integrated before (genre code in the
    /// GD SDK's files, or the old MeticaAnalyticsStartup.cs) is left as it is. A game whose own code
    /// already initializes the genre or sends install / sessionStart gets nothing written.</para>
    /// </summary>
    public sealed class AnalyticsStartupStep : MeticaStep
    {
        public const string StepTitle = "Start Metica analytics";

        public const string ScriptFolder = "Assets/MeticaGenreAnalytics";
        public const string ClassName = "MeticaGenreAnalytics";
        public static string ScriptPath => ScriptFolder + "/" + ClassName + ".cs";

        /// <summary>The PlayerPrefs key that marks the install event as sent on this device.</summary>
        public const string InstallLoggedKey = "Metica_InstallLogged";

        /// <summary>Where the genre asset sits in GD SDK 6.2.x's Resources (its Genre Creator).</summary>
        public const string GdSdkGenreFolder = "Configurations/Game Genres";

        // An earlier version of the tool: its startup script, and its marker in AnalyticsManager.
        private static string OldScriptPath => MeticaPaths.GenresRoot + "/MeticaAnalyticsStartup.cs";
        private const string OldInitMarker = "Metica analytics: initialize the genre's analytics asset";

        private const string BelowSdk62 = "below GD SDK 6.2.0 only";

        public override string Title => StepTitle;

        public override string Summary =>
            $"Add {ClassName}.cs: the genre for game code, install and sessionStart when the splash scene ends.";

        public override string Why =>
            $"Writes {ScriptPath} — the game's own file, outside the SDK, so an SDK upgrade never touches it. " +
            $"Game code logs every genre event through {ClassName}.Genre (<Game>Analytics, loaded from Resources). " +
            "Before the first scene loads it initializes the genre; when the game leaves its first scene (the " +
            $"splash) it sends install (first launch only, PlayerPrefs {InstallLoggedKey}) and sessionStart — " +
            "once per launch, a bool keeps a return to the splash from sending them again. " +
            (MeticaPaths.HasGDSdk
                ? "It also hands the genre AdjustAnalyticsNetwork.AdId / AppToken and FirebaseAnalyticsNetwork.AppInstanceId " +
                  "(added by the two steps before, as GD SDK 6.2.x has them) once they are known — install and " +
                  "sessionStart may go out without the Adjust ID, later events carry it. No genre code goes in any GD SDK " +
                  "file. Upgrading to GD SDK 6.2.6+: remake the genre with the SDK's Genre Creator, then in this file " +
                  $"change the using and GenrePath and delete the lines marked \"{BelowSdk62}\". "
                : "") +
            "If the game's own code already initializes the genre or sends install / sessionStart, nothing is written.";

        public override string ActionLabel
        {
            get
            {
                var game = GenreFilesStep.GameName;
                if (!GenreReady(game) || EarlierIntegration != null || GameCodeCalls().Count > 0) return null;
                if (MeticaPaths.HasGDSdk && MissingSdkLines().Count > 0) return null;
                return Written(game) ? null : $"Add {ClassName}.cs";
            }
        }

        public override IEnumerable<string> TouchedPaths
        {
            get
            {
                var asmdef = AdjustWiringStep.AsmdefFor(ScriptPath);
                return asmdef == null ? new[] { ScriptPath } : new[] { ScriptPath, asmdef };
            }
        }

        public override string ReviewHint => $"One new script, {ScriptPath} — nothing in the SDK or the game's own code.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var game = GenreFilesStep.GameName;
            if (!GenreReady(game)) return result.Problem("Create the genre files first.").Seal();

            var earlier = EarlierIntegration;
            if (earlier != null)
            {
                result.Note($"Integrated by an earlier version of the tool ({earlier}) — left as it is.");
                return result.Seal();
            }

            var calls = GameCodeCalls();
            var written = Written(game);
            if (calls.Count > 0)
            {
                foreach (var call in calls) result.Note($"Game code: {call}");
                if (written)
                    result.Warning($"The game already starts analytics itself, and {ScriptPath} does too — " +
                                   "events would be sent twice. Delete one.");
                else if (MeticaPaths.HasGDSdk)
                    result.Warning("The game starts analytics itself — it must also hand AdjustAnalyticsNetwork.AdId / " +
                                   "AppToken to the genre (UpdateAdjustInfo), or Metica events go without the Adjust ID.");
                return result.Seal();
            }

            if (MeticaPaths.HasGDSdk)
            {
                var missing = MissingSdkLines();
                if (missing.Count > 0)
                    return result.Problem($"Run {string.Join(" and ", missing)} first — {ClassName}.cs reads what they add.").Seal();
            }

            if (!written)
            {
                result.Problem(MeticaPaths.FileExists(ScriptPath)
                    ? $"{ScriptPath} is for a different genre — rewrite it for {game}."
                    : "Not set up yet.");
                return result.Seal();
            }

            result.Note($"{ScriptPath} — {ClassName}.Genre ({game}Analytics); install (first launch) and sessionStart " +
                        "when the splash scene ends" + (MeticaPaths.HasGDSdk ? "; Adjust / Firebase IDs handed to the genre" : ""));

            var asmdef = AdjustWiringStep.AsmdefFor(ScriptPath);
            if (asmdef != null && MeticaPaths.HasGDSdk)
                result.Warning($"{ScriptPath} is inside {asmdef}, which can't see the GD SDK's AdjustAnalyticsNetwork — " +
                               "move the script out of it.");
            else if (asmdef != null && !AdjustWiringStep.References(asmdef))
                result.Problem($"{Path.GetFileName(asmdef)} needs a reference to {GenreCreatorPackage.RuntimeAssembly}.");

            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            var game = GenreFilesStep.GameName;
            if (!GenreReady(game) || EarlierIntegration != null || GameCodeCalls().Count > 0) return;
            if (MeticaPaths.HasGDSdk && MissingSdkLines().Count > 0) return;

            var path = MeticaPaths.ToAbsolute(ScriptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var firebase = MeticaPaths.HasGDSdk && FirebaseWiringStep.HasFirebaseAnalytics;
            File.WriteAllText(path, Script(game, MeticaPaths.HasGDSdk, firebase), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ScriptPath);
            MeticaIntegrationLog.Record(Title, $"Wrote {ScriptPath} for {game}Analytics");

            if (!MeticaPaths.HasGDSdk) AdjustWiringStep.AddReference(AdjustWiringStep.AsmdefFor(ScriptPath));
        }

        /// <summary>
        /// The script. <paramref name="gdSdk"/> adds the ID hand-over from GD SDK's statics, and
        /// <paramref name="firebase"/> the Firebase part of it.
        /// </summary>
        internal static string Script(string game, bool gdSdk, bool firebase)
        {
            var type = game + "Analytics";
            var s = new StringBuilder();

            s.Append("// Written by the Metica Integration Tools (Analytics Integration → Start Metica analytics).\n");
            s.Append("// The game's own file: SDK upgrades never touch it.\n");
            if (gdSdk)
            {
                s.Append("//\n");
                s.Append("// Upgrading to GD SDK 6.2.6+: remake the genre with the SDK's Genre Creator (same name, same Excel),\n");
                s.Append($"// then here: delete the GameDistrict.MeticaAnalytics using, set GenrePath to \"{GdSdkGenreFolder}/{type}\",\n");
                s.Append($"// and delete every line marked \"{BelowSdk62}\".\n");
            }
            s.Append("using UnityEngine;\n");
            s.Append("using UnityEngine.SceneManagement;\n");
            s.Append(gdSdk ? $"using GameDistrict.MeticaAnalytics; // {BelowSdk62}\n" : "using GameDistrict.MeticaAnalytics;\n");
            if (gdSdk) s.Append("using Monetization.Runtime.Analytics;\n");
            s.Append("\n");
            s.Append("/// <summary>\n");
            s.Append("/// The game's Metica genre analytics: log every genre event through <see cref=\"Genre\"/>.\n");
            s.Append("/// Sends install (first launch only) and sessionStart (once per launch) when the game leaves\n");
            s.Append("/// its first scene, the splash.\n");
            s.Append("/// </summary>\n");
            s.Append($"public static class {ClassName}\n");
            s.Append("{\n");
            s.Append($"    private const string GenrePath = \"{type}\";\n");
            s.Append($"    private const string InstallKey = \"{InstallLoggedKey}\"; // never change it: a new key sends install again\n");
            s.Append("\n");
            s.Append($"    private static {type} _genre;\n");
            s.Append("    private static bool _initialized;\n");
            s.Append("\n");
            s.Append("    /// <summary>Log every genre event through this.</summary>\n");
            if (gdSdk)
            {
                s.Append($"    public static {type} Genre\n");
                s.Append("    {\n");
                s.Append("        get\n");
                s.Append("        {\n");
                s.Append($"            SyncIds(); // {BelowSdk62}\n");
                s.Append("            return _genre;\n");
                s.Append("        }\n");
                s.Append("    }\n");
            }
            else
            {
                s.Append($"    public static {type} Genre => _genre;\n");
            }
            s.Append("\n");
            s.Append("    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]\n");
            s.Append("    private static void Setup()\n");
            s.Append("    {\n");
            s.Append($"        _genre = Resources.Load<{type}>(GenrePath);\n");
            s.Append("        if (_genre == null)\n");
            s.Append("        {\n");
            s.Append($"            Debug.LogWarning(\"[{ClassName}] \" + GenrePath + \" not found in Resources — Metica genre analytics not started.\");\n");
            s.Append("            return;\n");
            s.Append("        }\n");
            s.Append("\n");
            s.Append(gdSdk
                ? $"        _genre.Initialize(); // {BelowSdk62} — 6.2.0+ starts Metica itself\n"
                : "        _genre.Initialize();\n");
            s.Append("        SceneManager.activeSceneChanged += OnSceneChanged;\n");
            s.Append("    }\n");
            s.Append("\n");
            s.Append("    // Leaving the first scene (the splash): install and sessionStart.\n");
            s.Append("    // Once per launch, even if the game goes back to the splash.\n");
            s.Append("    private static void OnSceneChanged(Scene from, Scene to)\n");
            s.Append("    {\n");
            s.Append("        if (_initialized || !from.IsValid()) return;\n");
            s.Append("        _initialized = true;\n");
            s.Append("\n");
            s.Append("        if (PlayerPrefs.GetInt(InstallKey, 0) == 0)\n");
            s.Append("        {\n");
            s.Append("            Genre.LogInstallEvent(null);\n");
            s.Append("            PlayerPrefs.SetInt(InstallKey, 1);\n");
            s.Append("            PlayerPrefs.Save();\n");
            s.Append("        }\n");
            s.Append("        Genre.LogSessionStartEvent(null);\n");
            s.Append("    }\n");

            if (gdSdk)
            {
                s.Append("\n");
                s.Append($"    // ── {BelowSdk62}: from here to the end of the class. 6.2.0+'s genre reads these itself. ──\n");
                s.Append(firebase ? "    private static string _adid, _firebaseId;\n" : "    private static string _adid;\n");
                s.Append("\n");
                s.Append("    // Hands the genre the Adjust ID and app token" + (firebase ? ", and the Firebase app instance ID," : "") +
                         " once known.\n");
                s.Append("    private static void SyncIds()\n");
                s.Append("    {\n");
                s.Append("        if (_genre == null) return;\n");
                s.Append("\n");
                s.Append("        var adid = AdjustAnalyticsNetwork.AdId;\n");
                s.Append("        if (!string.IsNullOrEmpty(adid) && adid != _adid)\n");
                s.Append("        {\n");
                s.Append("            _adid = adid;\n");
                s.Append("            _genre.UpdateAdjustInfo(adid, AdjustAnalyticsNetwork.AppToken);\n");
                s.Append("        }\n");
                if (firebase)
                {
                    s.Append("\n");
                    s.Append("        var firebaseId = FirebaseAnalyticsNetwork.AppInstanceId;\n");
                    s.Append("        if (!string.IsNullOrEmpty(firebaseId) && firebaseId != _firebaseId)\n");
                    s.Append("        {\n");
                    s.Append("            _firebaseId = firebaseId;\n");
                    s.Append("            _genre.UpdateFirebaseInfo(firebaseId);\n");
                    s.Append("        }\n");
                }
                s.Append("    }\n");
            }

            s.Append("}\n");
            return s.ToString();
        }

        // ── Reading the project ────────────────────────────────────────────────

        private static bool GenreReady(string game) =>
            GenreFilesStep.NameError(game) == null && MeticaPaths.FileExists(GenreFilesStep.AssetPath(game));

        /// <summary>The script is there, for this genre.</summary>
        private static bool Written(string game) =>
            MeticaPaths.FileExists(ScriptPath)
            && SourcePatcher.ReadAll(ScriptPath).Contains($"Resources.Load<{game}Analytics>(");

        /// <summary>
        /// Where an earlier version of the tool integrated this project, or null. Those projects
        /// are left as they are: this flow's new shape is for new integrations only.
        /// </summary>
        internal static string EarlierIntegration
        {
            get
            {
                if (MeticaPaths.FileExists(OldScriptPath)) return OldScriptPath;
                if (!MeticaPaths.HasGDSdk) return null;

                if (SourcePatcher.Contains(MeticaPaths.AnalyticsManager, OldInitMarker))
                    return "genre code in " + Path.GetFileName(MeticaPaths.AnalyticsManager);
                if (SourcePatcher.Contains(MeticaPaths.AdjustAnalyticsNetwork, AdjustWiringStep.AfterMarker))
                    return "genre code in " + Path.GetFileName(MeticaPaths.AdjustAnalyticsNetwork);
                if (SourcePatcher.Contains(FirebaseWiringStep.GdFirebaseFile, FirebaseWiringStep.Marker))
                    return "genre code in " + Path.GetFileName(FirebaseWiringStep.GdFirebaseFile);
                return null;
            }
        }

        /// <summary>GD SDK: the steps whose lines the script reads, while they're still missing.</summary>
        private static List<string> MissingSdkLines()
        {
            var missing = new List<string>();
            var adjust = MeticaPaths.AdjustAnalyticsNetwork;
            if (!MeticaPaths.FileExists(adjust) || AdjustWiringStep.GdMissing(adjust).Count > 0)
                missing.Add(AdjustWiringStep.StepTitle);
            if (FirebaseWiringStep.HasFirebaseAnalytics && FirebaseWiringStep.GdMissing().Count > 0)
                missing.Add(FirebaseWiringStep.StepTitle);
            return missing;
        }

        private static readonly Dictionary<string, (DateTime written, string[] code)> Cache =
            new Dictionary<string, (DateTime, string[])>();

        /// <summary>
        /// Where the game's own code already starts analytics: "file — what" for every script
        /// outside the genre folder, this step's script and the SDKs that calls Initialize on a genre
        /// analytics object, LogInstallEvent or LogSessionStartEvent.
        /// </summary>
        private static List<string> GameCodeCalls()
        {
            var found = new List<string>();
            var assets = Application.dataPath;
            var skipped = new[]
            {
                MeticaPaths.GenresRoot + "/", ScriptFolder + "/", MeticaPaths.MeticaSdkRoot + "/", "Assets/GDMonetization/"
            };

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
