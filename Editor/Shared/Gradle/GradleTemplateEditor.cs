using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Brings <c>baseProjectTemplate.gradle</c> into the shape Metica's Kotlin native SDK
    /// needs. Most projects already carry an AGP version on the plugin ids, and that version
    /// is normally treated as the source of truth, with the buildscript written to match it —
    /// never the other way round.
    ///
    /// <para>The one exception is a target API this project's AGP cannot build: Android
    /// requires AGP 8+ once Target API Level reaches <see cref="MinimumApiForAgp8"/>, so there
    /// the plugin ids themselves are raised to <see cref="RecommendedAgpVersion"/> — Metica's
    /// documented version — before the buildscript is aligned to match.</para>
    ///
    /// <para>Three further rules, all of which fail the Gradle build rather than the C#
    /// compile: a <c>buildscript</c> block must exist and sit above <c>plugins</c>; its AGP
    /// classpath must equal the <c>com.android.application</c> version; and the Kotlin Gradle
    /// plugin must be on that classpath.</para>
    /// </summary>
    public static class GradleTemplateEditor
    {
        public const string KotlinClasspath = "classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:1.9.22'";

        /// <summary>Target API Level at or above which Android requires AGP 8+.</summary>
        public const int MinimumApiForAgp8 = 33;

        /// <summary>The AGP version Metica's docs recommend once AGP 8 is required.</summary>
        public const string RecommendedAgpVersion = "8.4.0";

        private const string AgpCoordinate = "com.android.tools.build:gradle";

        private static readonly Regex ApplicationVersion =
            new Regex(@"id\s+'com\.android\.application'\s+version\s+'([0-9]+(?:\.[0-9]+)*)'");

        private static readonly Regex AgpClasspathVersion =
            new Regex(@"com\.android\.tools\.build:gradle:([0-9]+(?:\.[0-9]+)*)");

        private static readonly Regex LibraryVersion =
            new Regex(@"id\s+'com\.android\.library'\s+version\s+'([0-9]+(?:\.[0-9]+)*)'");

        /// <summary>What the file needs, from the point of view of Metica.</summary>
        public sealed class Report
        {
            public bool FileMissing;
            public string PluginVersion;          // the com.android.application version
            public string ClasspathVersion;       // the buildscript AGP classpath version
            public bool HasBuildscript;
            public bool BuildscriptAbovePlugins;
            public bool HasKotlinClasspath;
            public bool AgpTooOldForTarget;       // Target API >= MinimumApiForAgp8 but AGP is below 8
            public readonly List<string> MismatchedPluginIds = new List<string>();

            public bool NeedsWork =>
                !FileMissing &&
                (!HasBuildscript
                 || !BuildscriptAbovePlugins
                 || !HasKotlinClasspath
                 || ClasspathVersion == null
                 || (PluginVersion != null && ClasspathVersion != PluginVersion)
                 || AgpTooOldForTarget
                 || MismatchedPluginIds.Count > 0);
        }

        public static Report Inspect()
        {
            var report = new Report();

            if (!MeticaPaths.FileExists(MeticaPaths.BaseProjectTemplateGradle))
            {
                report.FileMissing = true;
                return report;
            }

            var text = SourcePatcher.ReadAll(MeticaPaths.BaseProjectTemplateGradle);
            var lines = Split(text);

            var application = ApplicationVersion.Match(text);
            report.PluginVersion = application.Success ? application.Groups[1].Value : null;

            var classpath = AgpClasspathVersion.Match(text);
            report.ClasspathVersion = classpath.Success ? classpath.Groups[1].Value : null;

            var buildscript = IndexOfBlockStart(lines, "buildscript");
            var plugins = IndexOfBlockStart(lines, "plugins");

            report.HasBuildscript = buildscript >= 0;
            report.BuildscriptAbovePlugins = buildscript >= 0 && (plugins < 0 || buildscript < plugins);
            report.HasKotlinClasspath = text.Contains("kotlin-gradle-plugin");
            report.AgpTooOldForTarget = report.PluginVersion != null
                                         && RequiresAgp8() && !AtLeastAgp8(report.PluginVersion);

            // The library plugin has to agree with the application plugin too.
            if (report.PluginVersion != null)
            {
                foreach (Match match in LibraryVersion.Matches(text))
                    if (match.Groups[1].Value != report.PluginVersion)
                        report.MismatchedPluginIds.Add($"com.android.library {match.Groups[1].Value}");
            }

            return report;
        }

        /// <summary>Applies the three rules. Safe to run twice — a compliant file is left alone.</summary>
        public static List<string> Fix()
        {
            var log = new List<string>();

            if (!MeticaPaths.FileExists(MeticaPaths.BaseProjectTemplateGradle))
            {
                log.Add("baseProjectTemplate.gradle not found. Enable Player Settings → Publishing " +
                        "Settings → Custom Base Gradle Template, then run this again.");
                return log;
            }

            var text = SourcePatcher.ReadAll(MeticaPaths.BaseProjectTemplateGradle);
            var lines = Split(text);

            var application = ApplicationVersion.Match(text);
            if (!application.Success)
            {
                log.Add("No com.android.application plugin id found, so there is no version to match. " +
                        "Set up baseProjectTemplate.gradle by hand.");
                return log;
            }

            var agp = application.Groups[1].Value;

            if (RequiresAgp8() && !AtLeastAgp8(agp))
            {
                agp = RecommendedAgpVersion;
                if (SetPluginVersions(lines, agp))
                    log.Add($"Target API {MinimumApiForAgp8}+ needs AGP 8 or later — set " +
                            $"com.android.application and com.android.library to {agp}");
            }

            var plugins = IndexOfBlockStart(lines, "plugins");
            var buildscript = IndexOfBlockStart(lines, "buildscript");

            if (buildscript < 0)
            {
                var block = new[]
                {
                    "buildscript {",
                    "    dependencies {",
                    $"        classpath '{AgpCoordinate}:{agp}'",
                    $"        {KotlinClasspath}",
                    "    }",
                    "}"
                };

                lines.InsertRange(plugins >= 0 ? plugins : 0, block);
                log.Add($"Added a buildscript block above plugins, pinned to AGP {agp}");
            }
            else
            {
                if (!TryFindBlock(lines, buildscript, out var open, out var close))
                {
                    log.Add("Found a buildscript block but could not match its braces. Fix " +
                            "baseProjectTemplate.gradle by hand.");
                    return log;
                }

                // Gradle requires buildscript before plugins.
                if (plugins >= 0 && buildscript > plugins)
                {
                    var body = lines.GetRange(buildscript, close - buildscript + 1);
                    lines.RemoveRange(buildscript, close - buildscript + 1);

                    plugins = IndexOfBlockStart(lines, "plugins");
                    lines.InsertRange(plugins >= 0 ? plugins : 0, body);
                    log.Add("Moved the buildscript block above plugins");

                    buildscript = IndexOfBlockStart(lines, "buildscript");
                    if (!TryFindBlock(lines, buildscript, out open, out close))
                    {
                        log.Add("Lost track of the buildscript block after moving it. Check the file.");
                        return log;
                    }
                }

                close = EnsureAgpClasspath(lines, open, close, agp, log);
                EnsureKotlinClasspath(lines, open, close, log);
            }

            if (log.Count == 0)
            {
                log.Add("baseProjectTemplate.gradle already matches what Metica needs.");
                return log;
            }

            SourcePatcher.WriteWithBackup(MeticaPaths.BaseProjectTemplateGradle,
                string.Join(DetectNewline(text), lines));

            return log;
        }

        // ── AGP-for-target-API floor ────────────────────────────────────────────

        /// <summary>
        /// True once Target API Level reaches <see cref="MinimumApiForAgp8"/>. Auto resolves
        /// to whatever the highest installed platform is, which in practice is never below
        /// the floor, so it counts as requiring AGP 8 too rather than being read as unset.
        /// </summary>
        private static bool RequiresAgp8()
        {
            var target = (int)PlayerSettings.Android.targetSdkVersion;
            return target < 0 || target >= MinimumApiForAgp8;
        }

        private static bool AtLeastAgp8(string version) =>
            version != null && int.TryParse(version.Split('.')[0], out var major) && major >= 8;

        /// <summary>Rewrites every com.android.application/library version to <paramref name="agp"/>.</summary>
        private static bool SetPluginVersions(List<string> lines, string agp)
        {
            var changed = false;

            for (var i = 0; i < lines.Count; i++)
            {
                lines[i] = ReplaceVersionIfDifferent(lines[i], ApplicationVersion, agp, ref changed);
                lines[i] = ReplaceVersionIfDifferent(lines[i], LibraryVersion, agp, ref changed);
            }

            return changed;
        }

        private static string ReplaceVersionIfDifferent(string line, Regex pattern, string agp, ref bool changed)
        {
            var match = pattern.Match(line);
            if (!match.Success || match.Groups[1].Value == agp) return line;

            changed = true;
            var group = match.Groups[1];
            return line.Substring(0, group.Index) + agp + line.Substring(group.Index + group.Length);
        }

        // ── Pieces ─────────────────────────────────────────────────────────────

        /// <summary>Makes the AGP classpath equal <paramref name="agp"/>, adding it if absent.</summary>
        private static int EnsureAgpClasspath(List<string> lines, int open, int close, string agp,
            List<string> log)
        {
            for (var i = open; i <= close; i++)
            {
                var match = AgpClasspathVersion.Match(lines[i]);
                if (!match.Success) continue;

                if (match.Groups[1].Value == agp) return close;

                lines[i] = lines[i].Replace($"gradle:{match.Groups[1].Value}", $"gradle:{agp}");
                log.Add($"Set the buildscript AGP classpath to {agp}, matching com.android.application");
                return close;
            }

            lines.Insert(DependenciesLine(lines, open, close), $"        classpath '{AgpCoordinate}:{agp}'");
            log.Add($"Added the AGP classpath at {agp}");
            return close + 1;
        }

        private static void EnsureKotlinClasspath(List<string> lines, int open, int close, List<string> log)
        {
            for (var i = open; i <= close; i++)
                if (lines[i].Contains("kotlin-gradle-plugin")) return;

            lines.Insert(DependenciesLine(lines, open, close), $"        {KotlinClasspath}");
            log.Add("Added the Kotlin Gradle plugin classpath");
        }

        /// <summary>First line inside the block's dependencies list, or just inside the block.</summary>
        private static int DependenciesLine(List<string> lines, int open, int close)
        {
            for (var i = open; i <= close; i++)
                if (lines[i].TrimStart().StartsWith("dependencies")) return i + 1;

            return open + 1;
        }

        private static int IndexOfBlockStart(List<string> lines, string keyword) =>
            lines.FindIndex(l => l.TrimStart().StartsWith(keyword));

        private static bool TryFindBlock(List<string> lines, int declaration, out int open, out int close)
        {
            open = close = -1;
            if (declaration < 0) return false;

            var depth = 0;
            for (var i = declaration; i < lines.Count; i++)
            {
                foreach (var character in StripLiterals(lines[i]))
                {
                    if (character == '{')
                    {
                        if (open < 0) open = i;
                        depth++;
                    }
                    else if (character == '}')
                    {
                        depth--;
                        if (depth != 0 || open < 0) continue;

                        close = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string StripLiterals(string line)
        {
            var withoutComment = Regex.Replace(line, "//.*$", string.Empty);
            return Regex.Replace(withoutComment, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", "''");
        }

        private static List<string> Split(string text) =>
            text.Replace("\r\n", "\n").Split('\n').ToList();

        private static string DetectNewline(string text) => text.Contains("\r\n") ? "\r\n" : "\n";
    }
}
