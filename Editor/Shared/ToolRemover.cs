using UnityEditor;
using UnityEditor.PackageManager;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Removes this package from the project. Reached from GameDistrict/Metica and from the ⋮
    /// menu of either wizard window — one action, not a step at the end of one flow.
    ///
    /// <para>Refused while the project has generated genres: they inherit from
    /// GDMeticaAnalytics, which ships in this package's Runtime, so removing the package would
    /// take their base class with it. Ads Integration writes everything it produces into the
    /// project itself and needs nothing from the package at runtime, so an ads-only project can
    /// remove it freely.</para>
    /// </summary>
    public static class ToolRemover
    {
        private const string PackageName = "com.gamedistrict.metica-integration-tools";

        /// <summary>This tool's own per-project settings — meaningless once the tool is gone.</summary>
        private const string SettingsFolder = "Assets/MeticaIntegrationToolsSettings";

        [MenuItem("GameDistrict/Metica/Remove Integration Tools...", false, 100)]
        public static void Remove()
        {
            if (GenreDefinitionStep.CountGenres() > 0)
            {
                EditorUtility.DisplayDialog("Can't remove Metica Integration Tools",
                    $"This project has genres in {MeticaPaths.GenresRoot}, and they need this package " +
                    "at runtime — so it has to stay.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Remove Metica Integration Tools",
                    $"Remove {PackageName} from this project?\n\nYour Metica integration stays — only the " +
                    "tool goes.",
                    "Remove", "Cancel"))
                return;

            // Its assets' script lives in this package, so left behind they would show up as
            // missing scripts.
            if (AssetDatabase.IsValidFolder(SettingsFolder))
                AssetDatabase.DeleteAsset(SettingsFolder);

            PackageRequests.Track(Client.Remove(PackageName), "Removing Metica Integration Tools", request =>
            {
                if (request.Status == StatusCode.Failure)
                    EditorUtility.DisplayDialog("Could not remove the package",
                        request.Error?.message ?? "Unknown error.", "OK");
            });
        }
    }
}
