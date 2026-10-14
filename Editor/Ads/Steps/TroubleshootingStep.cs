using System.Collections.Generic;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The optional Android build fixes, as one checklist: Moloco (only when the project has
    /// it), Gradle 8.6 (with MAX's dexing property on Gradle 8), JDK 17, Kotlin and AGP in the
    /// base Gradle template. Each row shows whether it is set, and opens to the controls that
    /// set it. Nothing here blocks the run — plenty of projects build without these.
    ///
    /// <para>The rows are <see cref="AndroidBuildRows"/>, shared with the Analytics flow's
    /// required build setup.</para>
    /// </summary>
    public sealed class TroubleshootingStep : MeticaStep
    {
        public const string StepTitle = "Dependencies & troubleshooting";

        private readonly AndroidBuildRows _rows = new AndroidBuildRows(StepTitle);

        public override string Title => StepTitle;

        public override string Summary => "Optional fixes for common Android build problems. Open a row to fix it.";

        public override string Why =>
            "None of these block the run — many projects build without them. Moloco only appears if the " +
            "project has it. Gradle 8.6 and JDK 17 are editor preferences, per machine. Kotlin and AGP are " +
            "set in baseProjectTemplate.gradle, backed up first. On Gradle 8, MAX's dexing property has to be " +
            $"{MaxDexingProperty.Required}; MAX 8.2.1 and newer already set it.";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.BaseProjectTemplateGradle,
            MeticaPaths.GradleProperties,
            MeticaPaths.MolocoDependencies,
            MaxDexingProperty.File
        };

        public override string ReviewHint => "Only the rows you fixed should have changed anything.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var items = Items().ToList();
            var todo = items.Count(item => !item.Done);

            if (todo == 0) result.Note("All set");
            else result.Warning($"{todo} of {items.Count} not set — open a row to fix it, or leave it if your build works.");

            return result.Seal();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result) => Items();

        private IEnumerable<StepItem> Items()
        {
            if (MolocoVersions.Installed) yield return _rows.Moloco();
            foreach (var row in _rows.Required()) yield return row;
        }
    }
}
