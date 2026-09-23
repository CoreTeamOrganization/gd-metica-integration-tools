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
            "Downloads and imports a Metica SDK release, or confirms the one already installed matches " +
            "the target version. Anything else is removed first.";

        public override string ActionLabel => ActionForState();

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.MeticaSdkRoot,
            MeticaPaths.MeticaSdkAsmdef
        };

        public override string ReviewHint =>
            "Expect a large diff — a whole SDK arrived. What matters is that nothing outside " +
            "Assets/MeticaSdk changed.";

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            // No GD SDK is not a fault — it is the standalone install, and the wizard has
            // already switched to the run that suits it.
            if (MeticaPaths.HasGDSdk)
                ReportSdkVersion(result);
            else
                result.Note("No GD Monetization SDK in this project — installing Metica on its own.");

            if (!MeticaPaths.DirectoryExists("Assets/MaxSdk/Scripts"))
                result.Problem("AppLovin MAX plugin not found at Assets/MaxSdk. Metica mediates through " +
                               "MAX, and Metica.SDK.asmdef references MaxSdk.Scripts, so MAX has to be " +
                               "installed first.");

            var installed = InstalledVersion();

            if (installed == null)
            {
                result.Problem("No Metica SDK in the project. Press the button below to import the " +
                               "target version.");
                return result.Seal();
            }

            result.Note($"Metica Unity SDK {installed} installed");

            if (IsStale(installed, out var target))
            {
                result.Problem($"Metica {installed} does not match the target version {target}. Remove " +
                               "it first — importing a different version over it leaves the old files " +
                               "behind, and they still compile.");
                return result.Seal();
            }

            Require(result, MeticaPaths.MeticaSdkAsmdef, "the Metica.SDK assembly definition");
            Require(result, MeticaPaths.MeticaSdkRoot + "/Runtime/Sdk/MeticaSdk.cs", "the MeticaSdk entry point");
            Require(result, MeticaPaths.MeticaDependencies, "the Android dependency declaration");
            Require(result, MeticaPaths.MeticaSdkRoot + "/Editor/MeticaIOSBuildPostProcessor.cs",
                "the iOS build post-processor");

            if (!MeticaPaths.DirectoryExists(MeticaPaths.MeticaXcFramework))
                result.Problem($"Missing {MeticaPaths.MeticaXcFramework} — the iOS binary did not import. " +
                               "Re-import with every item selected.");

            if (result.Problems.Count > 0) return result.Seal();

            if (!TemplateWriter.TypeIsLoaded("Metica.MeticaSdk"))
                result.Problem("Metica.SDK has not compiled. Wait for Unity to finish compiling and " +
                               "Re-check; if it stays red, the Console has the reason.");
            else
                result.Note("Metica.SDK compiled");

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

            return installed == null ? "Download and import" : "Re-import the selected release";
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        public override void DrawBody(VerifyResult result)
        {
            var installed = InstalledVersion();
            if (installed != null && IsStale(installed, out _))
            {
                EditorGUILayout.HelpBox(
                    $"Metica {installed} does not match the target version. The button deletes " +
                    $"{MeticaPaths.MeticaSdkRoot}; after that, Download and import fetches the target " +
                    "version.",
                    MessageType.Warning);
                return;
            }

            var target = TargetVersion();
            EditorGUILayout.LabelField(
                target == null
                    ? "No target version set — the button has nothing to install."
                    : $"Target version: Metica {target}. The button downloads and imports it from GitHub.",
                EditorStyles.wordWrappedLabel);

            if (MeticaPaths.FileExists(MeticaPaths.TargetVersionAsset))
            {
                if (GUILayout.Button("Open this project's target version override"))
                    Selection.activeObject =
                        AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.TargetVersionAsset);
            }
            else if (GUILayout.Button("Pin a different target version for this project"))
            {
                CreateLocalTargetVersionOverride();
            }
        }

        /// <summary>
        /// Copies the packaged default into the project so it can be edited — the packaged
        /// asset itself is shared across every project on this package version and is often
        /// read-only (git packages live in Library/PackageCache).
        /// </summary>
        private static void CreateLocalTargetVersionOverride()
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
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.TargetVersionAsset);

            MeticaIntegrationLog.Record("Metica SDK", $"Created {MeticaPaths.TargetVersionAsset}");
        }

        // ── Install / remove ───────────────────────────────────────────────────

        private void InstallSelectedRelease()
        {
            var target = TargetVersion();
            if (target == null)
            {
                MeticaIntegrationLog.Record(Title, "MeticaTargetVersion.asset has no version set.");
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
                    $"Target version {target} is not among the ten most recent releases. Pin a newer " +
                    "target version (button above), or install it by hand from " +
                    $"{MeticaReleases.ReleasesPage}.");
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
                $"Delete {MeticaPaths.MeticaSdkRoot}?\n\nMetica {installed} does not match the target " +
                "version. Importing a different package over it would leave the old files in place.",
                "Delete", "Cancel"))
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
            if (!MeticaPaths.FileExists(MeticaPaths.InitializeOnLoad))
            {
                result.Note($"GD Monetization SDK at {MeticaPaths.GDRoot} (version not readable)");
                return;
            }

            var source = SourcePatcher.ReadAll(MeticaPaths.InitializeOnLoad);
            var match = Regex.Match(source, "string\\s+Version\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)\"");

            if (!match.Success)
            {
                result.Note($"GD Monetization SDK at {MeticaPaths.GDRoot} (version not readable)");
                return;
            }

            var version = match.Groups[1].Value;
            result.Note($"GD Monetization SDK {version} at {MeticaPaths.GDRoot}");

            if (int.TryParse(version.Split('.')[0], out var major) && major >= 6)
                result.Note("This workflow is built for the v5 line. On 6.x the Metica ads wrapper already " +
                            "ships with the SDK, so check what is actually missing before letting the tool " +
                            "write anything.");
        }

        private static void Require(VerifyResult result, string path, string what)
        {
            if (!MeticaPaths.FileExists(path))
                result.Problem($"Missing {path} — {what} did not import.");
        }
    }
}
