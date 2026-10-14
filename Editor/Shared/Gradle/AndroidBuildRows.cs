using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The Android build checklist rows — Moloco, Gradle 8.6 (with MAX's dexing property on
    /// Gradle 8), JDK 17, Kotlin and AGP in the base Gradle template — each saying whether it is
    /// set and holding the controls that set it. Shared by the Ads flow's optional
    /// Troubleshooting step and the Analytics flow's required build setup, so there is one
    /// implementation of each row.
    /// </summary>
    internal sealed class AndroidBuildRows
    {
        private readonly string _logTitle;
        private readonly GradleVersionStep _gradle = new GradleVersionStep();
        private readonly GradleJdkStep _jdk = new GradleJdkStep();

        /// <param name="logTitle">The step title the rows' actions log under.</param>
        public AndroidBuildRows(string logTitle) => _logTitle = logTitle;

        /// <summary>The four rows Metica's Android SDK needs: Gradle, JDK, Kotlin, AGP.</summary>
        public IEnumerable<StepItem> Required()
        {
            yield return Gradle();
            yield return Jdk();

            var template = GradleTemplateEditor.Inspect();
            yield return Kotlin(template);
            yield return Agp(template);
        }

        public StepItem Moloco()
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
                new StepButton("Resolve Android dependencies", () => AndroidDependencies.Resolve(_logTitle),
                    AndroidDependencies.NeedsResolve()),
                new StepButton("Select Dependencies.xml",
                    () => SelectAndPing(UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(MeticaPaths.MolocoDependencies)),
                    icon: StepIcon.File),
                new StepButton("Metica requirements page", () => Application.OpenURL(MolocoVersions.DocsUrl))
            });
        }

        /// <summary>Gradle 8.6, and — once Unity builds with Gradle 8 — MAX's dexing property.</summary>
        public StepItem Gradle()
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

        public StepItem Jdk()
        {
            var result = _jdk.Verify();
            var status = result.Problems.FirstOrDefault() ?? result.Notes.FirstOrDefault() ?? "";
            return new StepItem("JDK 17", result.Ok, status, _jdk.Controls(result).ToList());
        }

        public StepItem Kotlin(GradleTemplateEditor.Report report)
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

        public StepItem Agp(GradleTemplateEditor.Report report)
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
        private void EditTemplate(System.Func<List<string>> edit)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.BaseProjectTemplateGradle))
                MeticaIntegrationLog.Record(_logTitle, KotlinTemplateStep.EnableCustomBaseTemplate());

            MeticaIntegrationLog.Record(_logTitle, edit());
        }

        private static void SelectAndPing(Object asset)
        {
            if (asset == null) return;
            UnityEditor.Selection.activeObject = asset;
            UnityEditor.EditorGUIUtility.PingObject(asset);
        }
    }
}
