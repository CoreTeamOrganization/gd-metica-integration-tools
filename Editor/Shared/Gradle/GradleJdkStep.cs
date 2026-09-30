using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // A folder that is not a JDK breaks the Gradle build outright.
            if (!IsJdk(jdkHome))
            {
                result.Problem($"Not a JDK (no bin/java): {jdkHome}");
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
            // An installed JDK 17+ is offered directly, when this machine has one and it is not
            // already the one set.
            var installed = FindInstalledJdk();
            if (installed != null && !result.Ok)
                yield return new StepButton($"Use JDK {installed.Value.version} at {installed.Value.home}",
                    () => UseJdk(installed.Value.home));

            if (!result.Ok)
                yield return new StepButton("Download JDK 17…", DownloadAndUse, icon: StepIcon.Folder);

            yield return new StepButton("Choose a JDK folder…", ChooseJdkFolder, icon: StepIcon.Folder);
        }

        /// <summary>
        /// Asks for a folder, downloads the latest Eclipse Temurin 17 for this OS and CPU from
        /// Adoptium's API, unpacks it there and sets it as org.gradle.java.home — Metica's step 3.
        /// </summary>
        private static void DownloadAndUse()
        {
            const string title = "JDK 17 for Gradle";
            var parent = EditorUtility.OpenFolderPanel("Where should JDK 17 go?", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(parent)) return;

            var windows = Application.platform == RuntimePlatform.WindowsEditor;
            var os = windows ? "windows" : Application.platform == RuntimePlatform.OSXEditor ? "mac" : "linux";
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                       System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x64";
            var url = $"https://api.adoptium.net/v3/binary/latest/17/ga/{os}/{arch}/jdk/hotspot/normal/eclipse";
            var archive = Path.Combine(Application.temporaryCachePath, windows ? "jdk17.zip" : "jdk17.tar.gz");

            var before = Directory.GetDirectories(parent);

            if (!Downloads.TryDownload(url, archive, "Downloading JDK 17", out var error))
            {
                MeticaIntegrationLog.Record(title, $"Could not download JDK 17: {error}");
                return;
            }

            if (!Unpack(archive, parent, windows, out error))
            {
                MeticaIntegrationLog.Record(title, $"Could not unpack JDK 17: {error}");
                return;
            }

            File.Delete(archive);

            // The archive holds one top folder (jdk-17.0.x+y); on macOS the JDK itself is in Contents/Home.
            var home = Directory.GetDirectories(parent)
                .Except(before)
                .Concat(Directory.GetDirectories(parent, "jdk-17*"))
                .Select(folder => IsJdk(folder) ? folder : Path.Combine(folder, "Contents", "Home"))
                .FirstOrDefault(IsJdk);

            if (home == null)
            {
                MeticaIntegrationLog.Record(title, $"Unpacked into {parent}, but found no JDK there. Choose its folder by hand.");
                return;
            }

            MeticaIntegrationLog.Record(title, $"Downloaded JDK {VersionAt(home)} to {home}");
            UseJdk(home);
        }

        /// <summary>Windows gets a .zip; macOS and Linux a .tar.gz, unpacked with the system tar (keeps permissions).</summary>
        private static bool Unpack(string archive, string folder, bool zip, out string error)
        {
            if (zip) return Downloads.TryUnzip(archive, folder, "Unpacking JDK 17", out error);

            error = null;
            try
            {
                EditorUtility.DisplayProgressBar("Unpacking JDK 17", archive, 0.5f);
                var tar = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{archive}\" -C \"{folder}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                tar?.WaitForExit();
                if (tar == null || tar.ExitCode != 0) error = "tar failed.";
                return error == null;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── Actions ────────────────────────────────────────────────────────────

        private static void ChooseJdkFolder()
        {
            var chosen = EditorUtility.OpenFolderPanel("JDK 17 or later — the folder that holds bin/", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(chosen)) return;

            if (!IsJdk(chosen))
            {
                EditorUtility.DisplayDialog("Not a JDK",
                    $"{chosen} is not a JDK — it has no bin/java.\n\nPick the JDK's own folder, e.g. " +
                    "C:\\Program Files\\Eclipse Adoptium\\jdk-17…", "OK");
                return;
            }

            UseJdk(chosen);
        }

        private static void UseJdk(string chosen)
        {
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

        // ── Finding a JDK ───────────────────────────────────────────────────────

        /// <summary>A JDK has its launcher in bin/ — java.exe on Windows, java elsewhere.</summary>
        private static bool IsJdk(string directory) =>
            !string.IsNullOrEmpty(directory)
            && (File.Exists(Path.Combine(directory, "bin", "java.exe")) || File.Exists(Path.Combine(directory, "bin", "java")));

        /// <summary>
        /// The newest JDK 17+ on this machine: JAVA_HOME, the JDK set in Unity's External Tools,
        /// and the usual install folders (Adoptium, Oracle, Corretto, Microsoft, Zulu; macOS's
        /// JavaVirtualMachines). Null when there is none.
        /// </summary>
        private static (string home, Version version)? FindInstalledJdk()
        {
            var candidates = new List<string>
            {
                Environment.GetEnvironmentVariable("JAVA_HOME"),
                EditorPrefs.GetString("JdkPath", string.Empty),
                EditorPrefs.GetString("Jdk11Path", string.Empty)
            };

            var roots = Application.platform == RuntimePlatform.OSXEditor
                ? new[] { "/Library/Java/JavaVirtualMachines" }
                : new[]
                {
                    @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Java", @"C:\Program Files\Amazon Corretto",
                    @"C:\Program Files\Microsoft", @"C:\Program Files\Zulu"
                };

            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var folder in Directory.GetDirectories(root))
                        candidates.Add(Application.platform == RuntimePlatform.OSXEditor
                            ? Path.Combine(folder, "Contents", "Home")
                            : folder);
                }
                catch
                {
                    // Unreadable folder — skip it.
                }
            }

            return candidates
                .Where(IsJdk)
                .Select(home => (home, version: VersionAt(home)))
                .Where(jdk => jdk.version != null && jdk.version >= Required)
                .OrderByDescending(jdk => jdk.version)
                .Select(jdk => ((string, Version)?)jdk)
                .FirstOrDefault();
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
