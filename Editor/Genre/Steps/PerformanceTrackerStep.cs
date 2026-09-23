using System;
using System.Threading;
using UnityEditor;
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

        public override string Summary =>
            "GDMeticaAnalytics can log \"perfStats\" and \"loadTime\" events using the GD Performance " +
            "Tracker package. Only needed if you log those two events — skip it otherwise.";

        public override string ActionLabel => "Add GD Performance Tracker";

        public override string ReviewHint =>
            "Check Packages/manifest.json gained com.gamedistrict.performance-tracker.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (IsInstalled())
                result.Note("GD Performance Tracker installed");
            else
                result.Problem("GD Performance Tracker is not installed. Only needed for the perfStats " +
                               "and loadTime events — skip this step if you are not logging those.");

            return result.Seal();
        }

        public override void Apply()
        {
            if (IsInstalled())
            {
                MeticaIntegrationLog.Record(Title, "Already installed.");
                return;
            }

            var request = Client.Add(GitUrl);

            try
            {
                EditorUtility.DisplayProgressBar(Title, GitUrl, 0.5f);
                while (!request.IsCompleted)
                    Thread.Sleep(50);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            MeticaIntegrationLog.Record(Title, request.Status == StatusCode.Failure
                ? $"Could not add the package: {request.Error?.message}"
                : "Added GD Performance Tracker");
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                $"Adds {GitUrl} via git URL. GDMeticaAnalytics already calls its API once installed — " +
                "see ConfigurePerformanceTracking, MarkGameInteractive, LogPerfStatsEvent and " +
                "LogLoadTimeEvent.\n\n" +
                "Skip this step if you are not logging perfStats or loadTime.",
                MessageType.Info);
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
