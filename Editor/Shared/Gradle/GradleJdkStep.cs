using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Points Gradle at a JDK new enough to build Metica's native Android SDK.
    ///
    /// <para>Metica asks for JDK 17 or later. Unlike the Gradle version (an editor
    /// preference), this is a project file: gradleTemplate.properties'
    /// org.gradle.java.home. That file only exists once Custom Gradle Properties Template
    /// is ticked under Player Settings → Publishing Settings — this step ticks it the same
    /// way KotlinTemplateStep ticks Custom Base Gradle Template, by copying Unity's own
    /// default so nothing else in the file is guessed.</para>
    ///
    /// <para>org.gradle.java.home is a local machine path, not something to commit — every
    /// machine that builds Android has to set its own. Optional for the same reason
    /// GradleVersionStep is: whether it bites depends on the rest of the build.</para>
    /// </summary>
    public sealed class GradleJdkStep : MeticaStep
    {
        /// <summary>The floor Metica's Android setup guide documents.</summary>
        private static readonly Version Required = new Version(17, 0);

        private const string DefaultTemplate = "Tools/GradleTemplates/gradleTemplate.properties";

        private static readonly Regex JavaHomeLine =
            new Regex(@"^org\.gradle\.java\.home\s*=\s*(.*)$", RegexOptions.Multiline);

        private static readonly Regex ReleaseVersion =
            new Regex("JAVA_VERSION\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)");

        public override string Title => "JDK 17 for Gradle";

        public override bool Optional => true;

        public override string Summary => "Point Gradle at JDK 17+.";

        public override string Why =>
            "Metica's Android SDK needs JDK 17+ (AWS Corretto or Adoptium). This sets " +
            "org.gradle.java.home in gradleTemplate.properties, turning on Custom Gradle Properties " +
            "Template first if needed. The path is this machine's — don't commit it as-is. Skip this " +
            "if your Android build already works.";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.GradleProperties };

        public override string ReviewHint => "org.gradle.java.home is this machine's path — don't commit it as-is.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (!MeticaPaths.FileExists(MeticaPaths.GradleProperties))
            {
                result.Problem("No JDK set — pick one below.");
                return result.Seal();
            }

            var text = SourcePatcher.ReadAll(MeticaPaths.GradleProperties);
            var match = JavaHomeLine.Match(text);

            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                result.Problem("No JDK set — pick one below.");
                return result.Seal();
            }

            var jdkHome = match.Groups[1].Value.Trim();
            if (!Directory.Exists(jdkHome))
            {
                result.Problem($"JDK folder not found: {jdkHome}");
                return result.Seal();
            }

            var found = VersionAt(jdkHome);
            if (found == null)
            {
                result.Note($"JDK at {jdkHome} (version unknown)");
                return result.Seal();
            }

            if (found < Required)
                result.Problem($"JDK {found} is below {Required}.");
            else
                result.Note($"JDK {found}");

            return result.Seal();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            yield return new StepButton("Choose a JDK folder…", ChooseJdkFolder, icon: StepIcon.Folder);
        }

        // ── Actions ────────────────────────────────────────────────────────────

        private static void ChooseJdkFolder()
        {
            var chosen = EditorUtility.OpenFolderPanel("JDK 17 or later", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(chosen)) return;

            var found = VersionAt(chosen);
            if (found != null && found < Required
                && !EditorUtility.DisplayDialog("JDK is too old",
                    $"That folder holds JDK {found}. Metica needs {Required} or later.\n\nUse it anyway?",
                    "Use it", "Cancel"))
                return;

            var log = new List<string>();

            if (!MeticaPaths.FileExists(MeticaPaths.GradleProperties))
                log.AddRange(EnableCustomPropertiesTemplate());

            if (MeticaPaths.FileExists(MeticaPaths.GradleProperties))
                log.Add(WriteJavaHome(chosen, found));

            MeticaIntegrationLog.Record("JDK 17 for Gradle", log);
        }

        private static string WriteJavaHome(string jdkHome, Version found)
        {
            var text = SourcePatcher.ReadAll(MeticaPaths.GradleProperties);
            var forward = jdkHome.Replace('\\', '/');
            var line = $"org.gradle.java.home={forward}";

            var match = JavaHomeLine.Match(text);
            var updated = match.Success
                ? JavaHomeLine.Replace(text, line.Replace("$", "$$"), 1)
                : text.TrimEnd('\n', '\r') + "\n" + line + "\n";

            SourcePatcher.WriteWithBackup(MeticaPaths.GradleProperties, updated);
            AssetDatabase.Refresh();

            return found == null
                ? $"Set org.gradle.java.home to {forward}"
                : $"Set org.gradle.java.home to JDK {found} at {forward}";
        }

        /// <summary>
        /// Ticks Custom Gradle Properties Template the same way KotlinTemplateStep ticks
        /// Custom Base Gradle Template: copying Unity's own default, since useAndroidX and
        /// enableJetifier already belong in it and guessing those would risk the build.
        /// </summary>
        private static List<string> EnableCustomPropertiesTemplate()
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
                        "→ Publishing Settings → Custom Gradle Properties Template by hand, then run this again.");
                return log;
            }

            if (source == null || !File.Exists(source))
            {
                log.Add("Could not find Unity's default gradleTemplate.properties to copy. Tick Player " +
                        "Settings → Publishing Settings → Custom Gradle Properties Template by hand, then " +
                        "run this again.");
                return log;
            }

            var target = MeticaPaths.ToAbsolute(MeticaPaths.GradleProperties);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            File.Copy(source, target);
            AssetDatabase.ImportAsset(MeticaPaths.GradleProperties);

            log.Add("Enabled Custom Gradle Properties Template by copying Unity's default into " +
                     MeticaPaths.GradleProperties);
            return log;
        }

        // ── Version detection ───────────────────────────────────────────────────

        /// <summary>
        /// Reads a JDK install's version from its own release file (JAVA_VERSION="17.0.9"),
        /// shipped by every mainstream distribution — no need to invoke java itself.
        /// </summary>
        private static Version VersionAt(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;

            try
            {
                var release = Path.Combine(directory, "release");
                if (!File.Exists(release)) return null;

                var match = ReleaseVersion.Match(File.ReadAllText(release));
                if (!match.Success) return null;

                var raw = match.Groups[1].Value;
                if (!raw.Contains('.')) raw += ".0";
                return Version.TryParse(raw, out var version) ? version : null;
            }
            catch { return null; }
        }
    }
}
