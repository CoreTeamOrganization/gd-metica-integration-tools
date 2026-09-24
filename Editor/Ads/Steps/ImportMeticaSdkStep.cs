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
            TargetVersion() is string target ? $"Install the Metica SDK v{target}." : "Install the Metica SDK.";

        public override string Why =>
            "Downloads the pinned Metica SDK release from GitHub and imports it. Any other installed " +
            "version is removed first — importing over it would leave old files behind that still " +
            "compile. Use Change target version to pin a different release for this project only.";

        public override string ActionLabel => ActionForState();

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.MeticaSdkRoot,
            MeticaPaths.MeticaSdkAsmdef
        };

        public override string ReviewHint => "Only Assets/MeticaSdk should change.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.HasGDSdk) ReportSdkVersion(result);

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
                result.Problem("Metica SDK hasn't compiled — check the Console.");
            else
                result.Note($"Metica {installed}");

            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            var installed = InstalledVersion();

            if (installed != null && IsStale(installed, out _))
            {
                RemoveInstalledSdk(installed);
                return;
            }

            InstallSelectedRelease();
        }

        private string ActionForState()
        {
            var installed = InstalledVersion();
            if (installed != null && IsStale(installed, out _)) return $"Remove Metica {installed}";
            if (installed != null) return "Re-import";

            var target = TargetVersion();
            return target == null ? "Download and import" : $"Download and import {target}";
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        public override void DrawBody(VerifyResult result)
        {
            if (GUILayout.Button("Change target version…")) OpenTargetVersionOverride();
        }

        /// <summary>
        /// Selects this project's own target-version asset, copying the packaged default into
        /// the project first if there is none yet — the packaged one is shared across every
        /// project on this package version and is often read-only (git packages live in
        /// Library/PackageCache).
        /// </summary>
        private static void OpenTargetVersionOverride()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.TargetVersionAsset))
            {
                if (MeticaPaths.PackagedTargetVersionAsset == null ||
                    !MeticaPaths.FileExists(MeticaPaths.PackagedTargetVersionAsset))
                {
                    MeticaIntegrationLog.Record("Metica SDK", "Could not find the packaged default to copy.");
                    return;
                }

                var folder = Path.GetDirectoryName(MeticaPaths.ToAbsolute(MeticaPaths.TargetVersionAsset));
                Directory.CreateDirectory(folder ?? ".");
                AssetDatabase.Refresh();

                AssetDatabase.CopyAsset(MeticaPaths.PackagedTargetVersionAsset, MeticaPaths.TargetVersionAsset);
                MeticaIntegrationLog.Record("Metica SDK", $"Created {MeticaPaths.TargetVersionAsset}");
            }

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.TargetVersionAsset);
        }

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
            if (!EditorUtility.DisplayDialog("Remove the Metica SDK",
                $"Delete {MeticaPaths.MeticaSdkRoot} (Metica {installed})?", "Delete", "Cancel"))
                return;

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

        /// <summary>
        /// The project's own override if it created one, otherwise the version the package
        /// ships with by default.
        /// </summary>
        private static string TargetVersion()
        {
            var path = MeticaPaths.FileExists(MeticaPaths.TargetVersionAsset)
                ? MeticaPaths.TargetVersionAsset
                : MeticaPaths.PackagedTargetVersionAsset;

            if (path == null || !MeticaPaths.FileExists(path)) return null;

            var asset = AssetDatabase.LoadAssetAtPath<MeticaTargetVersion>(path);
            return string.IsNullOrEmpty(asset?.Version) ? null : asset.Version;
        }

        private static void ReportSdkVersion(VerifyResult result)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.InitializeOnLoad)) return;

            var source = SourcePatcher.ReadAll(MeticaPaths.InitializeOnLoad);
            var match = Regex.Match(source, "string\\s+Version\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)\"");
            if (!match.Success) return;

            var version = match.Groups[1].Value;
            result.Note($"GD SDK {version}");
        }

        private static void Require(VerifyResult result, string path)
        {
            if (!MeticaPaths.FileExists(path))
                result.Problem($"{Path.GetFileName(path)} missing — re-import the SDK.");
        }
    }
}
