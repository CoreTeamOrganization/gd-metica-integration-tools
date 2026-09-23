using System.Linq;
using System.Collections.Generic;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Writes the GDMonetization-side Metica ads wrapper: the ad network, the init gate,
    /// the four ad units, the config ScriptableObject and the consent service. All new
    /// files, so there is nothing to merge. Nothing here touches analytics.
    /// </summary>
    public sealed class WrapperFilesStep : MeticaStep
    {
        public override string Title => "Wrapper files";

        public override string Summary =>
            "Adds AdNetworkMetica, MeticaInitializer, the Interstitial / Rewarded / Banner / MRec units, " +
            "MeticaConfiguration and MeticaConsentSettings.";

        public override string ActionLabel => "Write wrapper files";

        private bool _overwrite;

        private static IEnumerable<(string template, string target)> Files()
        {
            var ads = MeticaPaths.AdsMeticaFolder;
            var scripts = MeticaPaths.RuntimeScripts;

            yield return ("AdNetworkMetica.cs.txt", MeticaPaths.Combine(ads, "AdNetworkMetica.cs"));
            yield return ("MeticaInterstitial.cs.txt", MeticaPaths.Combine(ads, "MeticaInterstitial.cs"));
            yield return ("MeticaRewarded.cs.txt", MeticaPaths.Combine(ads, "MeticaRewarded.cs"));
            yield return ("MeticaBanner.cs.txt", MeticaPaths.Combine(ads, "MeticaBanner.cs"));
            yield return ("MeticaMRec.cs.txt", MeticaPaths.Combine(ads, "MeticaMRec.cs"));
            yield return ("MeticaInitializer.cs.txt", MeticaPaths.Combine(ads, "MeticaInitializer.cs"));
            yield return ("MeticaConfiguration.cs.txt", MeticaPaths.Combine(scripts, "Configurations/MeticaConfiguration.cs"));
            yield return ("MeticaConsentSettings.cs.txt", MeticaPaths.Combine(scripts, "Consent/Services/MeticaConsentSettings.cs"));
        }

        public override IEnumerable<string> TouchedPaths => Files().Select(file => file.target);

        public override string ReviewHint =>
            "Eight new files, nothing modified. AdNetworkMetica should implement IAdNetworkService and " +
            "go through MeticaInitializer.InitializeAdsWithCallback.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.RuntimeScripts == null)
            {
                result.Problem("GD Monetization SDK root not resolved — go back to the Metica SDK step.");
                return result.Seal();
            }

            foreach (var (_, target) in Files())
                if (!MeticaPaths.FileExists(target))
                    result.Problem($"Missing {target}");

            if (result.Problems.Count > 0) return result.Seal();

            result.Note("All 8 wrapper files present");

            // The wrapper cannot compile until the patch step supplies AdPlatforms.METICA, Tag.Metica
            // and MonetizationConfigurationsPath.Metica, so a missing type here is expected
            // beforehand and only worth reporting once the patch step has run.
            if (SourcePatcher.Contains(MeticaPaths.AdPlatforms, "METICA")
                && !TemplateWriter.TypeIsLoaded("Monetization.Runtime.Configurations.MeticaConfiguration"))
            {
                result.Problem("The wrapper files are in place but have not compiled. Check the Console — " +
                               "if the errors mention AdPlatforms, Tag or MonetizationConfigurationsPath, " +
                               "finish the patch step.");
            }

            return result.Seal();
        }

        public override void Apply()
        {
            var messages = TemplateWriter.WriteAll(Files(), _overwrite, out _);
            MeticaIntegrationLog.Record(Title, messages);
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "AdNetworkMetica implements the plain IAdNetworkService that Admob and AppLovin already " +
                "use, so AdNetworkController and the two other ad networks stay untouched. " +
                "MeticaInitializer is the single guard against initializing twice.\n\n" +
                "Compile errors right after this step are expected — step 4 adds the symbols they use.",
                MessageType.Info);

            _overwrite = EditorGUILayout.ToggleLeft(
                "Overwrite files that already exist (off keeps local edits)", _overwrite);
        }
    }
}
