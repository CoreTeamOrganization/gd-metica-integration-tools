using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Moves the Metica on/off switch from a build-time flag to remote config.
    ///
    /// <para>Only v6.0.0–v6.2.0 need this. Those releases read the flag straight off
    /// SDKConfiguration, so turning Metica off meant shipping a build. v6.2.1 changed it to
    /// MonetizationPreferences.UseMetica, fed from RemoteConfiguration — the patch step already adds
    /// that plumbing, so all that is left here is to make AdsManager read it.</para>
    ///
    /// <para>On v5 (AdsManager was written fresh by the patch step) and on v6.2.1+ (already remote)
    /// there is nothing to do.</para>
    /// </summary>
    public sealed class RemoteSwitchStep : MeticaStep
    {
        /// <summary>The two names the build-time flag went by before it became remote.</summary>
        private static readonly string[] LegacyFlags = { "sdkConfig.UseMeticaForAds", "sdkConfig.UseMetica" };

        private const string RemoteRead = "MonetizationPreferences.UseMetica.Get()";

        public override string Title => "Remote Metica switch";

        public override string Summary => "Read the Metica on/off switch from remote config.";

        public override string Why =>
            "v6.0.0–v6.2.0 read the flag off SDKConfiguration, so turning Metica off meant shipping a " +
            "build. This points AdsManager at MonetizationPreferences.UseMetica (filled from remote config " +
            "by the patch step) and removes the dead SDKConfiguration field. The flag applies on the next " +
            "launch — ads start about 2s in, before the fetch lands. Nothing to do on v5 or v6.2.1+.";

        public override string ActionLabel => "Switch to the remote flag";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.AdsManager,
            MeticaPaths.Combine(MeticaPaths.RuntimeScripts, "Configurations/SDKConfiguration.cs")
        };

        public override string ReviewHint =>
            "AdsManager reads MonetizationPreferences.UseMetica.Get(); the SDKConfiguration field is gone.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (!SourcePatcher.Exists(MeticaPaths.AdsManager))
            {
                result.Problem("AdsManager.cs not found.");
                return result.Seal();
            }

            if (SourcePatcher.Contains(MeticaPaths.AdsManager, RemoteRead))
            {
                result.Note("Already remote");
                return result.Seal();
            }

            var legacy = LegacyFlags.FirstOrDefault(flag => SourcePatcher.Contains(MeticaPaths.AdsManager, flag));

            if (legacy == null)
            {
                result.Note("Nothing to switch");
                return result.Seal();
            }

            result.Problem($"AdsManager still reads {legacy}.");

            return result.Seal();
        }

        public override void Apply()
        {
            MeticaIntegrationLog.Record(Title, ApplyPatches());
            AssetDatabase.Refresh();
        }

        /// <summary>The edits themselves, through <see cref="SourcePatcher"/> only.</summary>
        internal static List<string> ApplyPatches()
        {
            var log = new List<string>();
            var path = MeticaPaths.AdsManager;

            var legacy = LegacyFlags.FirstOrDefault(flag => SourcePatcher.Contains(path, flag));
            if (legacy == null)
            {
                log.Add("No build-time Metica flag in AdsManager — nothing to do.");
                return log;
            }

            // The SDKConfiguration load exists only to read that flag.
            var loadOutcome = SourcePatcher.ReplaceFirst(path, RemoteRead,
                "var sdkConfig = Resources.Load<SDKConfiguration>(MonetizationConfigurationsPath.SDK);",
                "var useMetica = MonetizationPreferences.UseMetica.Get();");

            log.Add(SourcePatcher.IsSatisfied(loadOutcome)
                ? "AdsManager now reads MonetizationPreferences.UseMetica"
                : "Could not find the SDKConfiguration load in AdsManager — replace it by hand with: " +
                  "var useMetica = MonetizationPreferences.UseMetica.Get();");

            // Then every use of the old flag becomes the local.
            var replaced = SourcePatcher.ReplaceEvery(path, legacy, "useMetica");
            if (replaced > 0) log.Add($"Replaced {replaced} use(s) of {legacy} with useMetica");

            // And the field it pointed at is now dead weight: leaving it invites someone to
            // toggle it and wonder why nothing happens.
            var field = MeticaPaths.Combine(MeticaPaths.RuntimeScripts, "Configurations/SDKConfiguration.cs");
            var name = legacy.Substring("sdkConfig.".Length);
            var removed = SourcePatcher.RemoveLinesContaining(field, $"public bool {name}");
            if (removed > 0) log.Add($"Removed the dead {name} field from SDKConfiguration");

            return log;
        }
    }
}
