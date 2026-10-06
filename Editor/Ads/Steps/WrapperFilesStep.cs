using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
            "MRec units, MeticaConfiguration and MeticaConsentSettings. MeticaInitializer is for GD SDK v5 " +
            "only: it is skipped when the project already has one, or an AdNetworkMetica of its own. " +
            "AdNetworkMetica implements the " +
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
            if (InitializerSkipReason() == null)
                yield return ("MeticaInitializer.cs.txt", InitializerTarget);
            yield return ("MeticaConfiguration.cs.txt", MeticaPaths.Combine(scripts, "Configurations/MeticaConfiguration.cs"));
            yield return ("MeticaConsentSettings.cs.txt", MeticaPaths.Combine(scripts, "Consent/Services/MeticaConsentSettings.cs"));
        }

        public override IEnumerable<string> TouchedPaths => Files().Select(file => file.target);

        public override string ReviewHint => "New files only (eight, or seven without MeticaInitializer), nothing modified.";

        // ── MeticaInitializer: v5 only ─────────────────────────────────────────

        private static string InitializerTarget => MeticaPaths.Combine(MeticaPaths.AdsMeticaFolder, "MeticaInitializer.cs");

        private static string AdNetworkMeticaPath => MeticaPaths.Combine(MeticaPaths.AdsMeticaFolder, "AdNetworkMetica.cs");

        private static readonly Regex DeclaresInitializer = new Regex(@"\b(?:class|struct)\s+MeticaInitializer\b");

        /// <summary>
        /// Why the tool's MeticaInitializer is not written, or null when it is. Only the
        /// tool's own AdNetworkMetica calls it, so it is left out when the project already has
        /// a MeticaInitializer (v6.2.x ships Analytics.MeticaInitializer — a second one in
        /// Monetization.Runtime.Ads shadows it for the SDK's AdNetworkMetica, which then never
        /// initializes Metica), or an AdNetworkMetica of its own (v6.0.x initializes inline).
        /// Once the tool's file exists it stays listed, so the step can report it.
        /// </summary>
        internal static string InitializerSkipReason()
        {
            if (MeticaPaths.FileExists(InitializerTarget)) return null;

            var existing = OtherInitializer();
            if (existing != null)
                return $"MeticaInitializer.cs skipped — the project already has one ({existing}).";

            if (MeticaPaths.FileExists(AdNetworkMeticaPath)
                && !SourcePatcher.ReadAll(AdNetworkMeticaPath).Contains("InitializeAdsWithCallback"))
                return "MeticaInitializer.cs skipped — the project's own AdNetworkMetica initializes Metica itself.";

            return null;
        }

        /// <summary>A script other than the tool's that declares MeticaInitializer, in any namespace; or null.</summary>
        private static string OtherInitializer()
        {
            try
            {
                return AssetDatabase.FindAssets("MeticaInitializer t:MonoScript")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => path.EndsWith(".cs") && path != InitializerTarget)
                    .FirstOrDefault(path => MeticaPaths.FileExists(path)
                                            && DeclaresInitializer.IsMatch(SourcePatcher.ReadAll(path)));
            }
            catch
            {
                return null; // no asset database (outside the editor)
            }
        }

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

            var skipped = InitializerSkipReason();
            if (skipped != null) result.Note(skipped);

            // The tool's MeticaInitializer next to an async AdNetworkMetica: that one is the
            // SDK's (v6), it resolves "MeticaInitializer" to the tool's class in its own
            // namespace instead of Analytics.MeticaInitializer, and Metica never initializes.
            if (MeticaPaths.FileExists(InitializerTarget)
                && MeticaPaths.FileExists(AdNetworkMeticaPath)
                && SourcePatcher.ReadAll(AdNetworkMeticaPath).Contains("IAsyncAdNetworkService"))
            {
                result.Problem($"{InitializerTarget} shadows the SDK's MeticaInitializer — delete it (and its .meta).");
                result.Problem("The SDK's async AdNetworkMetica is in Monetization.Runtime.Ads, so it resolves " +
                               "MeticaInitializer to this tool-written class in the same namespace instead of " +
                               "Analytics.MeticaInitializer. It compiles, but Metica never initializes. The tool's " +
                               "MeticaInitializer is for GD SDK v5 only.");
                return result.Seal();
            }

            // The wrapper cannot compile until the patch step supplies AdPlatforms.METICA, Tag.Metica
            // and MonetizationConfigurationsPath.Metica, so a missing type here is expected
            // beforehand and only worth reporting once the patch step has run.
            // Right after the patch step Unity has not compiled it yet: wait, don't fail.
            if (SourcePatcher.Contains(MeticaPaths.AdPlatforms, "METICA")
                && !TemplateWriter.TypeIsLoaded("Monetization.Runtime.Configurations.MeticaConfiguration"))
            {
                if (ScriptCompile.Pending(MeticaPaths.RuntimeScripts)) result.Wait();
                else result.Problem("Wrapper files haven't compiled — check the Console.");
            }

            return result.Seal();
        }

        public override void Apply()
        {
            var messages = TemplateWriter.WriteAll(Files(), _overwrite, out _);
            MeticaIntegrationLog.Record(Title, messages);

            var skipped = InitializerSkipReason();
            if (skipped != null) MeticaIntegrationLog.Record(Title, skipped);
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            yield return new StepToggle("Overwrite existing files", _overwrite, value => _overwrite = value);
        }
    }
}
