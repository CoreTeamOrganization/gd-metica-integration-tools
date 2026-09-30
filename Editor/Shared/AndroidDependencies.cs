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
    /// Whether every Android library the project's SDKs declare has been resolved into the
    /// build, and running External Dependency Manager's Force Resolve when not.
    ///
    /// <para>Generic on purpose: it reads every *Dependencies.xml in Assets (Metica, MAX, each
    /// mediation network, Firebase…) and checks each declared <c>group:artifact</c> reached
    /// mainTemplate.gradle — or Assets/Plugins/Android, for an EDM set to copy libraries
    /// instead of patching the template. No SDK or version is hardcoded. iOS pods resolve at
    /// Xcode build time, so only Android is checked.</para>
    /// </summary>
    internal static class AndroidDependencies
    {
        private static readonly Regex AndroidPackage =
            new Regex("<androidPackage\\s+[^>]*spec\\s*=\\s*\"([^\":]+):([^\":]+)[^\"]*\"", RegexOptions.IgnoreCase);

        /// <summary>Where Unity keeps the template Unity copies when Custom Main Gradle Template is ticked.</summary>
        private const string DefaultMainTemplate = "Tools/GradleTemplates/mainTemplate.gradle";

        /// <summary>
        /// Every library declared under <paramref name="folder"/> (default: all of Assets) as
        /// group:artifact, once each. A step checks only its own SDK's folder, so a later
        /// step adding a library never un-does an earlier step's sign-off.
        /// </summary>
        public static List<string> Declared(string folder = "Assets")
        {
            var root = MeticaPaths.ToAbsolute(folder);
            if (!Directory.Exists(root)) return new List<string>();

            return Directory.EnumerateFiles(root, "*Dependencies.xml", SearchOption.AllDirectories)
                .SelectMany(file =>
                {
                    try { return AndroidPackage.Matches(File.ReadAllText(file)).Cast<Match>(); }
                    catch { return Enumerable.Empty<Match>(); }
                })
                .Select(match => $"{match.Groups[1].Value.Trim()}:{match.Groups[2].Value.Trim()}")
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// The declared libraries not in the build yet. Null when there is nothing to check
        /// against — Custom Main Gradle Template is off.
        /// </summary>
        public static List<string> Unresolved(string folder = "Assets")
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MainTemplateGradle)) return null;

            var template = SourcePatcher.ReadAll(MeticaPaths.MainTemplateGradle);
            var copied = CopiedLibraries();

            return Declared(folder)
                .Where(library => !template.Contains(library) && !copied.Any(file => file.Contains(library.Split(':')[1])))
                .ToList();
        }

        /// <summary>
        /// The problem line for a step that needs its libraries resolved, plus one line per
        /// missing library (shown under Why?). Nothing when everything is resolved.
        /// </summary>
        public static void Report(VerifyResult result, string folder)
        {
            var unresolved = Unresolved(folder);

            if (unresolved == null)
            {
                result.Problem("Custom Main Gradle Template is off — resolving turns it on.");
                return;
            }

            if (unresolved.Count == 0)
            {
                result.Note("Android dependencies resolved");
                return;
            }

            result.Problem(EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android
                ? "Switch the build target to Android, then resolve."
                : $"{unresolved.Count} Android librar{(unresolved.Count == 1 ? "y isn't" : "ies aren't")} resolved yet.");

            foreach (var library in unresolved) result.Problem($"Not resolved: {library}");
        }

        /// <summary>Anything under <paramref name="folder"/> (default: all of Assets) still to resolve.</summary>
        public static bool NeedsResolve(string folder = "Assets")
        {
            var unresolved = Unresolved(folder);
            return unresolved == null || unresolved.Count > 0;
        }

        /// <summary>
        /// Turns Custom Main Gradle Template on if it is off, then runs Force Resolve — the same
        /// as Assets → External Dependency Manager → Android Resolver → Force Resolve.
        /// </summary>
        public static void Resolve(string logTitle)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MainTemplateGradle))
                MeticaIntegrationLog.Record(logTitle, EnableCustomMainTemplate());

            // EDM refuses to run unless Android is the active build target, and says so in a
            // modal dialog — catching it here gives a clear log line instead.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                MeticaIntegrationLog.Record(logTitle,
                    "Android is not the active build target, so the resolver refuses to run. Switch to it in " +
                    "File → Build Settings → Platform, then resolve again.");
                return;
            }

            // EDM is optional and its entry point has moved between versions, so it is called
            // by reflection rather than referenced.
            var resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a =>
                {
                    try { return a.GetType("GooglePlayServices.PlayServicesResolver"); }
                    catch { return null; }
                })
                .FirstOrDefault(t => t != null);

            if (resolver == null)
            {
                MeticaIntegrationLog.Record(logTitle,
                    "External Dependency Manager not found. Run Assets → External Dependency Manager → Android " +
                    "Resolver → Force Resolve by hand.");
                return;
            }

            var method = new[] { "MenuForceResolve", "MenuResolve", "Resolve" }
                .Select(name => resolver.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null))
                .FirstOrDefault(m => m != null);

            if (method == null)
            {
                MeticaIntegrationLog.Record(logTitle, "Found the resolver but no way to call it. Run Force Resolve from the menu.");
                return;
            }

            try
            {
                method.Invoke(null, null);
                MeticaIntegrationLog.Record(logTitle, $"Ran the Android resolver ({method.Name})");
            }
            catch (Exception e)
            {
                MeticaIntegrationLog.Record(logTitle,
                    $"The Android resolver threw: {e.InnerException?.Message ?? e.Message}. Run Force Resolve from the menu.");
            }
        }

        /// <summary>
        /// Ticks Custom Main Gradle Template by doing what the tick does: copying Unity's own
        /// default mainTemplate.gradle into Assets/Plugins/Android.
        /// </summary>
        private static string EnableCustomMainTemplate()
        {
            try
            {
                var engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
                var source = string.IsNullOrEmpty(engine) ? null : Path.Combine(engine, DefaultMainTemplate);
                if (source == null || !File.Exists(source))
                    return "Could not find Unity's default mainTemplate.gradle. Tick Player Settings → Publishing " +
                           "Settings → Custom Main Gradle Template by hand.";

                var target = MeticaPaths.ToAbsolute(MeticaPaths.MainTemplateGradle);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
                File.Copy(source, target);
                AssetDatabase.ImportAsset(MeticaPaths.MainTemplateGradle);
                return "Enabled Custom Main Gradle Template by copying Unity's default into " + MeticaPaths.MainTemplateGradle;
            }
            catch (Exception e)
            {
                return $"Could not enable Custom Main Gradle Template ({e.Message}). Tick it in Player Settings → " +
                       "Publishing Settings by hand.";
            }
        }

        /// <summary>Library file names EDM copied into Assets/Plugins/Android, when set to copy instead of patch.</summary>
        private static List<string> CopiedLibraries()
        {
            var folder = MeticaPaths.ToAbsolute("Assets/Plugins/Android");
            if (!Directory.Exists(folder)) return new List<string>();

            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".aar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .ToList();
        }
    }
}
