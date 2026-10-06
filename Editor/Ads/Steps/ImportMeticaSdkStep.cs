using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Gets a current Metica SDK into the project.
    ///
    /// <para>Only one version counts as current: the one pinned in MeticaTargetVersion.asset,
    /// resolved against the ten most recent published releases. A project already on it is
    /// left alone; a project on any other version has to have it removed first, because
    /// importing a package over a different version leaves both sets of files behind and the
    /// stale ones still compile.</para>
    /// </summary>
    public sealed class ImportMeticaSdkStep : MeticaStep
    {
        public override string Title => "Metica SDK";

        public override string Summary =>
            TargetVersion() is string target
                ? $"Install the Metica SDK v{target} and resolve its Android libraries."
                : "Install the Metica SDK and resolve its Android libraries.";

        public override string Why =>
            "Downloads the pinned Metica SDK release from GitHub and imports it. Any other Metica is " +
            "removed first, wherever it is — another version in Assets, Metica v1 or 2.x from the " +
            "Package Manager (com.metica.unity / com.metica.sdk.unity), v1's MeticaSdkConfiguration " +
            "asset — since two Metica SDKs side by side do not compile. Analytics abstractions and " +
            "this tool are never touched. Then External Dependency Manager's Force Resolve pulls every Android library the " +
            "project's SDKs declare into mainTemplate.gradle (turning Custom Main Gradle Template on if " +
            "needed); the step passes only once all of them are there. It needs Android as the build " +
            "target. Enable iOS also ticks iOS on MeticaSDKFramework.xcframework, which Metica's iOS " +
            "build step needs. Change target version pins a different release for this project only.";

        public override string ActionLabel => ActionForState();

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.MeticaSdkRoot,
            MeticaPaths.MainTemplateGradle,
            MeticaPaths.MeticaXcFramework + ".meta",
            "ProjectSettings/AndroidResolverDependencies.xml",

            // Where old Metica is removed from.
            "Packages/manifest.json",
            "Packages/packages-lock.json",
            "Assets/Metica"
        };

        public override string ReviewHint => "Assets/MeticaSdk, plus the resolved libraries in mainTemplate.gradle.";

        /// <summary>
        /// Off by default: most runs are Android-only, and enabling iOS unasked would make the
        /// step demand an iOS framework state nobody touched. Per project. Also read by the
        /// Metica settings step, which only asks for iOS keys when this is on.
        /// </summary>
        internal static bool EnableIos
        {
            get => EditorPrefs.GetBool(EnableIosKey, false);
            set => EditorPrefs.SetBool(EnableIosKey, value);
        }

        private static string EnableIosKey =>
            $"GameDistrict.MeticaIntegrationTools.ResolveLibraries.EnableIos.{Application.dataPath.GetHashCode():X8}";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.HasGDSdk)
            {
                var gdSdk = GdSdkVersion.Reported();
                if (gdSdk != null) result.Note($"GD SDK {gdSdk}");

                if (!GdSdkVersion.IsSupported(gdSdk))
                {
                    result.Problem($"GD SDK {gdSdk} isn't supported — needs {GdSdkVersion.Minimum}+.");
                    return result.Seal();
                }
            }

            if (!MeticaPaths.DirectoryExists("Assets/MaxSdk/Scripts"))
                result.Problem("Install the AppLovin MAX plugin first.");

            // Any other Metica — another version, v1 from the Package Manager, a copy elsewhere
            // in Assets — goes before the target comes in.
            var old = MeticaInstalls.Find(TargetVersion());
            if (old.Count > 0)
            {
                result.Problem($"Old Metica found — remove it, then add {TargetVersion() ?? "the target version"}.");
                foreach (var install in old) result.Problem("Remove: " + install.Label);
                return result.Seal();
            }

            var installed = InstalledVersion();

            if (installed == null)
            {
                result.Problem("Metica SDK not installed.");
                return result.Seal();
            }

            Require(result, MeticaPaths.MeticaSdkAsmdef);
            Require(result, MeticaPaths.MeticaSdkRoot + "/Runtime/Sdk/MeticaSdk.cs");
            Require(result, MeticaPaths.MeticaDependencies);
            Require(result, MeticaPaths.MeticaSdkRoot + "/Editor/MeticaIOSBuildPostProcessor.cs");

            if (!MeticaPaths.DirectoryExists(MeticaPaths.MeticaXcFramework))
                result.Problem("iOS framework missing — re-import with everything selected.");

            if (result.Problems.Count > 0) return result.Seal();

            if (!TemplateWriter.TypeIsLoaded("Metica.MeticaSdk"))
            {
                if (ScriptCompile.Pending(MeticaPaths.MeticaSdkRoot)) result.Wait();
                else result.Problem("Metica SDK hasn't compiled — check the Console.");
                return result.Seal();
            }

            result.Note($"Metica {installed}");

            // Installed is not done: its Android libraries have to reach the build too.
            AndroidDependencies.Report(result, MeticaPaths.MeticaSdkRoot);

            if (MeticaPaths.FileExists(MeticaPaths.GradleProperties))
            {
                var properties = SourcePatcher.ReadAll(MeticaPaths.GradleProperties);
                if (!properties.Contains("android.useAndroidX=true"))
                    result.Problem("gradleTemplate.properties needs android.useAndroidX=true.");
                if (!properties.Contains("android.enableJetifier=true"))
                    result.Problem("gradleTemplate.properties needs android.enableJetifier=true.");
            }

            if (EnableIos)
            {
                var importer = LoadXcFrameworkImporter();
                if (importer == null) result.Note("Couldn't check the iOS framework.");
                else if (!importer.GetCompatibleWithPlatform(BuildTarget.iOS)) result.Problem("iOS framework isn't enabled for iOS.");
                else result.Note("iOS enabled");
            }

            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            if (!Supported)
            {
                MeticaIntegrationLog.Record(Title,
                    $"GD SDK {GdSdkVersion.Reported()} isn't supported — needs {GdSdkVersion.Minimum}+. Nothing changed.");
                return;
            }

            var old = MeticaInstalls.Find(TargetVersion());
            if (old.Count > 0)
            {
                MeticaInstalls.Remove(old, Title, null);
                return;
            }

            var installed = InstalledVersion();

            if (installed != null && (AndroidDependencies.NeedsResolve(MeticaPaths.MeticaSdkRoot) || IosPending))
            {
                if (IosPending) EnableIosFramework();
                if (AndroidDependencies.NeedsResolve(MeticaPaths.MeticaSdkRoot)) AndroidDependencies.Resolve(Title);
                return;
            }

            InstallSelectedRelease();
        }

        /// <summary>Enable iOS is on, and the framework is not enabled for iOS yet.</summary>
        private static bool IosPending
        {
            get
            {
                if (!EnableIos) return false;
                var importer = LoadXcFrameworkImporter();
                return importer != null && !importer.GetCompatibleWithPlatform(BuildTarget.iOS);
            }
        }

        /// <summary>Standalone projects, and GD SDK 5.3.0 or newer (or unreadable).</summary>
        private static bool Supported => !MeticaPaths.HasGDSdk || GdSdkVersion.IsSupported(GdSdkVersion.Reported());

        private string ActionForState()
        {
            if (!Supported) return null;

            var old = MeticaInstalls.Find(TargetVersion());
            if (old.Count == 1) return old[0].Version == null ? "Remove old Metica" : $"Remove Metica {old[0].Version}";
            if (old.Count > 1) return $"Remove old Metica ({old.Count})";

            var installed = InstalledVersion();
            if (installed != null && AndroidDependencies.NeedsResolve(MeticaPaths.MeticaSdkRoot))
                return IosPending ? "Resolve Android, enable iOS" : "Resolve Android dependencies";
            if (installed != null && IosPending) return "Enable iOS framework";
            if (installed != null) return "Re-import";

            var target = TargetVersion();
            return target == null ? "Download and import" : $"Download and import {target}";
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Change target version only before the SDK is imported — after that the version is
        /// settled; the Enable iOS switch once it is.
        /// </summary>
        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (!Supported) yield break;

            if (InstalledVersion() == null)
                yield return new StepButton("Change target version…", TargetVersions.OpenOverride);
            else
                yield return new StepToggle("Enable iOS", EnableIos, value => EnableIos = value);
        }

        private static void EnableIosFramework()
        {
            var importer = LoadXcFrameworkImporter();
            if (importer == null)
            {
                MeticaIntegrationLog.Record("Metica SDK",
                    "Metica xcframework importer not found — tick iOS on it in the Inspector by hand.");
                return;
            }

            if (importer.GetCompatibleWithPlatform(BuildTarget.iOS)) return;

            importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
            importer.SaveAndReimport();
            MeticaIntegrationLog.Record("Metica SDK", "Enabled the Metica xcframework for iOS");
        }

        private static PluginImporter LoadXcFrameworkImporter() =>
            MeticaPaths.DirectoryExists(MeticaPaths.MeticaXcFramework)
                ? AssetImporter.GetAtPath(MeticaPaths.MeticaXcFramework) as PluginImporter
                : null;

        // ── Install / remove ───────────────────────────────────────────────────

        private void InstallSelectedRelease()
        {
            var target = TargetVersion();
            if (target == null)
            {
                MeticaIntegrationLog.Record(Title, "No target version set.");
                return;
            }

            if (!MeticaReleases.TryFetch(out var releases) || releases.Count == 0)
            {
                MeticaIntegrationLog.Record(Title,
                    MeticaReleases.LastError ?? "Could not read the Metica release list.");
                return;
            }

            var chosen = releases.FirstOrDefault(release => release.Version == target);
            if (chosen == null)
            {
                MeticaIntegrationLog.Record(Title,
                    $"Metica {target} is not among the ten most recent releases — change the target " +
                    $"version, or install it by hand from {MeticaReleases.ReleasesPage}.");
                return;
            }

            if (!MeticaReleases.TryDownload(chosen, out var package))
            {
                MeticaIntegrationLog.Record(Title, MeticaReleases.LastError ?? "Download failed.");
                return;
            }

            MeticaIntegrationLog.Record(Title, $"Downloaded Metica {chosen.Tag}");

            // Interactive, so the contents are visible before anything is written.
            AssetDatabase.ImportPackage(package, true);
        }

        // ── Reading the project ────────────────────────────────────────────────

        /// <summary>
        /// Version from Assets/MeticaSdk/package.json, or null when it is absent. Only the
        /// target lives there by the time this matters — <see cref="MeticaInstalls.Find"/>
        /// lists any other version for removal first.
        /// </summary>
        private static string InstalledVersion()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MeticaPackageJson)) return null;

            var json = File.ReadAllText(MeticaPaths.ToAbsolute(MeticaPaths.MeticaPackageJson));
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// The pinned version — this project's override (MeticaPaths.TargetVersionAsset) if it
        /// created one, otherwise the version the package ships with. Anything else installed
        /// counts as old. Unreadable means no version is judged old: the tool does not demand a
        /// delete when it cannot tell what the target is.
        /// </summary>
        private static string TargetVersion() => TargetVersions.MeticaSdk;

        private static void Require(VerifyResult result, string path)
        {
            if (!MeticaPaths.FileExists(path))
                result.Problem($"{Path.GetFileName(path)} missing — re-import the SDK.");
        }
    }
}
