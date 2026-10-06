using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>One line of game code that uses the Metica v1 API.</summary>
    internal sealed class V1Reference
    {
        /// <summary>Project-relative path ("Assets/...").</summary>
        public string Path;

        /// <summary>1-based.</summary>
        public int Line;

        public string Text;

        /// <summary>What it is, and what replaces it.</summary>
        public string Hint;
    }

    /// <summary>
    /// Finds game code that still uses the Metica v1 API, by names that exist in v1 and nowhere
    /// in Metica 2.x. Text only — it works while the project does not compile, which is exactly
    /// when it is needed.
    ///
    /// <para>The names were checked against the Metica 2.45.2 source: none of them appears
    /// there. MeticaAds, MeticaAdsCallbacks and MeticaSdk exist in both versions, so they only
    /// count in a file that also uses a v1 namespace. Comments are ignored. Metica's own SDK
    /// folder and this tool's standalone runtime are never scanned.</para>
    /// </summary>
    internal static class MeticaV1Code
    {
        private static readonly (Regex pattern, string hint)[] Markers =
        {
            (new Regex(@"\bMetica\.ADS\b"), "v1 namespace — remove it"),
            (new Regex(@"\busing\s+Metica\.SDK\s*;"), "v1 namespace — remove it"),
            (new Regex(@"\bMetica\.SDK\.\w"), "v1 namespace — remove it"),
            (new Regex(@"\bIsMeticaAdsEnabled\b"), "v1 per-user switch — now MeticaAdsManager.IsEnabled (the remote switch)"),
            (new Regex(@"\bInitializeWithResultAsync\b|\bMeticaAdsInitializationResult\b"), "v1 init — now MeticaAdsManager.Initialize()"),
            (new Regex(@"\bNotifyAd(LoadAttempt|LoadFailed|LoadSuccess|ShowSuccess)\b"), "v1 MAX reporting — remove, there is no 2.x equivalent"),
            (new Regex(@"\bToMeticaAd\b|\bToAdInfo\b"), "v1 conversion — remove, 2.x has none"),
            (new Regex(@"\bMeticaSdk\.CurrentUserId\b"), "v1 user id — remove, the runtime sets it")
        };

        /// <summary>Names both versions have: v1 only inside a file that uses a v1 namespace.</summary>
        private static readonly Regex Shared = new Regex(@"\b(MeticaAds|MeticaAdsCallbacks|MeticaSdk)\.\w");

        private static readonly Regex V1Namespace = new Regex(@"\bMetica\.ADS\b|\busing\s+Metica\.SDK\s*;");

        private static readonly Dictionary<string, (DateTime written, long length, List<V1Reference> found)> Cache =
            new Dictionary<string, (DateTime, long, List<V1Reference>)>();

        /// <summary>The open project's v1 references.</summary>
        public static List<V1Reference> ScanProject() =>
            Scan(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
                MeticaPaths.MeticaSdkRoot, MeticaPaths.StandaloneRoot);

        /// <param name="projectRoot">The folder holding Assets/.</param>
        /// <param name="skippedFolders">Project-relative folders never scanned.</param>
        internal static List<V1Reference> Scan(string projectRoot, params string[] skippedFolders)
        {
            var assets = System.IO.Path.Combine(projectRoot, "Assets");
            var skipped = skippedFolders.Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f.TrimEnd('/') + "/").ToArray();
            var result = new List<V1Reference>();
            if (!Directory.Exists(assets)) return result;

            foreach (var file in Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories))
            {
                var relative = "Assets/" + file.Substring(assets.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (skipped.Any(s => relative.StartsWith(s, StringComparison.Ordinal))) continue;

                var info = new FileInfo(file);
                if (!Cache.TryGetValue(file, out var cached) || cached.written != info.LastWriteTimeUtc || cached.length != info.Length)
                {
                    cached = (info.LastWriteTimeUtc, info.Length, ScanFile(relative, File.ReadAllText(file)));
                    Cache[file] = cached;
                }

                result.AddRange(cached.found);
            }

            return result;
        }

        internal static List<V1Reference> ScanFile(string relativePath, string text)
        {
            var found = new List<V1Reference>();

            // Cheap test first: almost every file has none of this.
            if (text.IndexOf("Metica", StringComparison.Ordinal) < 0) return found;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var code = WithoutComments(lines);
            var v1File = code.Any(l => V1Namespace.IsMatch(l));

            for (var i = 0; i < code.Length; i++)
            {
                var line = code[i];
                if (line.IndexOf("Metica", StringComparison.Ordinal) < 0
                    && line.IndexOf("NotifyAd", StringComparison.Ordinal) < 0
                    && line.IndexOf("ToAdInfo", StringComparison.Ordinal) < 0) continue;

                var hint = Markers.FirstOrDefault(m => m.pattern.IsMatch(line)).hint;
                if (hint == null && v1File && Shared.IsMatch(line)) hint = "v1 API — replace with MeticaAdsManager, or remove";
                if (hint == null) continue;

                found.Add(new V1Reference { Path = relativePath, Line = i + 1, Text = lines[i].Trim(), Hint = hint });
            }

            return found;
        }

        /// <summary>
        /// The lines with // and /* */ comments removed and string contents blanked; line count
        /// unchanged.
        /// </summary>
        private static string[] WithoutComments(string[] lines)
        {
            var result = new string[lines.Length];
            var inBlock = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var kept = new System.Text.StringBuilder(line.Length);
                var inString = false;

                for (var c = 0; c < line.Length; c++)
                {
                    if (inBlock)
                    {
                        if (line[c] == '*' && c + 1 < line.Length && line[c + 1] == '/') { inBlock = false; c++; }
                        continue;
                    }

                    // A quote opens or closes a string, unless escaped or a '"' char literal.
                    var isQuote = line[c] == '"' && (c == 0 || line[c - 1] != '\\')
                                  && !(c > 0 && line[c - 1] == '\'' && c + 1 < line.Length && line[c + 1] == '\'');
                    if (isQuote)
                    {
                        inString = !inString;
                        kept.Append('"');
                        continue;
                    }

                    if (!inString && line[c] == '/' && c + 1 < line.Length)
                    {
                        if (line[c + 1] == '/') break;
                        if (line[c + 1] == '*') { inBlock = true; c++; continue; }
                    }

                    // String contents are blanked: a name inside text is not a call.
                    kept.Append(inString ? ' ' : line[c]);
                }

                result[i] = kept.ToString();
            }

            return result;
        }
    }
}
