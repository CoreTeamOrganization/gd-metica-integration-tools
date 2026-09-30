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
            "Downloads the pinned Metica SDK release from GitHub and imports it. Any other installed " +
            "version is removed first — importing over it would leave old files behind that still " +
            "compile. Then External Dependency Manager's Force Resolve pulls every Android library the " +
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
            "ProjectSettings/AndroidResolverDependencies.xml"
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

            var installed = InstalledVersion();

            if (installed == null)
            {
                result.Problem("Metica SDK not installed.");
                return result.Seal();
            }

            if (IsStale(installed, out var target))
            {
                result.Problem($"Metica {installed} installed — target is {target}.");
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
                result.Problem("Metica SDK hasn't compiled — check the Console.");
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

            var installed = InstalledVersion();

            if (installed != null && IsStale(installed, out _))
            {
                RemoveInstalledSdk(installed);
                return;
            }

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

            var installed = InstalledVersion();
            if (installed != null && IsStale(installed, out _)) return $"Remove Metica {installed}";
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

        private void RemoveInstalledSdk(string installed)
        {
            // The whole folder goes; its contents are listed so it is clear what that means.
            var contents = DeleteConfirm.Contents(MeticaPaths.MeticaSdkRoot);
            var paths = new List<string> { $"{MeticaPaths.MeticaSdkRoot}/  (the whole folder)" };
            paths.AddRange(contents.Select(path => "    " + path));

            if (!DeleteConfirm.Ask($"Remove Metica {installed}", paths)) return;

            AssetDatabase.DeleteAsset(MeticaPaths.MeticaSdkRoot);
            AssetDatabase.Refresh();

            MeticaIntegrationLog.Record(Title, $"Removed Metica {installed} from {MeticaPaths.MeticaSdkRoot}");
        }

        // ── Reading the project ────────────────────────────────────────────────

        /// <summary>Version from the installed package.json, or null when Metica is absent.</summary>
        private static string InstalledVersion()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MeticaPackageJson)) return null;

            var json = File.ReadAllText(MeticaPaths.ToAbsolute(MeticaPaths.MeticaPackageJson));
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Stale means "does not match the pinned target version" — read from this project's own
        /// override (MeticaPaths.TargetVersionAsset) if it created one, otherwise the version the
        /// package ships with by default. Older, newer, or a GDSDK project that installed Metica
        /// long before this tool existed all count as stale. Missing or unreadable counts as
        /// fine: the tool should not demand a delete when it cannot tell what the target actually
        /// is.
        /// </summary>
        private static bool IsStale(string installed, out string target)
        {
            target = TargetVersion();
            return target != null && installed != target;
        }

        private static string TargetVersion() => TargetVersions.MeticaSdk;

        private static void Require(VerifyResult result, string path)
        {
            if (!MeticaPaths.FileExists(path))
                result.Problem($"{Path.GetFileName(path)} missing — re-import the SDK.");
        }
    }
}
