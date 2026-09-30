using System;
using System.Collections.Generic;
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

        public override string Summary => "Point Unity at Gradle 8.6+.";

        public override string Why =>
            "Metica's Android SDK needs Gradle 8.6+; Unity 2022.3 bundles 7.5.1. Download Gradle from " +
            "gradle.org, unzip it somewhere permanent, and pick the folder that holds bin/ and lib/. " +
            "It's an editor preference, per machine — nothing is committed. Skip this if your Android " +
            "build already works.";

        public override string ReviewHint => "Nothing in the project changes — it's an editor preference.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var custom = ReadGradlePath(out var readable);
            if (!readable)
            {
                result.Note("Couldn't read the Gradle setting — check Preferences → External Tools.");
                return result.Seal();
            }

            if (string.IsNullOrEmpty(custom))
            {
                var bundled = BundledVersion();
                result.Problem(bundled == null
                    ? $"Using Unity's bundled Gradle — needs {Required}+."
                    : $"Using Unity's bundled Gradle {bundled} — needs {Required}+.");
                return result.Seal();
            }

            if (!Directory.Exists(custom))
            {
                result.Problem($"Gradle folder not found: {custom}");
                return result.Seal();
            }

            var found = VersionAt(custom);
            if (found == null)
            {
                result.Note($"Gradle at {custom} (version unknown)");
                return result.Seal();
            }

            if (found < Required)
                result.Problem($"Gradle {found} is below {Required}.");
            else
                result.Note($"Gradle {found}");

            return result.Seal();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            yield return new StepButton("Choose a Gradle folder…", ChooseGradleFolder, icon: StepIcon.Folder);
            yield return new StepButton("Open External Tools",
                () => SettingsService.OpenUserPreferences("Preferences/External Tools"));
            yield return new StepButton("Use Unity's bundled Gradle", () =>
            {
                if (WriteGradlePath(string.Empty))
                    MeticaIntegrationLog.Record(Title, "Cleared the Gradle path — back to Unity's bundled Gradle");
            });
        }

        // ── Actions ────────────────────────────────────────────────────────────

        public const string DownloadVersion = "8.6";
        private const string DownloadUrl = "https://services.gradle.org/distributions/gradle-8.6-all.zip";

        /// <summary>
        /// Asks for a folder, downloads Gradle 8.6 there, unzips it, and points Unity at it —
        /// with "Gradle installed with Unity" turned off. A folder that already holds the
        /// unzipped release is reused instead of downloading again.
        /// </summary>
        internal static void DownloadAndUse()
        {
            const string title = "Gradle 8.6 or later";
            var parent = EditorUtility.OpenFolderPanel($"Where should Gradle {DownloadVersion} go?", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(parent)) return;

            var home = Path.Combine(parent, $"gradle-{DownloadVersion}");
            if (!Directory.Exists(Path.Combine(home, "lib")))
            {
                var zip = Path.Combine(Application.temporaryCachePath, $"gradle-{DownloadVersion}-all.zip");
                if (!Downloads.TryDownload(DownloadUrl, zip, $"Downloading Gradle {DownloadVersion}", out var error))
                {
                    MeticaIntegrationLog.Record(title, $"Could not download Gradle {DownloadVersion}: {error}");
                    return;
                }

                if (!Downloads.TryUnzip(zip, parent, $"Unzipping Gradle {DownloadVersion}", out error))
                {
                    MeticaIntegrationLog.Record(title, $"Could not unzip Gradle {DownloadVersion}: {error}");
                    return;
                }

                File.Delete(zip);
                MakeLauncherExecutable(home);
                MeticaIntegrationLog.Record(title, $"Downloaded Gradle {DownloadVersion} to {home}");
            }

            if (!WriteGradlePath(home))
            {
                MeticaIntegrationLog.Record(title,
                    $"Gradle is at {home}, but the path could not be set. Set it in Edit → Preferences → External Tools.");
                return;
            }

            MeticaIntegrationLog.Record(title, $"Unity now builds with Gradle {DownloadVersion} at {home}");
        }

        /// <summary>A zip carries no Unix permissions, so on macOS bin/gradle has to be made executable again.</summary>
        private static void MakeLauncherExecutable(string home)
        {
            if (Application.platform != RuntimePlatform.OSXEditor && Application.platform != RuntimePlatform.LinuxEditor)
                return;

            try
            {
                var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{Path.Combine(home, "bin", "gradle")}\"");
                chmod?.WaitForExit(5000);
            }
            catch (Exception e)
            {
                MeticaIntegrationLog.Record("Gradle 8.6 or later", $"Could not make bin/gradle executable: {e.Message}");
            }
        }

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

        /// <summary>
        /// The editor preferences behind External Tools' Gradle fields: "Gradle installed with
        /// Unity" and the custom path. Read and written directly — setting the path through
        /// AndroidExternalToolsSettings alone did not persist it.
        /// </summary>
        private const string GradleEmbeddedPref = "GradleUseEmbedded";
        private const string GradlePathPref = "GradlePath";

        private static string ReadGradlePath(out bool readable)
        {
            readable = true;
            return EditorPrefs.GetBool(GradleEmbeddedPref, true)
                ? string.Empty
                : EditorPrefs.GetString(GradlePathPref, string.Empty);
        }

        /// <summary>
        /// Sets the Gradle path, and the "Gradle installed with Unity" tick to match: off for a
        /// custom path, on for an empty one — otherwise Unity keeps using its bundled Gradle.
        /// </summary>
        private static bool WriteGradlePath(string path)
        {
            var custom = !string.IsNullOrEmpty(path);
            EditorPrefs.SetBool(GradleEmbeddedPref, !custom);
            if (custom) EditorPrefs.SetString(GradlePathPref, path.Replace('/', Path.DirectorySeparatorChar));

            // Keep Unity's own settings object in step too, where it can be reached.
            try
            {
                var property = GradlePathProperty();
                if (property != null && property.CanWrite) property.SetValue(null, custom ? path : string.Empty);
            }
            catch
            {
                // The preferences above are what Unity reads; this is only a courtesy.
            }

            // Confirm it stuck.
            var now = ReadGradlePath(out _);
            return custom
                ? string.Equals(Path.GetFullPath(now), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                : now.Length == 0;
        }

        /// <summary>The Gradle version Unity builds with right now: the custom one, else the bundled one.</summary>
        internal static Version InUse()
        {
            var custom = ReadGradlePath(out var readable);
            if (!readable) return null;
            return string.IsNullOrEmpty(custom) ? BundledVersion() : VersionAt(custom);
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
