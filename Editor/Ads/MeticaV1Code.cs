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
    /// count in a file that also uses v1 — a v1 namespace or any v1-only name. Comments and
    /// strings are ignored. Metica's own SDK
    /// folder and this tool's standalone runtime are never scanned.</para>
    /// </summary>
    internal static class MeticaV1Code
    {
        private static readonly (Regex pattern, string hint)[] Markers =
        {
            (new Regex(@"\bMetica\.ADS\b"), "v1 namespace — remove it"),
            (new Regex(@"\busing\s+Metica\.SDK\s*;"), "v1 namespace — remove it"),
            (new Regex(@"\bMetica\.SDK\.\w"), "v1 namespace — remove it"),
            (new Regex(@"\bInitializeWithResultAsync\b|\bMeticaAdsInitializationResult\b"), "v1 init — now MeticaAdsManager.Initialize()"),
            (new Regex(@"\bNotifyAd(LoadAttempt|LoadFailed|LoadSuccess|ShowSuccess)\b"), "v1 MAX reporting — remove, there is no 2.x equivalent"),
            (new Regex(@"\bToMeticaAd\b|\bToAdInfo\b"), "v1 conversion — remove, 2.x has none"),
            (new Regex(@"\bMeticaSdk\.CurrentUserId\b"), "v1 user id — remove, the runtime sets it")
        };

        /// <summary>Names both versions have: v1 only inside a file that uses a v1 namespace.</summary>
        private static readonly Regex Shared = new Regex(@"\b(MeticaAds|MeticaAdsCallbacks|MeticaSdk)\.\w");

        /// <summary>
        /// v1's usual name for its per-user switch — but a game's own variable, not Metica API,
        /// and a game may keep the name for the 2.x switch. Counts only in a file that also has
        /// real v1 API.
        /// </summary>
        private static readonly Regex V1SwitchName = new Regex(@"\bIsMeticaAdsEnabled\b");

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
            // A v1 file: one using a v1 namespace, or any v1-only name — a game may have
            // commented its v1 usings out and left the calls.
            var v1File = code.Any(l => V1Namespace.IsMatch(l) || Markers.Any(m => m.pattern.IsMatch(l)));

            for (var i = 0; i < code.Length; i++)
            {
                var line = code[i];
                if (line.IndexOf("Metica", StringComparison.Ordinal) < 0
                    && line.IndexOf("NotifyAd", StringComparison.Ordinal) < 0
                    && line.IndexOf("ToAdInfo", StringComparison.Ordinal) < 0) continue;

                var hint = Markers.FirstOrDefault(m => m.pattern.IsMatch(line)).hint;
                if (hint == null && v1File && Shared.IsMatch(line)) hint = "v1 API — replace with MeticaAdsManager, or remove";
                if (hint == null && v1File && V1SwitchName.IsMatch(line))
                    hint = "v1 per-user switch — now MeticaAdsManager.IsEnabled (the remote switch)";
                if (hint == null) continue;

                found.Add(new V1Reference { Path = relativePath, Line = i + 1, Text = lines[i].Trim(), Hint = hint });
            }

            return found;
        }

        // ── Comment out / remove ───────────────────────────────────────────────

        private static readonly Regex SteersCode =
            new Regex(@"^\s*(if|else|while|for|foreach|switch|case|return|try|catch|finally)\b");

        private static readonly Regex Declares =
            new Regex(@"^\s*(\[|(public|private|protected|internal|static|readonly|const|event|override|virtual|abstract|async)\b)");

        /// <summary>
        /// The lines (0-based, inclusive) one v1 reference covers — its whole statement — or
        /// null with a reason when it should be edited by hand. A line that steers code (if,
        /// else, return, a method or block header) is never touched: taking it out would
        /// change what runs, or break the braces.
        /// </summary>
        internal static (int first, int last)? Span(string[] code, int line, out string reason)
        {
            reason = null;
            var first = line - 1;
            if (first < 0 || first >= code.Length) { reason = "line not found"; return null; }

            if (SteersCode.IsMatch(code[first]))
            {
                reason = "steers code (if / else / return…) — edit by hand";
                return null;
            }

            if (Declares.IsMatch(code[first]))
            {
                reason = "a field or member other code may use — edit by hand";
                return null;
            }

            // Grow down until the brackets close and the statement ends. A lambda or delegate
            // body ("+= ad => { … };") is part of its statement.
            var last = first;
            var parens = 0;
            var braces = 0;
            var lambda = false;
            while (true)
            {
                foreach (var c in code[last])
                {
                    if (c == '(') parens++;
                    else if (c == ')') parens--;
                    else if (c == '{') braces++;
                    else if (c == '}') braces--;
                }

                lambda |= code[last].Contains("=>") || Regex.IsMatch(code[last], @"\bdelegate\b");
                var end = code[last].TrimEnd();
                var continues = parens > 0 || (lambda && braces > 0)
                                || end.EndsWith(",") || end.EndsWith("=") || end.EndsWith("=>") || end.EndsWith("+")
                                || end.EndsWith("&&") || end.EndsWith("||") || end.EndsWith("?") || end.EndsWith(":");
                if (!continues || last + 1 >= code.Length || last - first >= 40) break;
                last++;
            }

            var next = last + 1;
            while (next < code.Length && code[next].Trim().Length == 0) next++;
            var opensBlock = code[last].TrimEnd().EndsWith("{") || (next < code.Length && code[next].TrimStart().StartsWith("{"));

            if (parens != 0 || braces != 0 || opensBlock || (lambda && !code[last].TrimEnd().EndsWith(";")))
            {
                reason = "a declaration or block, not a single statement — edit by hand";
                return null;
            }

            return (first, last);
        }

        /// <summary>
        /// Comments out (<c>// </c>) or removes the v1 statements at <paramref name="lines"/>
        /// (1-based) in one file. Lines that need a hand edit (see <see cref="Span"/>) are
        /// left alone and counted in <paramref name="skipped"/>. Returns how many statements
        /// were changed. A copy of the file before the tool's first edit is kept in the backup
        /// folder.
        /// </summary>
        internal static int Edit(string path, IEnumerable<int> lines, bool remove, out int skipped)
        {
            skipped = 0;
            var text = SourcePatcher.ReadAll(path);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var all = text.Replace("\r\n", "\n").Split('\n').ToList();
            var code = WithoutComments(all.ToArray());

            var spans = new List<(int first, int last)>();
            foreach (var line in lines.Distinct())
            {
                var span = Span(code, line, out _);
                if (span == null) { skipped++; continue; }
                if (!spans.Any(s => s.first <= span.Value.last && span.Value.first <= s.last)) spans.Add(span.Value);
            }

            // Bottom up, so earlier line numbers stay valid.
            foreach (var (first, last) in spans.OrderByDescending(s => s.first))
            {
                if (remove)
                {
                    all.RemoveRange(first, last - first + 1);
                    continue;
                }

                for (var i = first; i <= last; i++)
                {
                    if (all[i].Trim().Length == 0) continue;
                    var indent = all[i].Length - all[i].TrimStart().Length;
                    all[i] = all[i].Substring(0, indent) + "// " + all[i].Substring(indent);
                }
            }

            if (spans.Count > 0) SourcePatcher.WriteWithBackup(path, string.Join(newline, all));
            return spans.Count;
        }

        /// <summary>
        /// For each of <paramref name="lines"/> (1-based) in one file: why it must be edited by
        /// hand, or null when Comment / Remove can take it.
        /// </summary>
        internal static Dictionary<int, string> HandEditReasons(string path, IEnumerable<int> lines)
        {
            var code = WithoutComments(SourcePatcher.ReadAll(path).Replace("\r\n", "\n").Split('\n'));
            var reasons = new Dictionary<int, string>();
            foreach (var line in lines)
            {
                Span(code, line, out var reason);
                reasons[line] = reason;
            }
            return reasons;
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
