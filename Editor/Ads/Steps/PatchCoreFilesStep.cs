using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// <para>Two analytics-file edits. AdjustAnalyticsNetwork.GetAdSource gets a METICA case —
    /// without it every Metica revenue event goes to Adjust with a null source and Adjust
    /// drops it. And AnalyticsManager.ReportAdRevenue keeps firing OnAdRevenuePaidEvent
    /// directly: a ThreadDispatcher wrap an earlier version of this tool added is undone.</para>
    /// </summary>
    public sealed class PatchCoreFilesStep : MeticaStep
    {
        public override string Title => "Patch the existing SDK files";

        public override string Summary => "Patch the GD SDK's existing files for Metica.";

        public override string Why =>
            "Additions only: AdPlatforms.METICA, Tag.Metica, the MeticaSettings resource path, the " +
            "AdRevenueInfo payload, the AdUnits Metica section, the UseMetica preference and remote flag, " +
            "the AdsManager network switch, a METICA case in AdjustAnalyticsNetwork.GetAdSource " +
            "(\"applovin_max_sdk\" — without it Adjust drops Metica revenue), and on GD SDK 5.0–5.2 " +
            "MeticaConsentSettings in ConsentManager's fixed consent list. OnAdRevenuePaidEvent stays " +
            "invoked directly — an older " +
            "ThreadDispatcher wrap around it is undone. AdNetworkController, AdNetworkAdmob and " +
            "AdNetworkAppLovin are not touched. Originals are " +
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
        /// <summary>The wrap an earlier version of this tool added; it must not be there.</summary>
        private const string WrappedRevenueDispatch = "ThreadDispatcher.Enqueue(() => OnAdRevenuePaidEvent?.Invoke(adRevenueInfo));";
        private const string DirectRevenueDispatch = "OnAdRevenuePaidEvent?.Invoke(adRevenueInfo);";

        /// <summary>Metica mediates through MAX, so Adjust gets MAX's ad source.</summary>
        private const string MeticaAdSource = "AdPlatforms.METICA => \"applovin_max_sdk\",";
        private const string AdSourceAnchor = "AdPlatforms.APPLOVIN =>";

        /// <summary>GD SDK 5.0–5.2 only: its consent services are a fixed list, with no way to add one.</summary>
        private const string MarkerConsent = "new MeticaConsentSettings()";
        private const string ConsentAnchor = "new AdjustConsentSettings(),";

        /// <summary>Before 5.3.0 ConsentManager has no AddAndUpdateConsentService.</summary>
        private static bool ConsentListIsFixed =>
            SourcePatcher.Exists(MeticaPaths.ConsentManager)
            && !SourcePatcher.Contains(MeticaPaths.ConsentManager, "AddAndUpdateConsentService");

        /// <summary>
        /// AdPlatforms.METICA mapped to a string, in a switch expression (METICA => "…") or a
        /// switch statement (case METICA: return "…";). Any non-empty string counts — a game may
        /// have mapped it by hand. Run on code with comments stripped.
        /// </summary>
        private static readonly Regex MeticaMapped =
            new Regex(@"AdPlatforms\.METICA\s*(?:=>|:\s*return)\s*""[^""]+""");

        /// <summary>AdPlatforms.METICA mapped to null, or to "".</summary>
        private static readonly Regex MeticaMappedToNothing =
            new Regex(@"AdPlatforms\.METICA\s*(?:=>|:\s*return)\s*(?:null\b|"""")");

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
            MeticaPaths.AnalyticsManager,
            MeticaPaths.AdjustAnalyticsNetwork,
            MeticaPaths.ConsentManager
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
                (MeticaPaths.AdsManager, MarkerAdsManager, "AdsManager Metica network")
            };

            var missing = checks
                .Where(c => !SourcePatcher.Contains(c.path, c.marker))
                .Select(c => c.what)
                .ToList();

            if (RevenueDispatchWrapped())
                missing.Add("OnAdRevenuePaidEvent is inside ThreadDispatcher — it has to be invoked directly");

            if (ConsentListIsFixed && !SourcePatcher.Contains(MeticaPaths.ConsentManager, MarkerConsent))
                missing.Add("ConsentManager's consent list has no MeticaConsentSettings");

            if (!AdjustKnowsMetica())
                missing.Add("AdPlatforms.METICA case in AdjustAnalyticsNetwork.GetAdSource — Adjust drops Metica revenue without it");

            if (missing.Count == 0)
            {
                result.Note("All patches in place");
                return result.Seal();
            }

            // One count up front; the list itself goes under "Why?" as the extra problems.
            // The direct-invoke rule and the Adjust ad source count as patches too.
            result.Problem($"{missing.Count} of {checks.Length + 2 + (ConsentListIsFixed ? 1 : 0)} patches missing.");
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
            PatchAdjustAnalyticsNetwork(log);

            // GD SDK 5.0–5.2: consent changes reach only the services in ConsentManager's fixed
            // list. The Pre530 AdNetworkMetica applies consent once Metica is up; this keeps it
            // updated when the player changes it later.
            if (ConsentListIsFixed)
                Run(log, "ConsentManager Metica consent",
                    SourcePatcher.InsertAfterLine(MeticaPaths.ConsentManager, MarkerConsent, ConsentAnchor,
                        "            new MeticaConsentSettings(),"),
                    MeticaPaths.ConsentManager,
                    "add new MeticaConsentSettings(), to the ConsentServices list");

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
        /// OnAdRevenuePaidEvent is invoked directly, never inside ThreadDispatcher.Enqueue. An
        /// earlier version of this tool added that wrap; it is undone here, on live code lines
        /// only — a commented-out copy is left as it is.
        /// </summary>
        private static void PatchAnalyticsManager(List<string> log)
        {
            if (!RevenueDispatchWrapped()) return;

            var path = MeticaPaths.AnalyticsManager;
            var text = SourcePatcher.ReadAll(path);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n')
                .Select(line => line.TrimStart().StartsWith(WrappedRevenueDispatch)
                    ? line.Replace(WrappedRevenueDispatch, DirectRevenueDispatch)
                    : line);

            SourcePatcher.WriteWithBackup(path, string.Join(newline, lines));
            log.Add($"{path}: OnAdRevenuePaidEvent is invoked directly again (removed the ThreadDispatcher wrap)");
        }

        /// <summary>A live (not commented-out) line still wraps OnAdRevenuePaidEvent in ThreadDispatcher.</summary>
        private static bool RevenueDispatchWrapped() =>
            SourcePatcher.Exists(MeticaPaths.AnalyticsManager)
            && SourcePatcher.ReadAll(MeticaPaths.AnalyticsManager).Replace("\r\n", "\n").Split('\n')
                .Any(line => line.TrimStart().StartsWith(WrappedRevenueDispatch));

        /// <summary>
        /// AdjustAnalyticsNetwork.GetAdSource maps APPLOVIN and ADMOB and returns null for
        /// anything else, and Adjust drops a revenue event with a null source. Metica mediates
        /// through MAX, so METICA gets MAX's source, on the line after APPLOVIN's.
        ///
        /// <para>Only the stock shape is edited: the APPLOVIN arm has to sit inside
        /// GetAdSource's switch, ahead of its "_ =>" default. Anything else — or METICA already
        /// mapped to null — is reported with the line to add, never guessed at.</para>
        /// </summary>
        private static void PatchAdjustAnalyticsNetwork(List<string> log)
        {
            const string what = "Adjust ad source for Metica";
            var path = MeticaPaths.AdjustAnalyticsNetwork;
            var hint = $"in GetAdSource, add {MeticaAdSource} after the AdPlatforms.APPLOVIN line";

            if (!SourcePatcher.Exists(path))
            {
                Run(log, what, O.FileMissing, path, hint);
                return;
            }

            if (AdjustKnowsMetica())
            {
                Run(log, what, O.AlreadyApplied, path, hint);
                return;
            }

            var text = SourcePatcher.ReadAll(path);
            if (MeticaMappedToNothing.IsMatch(WithoutComments(text)))
            {
                log.Add($"{what}: {path} maps AdPlatforms.METICA to no source, so Adjust drops Metica " +
                        "revenue. Change that line by hand to " + MeticaAdSource);
                return;
            }

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            var anchor = AdSourceAnchorLine(lines);
            if (anchor < 0)
            {
                Run(log, what, O.AnchorNotFound, path, hint);
                return;
            }

            // Same indentation as the APPLOVIN arm.
            var indent = lines[anchor].Substring(0, lines[anchor].Length - lines[anchor].TrimStart().Length);
            lines.Insert(anchor + 1, indent + MeticaAdSource);

            SourcePatcher.WriteWithBackup(path, string.Join(newline, lines));
            Run(log, what, O.Applied, path, hint);
        }

        /// <summary>
        /// The line of GetAdSource's APPLOVIN arm, or -1 unless the method has the stock shape:
        /// a switch expression whose APPLOVIN arm comes before its "_ =>" default.
        /// </summary>
        private static int AdSourceAnchorLine(List<string> lines)
        {
            var method = lines.FindIndex(line => line.Contains("GetAdSource(string"));
            if (method < 0) return -1;

            var anchor = -1;
            for (var i = method + 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//")) continue;
                if (line == "};") return -1; // end of the switch, no default seen
                if (anchor < 0 && line.StartsWith(AdSourceAnchor)) anchor = i;
                else if (line.StartsWith("_ =>")) return anchor;
            }

            return -1;
        }

        /// <summary>
        /// AdjustAnalyticsNetwork maps AdPlatforms.METICA to a real source — this tool's line or
        /// a mapping the game added itself.
        /// </summary>
        private static bool AdjustKnowsMetica() =>
            SourcePatcher.Exists(MeticaPaths.AdjustAnalyticsNetwork)
            && MeticaMapped.IsMatch(WithoutComments(SourcePatcher.ReadAll(MeticaPaths.AdjustAnalyticsNetwork)));

        private static readonly Regex Comments = new Regex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline);

        /// <summary>The code with // and /* */ comments blanked, so a commented-out mapping doesn't count.</summary>
        private static string WithoutComments(string code) => Comments.Replace(code, " ");

        // ── Helpers ────────────────────────────────────────────────────────────


        private static void Run(List<string> log, string what, O outcome, string path, string manualHint)
        {
            log.Add(outcome == O.AnchorNotFound
                ? $"{what}: anchor not found in {path}. Apply by hand — {manualHint}."
                : SourcePatcher.Describe(outcome, path, manualHint));
        }
    }
}
