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

        public override string Summary =>
            "Runs the Android dependency resolver so the Metica Maven artifact reaches Gradle. Tick " +
            "Enable iOS to also enable the Metica xcframework for iOS.";

        public override string ActionLabel => EnableIos ? "Resolve Android, enable iOS" : "Resolve Android";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.MainTemplateGradle,
            MeticaPaths.MeticaXcFramework + ".meta",
            "ProjectSettings/AndroidResolverDependencies.xml"
        };

        public override string ReviewHint =>
            "Check mainTemplate.gradle gained com.metica:metica-sdk. If Enable iOS was ticked, also check " +
            "the xcframework meta now has iOS enabled.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            // ── Android ────────────────────────────────────────────────────────
            if (!MeticaPaths.FileExists(MeticaPaths.MainTemplateGradle))
            {
                result.Problem($"{MeticaPaths.MainTemplateGradle} not found. Enable Player Settings → " +
                               "Publishing Settings → Custom Main Gradle Template, then resolve again.");
            }
            else if (!SourcePatcher.Contains(MeticaPaths.MainTemplateGradle, AndroidArtifact))
            {
                result.Problem($"{AndroidArtifact} is not in mainTemplate.gradle yet. Run the resolver below.");
            }
            else
            {
                var declared = ReadDeclaredAndroidSpec();
                result.Note(declared == null
                    ? "Metica Android artifact is in mainTemplate.gradle"
                    : $"Resolved {declared} into mainTemplate.gradle");
            }

            if (MeticaPaths.FileExists(MeticaPaths.GradleProperties))
            {
                var properties = SourcePatcher.ReadAll(MeticaPaths.GradleProperties);
                if (!properties.Contains("android.useAndroidX=true"))
                    result.Problem("gradleTemplate.properties is missing android.useAndroidX=true.");
                if (!properties.Contains("android.enableJetifier=true"))
                    result.Problem("gradleTemplate.properties is missing android.enableJetifier=true.");
                if (properties.Contains("org.gradle.java.home"))
                    result.Note("gradleTemplate.properties pins org.gradle.java.home — that is a local " +
                                "machine path, do not commit it.");
            }

            // ── iOS ────────────────────────────────────────────────────────────
            if (!EnableIos)
            {
                result.Note("Enable iOS is unticked — this step is not checking the iOS xcframework.");
            }
            else
            {
                var importer = LoadXcFrameworkImporter();
                if (importer == null)
                {
                    result.Note("Could not inspect the Metica xcframework importer. If you ship on iOS, " +
                                "check that iOS is ticked for MeticaSDKFramework.xcframework in the Inspector.");
                }
                else if (!importer.GetCompatibleWithPlatform(BuildTarget.iOS))
                {
                    result.Problem("MeticaSDKFramework.xcframework is not enabled for iOS. Metica's build " +
                                   "post-processor cannot embed it until it is.");
                }
                else
                {
                    result.Note("Metica xcframework enabled for iOS");
                }
            }

            return result.Seal();
        }

        public override void Apply()
        {
            if (EnableIos) EnableIosFramework();
            RunAndroidResolver();
        }

        public override void DrawBody(VerifyResult result)
        {
            EnableIos = EditorGUILayout.ToggleLeft("Enable iOS", EnableIos);

            EditorGUILayout.HelpBox(
                (EnableIos
                    ? "The button does two things: enables the iOS xcframework, and runs the same Force " +
                      "Resolve that Assets → External Dependency Manager → Android Resolver → Force " +
                      "Resolve does."
                    : "The button runs the same Force Resolve that Assets → External Dependency Manager → " +
                      "Android Resolver → Force Resolve does. Tick Enable iOS above to also enable the " +
                      "Metica xcframework for iOS.") +
                " That resolver only runs while Android is the active build target (File → Build " +
                "Settings), and the button checks for that itself and logs a clear message if it is not.\n\n" +
                "The resolver needs network access and can take a while — give it a moment, then Re-check. " +
                "If the button logs that it could not find the resolver, run Force Resolve from that menu " +
                "by hand instead.",
                MessageType.Info);
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
