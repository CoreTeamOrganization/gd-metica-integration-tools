using System;
using System.IO;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Resolves the project paths the wizard works with. Nothing is hardcoded to
    /// "Assets/GDMonetization" — the GD root is discovered from a known file, so the
    /// tool works in games that keep the SDK somewhere else.
    /// </summary>
    public static class MeticaPaths
    {
        public const string MeticaSdkRoot = "Assets/MeticaSdk";

        private static string _gdRoot;
        private static bool _gdRootScanned;

        /// <summary>
        /// Project-relative root of the GD Monetization SDK (e.g. "Assets/GDMonetization"),
        /// or null when the SDK is not in this project.
        ///
        /// <para>Finding it walks all of Assets, and this is read from OnGUI, so "not here"
        /// is cached too — that is the standalone case, and without the flag it would
        /// re-scan on every access.</para>
        /// </summary>
        public static string GDRoot
        {
            get
            {
                if (_gdRootScanned && (_gdRoot == null || Directory.Exists(ToAbsolute(_gdRoot))))
                    return _gdRoot;

                _gdRoot = FindGDRoot();
                _gdRootScanned = true;
                return _gdRoot;
            }
        }

        /// <summary>
        /// Whether the GD Monetization SDK is in this project. False means a standalone
        /// install: the tool writes a self-contained Metica ads runtime instead of wiring
        /// Metica into an SDK that is not there.
        /// </summary>
        public static bool HasGDSdk => GDRoot != null;

        // ── Standalone (non-GDSDK) install ─────────────────────────────────────

        /// <summary>Where the self-contained Metica ads runtime is written.</summary>
        public const string StandaloneRoot = "Assets/MeticaAds";

        /// <summary>Loaded by name at runtime, so it has to sit in a Resources folder.</summary>
        public const string StandaloneConfigAsset = "Assets/Resources/MeticaAdsConfig.asset";

        public static void ForgetCache()
        {
            _gdRoot = null;
            _gdRootScanned = false;
            _toolRoot = null;
            _toolRootResolved = false;
        }

        private static string FindGDRoot()
        {
            const string tail = "Runtime/Scripts/Ads/Core/AdsManager.cs";
            var assets = ToAbsolute("Assets");
            if (!Directory.Exists(assets)) return null;

            var hit = Directory.EnumerateFiles(assets, "AdsManager.cs", SearchOption.AllDirectories)
                .Select(ToProjectRelative)
                .FirstOrDefault(p => p != null && p.EndsWith(tail, StringComparison.Ordinal));

            if (hit == null) return null;
            return hit.Substring(0, hit.Length - tail.Length - 1);
        }

        // ── Runtime script paths ────────────────────────────────────────────────

        public static string RuntimeScripts => Combine(GDRoot, "Runtime/Scripts");
        public static string ResourcesConfigurations => Combine(GDRoot, "Runtime/Resources/Configurations");
        public static string EditorScripts => Combine(GDRoot, "Editor/Scripts");

        public static string AdsMeticaFolder => Combine(RuntimeScripts, "Ads/Metica");

        public static string AdPlatforms => Combine(RuntimeScripts, "Ads/AdPlatforms.cs");
        public static string AdRevenueInfo => Combine(RuntimeScripts, "Ads/AdRevenueInfo.cs");
        public static string AdsManager => Combine(RuntimeScripts, "Ads/Core/AdsManager.cs");
        public static string AdNetworkController => Combine(RuntimeScripts, "Ads/Core/AdNetworkController.cs");
        public static string IAdNetworkService => Combine(RuntimeScripts, "Ads/Core/IAdNetworkService.cs");
        public static string AdNetworkAdmob => Combine(RuntimeScripts, "Ads/Admob/AdNetworkAdmob.cs");
        public static string AdNetworkAppLovin => Combine(RuntimeScripts, "Ads/Applovin/AdNetworkAppLovin.cs");
        public static string InitializeOnLoad => Combine(RuntimeScripts, "MonetizationInitializeOnLoad.cs");
        public static string LoggerTag => Combine(RuntimeScripts, "Logger/Tag.cs");
        public static string ConfigurationsPath => Combine(RuntimeScripts, "MonetizationConfigurationsPath.cs");
        public static string Preferences => Combine(RuntimeScripts, "MonetizationPreferences.cs");
        public static string AdUnitsConfiguration => Combine(RuntimeScripts, "Configurations/AdUnitsConfiguration.cs");
        public static string RemoteConfiguration => Combine(RuntimeScripts, "Configurations/RemoteConfiguration.cs");
        public static string RemoteConfigManager => Combine(RuntimeScripts, "Remote/RemoteConfigManager.cs");
        public static string AnalyticsManager => Combine(RuntimeScripts, "Analytics/AnalyticsManager.cs");
        public static string MonetizationRemover => Combine(EditorScripts, "MenuItems/MonetizationRemover.cs");

        public static string MeticaSettingsAsset => Combine(ResourcesConfigurations, "MeticaSettings.asset");
        public static string AdUnitsSettingsAsset => Combine(ResourcesConfigurations, "AdUnitsSettings.asset");

        // ── Metica SDK paths ───────────────────────────────────────────────────

        public static string MeticaSdkAsmdef => MeticaSdkRoot + "/Runtime/Sdk/Metica.SDK.asmdef";
        public static string MeticaDependencies => MeticaSdkRoot + "/Editor/MeticaDependencies.xml";
        public static string MeticaPackageJson => MeticaSdkRoot + "/package.json";
        public static string MeticaXcFramework => MeticaSdkRoot + "/Plugins/iOS/MeticaSDKFramework.xcframework";

        // ── Platform build files ───────────────────────────────────────────────

        public const string MaxSdkVersionFile = "Assets/MaxSdk/Scripts/MaxSdk.cs";
        public const string MolocoDependencies = "Assets/MaxSdk/Mediation/Moloco/Editor/Dependencies.xml";

        public const string MainTemplateGradle = "Assets/Plugins/Android/mainTemplate.gradle";
        public const string BaseProjectTemplateGradle = "Assets/Plugins/Android/baseProjectTemplate.gradle";
        public const string GradleProperties = "Assets/Plugins/Android/gradleTemplate.properties";

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string _toolRoot;
        private static bool _toolRootResolved;

        /// <summary>
        /// Directory that holds this tool, so it can load its own templates. Resolved through
        /// the Package Manager rather than by scanning Assets — this tool ships as a package
        /// (Packages/com.gamedistrict.metica-integration-tools), not a copy inside Assets, and
        /// PackageInfo.FindForAssembly is the one lookup that works the same way whether the
        /// package is git-installed, embedded, or a local file: reference.
        /// </summary>
        public static string ToolRoot
        {
            get
            {
                if (_toolRootResolved) return _toolRoot;

                var info = PackageInfo.FindForAssembly(typeof(MeticaPaths).Assembly);
                _toolRoot = info?.assetPath;
                _toolRootResolved = true;
                return _toolRoot;
            }
        }

        public static string TemplatesRoot => ToolRoot == null ? null : ToolRoot + "/Editor/Ads/Templates";

        /// <summary>
        /// Per-project override of the target Metica SDK version. A package install is shared
        /// and often read-only (git packages live in Library/PackageCache), so this is where a
        /// project pins its own version — created on request, a copy of the packaged default.
        /// </summary>
        public const string TargetVersionAsset = "Assets/MeticaIntegrationToolsSettings/MeticaTargetVersion.asset";

        /// <summary>The default target version this tool ships with, inside the package itself.</summary>
        public static string PackagedTargetVersionAsset =>
            ToolRoot == null ? null : ToolRoot + "/Editor/Ads/MeticaTargetVersion.asset";

        public static string BackupRoot =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "MeticaIntegrationBackups")
                .Replace('\\', '/');

        public static string Combine(string a, string b) =>
            string.IsNullOrEmpty(a) ? null : (a.TrimEnd('/') + "/" + b.TrimStart('/'));

        /// <summary>
        /// Turns a project-relative path ("Assets/…") into an absolute one. A "Packages/…"
        /// path is resolved through the Package Manager rather than joined onto the project
        /// root: that prefix is a virtual path AssetDatabase understands, but it is only a
        /// real filesystem location for a local or embedded package. A git or registry
        /// package's actual files live under Library/PackageCache, and PackageInfo.resolvedPath
        /// is the one lookup that gives the real location either way.
        /// </summary>
        public static string ToAbsolute(string projectRelative)
        {
            if (string.IsNullOrEmpty(projectRelative)) return null;

            if (projectRelative.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var info = PackageInfo.FindForAssetPath(projectRelative);
                if (info != null)
                {
                    var suffix = projectRelative.Substring(("Packages/" + info.name).Length);
                    return (info.resolvedPath + suffix).Replace('\\', '/');
                }
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            return Path.Combine(projectRoot, projectRelative).Replace('\\', '/');
        }

        private static string ToProjectRelative(string absolute)
        {
            var normalized = absolute.Replace('\\', '/');
            var index = normalized.IndexOf("/Assets/", StringComparison.Ordinal);
            return index < 0 ? null : normalized.Substring(index + 1);
        }

        public static bool FileExists(string projectRelative) =>
            !string.IsNullOrEmpty(projectRelative) && File.Exists(ToAbsolute(projectRelative));

        public static bool DirectoryExists(string projectRelative) =>
            !string.IsNullOrEmpty(projectRelative) && Directory.Exists(ToAbsolute(projectRelative));
    }
}
