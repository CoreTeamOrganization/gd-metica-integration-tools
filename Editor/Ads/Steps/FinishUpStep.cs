using System.Collections.Generic;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The last mile: teach the Remove SDK menu about Metica. Whether Metica actually serves
    /// ads in production is a remote config decision, kept there deliberately — this step does
    /// not expose a local UseMetica override, so there is nothing here to drift from what
    /// remote config says.
    /// </summary>
    public sealed class FinishUpStep : MeticaStep
    {
        private const string RemoverMarker = "\"Assets/MeticaSdk\"";

        public override string Title => "Finish up";

        public override string Summary => "Add the Metica folders to the Remove SDK menu.";

        public override string Why =>
            "So the GD SDK's own Remove SDK menu also deletes Metica. After this, set UseMetica in the " +
            "Monetization Firebase Remote Config payload — ads start about 2s after launch, before the " +
            "fetch lands, so a flipped value applies on the next launch (same as v6.2.4). Then build to a " +
            "device and look for the Metica tag: SDK version, \"Metica initialization completed\" and the " +
            "TRIAL / HOLDOUT user group.";

        public override string ActionLabel => "Update the Remove SDK menu";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.MonetizationRemover };

        public override string ReviewHint => "Three folder entries added to the Remove SDK menu.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var remover = MeticaPaths.MonetizationRemover;
            if (!MeticaPaths.FileExists(remover))
                result.Note("No Remove SDK menu — skipped");
            else if (!SourcePatcher.Contains(remover, RemoverMarker))
                result.Problem("Remove SDK menu doesn't list the Metica folders yet.");
            else
                result.Note("Remove SDK menu updated");

            return result.Seal();
        }

        public override void Apply()
        {
            var remover = MeticaPaths.MonetizationRemover;
            if (!MeticaPaths.FileExists(remover))
            {
                MeticaIntegrationLog.Record(Title, "MonetizationRemover.cs not found — nothing to update.");
                return;
            }

            var outcome = SourcePatcher.InsertBeforeLine(remover, RemoverMarker,
                "\"Assets/Plugins/Android/AndroidManifest.xml\"",
                "            \"Assets/Metica\",\n" +
                "            \"Assets/MeticaSdk\",\n" +
                "            \"Assets/MeticaSDK\",");

            MeticaIntegrationLog.Record(Title, SourcePatcher.IsSatisfied(outcome)
                ? SourcePatcher.Describe(outcome, remover, null)
                : "Could not find the PathsToDelete anchor. Add \"Assets/Metica\", \"Assets/MeticaSdk\" and " +
                  "\"Assets/MeticaSDK\" to PathsToDelete in MonetizationRemover.cs by hand.");

            AssetDatabase.Refresh();
        }
    }
}
