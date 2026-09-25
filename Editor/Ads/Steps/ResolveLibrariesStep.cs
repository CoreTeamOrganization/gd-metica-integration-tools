using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Pulls in the native libraries the imported package declares.
    ///
    /// Android: runs the External Dependency Manager so com.metica:metica-sdk lands in
    /// mainTemplate.gradle. Always on. iOS: enables the xcframework for iOS, without which
    /// Metica's build post-processor cannot find it in the Xcode project — behind an Enable
    /// iOS tick, off by default, since not every run is shipping iOS yet.
    ///
    /// <para>The Gradle version and the base template's Kotlin classpath are the two
    /// optional steps at the end of the run, not this one's business.</para>
    /// </summary>
    public sealed class ResolveLibrariesStep : MeticaStep
    {
        private const string AndroidArtifact = "com.metica:metica-sdk";

        /// <summary>
        /// Off by default: most runs through this step are iterating on Android, and enabling
        /// iOS unasked would make the step demand an iOS xcframework state the developer never
        /// touched. Per project, so it does not reset every domain reload.
        /// </summary>
        private static string EnableIosKey =>
            $"GameDistrict.MeticaIntegrationTools.ResolveLibraries.EnableIos.{Application.dataPath.GetHashCode():X8}";

        private static bool EnableIos
        {
            get => EditorPrefs.GetBool(EnableIosKey, false);
            set => EditorPrefs.SetBool(EnableIosKey, value);
        }

        public override string Title => "Resolve libraries";

        public override string Summary => "Pull Metica's Android library into Gradle.";

        public override string Why =>
            "Runs External Dependency Manager's Force Resolve (same as Assets → External Dependency " +
            "Manager → Android Resolver → Force Resolve) so com.metica:metica-sdk lands in " +
            "mainTemplate.gradle. It only runs with Android as the active build target, needs network, " +
            "and can take a moment — Re-check after. Enable iOS also ticks iOS on " +
            "MeticaSDKFramework.xcframework, which Metica's iOS build step needs.";

        public override string ActionLabel => EnableIos ? "Resolve Android, enable iOS" : "Resolve Android";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.MainTemplateGradle,
            MeticaPaths.MeticaXcFramework + ".meta",
            "ProjectSettings/AndroidResolverDependencies.xml"
        };

        public override string ReviewHint => "mainTemplate.gradle gains com.metica:metica-sdk.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            // ── Android ────────────────────────────────────────────────────────
            if (!MeticaPaths.FileExists(MeticaPaths.MainTemplateGradle))
            {
                result.Problem("Custom Main Gradle Template is off.");
            }
            else if (!SourcePatcher.Contains(MeticaPaths.MainTemplateGradle, AndroidArtifact))
            {
                result.Problem(EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android
                    ? "Switch the build target to Android, then resolve."
                    : "Not resolved yet.");
            }
            else
            {
                var declared = ReadDeclaredAndroidSpec();
                result.Note(declared == null ? "Resolved" : $"Resolved {declared}");
            }

            if (MeticaPaths.FileExists(MeticaPaths.GradleProperties))
            {
                var properties = SourcePatcher.ReadAll(MeticaPaths.GradleProperties);
                if (!properties.Contains("android.useAndroidX=true"))
                    result.Problem("gradleTemplate.properties needs android.useAndroidX=true.");
                if (!properties.Contains("android.enableJetifier=true"))
                    result.Problem("gradleTemplate.properties needs android.enableJetifier=true.");
            }

            // ── iOS ────────────────────────────────────────────────────────────
            if (EnableIos)
            {
                var importer = LoadXcFrameworkImporter();
                if (importer == null)
                    result.Note("Couldn't check the iOS framework.");
                else if (!importer.GetCompatibleWithPlatform(BuildTarget.iOS))
                    result.Problem("iOS framework isn't enabled for iOS.");
                else
                    result.Note("iOS enabled");
            }

            return result.Seal();
        }

        public override void Apply()
        {
            if (EnableIos) EnableIosFramework();
            RunAndroidResolver();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            yield return new StepToggle("Enable iOS", EnableIos, value => EnableIos = value);
        }

        // ── Actions ────────────────────────────────────────────────────────────

        private static void EnableIosFramework()
        {
            var importer = LoadXcFrameworkImporter();
            if (importer == null)
            {
                MeticaIntegrationLog.Record("Resolve libraries",
                    "Metica xcframework importer not found — tick iOS on it in the Inspector by hand.");
                return;
            }

            if (importer.GetCompatibleWithPlatform(BuildTarget.iOS))
            {
                MeticaIntegrationLog.Record("Resolve libraries", "Metica xcframework was already enabled for iOS");
                return;
            }

            importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
            importer.SaveAndReimport();
            MeticaIntegrationLog.Record("Resolve libraries", "Enabled the Metica xcframework for iOS");
        }

        private static void RunAndroidResolver()
        {
            // EDM4U's own resolver refuses to run unless Android is the active build target,
            // and says so through a modal dialog rather than a return value. Catching that here
            // first means a clear log entry instead of that dialog appearing unexplained.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                MeticaIntegrationLog.Record("Resolve libraries",
                    "Android is not the active build target, so the Android Resolver refuses to run. " +
                    "Switch to it in File → Build Settings → Platform, then press this button again.");
                return;
            }

            // EDM4U is optional and its entry point has moved between versions, so it is
            // called by reflection rather than referenced.
            var resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a =>
                {
                    try { return a.GetType("GooglePlayServices.PlayServicesResolver"); }
                    catch { return null; }
                })
                .FirstOrDefault(t => t != null);

            if (resolver == null)
            {
                MeticaIntegrationLog.Record("Resolve libraries",
                    "External Dependency Manager not found. Run Assets → External Dependency Manager → " +
                    "Android Resolver → Force Resolve by hand.");
                return;
            }

            var method = new[] { "MenuForceResolve", "MenuResolve", "Resolve" }
                .Select(name => resolver.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null))
                .FirstOrDefault(m => m != null);

            if (method == null)
            {
                MeticaIntegrationLog.Record("Resolve libraries",
                    "Found the resolver but not a parameterless resolve method. Run Force Resolve from the menu.");
                return;
            }

            try
            {
                method.Invoke(null, null);
                MeticaIntegrationLog.Record("Resolve libraries", $"Ran the Android resolver ({method.Name})");
            }
            catch (Exception e)
            {
                MeticaIntegrationLog.Record("Resolve libraries",
                    $"The Android resolver threw: {e.InnerException?.Message ?? e.Message}. Run Force Resolve from the menu.");
            }
        }

        /// <summary>The artifact MeticaDependencies.xml asks for, e.g. com.metica:metica-sdk:2.5.1.</summary>
        private static string ReadDeclaredAndroidSpec()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MeticaDependencies)) return null;

            var xml = SourcePatcher.ReadAll(MeticaPaths.MeticaDependencies);
            var match = Regex.Match(xml, "spec\\s*=\\s*\"([^\"]*metica[^\"]*)\"", RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : null;
        }

        private static PluginImporter LoadXcFrameworkImporter()
        {
            if (!MeticaPaths.DirectoryExists(MeticaPaths.MeticaXcFramework)) return null;
            return AssetImporter.GetAtPath(MeticaPaths.MeticaXcFramework) as PluginImporter;
        }
    }
}
