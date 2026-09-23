using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Points Unity at a Gradle new enough to build Metica's native Android SDK.
    ///
    /// <para>Metica asks for Gradle 8.6 or later. Unity 2022.3 bundles 7.5.1, so a project on
    /// the bundled Gradle has to be pointed at an external install through
    /// Preferences → External Tools. That setting is a preference of the editor, not of the
    /// project: it does not travel in the repository, and every machine that builds the game
    /// has to set it.</para>
    ///
    /// <para>Optional, because whether it bites depends on the rest of the build. Plenty of
    /// games ship without touching it — a newer Unity may already bundle enough Gradle, and
    /// some setups never exercise the path that needs it. The step reports what it finds and
    /// lets you decide.</para>
    /// </summary>
    public sealed class GradleVersionStep : MeticaStep
    {
        /// <summary>The floor from Metica's Unity SDK setup guide.</summary>
        private static readonly Version Required = new Version(8, 6);

        private static readonly Regex LauncherJarVersion =
            new Regex(@"gradle-launcher-([0-9]+(?:\.[0-9]+)+)");

        private static readonly Regex FolderVersion = new Regex(@"gradle[-_]?([0-9]+(?:\.[0-9]+)+)");

        public override string Title => "Gradle 8.6 or later";

        public override bool Optional => true;

        public override string Summary =>
            "Metica's Android SDK needs Gradle 8.6+. Unity 2022.3 bundles 7.5.1, which is too old, so " +
            "the editor has to be pointed at an external install. This is an editor preference, not a " +
            "project setting — it is per machine and is not committed.";

        public override string ReviewHint =>
            "Nothing in the project changed. Confirm Edit → Preferences → External Tools now shows " +
            "Gradle Installed with Unity unticked, with the path below it pointing at your 8.6+ install.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var custom = ReadGradlePath(out var readable);
            if (!readable)
            {
                result.Note("Could not read the Android Gradle preference on this Unity version. Check " +
                            "Edit → Preferences → External Tools by hand.");
                return result.Seal();
            }

            if (string.IsNullOrEmpty(custom))
            {
                var bundled = BundledVersion();
                result.Problem(bundled == null
                    ? "Unity is using the Gradle bundled with the editor. Metica needs 8.6 or later; " +
                      "untick Gradle Installed with Unity and point it at an external install."
                    : $"Unity is using the bundled Gradle {bundled}, below the {Required} Metica needs. " +
                      "Untick Gradle Installed with Unity and point it at an external install.");
                return result.Seal();
            }

            if (!Directory.Exists(custom))
            {
                result.Problem($"The Gradle path points at {custom}, which does not exist on this machine.");
                return result.Seal();
            }

            var found = VersionAt(custom);
            if (found == null)
            {
                result.Note($"Using Gradle at {custom}, but its version could not be read. Make sure it " +
                            $"is {Required} or later.");
                return result.Seal();
            }

            if (found < Required)
                result.Problem($"Gradle {found} at {custom} is below the {Required} Metica needs.");
            else
                result.Note($"Gradle {found} at {custom}");

            return result.Seal();
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "Download Gradle 8.6+ from gradle.org and unzip it somewhere permanent, then point the " +
                "editor at the folder that contains bin/ and lib/.\n\n" +
                "Skip this step if your build already works — it matters for the Kotlin native SDK, and " +
                "not every game hits it.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Choose a Gradle folder…", GUILayout.Height(24)))
                ChooseGradleFolder();

            if (GUILayout.Button("Open External Tools", GUILayout.Height(24)))
                SettingsService.OpenUserPreferences("Preferences/External Tools");

            if (GUILayout.Button("Use Unity's bundled Gradle", GUILayout.Height(24)))
            {
                if (WriteGradlePath(string.Empty))
                    MeticaIntegrationLog.Record(Title, "Cleared the Gradle path — back to Unity's bundled Gradle");
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── Actions ────────────────────────────────────────────────────────────

        private void ChooseGradleFolder()
        {
            var chosen = EditorUtility.OpenFolderPanel("Gradle 8.6 or later", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(chosen)) return;

            var found = VersionAt(chosen);
            if (found != null && found < Required
                && !EditorUtility.DisplayDialog("Gradle is too old",
                    $"That folder holds Gradle {found}. Metica needs {Required} or later.\n\n" +
                    "Use it anyway?", "Use it", "Cancel"))
                return;

            if (!WriteGradlePath(chosen))
            {
                MeticaIntegrationLog.Record(Title,
                    "Could not write the Gradle path. Set it in Edit → Preferences → External Tools.");
                return;
            }

            MeticaIntegrationLog.Record(Title,
                found == null ? $"Set the Gradle path to {chosen}" : $"Set Gradle {found} at {chosen}");
        }

        // ── Unity's Android tool settings ──────────────────────────────────────

        /// <summary>
        /// UnityEditor.Android.AndroidExternalToolsSettings lives in an editor extension
        /// assembly the tool does not reference, so it is reached by reflection. Empty means
        /// the editor is on its bundled Gradle.
        /// </summary>
        private static PropertyInfo GradlePathProperty() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly =>
                {
                    try { return assembly.GetType("UnityEditor.Android.AndroidExternalToolsSettings"); }
                    catch { return null; }
                })
                .FirstOrDefault(type => type != null)
                ?.GetProperty("gradlePath", BindingFlags.Public | BindingFlags.Static);

        private static string ReadGradlePath(out bool readable)
        {
            var property = GradlePathProperty();
            readable = property != null && property.CanRead;

            if (!readable) return null;

            try { return property.GetValue(null) as string; }
            catch { readable = false; return null; }
        }

        private static bool WriteGradlePath(string path)
        {
            var property = GradlePathProperty();
            if (property == null || !property.CanWrite) return false;

            try
            {
                property.SetValue(null, path);
                return true;
            }
            catch { return false; }
        }

        // ── Version detection ──────────────────────────────────────────────────

        /// <summary>Version of the Gradle Unity ships inside the Android playback engine.</summary>
        private static Version BundledVersion()
        {
            try
            {
                var engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
                return string.IsNullOrEmpty(engine) ? null : VersionAt(Path.Combine(engine, "Tools", "gradle"));
            }
            catch { return null; }
        }

        /// <summary>
        /// Reads a Gradle install's version, from lib/gradle-launcher-&lt;version&gt;.jar if it
        /// is there and from the folder name otherwise — an unzipped release is normally
        /// named gradle-8.7, but it can be renamed, and the jar cannot.
        /// </summary>
        private static Version VersionAt(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;

            try
            {
                var lib = Path.Combine(directory, "lib");
                if (Directory.Exists(lib))
                {
                    var launcher = Directory.GetFiles(lib, "gradle-launcher-*.jar").FirstOrDefault();
                    if (launcher != null)
                    {
                        var fromJar = Parse(LauncherJarVersion, Path.GetFileNameWithoutExtension(launcher));
                        if (fromJar != null) return fromJar;
                    }
                }
            }
            catch { /* unreadable folder — fall through to the name */ }

            return Parse(FolderVersion, new DirectoryInfo(directory.TrimEnd('/', '\\')).Name);
        }

        private static Version Parse(Regex pattern, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var match = pattern.Match(text);
            if (!match.Success) return null;

            return Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
        }
    }
}
