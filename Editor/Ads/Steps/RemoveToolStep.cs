using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Removes the com.gamedistrict.metica-integration-tools package from this project's
    /// Packages/manifest.json — this wizard, its steps and its templates, in full.
    ///
    /// <para>The tool ships as a package, not a folder inside Assets, so removing it is a
    /// Package Manager operation (<see cref="Client.Remove"/>) rather than an asset delete —
    /// deleting Packages/&lt;id&gt; directly would not touch the manifest entry, and Unity would
    /// just re-resolve the package on the next domain reload.</para>
    ///
    /// <para>Last for a reason: nothing about the run can be shown as complete once this has
    /// happened, because the classes that draw that completion are what gets removed. Verify
    /// can therefore only ever report "still here" — there is no way to observe "gone" from
    /// inside a tool that no longer exists to observe it. Optional, so a team that expects to
    /// run this wizard again for a future Metica SDK bump can skip it and finish the run
    /// normally instead of being forced to destroy the tool to close it out.</para>
    /// </summary>
    public sealed class RemoveToolStep : MeticaStep
    {
        private const string PackageName = "com.gamedistrict.metica-integration-tools";

        public override string Title => "Remove the integration tool";

        public override bool Optional => true;

        public override string Summary =>
            $"Removes the {PackageName} package from this project entirely, once you are done " +
            "integrating for good. Skip it if you expect to run this wizard again later, for " +
            "instance for the next Metica SDK version.";

        public override string ActionLabel => "Remove the package";

        public override IEnumerable<string> TouchedPaths => new[] { "Packages/manifest.json" };

        public override string ReviewHint =>
            "There is nothing left to review — the package, and the window showing this text, are both gone.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            result.Problem($"{PackageName} is still installed. Keep it if you plan to run this wizard " +
                           "again for a future Metica SDK version; remove it once you are done for good " +
                           "— it should not ship in the game.");
            return result.Seal();
        }

        public override void Apply()
        {
            if (!EditorUtility.DisplayDialog("Remove the Metica integration tool",
                $"Remove the {PackageName} package from this project?\n\n" +
                "This removes the wizard itself — its steps, templates and everything else the " +
                "package provides. The window will close itself once Unity finishes removing it. " +
                "Run this only once the integration is finished for good.",
                "Remove", "Cancel"))
                return;

            var request = Client.Remove(PackageName);

            try
            {
                EditorUtility.DisplayProgressBar("Removing the Metica integration tool", PackageName, 0.5f);
                while (!request.IsCompleted)
                    Thread.Sleep(50);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (request.Status == StatusCode.Failure)
                MeticaIntegrationLog.Record(Title, $"Could not remove {PackageName}: {request.Error?.message}");
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "Removes the whole package — this window included. Skip it if you expect to run this " +
                "wizard again later, for instance for the next Metica SDK version. Run it once, at the " +
                "very end, when you are done integrating for good.",
                MessageType.Warning);
        }
    }
}
