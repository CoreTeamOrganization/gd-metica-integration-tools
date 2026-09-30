using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The optional Android build fixes, as one checklist: Moloco (only when the project has
    /// it), Gradle 8.6 (with MAX's dexing property on Gradle 8), JDK 17, Kotlin and AGP in the
    /// base Gradle template. Each row shows whether it is set, and opens to the controls that
    /// set it. Nothing here blocks the run — plenty of projects build without these.
    ///
    /// <para>The rows reuse the Gradle, JDK and template logic the Genre Creator flow also runs
    /// as steps, so there is one implementation of each, not two.</para>
    /// </summary>
    public sealed class TroubleshootingStep : MeticaStep
    {
        public const string StepTitle = "Dependencies & troubleshooting";

        private readonly GradleVersionStep _gradle = new GradleVersionStep();
        private readonly GradleJdkStep _jdk = new GradleJdkStep();

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
            if (MolocoVersions.Installed) yield return Moloco();
            yield return Gradle();
            yield return Jdk();

            var template = GradleTemplateEditor.Inspect();
            yield return Kotlin(template);
            yield return Agp(template);
        }

        // ── Rows ───────────────────────────────────────────────────────────────

        private static StepItem Moloco()
        {
            var android = MolocoVersions.AndroidVersion;
            var ios = MolocoVersions.IosVersion;
            var androidOld = MolocoVersions.BelowFloor(android);
            var iosOld = MolocoVersions.BelowFloor(ios);

            // Only the platform being built counts — the other one is fixed when it is built.
            var target = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            var done = target == UnityEditor.BuildTarget.Android ? !androidOld
                : target == UnityEditor.BuildTarget.iOS ? !iosOld
                : !androidOld && !iosOld;

            var status = $"Android {android?.ToString() ?? "?"} · iOS {ios?.ToString() ?? "?"}" +
                         (done ? "" : $" — needs {MolocoVersions.Minimum}+") +
                         (target == UnityEditor.BuildTarget.Android && iosOld ? " · iOS old, not the build target" : "") +
                         (target == UnityEditor.BuildTarget.iOS && androidOld ? " · Android old, not the build target" : "");

            return new StepItem("Moloco", done, status, new StepControl[]
            {
                new StepButton($"Fix Android Moloco → {MolocoVersions.Minimum}.0", MolocoVersions.FixAndroid, androidOld),
                new StepButton($"Fix iOS Moloco → {MolocoVersions.Minimum}.0", MolocoVersions.FixIos, iosOld),
                new StepButton("Resolve Android dependencies", () => AndroidDependencies.Resolve(StepTitle),
                    AndroidDependencies.NeedsResolve()),
                new StepButton("Select Dependencies.xml",
                    () => SelectAndPing(UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(MeticaPaths.MolocoDependencies)),
                    icon: StepIcon.File),
                new StepButton("Metica requirements page", () => Application.OpenURL(MolocoVersions.DocsUrl))
            });
        }

        /// <summary>Gradle 8.6, and — once Unity builds with Gradle 8 — MAX's dexing property.</summary>
        private StepItem Gradle()
        {
            var result = _gradle.Verify();
            var inUse = GradleVersionStep.InUse();
            var dexing = inUse != null && inUse.Major >= 8 && MaxDexingProperty.NeedsFix;

            var status = result.Problems.FirstOrDefault() ?? result.Notes.FirstOrDefault() ?? "";
            if (dexing) status += $" · MAX's dexing property must be {MaxDexingProperty.Required}";

            var actions = new List<StepControl>
            {
                new StepButton($"Download Gradle {GradleVersionStep.DownloadVersion}…", GradleVersionStep.DownloadAndUse,
                    icon: StepIcon.Folder)
            };
            actions.AddRange(_gradle.Controls(result));
            if (dexing) actions.Add(new StepButton("Fix dexing property", MaxDexingProperty.Fix));

            return new StepItem($"Gradle {GradleVersionStep.DownloadVersion}", result.Ok && !dexing, status, actions);
        }

        private StepItem Jdk()
        {
            var result = _jdk.Verify();
            var status = result.Problems.FirstOrDefault() ?? result.Notes.FirstOrDefault() ?? "";
            return new StepItem("JDK 17", result.Ok, status, _jdk.Controls(result).ToList());
        }

        private static StepItem Kotlin(GradleTemplateEditor.Report report)
        {
            var done = !report.FileMissing && report.HasBuildscript && report.BuildscriptAbovePlugins
                       && report.HasKotlinClasspath && report.ClasspathVersion != null
                       && report.ClasspathVersion == report.PluginVersion && report.MismatchedPluginIds.Count == 0;

            var status = report.FileMissing ? "Custom Base Gradle Template is off"
                : done ? "Already added"
                : !report.HasKotlinClasspath ? "Not added"
                : "The buildscript block needs fixing";

            return new StepItem("Kotlin", done, status, done
                ? new StepControl[0]
                : new StepControl[] { new StepButton("Add Kotlin", () => EditTemplate(GradleTemplateEditor.AddKotlin)) });
        }

        private static StepItem Agp(GradleTemplateEditor.Report report)
        {
            var version = report.PluginVersion;
            var target = GradleTemplateEditor.RecommendedAgpVersion;
            var done = GradleTemplateEditor.AtLeast(version, target);

            var status = report.FileMissing ? "Custom Base Gradle Template is off"
                : version == null ? "No AGP version found"
                : report.AgpTooOldForTarget ? $"AGP {version} — Target API {GradleTemplateEditor.MinimumApiForAgp8}+ needs AGP 8"
                : $"AGP {version}";

            return new StepItem($"AGP {target}", done, status, done || (!report.FileMissing && version == null)
                ? new StepControl[0]
                : new StepControl[] { new StepButton($"Bump AGP to {target}", () => EditTemplate(GradleTemplateEditor.BumpAgp)) });
        }

        /// <summary>Turns on Custom Base Gradle Template if needed, then runs the edit.</summary>
        private static void EditTemplate(System.Func<List<string>> edit)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.BaseProjectTemplateGradle))
                MeticaIntegrationLog.Record(StepTitle, KotlinTemplateStep.EnableCustomBaseTemplate());

            MeticaIntegrationLog.Record(StepTitle, edit());
        }
    }
}
