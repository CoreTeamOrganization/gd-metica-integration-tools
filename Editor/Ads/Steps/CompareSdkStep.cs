using System;
using System.Collections.Generic;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Optional first look: how this project's GD SDK differs from the original release it
    /// declares. Changes nothing — it counts, and opens the Compare window on request. Skip it
    /// with one click; it is there for projects whose SDK the team has edited.
    /// </summary>
    public sealed class CompareSdkStep : MeticaStep
    {
        public override string Title => "Compare with original GD SDK";

        public override bool Optional => true;

        public override string Summary => "See what this project changed in the GD SDK — or skip.";

        public override string Why =>
            "Optional. Lists every GD SDK file this project changed, added or is missing compared with " +
            "the original release it declares, with the diffs — useful before the patch step, or when a " +
            "patch says its anchor wasn't found. Nothing is changed. Skip it if you don't need it.";

        public override string ActionLabel => "Open Compare window";

        internal override bool Applies => MeticaPaths.HasGDSdk;

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            SdkCompareResult summary;
            try
            {
                summary = SdkCompare.Run(false);
            }
            catch (Exception e)
            {
                result.Note("Couldn't compare: " + e.Message);
                return result.Seal();
            }

            if (summary.Version == null)
                result.Note(summary.Problem ?? "GD SDK version unreadable");
            else if (summary.Changes.Count == 0)
                result.Note($"GD SDK {summary.Version} — matches the original");
            else
                result.Note($"GD SDK {summary.Version} — {summary.Changes.Count} file" +
                            $"{(summary.Changes.Count == 1 ? "" : "s")} differ from the original " +
                            $"({summary.Changes.Count(c => c.Kind == ChangeKind.Changed)} changed, " +
                            $"{summary.Changes.Count(c => c.Kind == ChangeKind.Added)} added, " +
                            $"{summary.Changes.Count(c => c.Kind == ChangeKind.Missing)} missing)");

            // Informational: always passes, so it can be signed off or skipped at once.
            return result.Seal();
        }

        public override void Apply() => SdkCompareWindow.Open();
    }
}
