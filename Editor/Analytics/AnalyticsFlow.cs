using System;
using System.Collections.Generic;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Metica analytics for games on GD SDK below 6.2.0, or on their own (custom) SDK: install
    /// what analytics needs, make the genre files from the game name and the Excel schema, tie
    /// Adjust to Metica, and the required Android build setup.
    ///
    /// <para>GD SDK 6.2.0 and newer carry analytics themselves (their own genre creator, and
    /// Adjust already tied to Metica), so this flow does not run there.</para>
    /// </summary>
    internal static class AnalyticsFlow
    {
        public const string Name = "Analytics Integration";

        /// <summary>The first GD SDK release with analytics built in.</summary>
        public static readonly Version BuiltInFrom = new Version(6, 2, 0);

        public const string FinishedNote =
            "Now in the game: log the genre's events where they happen. " +
            "Build to a device and check the Metica logs.";

        private static readonly MeticaStep[] Steps =
        {
            new AddMeticaSdkStep(),
            new AddAnalyticsAbstractionsStep(),
            new AddGenreCreatorStep(),
            new GenreFilesStep(),
            new AdjustWiringStep(),
            new AnalyticsStartupStep(),
            new AnalyticsBuildSetupStep()
        };

        /// <summary>
        /// Null when this project is one the flow covers; otherwise why not, for the Home card.
        /// </summary>
        public static string NotCovered
        {
            get
            {
                if (!MeticaPaths.HasGDSdk) return null;

                var reported = GdSdkVersion.Reported();
                var version = GdSdkVersion.Parse(reported);
                if (version == null) return "GD Monetization SDK version unreadable";
                if (version >= BuiltInFrom) return $"GD Monetization SDK {reported} has analytics built in — not covered here yet";
                if (!GdSdkVersion.IsSupported(reported)) return $"GD Monetization SDK {reported} isn't supported — needs {GdSdkVersion.Minimum}+";
                return null;
            }
        }

        public static IEnumerable<string> AllStepIds => Steps.Select(s => s.Id);

        public static MeticaFlow Create() =>
            new MeticaFlow(Name, () => NotCovered == null ? Steps : Array.Empty<MeticaStep>(), () => AllStepIds, FinishedNote);
    }
}
