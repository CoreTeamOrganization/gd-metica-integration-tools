using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using O = GameDistrict.MeticaIntegrationTools.SourcePatcher.Outcome;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The edits to files the SDK already has, almost all of them in the ads layer. Every
    /// edit is anchored to a line that exists in v5.5.0 and guarded by a marker, so running
    /// this twice changes nothing. When an anchor cannot be found the edit is reported instead
    /// of guessed, with the change to make by hand.
    ///
    /// <para>The one analytics-file edit: AnalyticsManager.ReportAdRevenue fires
    /// OnAdRevenuePaidEvent directly. Metica's revenue callbacks can land on a native thread,
    /// so a subscriber doing Unity API work there would fail intermittently — this step wraps
    /// the invoke in ThreadDispatcher.Enqueue, the same fix already applied elsewhere in the
    /// SDK.</para>
    /// </summary>
    public sealed class PatchCoreFilesStep : MeticaStep
    {
        public override string Title => "Patch the existing SDK files";

        public override string Summary => "Patch the GD SDK's existing files for Metica.";

        public override string Why =>
            "Additions only: AdPlatforms.METICA, Tag.Metica, the MeticaSettings resource path, the " +
            "AdRevenueInfo payload, the AdUnits Metica section, the UseMetica preference and remote flag, " +
            "the AdsManager network switch, and OnAdRevenuePaidEvent dispatched onto the main thread. " +
            "AdNetworkController, AdNetworkAdmob and AdNetworkAppLovin are not touched. Originals are " +
            "backed up to <project>/MeticaIntegrationBackups/ first; an edit whose anchor isn't found is " +
            "logged with what to add by hand rather than guessed.";

        public override string ActionLabel => "Apply patches";

        // ── Marker strings: presence means the edit is already in place ─────────

        private const string MarkerPlatform = "METICA = \"Metica\"";
        private const string MarkerPath = "\"Configurations/MeticaSettings\"";
        private const string MarkerRevenueField = "IReadOnlyDictionary<string, object> RevenuePayload;";
        private const string MarkerRevenueParam = "revenuePayload = null";
        private const string MarkerRevenueAssign = "RevenuePayload = revenuePayload";
        private const string MarkerAdUnits = "AdNetworkInfo Metica";
        private const string MarkerPreference = "GDPrefs_UseMetica";
        private const string MarkerRemoteFlag = "public bool UseMetica";
        private const string MarkerPersistHook = "PersistRemoteToggles;";
        private const string MarkerPersistMethod = "static void PersistRemoteToggles";
        private const string MarkerAdsManager = "new AdNetworkMetica()";
        private const string MarkerRevenueDispatch = "ThreadDispatcher.Enqueue(() => OnAdRevenuePaidEvent";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.AdPlatforms,
            MeticaPaths.LoggerTag,
            MeticaPaths.ConfigurationsPath,
            MeticaPaths.AdRevenueInfo,
            MeticaPaths.AdUnitsConfiguration,
            MeticaPaths.Preferences,
            MeticaPaths.RemoteConfiguration,
            MeticaPaths.RemoteConfigManager,
            MeticaPaths.AdsManager,
            MeticaPaths.AnalyticsManager
        };

        public override string ReviewHint =>
            "Read this one line by line. AdNetworkController.cs and IAdNetworkService.cs must not change.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.RuntimeScripts == null)
            {
                result.Problem("GD SDK not found.");
                return result.Seal();
            }

            var checks = new (string path, string marker, string what)[]
            {
                (MeticaPaths.AdPlatforms, MarkerPlatform, "AdPlatforms.METICA"),
                (MeticaPaths.LoggerTag, "Metica", "Tag.Metica"),
                (MeticaPaths.ConfigurationsPath, MarkerPath, "MonetizationConfigurationsPath.Metica"),
                (MeticaPaths.AdRevenueInfo, MarkerRevenueField, "AdRevenueInfo.RevenuePayload"),
                (MeticaPaths.AdRevenueInfo, MarkerRevenueParam, "AdRevenueInfo constructor parameter"),
                (MeticaPaths.AdRevenueInfo, MarkerRevenueAssign, "AdRevenueInfo payload assignment"),
                (MeticaPaths.AdUnitsConfiguration, MarkerAdUnits, "AdUnitsConfiguration.Metica"),
                (MeticaPaths.Preferences, MarkerPreference, "MonetizationPreferences.UseMetica"),
                (MeticaPaths.RemoteConfiguration, MarkerRemoteFlag, "RemoteConfiguration.UseMetica"),
                (MeticaPaths.RemoteConfigManager, MarkerPersistHook, "PersistRemoteToggles subscription"),
                (MeticaPaths.RemoteConfigManager, MarkerPersistMethod, "PersistRemoteToggles method"),
                (MeticaPaths.AdsManager, MarkerAdsManager, "AdsManager Metica network"),
                (MeticaPaths.AnalyticsManager, MarkerRevenueDispatch, "OnAdRevenuePaidEvent main-thread dispatch")
            };

            // A GD SDK version without the event has nothing to dispatch, so nothing to check.
            if (!HasRevenueEvent) checks = checks.Where(c => c.marker != MarkerRevenueDispatch).ToArray();

            var missing = checks
                .Where(c => !SourcePatcher.Contains(c.path, c.marker))
                .Select(c => c.what)
                .ToList();

            if (missing.Count == 0)
            {
                result.Note("All patches in place");
                return result.Seal();
            }

            // One count up front; the list itself goes under "Why?" as the extra problems.
            result.Problem($"{missing.Count} of {checks.Length} patches missing.");
            foreach (var what in missing)
                result.Problem($"Missing: {what}");

            return result.Seal();
        }

        public override void Apply()
        {
            MeticaIntegrationLog.Record(Title, ApplyPatches());
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// The edits themselves, through <see cref="SourcePatcher"/> only, so they run the same
        /// on disk or in memory (<see cref="MeticaPatchSet"/>). Returns what happened.
        /// </summary>
        internal static List<string> ApplyPatches()
        {
            var log = new List<string>();

            Run(log, "AdPlatforms.METICA",
                SourcePatcher.InsertAfterLine(MeticaPaths.AdPlatforms, MarkerPlatform,
                    "ADMOB", "        public const string METICA = \"Metica\";"),
                MeticaPaths.AdPlatforms,
                "public const string METICA = \"Metica\";");

            Run(log, "Tag.Metica",
                SourcePatcher.AppendEnumMember(MeticaPaths.LoggerTag, "Tag", "Metica"),
                MeticaPaths.LoggerTag, "add Metica to the Tag enum");

            // InApps only exists from v5.1.0; before that AppMetrica is the last path.
            Run(log, "MeticaSettings resource path",
                SourcePatcher.InsertAfterLine(MeticaPaths.ConfigurationsPath, MarkerPath,
                    SourcePatcher.Contains(MeticaPaths.ConfigurationsPath, "string InApps")
                        ? "string InApps"
                        : "string AppMetrica",
                    "        public static readonly string Metica = \"Configurations/MeticaSettings\";"),
                MeticaPaths.ConfigurationsPath,
                "public static readonly string Metica = \"Configurations/MeticaSettings\";");

            PatchAdRevenueInfo(log);
            PatchAdUnitsConfiguration(log);
            PatchPreferences(log);
            PatchRemoteConfig(log);
            PatchAdsManager(log);
            PatchAnalyticsManager(log);

            return log;
        }

        /// <summary>
        /// The async-init choice, only for a project that already has the async plumbing —
        /// anywhere else there is no choice to make. Changing it clears every sign-off, since
        /// the async cleanup step would then do something different from what was reviewed.
        /// </summary>
        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (!MeticaIntegrationMode.ProjectHasAsyncPath) yield break;

            yield return new StepChoice("Existing async init", new[] { "Callback", "Async" },
                MeticaIntegrationMode.IsCallback ? 0 : 1,
                selected =>
                {
                    var mode = selected == 0 ? InitMode.Callback : InitMode.Async;
                    if (mode == MeticaIntegrationMode.Current) return;

                    MeticaIntegrationMode.Current = mode;
                    MeticaIntegrationProgress.ClearAll(AdsFlow.AllStepIds.Distinct().ToArray());
                },
                MeticaIntegrationMode.IsCallback
                    ? "Callback: the async cleanup step removes it."
                    : "Async: the async cleanup step leaves it alone.");
        }

        // ── Individual patches ─────────────────────────────────────────────────

        private static void PatchAdRevenueInfo(List<string> log)
        {
            var path = MeticaPaths.AdRevenueInfo;
            SourcePatcher.EnsureUsing(path, "using System.Collections.Generic;");

            Run(log, "AdRevenueInfo payload field",
                SourcePatcher.InsertAfterLine(path, MarkerRevenueField,
                    "public readonly string AdPlacement;",
                    "        public readonly IReadOnlyDictionary<string, object> RevenuePayload; // Optional extra data (e.g. Metica user-group)"),
                path, "add a RevenuePayload field");

            Run(log, "AdRevenueInfo constructor parameter",
                SourcePatcher.ReplaceFirst(path, MarkerRevenueParam,
                    "double revenue, string adPlacement)",
                    "double revenue, string adPlacement, IReadOnlyDictionary<string, object> revenuePayload = null)"),
                path, "add an optional revenuePayload parameter to the constructor");

            Run(log, "AdRevenueInfo payload assignment",
                SourcePatcher.InsertAfterLine(path, MarkerRevenueAssign,
                    "AdPlacement = adPlacement;",
                    "            RevenuePayload = revenuePayload;"),
                path, "assign RevenuePayload in the constructor");
        }

        private static void PatchAdUnitsConfiguration(List<string> log)
        {
            Run(log, "AdUnitsConfiguration.Metica",
                SourcePatcher.InsertAfterLine(MeticaPaths.AdUnitsConfiguration, MarkerAdUnits,
                    "AdNetworkInfo Admob;", "        public AdNetworkInfo Metica;"),
                MeticaPaths.AdUnitsConfiguration, "add: public AdNetworkInfo Metica;");
        }

        /// <summary>
        /// Before v5.3.0 there is no RestorePurchaseOnce to sit after, and Preferences takes only
        /// a key — its Get() already defaults a bool to false, so the one-argument form behaves
        /// the same.
        /// </summary>
        private static void PatchPreferences(List<string> log)
        {
            var path = MeticaPaths.Preferences;
            var modern = SourcePatcher.Contains(path, "RestorePurchaseOnce");
            var takesDefault = SourcePatcher.Contains(path, "public Preferences(string key, T defaultValue)");

            Run(log, "MonetizationPreferences.UseMetica",
                SourcePatcher.InsertAfterLine(path, MarkerPreference,
                    modern ? "RestorePurchaseOnce" : "Preferences<int> SessionCount",
                    takesDefault
                        ? "    public static readonly Preferences<bool> UseMetica = new Preferences<bool>(\"GDPrefs_UseMetica\", false);"
                        : "    public static readonly Preferences<bool> UseMetica = new Preferences<bool>(\"GDPrefs_UseMetica\");"),
                path,
                "add a UseMetica preference keyed \"GDPrefs_UseMetica\"");
        }

        private static void PatchRemoteConfig(List<string> log)
        {
            Run(log, "RemoteConfiguration.UseMetica",
                SourcePatcher.InsertAfterLine(MeticaPaths.RemoteConfiguration, MarkerRemoteFlag,
                    "NextInterstitialDelay", "        public bool UseMetica;"),
                MeticaPaths.RemoteConfiguration, "add: public bool UseMetica;");

            // OnFetchCompleteWithSuccess (bool, string) arrived in v5.3.6. Before that the only
            // event is OnFetchComplete, with no arguments — the same one CreateAndUpdateConfig
            // uses there, which does not know about success either.
            var withSuccess = SourcePatcher.Contains(MeticaPaths.RemoteConfigManager, "OnFetchCompleteWithSuccess");

            Run(log, "PersistRemoteToggles subscription",
                SourcePatcher.InsertAfterLine(MeticaPaths.RemoteConfigManager, MarkerPersistHook,
                    "+= CreateAndUpdateConfig;",
                    withSuccess
                        ? "            OnFetchCompleteWithSuccess += PersistRemoteToggles;"
                        : "            OnFetchComplete += PersistRemoteToggles;"),
                MeticaPaths.RemoteConfigManager,
                "subscribe PersistRemoteToggles alongside CreateAndUpdateConfig");

            Run(log, "PersistRemoteToggles method",
                SourcePatcher.InsertBeforeLine(MeticaPaths.RemoteConfigManager, MarkerPersistMethod,
                    "public static void AddOrUpdateValue",
                    withSuccess
                        ? "        static void PersistRemoteToggles(bool success, string message)\n" +
                          "        {\n" +
                          "            if (!success || m_Configuration == null) return;\n" +
                          "            MonetizationPreferences.UseMetica.Set(m_Configuration.UseMetica);\n" +
                          "        }\n"
                        : "        static void PersistRemoteToggles()\n" +
                          "        {\n" +
                          "            if (m_Configuration == null) return;\n" +
                          "            MonetizationPreferences.UseMetica.Set(m_Configuration.UseMetica);\n" +
                          "        }\n"),
                MeticaPaths.RemoteConfigManager,
                "add a PersistRemoteToggles method that copies UseMetica into MonetizationPreferences");
        }

        private static void PatchAdsManager(List<string> log)
        {
            var path = MeticaPaths.AdsManager;
            SourcePatcher.EnsureUsing(path, "using System.Collections.Generic;");

            Run(log, "AdsManager Metica network",
                SourcePatcher.ReplaceFirst(path, MarkerAdsManager,
                    "adNetworks = new AdNetworkController[] { applovin, admob };",
                    "var metica = new AdNetworkController(new AdUnit[]\n" +
                    "            {\n" +
                    "                new MeticaInterstitial(),\n" +
                    "                new MeticaRewarded(),\n" +
                    "                new MeticaBanner(),\n" +
                    "                new MeticaMRec()\n" +
                    "            }, new AdNetworkMetica(), adUnits.Metica);\n" +
                    "\n" +
                    "            var useMetica = MonetizationPreferences.UseMetica.Get();\n" +
                    "            Message.LogWarning(Tag.SDK, \"Using \" + (useMetica ? \"Metica\" : \"AppLovin\") + \" as main AdNetwork\");\n" +
                    "\n" +
                    "            adNetworks = useMetica\n" +
                    "                ? new AdNetworkController[] { metica, admob }\n" +
                    "                : new AdNetworkController[] { applovin, admob };\n" +
                    "\n" +
                    "            AnalyticsManager.SendEvent(\"GameData\", new Dictionary<string, object>\n" +
                    "            {\n" +
                    "                { \"UseMetica\", useMetica }\n" +
                    "            });"),
                path,
                "build an AdNetworkController for the four Metica units and pick it when " +
                "MonetizationPreferences.UseMetica is set");
        }

        /// <summary>
        /// Metica's revenue callbacks can land on a native thread, so firing
        /// OnAdRevenuePaidEvent directly risks a subscriber doing Unity API work off the main
        /// thread. ThreadDispatcher already ships with the SDK (Dispatcher/ThreadDispatcher.cs)
        /// and AnalyticsManager.cs already has the Monetization.Runtime.Utilities using it
        /// lives in, so this is a one-line wrap, not a new dependency.
        /// </summary>
        private static void PatchAnalyticsManager(List<string> log)
        {
            // Only exists from v5.3.0. Without it there is no event to move onto the main thread.
            if (!HasRevenueEvent)
            {
                log.Add("OnAdRevenuePaidEvent is not in this GD SDK version — nothing to dispatch.");
                return;
            }

            Run(log, "OnAdRevenuePaidEvent thread dispatch",
                SourcePatcher.ReplaceFirst(MeticaPaths.AnalyticsManager, MarkerRevenueDispatch,
                    "OnAdRevenuePaidEvent?.Invoke(adRevenueInfo);",
                    "ThreadDispatcher.Enqueue(() => OnAdRevenuePaidEvent?.Invoke(adRevenueInfo));"),
                MeticaPaths.AnalyticsManager,
                "wrap the OnAdRevenuePaidEvent invoke in ThreadDispatcher.Enqueue(() => ...)");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>AnalyticsManager.OnAdRevenuePaidEvent was added in GD SDK v5.3.0.</summary>
        private static bool HasRevenueEvent =>
            SourcePatcher.Contains(MeticaPaths.AnalyticsManager, "event Action<AdRevenueInfo> OnAdRevenuePaidEvent");

        private static void Run(List<string> log, string what, O outcome, string path, string manualHint)
        {
            log.Add(outcome == O.AnchorNotFound
                ? $"{what}: anchor not found in {path}. Apply by hand — {manualHint}."
                : SourcePatcher.Describe(outcome, path, manualHint));
        }
    }
}
