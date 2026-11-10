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
    /// <para>In both, AppLovin MAX comes first (only when it is below 8.0.1 — Metica needs
    /// it), then the integration, and the optional Android build fixes last, in one
    /// checklist: none of them can be judged until there is a build to judge.</para>
    /// </summary>
    internal static class AdsFlow
    {
        public const string Name = "Ads Integration";

        // AppLovinMaxStep only shows while MAX is below 8.0.1 (MeticaStep.Applies). Resolving
        // Android libraries is part of the SDK steps themselves, not a step of its own.
        private static readonly MeticaStep[] GDSdkSteps =
        {
            new CompareSdkStep(),
            new AppLovinMaxStep(),
            new MeticaV1CodeStep(),
            new ImportMeticaSdkStep(),
            new WrapperFilesStep(),
            new GameAdditionsStep(),
            new PatchCoreFilesStep(),
            new RemoteSwitchStep(),
            new MeticaSettingsStep(),
            new AdUnitsStep(),
            new FinishUpStep(),
            new TroubleshootingStep(),
            new IntegrationReviewStep()
        };

        private static readonly MeticaStep[] StandaloneSteps =
        {
            new AppLovinMaxStep(),
            new MeticaV1CodeStep(),
            new ImportMeticaSdkStep(),
            new StandaloneRuntimeStep(),
            new StandaloneConfigStep(),
            new TroubleshootingStep(),

            // Optional patches of the game's own ads code — last, so the steps before stay reachable.
            new InitPatchStep(),
            new InterstitialPatchStep(),
            new RewardedPatchStep(),
            new BannerPatchStep(),
            new MRecPatchStep(),
            new RevenueLoggingStep(),

            // Optional, after everything that changes code: reviews all of it.
            new IntegrationReviewStep()
        };

        /// <summary>The steps of the run this project gets — what the integration review lists.</summary>
        internal static IReadOnlyList<MeticaStep> CurrentSteps => MeticaPaths.HasGDSdk ? GDSdkSteps : StandaloneSteps;

        /// <summary>Every Ads step id, both lists — what Reset sign-offs clears.</summary>
        public static IEnumerable<string> AllStepIds => GDSdkSteps.Concat(StandaloneSteps).Select(s => s.Id);

        public static MeticaFlow Create() =>
            new MeticaFlow(Name, () => MeticaPaths.HasGDSdk ? GDSdkSteps : StandaloneSteps, () => AllStepIds);
    }
}
