using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Gets AppLovin MAX to the version Metica needs. Only part of the run when MAX is missing
    /// or below <see cref="Minimum"/> — a project already on 8.1.0+ never sees it.
    ///
    /// <para>Installs the version pinned in MeticaTargetVersion.asset (8.1.0 by default, the
    /// closest to the MAX an old project has): AppLovin's own Unity plugin package from their
    /// GitHub releases, the same package the Integration Manager imports.</para>
    ///
    /// <para>An older MAX is removed first, like the Metica SDK — importing over it would leave
    /// old files behind. Removed means the plugin's own files: the mediation adapters
    /// (Assets/MaxSdk/Mediation) and AppLovinSettings.asset (the SDK key) are the project's
    /// own setup and are kept.</para>
    /// </summary>
    public sealed class AppLovinMaxStep : MeticaStep
    {
        /// <summary>What Metica needs. The version installed comes from the target-version asset.</summary>
        public static readonly Version Minimum = new Version(8, 1, 0);

        private const string MaxRoot = "Assets/MaxSdk";
        private const string KeptAdapters = "Mediation";
        private const string KeptSettings = "AppLovinSettings.asset";

        private const string ReleaseApi = "https://api.github.com/repos/AppLovin/AppLovin-MAX-Unity-Plugin/releases/tags/";

        private static readonly Regex DeclaredVersion = new Regex("_version\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)\"");
        private static readonly Regex PackageAsset =
            new Regex("\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.unitypackage)\"", RegexOptions.IgnoreCase);

        public override string Title => "AppLovin MAX version";

        public override string Summary => $"Metica needs AppLovin MAX {Minimum} or newer.";

        public override string Why =>
            $"Metica mediates through AppLovin MAX and needs {Minimum}+. The tool installs the version set in " +
            "MeticaTargetVersion.asset — AppLovin's official Unity plugin package from their GitHub releases, " +
            "the same one the Integration Manager imports. An old MAX is removed first, as its own action, " +
            "so no stale files remain; your mediation adapters (Assets/MaxSdk/Mediation) and AppLovinSettings " +
            "(SDK key) are kept. Scripts that use MAX won't compile until the import — that's expected. Then " +
            "Force Resolve pulls every Android library the project's SDKs declare into the build; the step " +
            "passes once all of them are there.";

        /// <summary>
        /// While MAX is below the floor — and, once this step has started replacing it, until
        /// the end, so the resolve after the import still happens here instead of the step
        /// vanishing the moment MAX reaches 8.1.0.
        /// </summary>
        internal override bool Applies
        {
            get
            {
                var installed = Installed();
                return installed == null || installed < Minimum || Upgrading;
            }
        }

        private static string UpgradingKey =>
            $"GameDistrict.MeticaIntegrationTools.MaxUpgrade.{Application.dataPath.GetHashCode():X8}";

        /// <summary>This project's MAX was replaced by this step. Per project, kept after it is done.</summary>
        private static bool Upgrading
        {
            get => EditorPrefs.GetBool(UpgradingKey, false);
            set => EditorPrefs.SetBool(UpgradingKey, value);
        }

        public override string ActionLabel
        {
            get
            {
                var target = TargetVersions.Max;
                if (target == null) return null;

                // Separate actions, like the Metica SDK, so each shows up as its own change:
                // remove, import, then resolve.
                var installed = Installed();
                if (installed == null) return $"Download and import MAX {target}";
                if (installed < Minimum) return $"Remove MAX {installed}";
                return AndroidDependencies.NeedsResolve(MaxRoot) ? "Resolve Android dependencies" : null;
            }
        }

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MaxRoot,
            "Assets/ExternalDependencyManager",
            MeticaPaths.MainTemplateGradle,
            "ProjectSettings/AndroidResolverDependencies.xml"
        };

        public override string ReviewHint =>
            "Assets/MaxSdk (Mediation/ and AppLovinSettings stay), EDM, and the resolved libraries in mainTemplate.gradle.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var installed = Installed();

            if (installed == null) result.Problem("AppLovin MAX isn't installed.");
            else if (installed < Minimum) result.Problem($"MAX {installed} is below {Minimum}.");
            else
            {
                result.Note($"MAX {installed}");
                AndroidDependencies.Report(result, MaxRoot);
            }

            if (TargetVersions.Max == null) result.Problem("No MAX version set in MeticaTargetVersion.asset.");
            else result.Note($"Target MAX {TargetVersions.Max}");

            return result.Seal();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            var installed = Installed();
            if (installed != null && installed >= Minimum) yield break;

            yield return new StepButton("Change target version…", TargetVersions.OpenOverride);
            yield return new StepButton("Open Integration Manager", OpenIntegrationManager);
        }

        /// <summary>
        /// An old MAX is removed first, as its own action; the import happens on the next
        /// press, once MAX is gone — interactive, so the contents are visible before anything
        /// is written.
        /// </summary>
        public override void Apply()
        {
            var target = TargetVersions.Max;
            if (target == null) return;

            var installed = Installed();
            if (installed != null && installed >= Minimum)
            {
                AndroidDependencies.Resolve(Title);
                return;
            }

            if (installed != null)
            {
                var paths = PathsToRemove();
                if (DeleteConfirm.Ask($"Remove AppLovin MAX {installed}", paths,
                        $"{MaxRoot}/{KeptAdapters}/ (your adapters) and {MaxRoot}/Resources/{KeptSettings} (SDK key)"))
                    RemoveOldMax(installed, paths);
                return;
            }

            if (!TryDownload(target, out var package)) return;

            Upgrading = true;
            AssetDatabase.ImportPackage(package, true);
        }

        private bool TryDownload(string version, out string package)
        {
            package = null;

            string url;
            using (var request = UnityWebRequest.Get(ReleaseApi + "release_" + version.Replace('.', '_')))
            {
                request.SetRequestHeader("User-Agent", "GameDistrict-MeticaIntegrationTools");
                var operation = request.SendWebRequest();
                while (!operation.isDone) System.Threading.Thread.Sleep(20);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    MeticaIntegrationLog.Record(Title, $"Could not find the MAX {version} release: {request.error}");
                    return false;
                }

                var match = PackageAsset.Match(request.downloadHandler.text);
                if (!match.Success)
                {
                    MeticaIntegrationLog.Record(Title, $"The MAX {version} release has no .unitypackage attached.");
                    return false;
                }

                url = match.Groups[1].Value;
            }

            package = Path.Combine(Application.temporaryCachePath, Path.GetFileName(url));
            if (!Downloads.TryDownload(url, package, $"Downloading AppLovin MAX {version}", out var error))
            {
                MeticaIntegrationLog.Record(Title, $"Could not download MAX {version}: {error}");
                return false;
            }

            MeticaIntegrationLog.Record(Title, $"Downloaded AppLovin MAX {version}");
            return true;
        }

        /// <summary>
        /// The plugin's own folders and files: everything in Assets/MaxSdk except Mediation/,
        /// and everything in Resources/ except AppLovinSettings.asset. What the dialog lists
        /// is exactly what gets deleted.
        /// </summary>
        private static List<string> PathsToRemove()
        {
            var paths = DeleteConfirm.Contents(MaxRoot, KeptAdapters, "Resources");
            paths.AddRange(DeleteConfirm.Contents($"{MaxRoot}/Resources", KeptSettings));
            return paths;
        }

        private void RemoveOldMax(Version installed, List<string> paths)
        {
            Upgrading = true;

            var deleted = paths.Where(path => AssetDatabase.DeleteAsset(path.TrimEnd('/'))).ToList();
            AssetDatabase.Refresh();

            MeticaIntegrationLog.Record(Title,
                $"Removed AppLovin MAX {installed}: {string.Join(", ", deleted)}. Kept {KeptAdapters}/ and {KeptSettings}");
        }

        /// <summary>The MAX plugin version from MaxSdk.cs, or null when MAX is not installed.</summary>
        public static Version Installed()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MaxSdkVersionFile)) return null;

            var match = DeclaredVersion.Match(SourcePatcher.ReadAll(MeticaPaths.MaxSdkVersionFile));
            if (!match.Success) return null;

            var text = match.Groups[1].Value.Contains(".") ? match.Groups[1].Value : match.Groups[1].Value + ".0";
            return Version.TryParse(text, out var version) ? version : null;
        }

        private void OpenIntegrationManager()
        {
            // AppLovin has moved this menu around; try the known paths in turn.
            var opened = new[] { "AppLovin/Integration Manager", "AppLovin/Integration Manager...", "AppLovin/MAX/Integration Manager" }
                .Any(EditorApplication.ExecuteMenuItem);

            if (!opened)
                MeticaIntegrationLog.Record(Title, "Could not open the AppLovin Integration Manager — open it from the AppLovin menu.");
        }
    }
}
