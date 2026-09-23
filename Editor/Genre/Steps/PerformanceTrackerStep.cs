using System;
using UnityEditor.PackageManager;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Offers to add GD Performance Tracker, the package GDMeticaAnalytics.LogPerfStatsEvent
    /// and LogLoadTimeEvent read their payloads from.
    ///
    /// <para>This used to run as a package-wide [InitializeOnLoad] popup — fine when the
    /// package only did Genre Creator, wrong once Ads Integration shares it: an ads-only
    /// project has no use for this and no reason to see it. As a step here instead, it only
    /// shows up to someone who opened the Genre Creator wizard in the first place.</para>
    /// </summary>
    public sealed class PerformanceTrackerStep : MeticaStep
    {
        private const string GitUrl = "https://github.com/CoreTeamOrganization/GDPerformanceTracker.git";
        private const string AssemblyName = "GDPerformanceTracker.Runtime";

        public override string Title => "GD Performance Tracker";

        public override bool Optional => true;

        public override string Summary => "Add GD Performance Tracker — only for perfStats / loadTime events.";

        public override string Why =>
            $"Adds {GitUrl} by git URL. GDMeticaAnalytics uses it for the perfStats and loadTime events " +
            "(ConfigurePerformanceTracking, MarkGameInteractive, LogPerfStatsEvent, LogLoadTimeEvent). " +
            "Skip this step if you don't log those two.";

        public override string ActionLabel => "Add GD Performance Tracker";

        public override string ReviewHint => "Packages/manifest.json gains com.gamedistrict.performance-tracker.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (IsInstalled())
                result.Note("Installed");
            else
                result.Problem("Not installed.");

            return result.Seal();
        }

        public override void Apply()
        {
            if (IsInstalled())
            {
                MeticaIntegrationLog.Record(Title, "Already installed.");
                return;
            }

            PackageRequests.Track(Client.Add(GitUrl), Title, request =>
                MeticaIntegrationLog.Record(Title, request.Status == StatusCode.Failure
                    ? $"Could not add the package: {request.Error?.message}"
                    : "Added GD Performance Tracker"));
        }

        private static bool IsInstalled()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == AssemblyName)
                    return true;
            return false;
        }
    }
}
