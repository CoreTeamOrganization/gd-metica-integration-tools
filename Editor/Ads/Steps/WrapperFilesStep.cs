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

        public override string Summary => "Add the Metica ads wrapper scripts.";

        public override string Why =>
            "Eight new files: AdNetworkMetica, MeticaInitializer, the Interstitial / Rewarded / Banner / " +
            "MRec units, MeticaConfiguration and MeticaConsentSettings. AdNetworkMetica implements the " +
            "same IAdNetworkService Admob and AppLovin use, so those stay untouched. Compile errors right " +
            "after this step are expected — the next step adds the symbols they use.";

        public override string ActionLabel => "Write wrapper files";

        private bool _overwrite;

        internal static IEnumerable<(string template, string target)> Files()
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

        public override string ReviewHint => "Eight new files, nothing modified.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.RuntimeScripts == null)
            {
                result.Problem("GD SDK not found.");
                return result.Seal();
            }

            var files = Files().ToList();
            var missing = files.Count(file => !MeticaPaths.FileExists(file.target));
            if (missing > 0)
            {
                result.Problem($"{missing} of {files.Count} wrapper files missing.");
                return result.Seal();
            }

            result.Note($"{files.Count} files present");

            // The wrapper cannot compile until the patch step supplies AdPlatforms.METICA, Tag.Metica
            // and MonetizationConfigurationsPath.Metica, so a missing type here is expected
            // beforehand and only worth reporting once the patch step has run.
            if (SourcePatcher.Contains(MeticaPaths.AdPlatforms, "METICA")
                && !TemplateWriter.TypeIsLoaded("Monetization.Runtime.Configurations.MeticaConfiguration"))
            {
                result.Problem("Wrapper files haven't compiled — check the Console.");
            }

            return result.Seal();
        }

        public override void Apply()
        {
            var messages = TemplateWriter.WriteAll(Files(), _overwrite, out _);
            MeticaIntegrationLog.Record(Title, messages);
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            yield return new StepToggle("Overwrite existing files", _overwrite, value => _overwrite = value);
        }
    }
}
