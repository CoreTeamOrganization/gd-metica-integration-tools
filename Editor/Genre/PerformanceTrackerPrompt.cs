using System;
using UnityEditor;
using UnityEditor.PackageManager;

namespace GameDistrict.MeticaIntegrationTools
{
    // Offers to add GD Performance Tracker (perfStats / loadTime events) via git URL.
    [InitializeOnLoad]
    internal static class PerformanceTrackerPrompt
    {
        private const string GitUrl  = "https://github.com/CoreTeamOrganization/GDPerformanceTracker.git";
        private const string SkipKey = "GDMeticaAnalytics.SkipPerfTrackerPrompt";

        static PerformanceTrackerPrompt()
        {
            if (SessionState.GetBool(SkipKey, false)) return;
            SessionState.SetBool(SkipKey, true); // ask at most once per editor session

            EditorApplication.delayCall += () =>
            {
                if (IsInstalled()) return;
                if (EditorUtility.DisplayDialog(
                        "GD Performance Tracker",
                        "GDMeticaAnalytics can log \"perfStats\" and \"loadTime\" events using the " +
                        "GD Performance Tracker package.\n\nAdd it to this project now?",
                        "Add Package", "Not Now"))
                {
                    Client.Add(GitUrl);
                }
            };
        }

        private static bool IsInstalled()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "GDPerformanceTracker.Runtime")
                    return true;
            return false;
        }
    }
}
