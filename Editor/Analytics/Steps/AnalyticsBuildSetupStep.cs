using System.Collections.Generic;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Metica's required Android build setup (docs.metica.com, Unity SDK steps 1–3): Gradle 8.6
    /// or later, JDK 17 for Gradle, and the Kotlin plugin (1.9.22) with AGP 8.4.0 in
    /// baseProjectTemplate.gradle. The same rows as the Ads flow's Troubleshooting step, but
    /// here every row must be set.
    /// </summary>
    public sealed class AnalyticsBuildSetupStep : MeticaStep
    {
        public const string StepTitle = "Set up the Android build";

        public const string DocsUrl = "https://docs.metica.com/api/unity-sdk/unity-sdk-2#step-2-update-baseprojecttemplate.gradle";

        private readonly AndroidBuildRows _rows = new AndroidBuildRows(StepTitle);

        public override string Title => StepTitle;

        public override string Summary => "Gradle 8.6, JDK 17, Kotlin and AGP — required by Metica. Open a row to fix it.";

        public override string Why =>
            "Metica's Android SDK needs all four (Metica Unity SDK docs, steps 1–3). Gradle 8.6 and JDK 17 are editor " +
            "preferences, per machine; JDK 17 is also written to gradleTemplate.properties. Kotlin " +
            $"({GradleTemplateEditor.KotlinClasspath.Split(':').Last().TrimEnd('\'')}) and AGP " +
            $"{GradleTemplateEditor.RecommendedAgpVersion} go in baseProjectTemplate.gradle, backed up first. On Gradle 8, " +
            $"MAX's dexing property has to be {MaxDexingProperty.Required}.";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.BaseProjectTemplateGradle,
            MeticaPaths.GradleProperties,
            MaxDexingProperty.File
        };

        public override string ReviewHint => "baseProjectTemplate.gradle and gradleTemplate.properties.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            // Android build files only: an iOS build never reads them. Asked again after a switch.
            if (!AndroidDependencies.IsAndroidTarget)
            {
                result.Note($"Android only — the build target is {UnityEditor.EditorUserBuildSettings.activeBuildTarget}. " +
                            "Switch to Android and this asks again; the rows below can be set now anyway.");
                return result.Seal();
            }

            foreach (var row in _rows.Required())
            {
                if (row.Done) result.Note(row.Title);
                else result.Problem($"{row.Title}: {row.Status}");
            }
            return result.Seal();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            foreach (var row in _rows.Required()) yield return row;
            yield return new StepButton("Metica docs", () => UnityEngine.Application.OpenURL(DocsUrl));
        }
    }
}
