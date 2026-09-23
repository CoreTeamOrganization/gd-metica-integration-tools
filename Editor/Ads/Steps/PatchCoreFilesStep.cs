using System.Collections.Generic;
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

        public override string Summary =>
            "Ads-layer edits: AdPlatforms.METICA, Tag.Metica, the MeticaSettings resource path, the " +
            "AdRevenueInfo payload, the AdUnits Metica section, the UseMetica preference and remote flag, " +
            "and the AdsManager network switch. Plus one analytics-layer edit: dispatches " +
            "OnAdRevenuePaidEvent onto the main thread. AdNetworkController, AdNetworkAdmob and " +
            "AdNetworkAppLovin are not touched.";

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
            "This is the step worth reading line by line. Ten files, all additive. Ads/Core/ must show " +
            "NO diff at all — if AdNetworkController.cs or IAdNetworkService.cs changed, something is " +
            "wrong.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.RuntimeScripts == null)
            {
                result.Problem("GD Monetization SDK root not resolved — go back to the Metica SDK step.");
                return result.Seal();
            }

            Check(result, MeticaPaths.AdPlatforms, MarkerPlatform, "AdPlatforms.METICA");
            CheckEnum(result, MeticaPaths.LoggerTag, "Metica", "Tag.Metica");
            Check(result, MeticaPaths.ConfigurationsPath, MarkerPath, "MonetizationConfigurationsPath.Metica");
            Check(result, MeticaPaths.AdRevenueInfo, MarkerRevenueField, "AdRevenueInfo.RevenuePayload field");
            Check(result, MeticaPaths.AdRevenueInfo, MarkerRevenueParam, "AdRevenueInfo constructor payload parameter");
            Check(result, MeticaPaths.AdRevenueInfo, MarkerRevenueAssign, "AdRevenueInfo payload assignment");
            Check(result, MeticaPaths.AdUnitsConfiguration, MarkerAdUnits, "AdUnitsConfiguration.Metica");
            Check(result, MeticaPaths.Preferences, MarkerPreference, "MonetizationPreferences.UseMetica");
            Check(result, MeticaPaths.RemoteConfiguration, MarkerRemoteFlag, "RemoteConfiguration.UseMetica");
            Check(result, MeticaPaths.RemoteConfigManager, MarkerPersistHook, "the PersistRemoteToggles subscription");
            Check(result, MeticaPaths.RemoteConfigManager, MarkerPersistMethod, "the PersistRemoteToggles method");
            Check(result, MeticaPaths.AdsManager, MarkerAdsManager, "the AdsManager Metica network");
            Check(result, MeticaPaths.AnalyticsManager, MarkerRevenueDispatch,
                "the OnAdRevenuePaidEvent thread dispatch");

            if (result.Problems.Count == 0)
                result.Note("All ads-layer edits in place");

            return result.Seal();
        }

        public override void Apply()
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

            Run(log, "MeticaSettings resource path",
                SourcePatcher.InsertAfterLine(MeticaPaths.ConfigurationsPath, MarkerPath,
                    "string InApps",
                    "        public static readonly string Metica = \"Configurations/MeticaSettings\";"),
                MeticaPaths.ConfigurationsPath,
                "public static readonly string Metica = \"Configurations/MeticaSettings\";");

            PatchAdRevenueInfo(log);
            PatchAdUnitsConfiguration(log);
            PatchPreferences(log);
            PatchRemoteConfig(log);
            PatchAdsManager(log);
            PatchAnalyticsManager(log);

            MeticaIntegrationLog.Record(Title, log);
            AssetDatabase.Refresh();
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "Originals are copied to <project>/MeticaIntegrationBackups/ before the first change.\n\n" +
                "Any edit whose anchor is missing is reported rather than guessed — the message tells you " +
                "what to add by hand.",
                MessageType.Info);
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

        private static void PatchPreferences(List<string> log)
        {
            Run(log, "MonetizationPreferences.UseMetica",
                SourcePatcher.InsertAfterLine(MeticaPaths.Preferences, MarkerPreference,
                    "RestorePurchaseOnce",
                    "    public static readonly Preferences<bool> UseMetica = new Preferences<bool>(\"GDPrefs_UseMetica\", false);"),
                MeticaPaths.Preferences,
                "add a UseMetica preference keyed \"GDPrefs_UseMetica\"");
        }

        private static void PatchRemoteConfig(List<string> log)
        {
            Run(log, "RemoteConfiguration.UseMetica",
                SourcePatcher.InsertAfterLine(MeticaPaths.RemoteConfiguration, MarkerRemoteFlag,
                    "NextInterstitialDelay", "        public bool UseMetica;"),
                MeticaPaths.RemoteConfiguration, "add: public bool UseMetica;");

            Run(log, "PersistRemoteToggles subscription",
                SourcePatcher.InsertAfterLine(MeticaPaths.RemoteConfigManager, MarkerPersistHook,
                    "+= CreateAndUpdateConfig;",
                    "            OnFetchCompleteWithSuccess += PersistRemoteToggles;"),
                MeticaPaths.RemoteConfigManager,
                "subscribe PersistRemoteToggles alongside CreateAndUpdateConfig");

            Run(log, "PersistRemoteToggles method",
                SourcePatcher.InsertBeforeLine(MeticaPaths.RemoteConfigManager, MarkerPersistMethod,
                    "public static void AddOrUpdateValue",
                    "        static void PersistRemoteToggles(bool success, string message)\n" +
                    "        {\n" +
                    "            if (!success || m_Configuration == null) return;\n" +
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
            Run(log, "OnAdRevenuePaidEvent thread dispatch",
                SourcePatcher.ReplaceFirst(MeticaPaths.AnalyticsManager, MarkerRevenueDispatch,
                    "OnAdRevenuePaidEvent?.Invoke(adRevenueInfo);",
                    "ThreadDispatcher.Enqueue(() => OnAdRevenuePaidEvent?.Invoke(adRevenueInfo));"),
                MeticaPaths.AnalyticsManager,
                "wrap the OnAdRevenuePaidEvent invoke in ThreadDispatcher.Enqueue(() => ...)");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void Run(List<string> log, string what, O outcome, string path, string manualHint)
        {
            log.Add(outcome == O.AnchorNotFound
                ? $"{what}: anchor not found in {path}. Apply by hand — {manualHint}."
                : SourcePatcher.Describe(outcome, path, manualHint));
        }

        private static void Check(VerifyResult result, string path, string marker, string what)
        {
            if (!MeticaPaths.FileExists(path))
                result.Problem($"Not found: {path}");
            else if (!SourcePatcher.Contains(path, marker))
                result.Problem($"{what} is missing from {path}");
        }

        private static void CheckEnum(VerifyResult result, string path, string member, string what)
        {
            if (!MeticaPaths.FileExists(path))
                result.Problem($"Not found: {path}");
            else if (!SourcePatcher.Contains(path, member))
                result.Problem($"{what} is missing from {path}");
        }
    }
}
