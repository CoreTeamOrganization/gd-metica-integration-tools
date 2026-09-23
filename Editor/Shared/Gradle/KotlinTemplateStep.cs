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

        public override string Summary =>
            "Metica's Android SDK is Kotlin, so the Kotlin Gradle plugin has to be on the buildscript " +
            "classpath. Enables Custom Base Gradle Template if it is off, then writes the buildscript " +
            "block — above plugins, with its AGP version matching your com.android.application.";

        public override string ActionLabel => "Enable the template and write the buildscript";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.BaseProjectTemplateGradle };

        public override string ReviewHint =>
            "Check baseProjectTemplate.gradle has a buildscript block above plugins, holding a " +
            "kotlin-gradle-plugin classpath and an AGP classpath equal to the com.android.application " +
            "version below it. The original is in the backup folder.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var report = GradleTemplateEditor.Inspect();

            if (report.FileMissing)
            {
                result.Problem("baseProjectTemplate.gradle does not exist, so Custom Base Gradle Template " +
                               "is off and there is nowhere to put the Kotlin classpath.");
                return result.Seal();
            }

            if (report.PluginVersion == null)
            {
                result.Problem("No com.android.application plugin id in baseProjectTemplate.gradle, so " +
                               "there is no AGP version to match the buildscript against.");
                return result.Seal();
            }

            if (!report.HasBuildscript)
                result.Problem($"No buildscript block. It is what holds the Kotlin and AGP " +
                               $"({report.PluginVersion}) classpaths.");
            else if (!report.BuildscriptAbovePlugins)
                result.Problem("The buildscript block sits below plugins. Gradle requires buildscript first.");

            if (report.HasBuildscript && report.ClasspathVersion == null)
                result.Problem("The buildscript block has no com.android.tools.build:gradle classpath.");
            else if (report.ClasspathVersion != null && report.ClasspathVersion != report.PluginVersion)
                result.Problem($"AGP mismatch: the buildscript classpath says {report.ClasspathVersion} " +
                               $"but com.android.application says {report.PluginVersion}. They have to " +
                               "agree, and the plugins section wins.");

            foreach (var mismatch in report.MismatchedPluginIds)
                result.Problem($"{mismatch} does not match com.android.application {report.PluginVersion}.");

            if (report.AgpTooOldForTarget)
                result.Problem($"Target API {GradleTemplateEditor.MinimumApiForAgp8}+ needs AGP 8 or later, " +
                               $"but com.android.application is {report.PluginVersion}. The button raises " +
                               $"it to {GradleTemplateEditor.RecommendedAgpVersion}, Metica's recommended " +
                               "version.");

            if (!report.HasKotlinClasspath)
                result.Problem("No kotlin-gradle-plugin on the buildscript classpath.");

            if (!report.NeedsWork)
                result.Note($"baseProjectTemplate.gradle is set up for Metica (AGP {report.PluginVersion})");

            return result.Seal();
        }

        public override void Apply()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.BaseProjectTemplateGradle))
                MeticaIntegrationLog.Record(Title, EnableCustomBaseTemplate());

            MeticaIntegrationLog.Record(Title, GradleTemplateEditor.Fix());
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                $"The AGP version is normally read from the com.android.application plugin id already in " +
                "your file and copied onto the buildscript classpath, so the buildscript agrees with the " +
                $"version you build with. The one exception is Target API {GradleTemplateEditor.MinimumApiForAgp8}" +
                $"+: Android requires AGP 8 there, so if yours is older the button raises " +
                $"com.android.application and com.android.library to {GradleTemplateEditor.RecommendedAgpVersion} " +
                "first. The Kotlin plugin is pinned to 1.9.22, the version Metica documents. The original " +
                "file is backed up first.\n\n" +
                "Skip this step if your Android build already works.",
                MessageType.Info);
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
