using System.Collections.Generic;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Ads Integration's two step lists: one for a project with the GD Monetization SDK
    /// (Metica is wired into its ads layer), one for a project without it (a self-contained
    /// Metica ads runtime is written instead). Which one runs is decided on every refresh, so
    /// dropping the SDK in or taking it out switches the run on the next check.
    ///
    /// <para>In both, the integration comes first and the Gradle steps last: both are
    /// optional and neither can be judged until there is a build to judge.</para>
    /// </summary>
    internal static class AdsFlow
    {
        public const string Name = "Ads Integration";

        private static readonly MeticaStep[] GDSdkSteps =
        {
            new ImportMeticaSdkStep(),
            new ResolveLibrariesStep(),
            new WrapperFilesStep(),
            new PatchCoreFilesStep(),
            new RemoteSwitchStep(),
            new MeticaSettingsStep(),
            new AdUnitsStep(),
            new AsyncCleanupStep(),
            new FinishUpStep(),
            new DependenciesStep(),
            new GradleVersionStep(),
            new KotlinTemplateStep()
        };

        private static readonly MeticaStep[] StandaloneSteps =
        {
            new ImportMeticaSdkStep(),
            new ResolveLibrariesStep(),
            new StandaloneRuntimeStep(),
            new StandaloneConfigStep(),
            new DependenciesStep(),
            new GradleVersionStep(),
            new KotlinTemplateStep()
        };

        /// <summary>Every Ads step id, both lists — what Reset sign-offs clears.</summary>
        public static IEnumerable<string> AllStepIds => GDSdkSteps.Concat(StandaloneSteps).Select(s => s.Id);

        public static MeticaFlow Create() =>
            new MeticaFlow(Name, () => MeticaPaths.HasGDSdk ? GDSdkSteps : StandaloneSteps, () => AllStepIds);
    }
}
