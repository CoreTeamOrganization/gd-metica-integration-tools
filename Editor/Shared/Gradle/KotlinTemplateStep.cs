using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Gives Metica's Kotlin Android SDK the Gradle plugin it builds with.
    ///
    /// <para>The classpath has to live in <c>baseProjectTemplate.gradle</c>, which only exists
    /// once Custom Base Gradle Template is ticked under Player Settings → Publishing Settings.
    /// That tick is really just the file: Unity copies its own default into
    /// Assets/Plugins/Android and treats the file's presence as the setting. So this step can
    /// turn it on by making the copy, then write the buildscript block into it.</para>
    ///
    /// <para>Optional, and separate from the Gradle version before it — they are independent,
    /// and Metica documents them as two steps. A game whose Android build already works can
    /// skip it.</para>
    /// </summary>
    public sealed class KotlinTemplateStep : MeticaStep
    {
        /// <summary>Where the editor keeps the template Unity copies when the box is ticked.</summary>
        private const string DefaultTemplate = "Tools/GradleTemplates/baseProjectTemplate.gradle";

        public override string Title => "Kotlin in the base Gradle template";

        public override bool Optional => true;

        public override string Summary => "Add Kotlin and a matching AGP to baseProjectTemplate.gradle.";

        public override string Why =>
            "Metica's Android SDK is Kotlin, so its Gradle plugin (1.9.22, per Metica's docs) has to be " +
            "on the buildscript classpath, next to an AGP classpath matching com.android.application. " +
            "Turns on Custom Base Gradle Template first if needed. At Target API " +
            $"{GradleTemplateEditor.MinimumApiForAgp8}+ it also raises AGP to " +
            $"{GradleTemplateEditor.RecommendedAgpVersion} — Android requires AGP 8 there. The original " +
            "file is backed up first. Skip this if your Android build already works.";

        public override string ActionLabel => "Fix the base Gradle template";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.BaseProjectTemplateGradle };

        public override string ReviewHint =>
            "buildscript above plugins, holding Kotlin and an AGP classpath equal to com.android.application.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var report = GradleTemplateEditor.Inspect();

            if (report.FileMissing)
            {
                result.Problem("Custom Base Gradle Template is off.");
                return result.Seal();
            }

            if (report.PluginVersion == null)
            {
                result.Problem("No com.android.application version in baseProjectTemplate.gradle.");
                return result.Seal();
            }

            if (!report.HasBuildscript)
                result.Problem("No buildscript block.");
            else if (!report.BuildscriptAbovePlugins)
                result.Problem("buildscript has to come before plugins.");

            if (report.HasBuildscript && report.ClasspathVersion == null)
                result.Problem("No AGP classpath in buildscript.");
            else if (report.ClasspathVersion != null && report.ClasspathVersion != report.PluginVersion)
                result.Problem($"AGP classpath {report.ClasspathVersion} ≠ com.android.application " +
                               $"{report.PluginVersion}.");

            foreach (var mismatch in report.MismatchedPluginIds)
                result.Problem($"{mismatch} ≠ com.android.application {report.PluginVersion}.");

            if (report.AgpTooOldForTarget)
                result.Problem($"AGP {report.PluginVersion} is too old for Target API " +
                               $"{GradleTemplateEditor.MinimumApiForAgp8}+.");

            if (!report.HasKotlinClasspath)
                result.Problem("No Kotlin plugin in buildscript.");

            if (!report.NeedsWork)
                result.Note($"Ready (AGP {report.PluginVersion})");

            return result.Seal();
        }

        public override void Apply()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.BaseProjectTemplateGradle))
                MeticaIntegrationLog.Record(Title, EnableCustomBaseTemplate());

            MeticaIntegrationLog.Record(Title, GradleTemplateEditor.Fix());
        }

        /// <summary>
        /// Ticks Custom Base Gradle Template by doing what the tick does: copying the editor's
        /// own default template into Assets/Plugins/Android. The default is copied rather than
        /// written from scratch because it carries the AGP version that matches this Unity
        /// install, and guessing that wrong would break the build.
        /// </summary>
        private static List<string> EnableCustomBaseTemplate()
        {
            var log = new List<string>();

            string source;
            try
            {
                var engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
                source = string.IsNullOrEmpty(engine) ? null : Path.Combine(engine, DefaultTemplate);
            }
            catch (Exception e)
            {
                log.Add($"Could not locate the Android playback engine ({e.Message}). Tick Player Settings " +
                        "→ Publishing Settings → Custom Base Gradle Template by hand, then run this again.");
                return log;
            }

            if (source == null || !File.Exists(source))
            {
                log.Add("Could not find Unity's default baseProjectTemplate.gradle to copy. Tick Player " +
                        "Settings → Publishing Settings → Custom Base Gradle Template by hand, then run " +
                        "this again.");
                return log;
            }

            var target = MeticaPaths.ToAbsolute(MeticaPaths.BaseProjectTemplateGradle);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            File.Copy(source, target);
            AssetDatabase.ImportAsset(MeticaPaths.BaseProjectTemplateGradle);

            log.Add("Enabled Custom Base Gradle Template by copying Unity's default into " +
                    MeticaPaths.BaseProjectTemplateGradle);

            return log;
        }
    }
}
