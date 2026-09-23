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

        public override string Summary => "Adds the Metica folders to the Remove SDK menu.";

        public override string ActionLabel => "Update the Remove SDK menu";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.MonetizationRemover };

        public override string ReviewHint => "Three folder entries added to the Remove SDK menu.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var remover = MeticaPaths.MonetizationRemover;
            if (!MeticaPaths.FileExists(remover))
                result.Note("MonetizationRemover.cs not found — skipping the Remove SDK menu update.");
            else if (!SourcePatcher.Contains(remover, RemoverMarker))
                result.Problem("The Remove SDK menu does not know about the Metica folders yet.");
            else
                result.Note("Remove SDK menu covers the Metica folders");

            result.Note("Remaining, and yours to decide: set UseMetica in the Monetization Firebase Remote " +
                        "Config payload. Ads initialise about 2s after launch, before the fetch lands, so a " +
                        "flipped remote value takes effect on the NEXT launch. That is how v6.2.4 behaves too.");
            result.Note("Build to a device and check the log for the Metica tag: SDK version, " +
                        "\"Metica initialization completed\" and the TRIAL / HOLDOUT user group.");

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
