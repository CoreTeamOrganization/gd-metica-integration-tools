using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Anchor-based, idempotent edits to existing C# files.
    ///
    /// Every patch carries a <c>marker</c> — a snippet that is present once the patch is
    /// in place. The marker is checked first, so re-running a patch is a no-op instead of
    /// a duplicate insertion. When an anchor cannot be found the patch refuses to guess:
    /// it reports the failure so the user can apply that one edit by hand.
    ///
    /// The original file is copied to &lt;project&gt;/MeticaIntegrationBackups/ before the
    /// first change, outside Assets so Unity never imports the backups.
    /// </summary>
    public static class SourcePatcher
    {
        public enum Outcome
        {
            Applied,
            AlreadyApplied,
            FileMissing,
            AnchorNotFound
        }

        public static bool IsSatisfied(Outcome outcome) =>
            outcome == Outcome.Applied || outcome == Outcome.AlreadyApplied;

        // ── In-memory mode ─────────────────────────────────────────────────────

        /// <summary>
        /// Project-relative path → contents while an <see cref="InMemory"/> scope is open;
        /// null otherwise. Lets the modified-file check run the real patch code against
        /// stock copies to learn what "patched" looks like, without touching the disk.
        /// </summary>
        private static Dictionary<string, string> _memory;

        /// <summary>
        /// Until disposed, every read, write and existence check here goes to
        /// <paramref name="files"/> instead of the disk, and nothing is backed up. A path
        /// that is not a key counts as missing.
        /// </summary>
        public static IDisposable InMemory(Dictionary<string, string> files)
        {
            _memory = files ?? throw new ArgumentNullException(nameof(files));
            return new MemoryScope();
        }

        private sealed class MemoryScope : IDisposable
        {
            public void Dispose() => _memory = null;
        }

        public static bool Exists(string projectRelativePath) =>
            _memory != null
                ? projectRelativePath != null && _memory.ContainsKey(projectRelativePath)
                : MeticaPaths.FileExists(projectRelativePath);

        public static bool Contains(string projectRelativePath, string needle)
        {
            if (!Exists(projectRelativePath)) return false;
            return ReadAll(projectRelativePath).Contains(needle);
        }

        /// <summary>
        /// Inserts <paramref name="insertion"/> on the line after the first line containing
        /// <paramref name="anchorContains"/>. <paramref name="insertion"/> must carry its own
        /// indentation and may span several lines.
        /// </summary>
        public static Outcome InsertAfterLine(string projectRelativePath, string marker,
            string anchorContains, string insertion)
        {
            if (!Exists(projectRelativePath)) return Outcome.FileMissing;

            var text = ReadAll(projectRelativePath);
            if (text.Contains(marker)) return Outcome.AlreadyApplied;

            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var index = lines.FindIndex(l => l.Contains(anchorContains));
            if (index < 0) return Outcome.AnchorNotFound;

            lines.InsertRange(index + 1, insertion.Replace("\r\n", "\n").Split('\n'));
            WriteAll(projectRelativePath, string.Join(newline, lines));
            return Outcome.Applied;
        }

        /// <summary>
        /// Inserts <paramref name="insertion"/> on the line *before* the first line containing
        /// <paramref name="anchorContains"/>.
        /// </summary>
        public static Outcome InsertBeforeLine(string projectRelativePath, string marker,
            string anchorContains, string insertion)
        {
            if (!Exists(projectRelativePath)) return Outcome.FileMissing;

            var text = ReadAll(projectRelativePath);
            if (text.Contains(marker)) return Outcome.AlreadyApplied;

            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var index = lines.FindIndex(l => l.Contains(anchorContains));
            if (index < 0) return Outcome.AnchorNotFound;

            lines.InsertRange(index, insertion.Replace("\r\n", "\n").Split('\n'));
            WriteAll(projectRelativePath, string.Join(newline, lines));
            return Outcome.Applied;
        }

        /// <summary>Replaces the first occurrence of <paramref name="find"/>.</summary>
        public static Outcome ReplaceFirst(string projectRelativePath, string marker,
            string find, string replacement)
        {
            if (!Exists(projectRelativePath)) return Outcome.FileMissing;

            var text = ReadAll(projectRelativePath);
            if (text.Contains(marker)) return Outcome.AlreadyApplied;

            var index = text.IndexOf(find, StringComparison.Ordinal);
            if (index < 0) return Outcome.AnchorNotFound;

            var patched = text.Substring(0, index) + replacement + text.Substring(index + find.Length);
            WriteAll(projectRelativePath, patched);
            return Outcome.Applied;
        }

        /// <summary>
        /// Appends <paramref name="insertion"/> just before the last closing brace of the file,
        /// i.e. as the final member of the last type. Used where there is no stable anchor line.
        /// </summary>
        public static Outcome AppendToLastType(string projectRelativePath, string marker, string insertion)
        {
            if (!Exists(projectRelativePath)) return Outcome.FileMissing;

            var text = ReadAll(projectRelativePath);
            if (text.Contains(marker)) return Outcome.AlreadyApplied;

            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            // Last line that closes the type — one before the namespace's closing brace.
            var closing = lines.FindLastIndex(l => l.Trim() == "}");
            if (closing < 0) return Outcome.AnchorNotFound;
            var typeClosing = lines.FindLastIndex(closing - 1, l => l.Trim() == "}");
            if (typeClosing < 0) return Outcome.AnchorNotFound;

            lines.InsertRange(typeClosing, insertion.Replace("\r\n", "\n").Split('\n'));
            WriteAll(projectRelativePath, string.Join(newline, lines));
            return Outcome.Applied;
        }


        /// <summary>
        /// Appends a member to an enum, fixing up the trailing comma on the current last
        /// member. Used for <c>Tag.Metica</c>, where v5 leaves the last entry comma-less.
        /// </summary>
        public static Outcome AppendEnumMember(string projectRelativePath, string enumName, string member)
        {
            if (!Exists(projectRelativePath)) return Outcome.FileMissing;

            var text = ReadAll(projectRelativePath);
            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var declaration = lines.FindIndex(l => l.Contains("enum " + enumName));
            if (declaration < 0) return Outcome.AnchorNotFound;

            var open = lines.FindIndex(declaration, l => l.Trim() == "{");
            if (open < 0) return Outcome.AnchorNotFound;

            var close = lines.FindIndex(open, l => l.Trim() == "}");
            if (close < 0) return Outcome.AnchorNotFound;

            // Already a member of this enum?
            for (var i = open + 1; i < close; i++)
                if (lines[i].Trim().TrimEnd(',') == member)
                    return Outcome.AlreadyApplied;

            var lastMember = -1;
            for (var i = close - 1; i > open; i--)
            {
                if (lines[i].Trim().Length == 0) continue;
                lastMember = i;
                break;
            }
            if (lastMember < 0) return Outcome.AnchorNotFound;

            var indent = lines[lastMember].Substring(0, lines[lastMember].Length - lines[lastMember].TrimStart().Length);
            if (!lines[lastMember].TrimEnd().EndsWith(","))
                lines[lastMember] = lines[lastMember].TrimEnd() + ",";

            lines.Insert(lastMember + 1, indent + member);
            WriteAll(projectRelativePath, string.Join(newline, lines));
            return Outcome.Applied;
        }

        /// <summary>
        /// Inserts a sibling type just before the namespace's closing brace — the last
        /// line in the file that is a bare "}".
        /// </summary>
        public static Outcome AppendTypeToNamespace(string projectRelativePath, string marker, string insertion)
        {
            if (!Exists(projectRelativePath)) return Outcome.FileMissing;

            var text = ReadAll(projectRelativePath);
            if (text.Contains(marker)) return Outcome.AlreadyApplied;

            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var namespaceClose = lines.FindLastIndex(l => l.Trim() == "}");
            if (namespaceClose < 0) return Outcome.AnchorNotFound;

            lines.InsertRange(namespaceClose, insertion.Replace("\r\n", "\n").Split('\n'));
            WriteAll(projectRelativePath, string.Join(newline, lines));
            return Outcome.Applied;
        }


        /// <summary>
        /// Deletes every line containing <paramref name="needle"/>. Intended for single-line
        /// declarations such as a field. Returns how many lines went.
        /// </summary>
        public static int RemoveLinesContaining(string projectRelativePath, string needle)
        {
            if (!Exists(projectRelativePath)) return 0;

            var text = ReadAll(projectRelativePath);
            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var removed = lines.RemoveAll(l => l.Contains(needle));
            if (removed > 0) WriteAll(projectRelativePath, string.Join(newline, lines));

            return removed;
        }

        /// <summary>
        /// Deletes a whole brace-delimited block — a type, a constructor, a method — found by
        /// a distinctive fragment of its declaration line, along with any doc comment or
        /// attributes immediately above it.
        /// </summary>
        public static bool RemoveBlockContaining(string projectRelativePath, string declarationContains)
        {
            if (!Exists(projectRelativePath)) return false;

            var text = ReadAll(projectRelativePath);
            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var declaration = lines.FindIndex(l => l.Contains(declarationContains));
            if (declaration < 0) return false;

            if (!TryFindBlock(lines, declaration, out var open, out var close)) return false;

            // Take any doc comment / attribute lines sitting directly above the declaration.
            var start = declaration;
            while (start > 0)
            {
                var above = lines[start - 1].TrimStart();
                if (above.StartsWith("///") || above.StartsWith("[") || above.StartsWith("//")) start--;
                else break;
            }

            // And one blank line above that, so removal does not leave a double gap.
            if (start > 0 && lines[start - 1].Trim().Length == 0) start--;

            lines.RemoveRange(start, close - start + 1);
            WriteAll(projectRelativePath, string.Join(newline, lines));
            return true;
        }

        /// <summary>
        /// Replaces the inside of a brace-delimited block, keeping its declaration line.
        /// <paramref name="body"/> carries its own indentation.
        /// </summary>
        public static bool ReplaceBlockBody(string projectRelativePath, string declarationContains, string body)
        {
            if (!Exists(projectRelativePath)) return false;

            var text = ReadAll(projectRelativePath);
            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var declaration = lines.FindIndex(l => l.Contains(declarationContains));
            if (declaration < 0) return false;

            if (!TryFindBlock(lines, declaration, out var open, out var close)) return false;

            lines.RemoveRange(open + 1, close - open - 1);
            lines.InsertRange(open + 1, body.Replace("\r\n", "\n").Split('\n'));

            WriteAll(projectRelativePath, string.Join(newline, lines));
            return true;
        }

        /// <summary>
        /// Locates the opening brace at or after <paramref name="declaration"/> and its match.
        /// String literals are blanked first so a brace inside one cannot skew the count.
        /// </summary>
        private static bool TryFindBlock(List<string> lines, int declaration, out int open, out int close)
        {
            open = close = -1;

            var depth = 0;
            for (var i = declaration; i < lines.Count; i++)
            {
                var stripped = StripLiterals(lines[i]);

                foreach (var character in stripped)
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

                // A declaration whose body never opens (e.g. an interface member) is not a block.
                if (open < 0 && stripped.Contains(";")) return false;
            }

            return false;
        }

        private static string StripLiterals(string line)
        {
            var withoutComment = Regex.Replace(line, "//.*$", string.Empty);
            var withoutStrings = Regex.Replace(withoutComment, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return Regex.Replace(withoutStrings, "'(?:\\\\.|[^'\\\\])*'", "''");
        }

        /// <summary>
        /// Replaces every occurrence of <paramref name="find"/>. Returns how many went.
        /// Unlike the anchored patches this has no marker: the count being zero is itself
        /// the "already applied" signal.
        /// </summary>
        public static int ReplaceEvery(string projectRelativePath, string find, string replacement)
        {
            if (!Exists(projectRelativePath)) return 0;

            var text = ReadAll(projectRelativePath);
            var count = 0;
            var index = text.IndexOf(find, StringComparison.Ordinal);

            while (index >= 0)
            {
                count++;
                index = text.IndexOf(find, index + find.Length, StringComparison.Ordinal);
            }

            if (count == 0) return 0;

            WriteAll(projectRelativePath, text.Replace(find, replacement));
            return count;
        }

        /// <summary>Makes sure a <c>using</c> directive is present at the top of the file.</summary>
        public static void EnsureUsing(string projectRelativePath, string usingLine)
        {
            if (!Exists(projectRelativePath)) return;

            var text = ReadAll(projectRelativePath);
            if (text.Contains(usingLine)) return;

            var newline = DetectNewline(text);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var lastUsing = lines.FindLastIndex(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal));
            lines.Insert(lastUsing >= 0 ? lastUsing + 1 : 0, usingLine);
            WriteAll(projectRelativePath, string.Join(newline, lines));
        }

        public static string Describe(Outcome outcome, string path, string manualHint)
        {
            switch (outcome)
            {
                case Outcome.Applied: return $"Patched {path}";
                case Outcome.AlreadyApplied: return $"{path} was already patched";
                case Outcome.FileMissing: return $"Not found: {path}";
                default:
                    return $"Could not find the anchor in {path}. Apply this edit by hand: {manualHint}";
            }
        }

        // ── File IO ────────────────────────────────────────────────────────────

        public static string ReadAll(string projectRelativePath) =>
            _memory != null
                ? _memory[projectRelativePath]
                : File.ReadAllText(MeticaPaths.ToAbsolute(projectRelativePath));

        /// <summary>
        /// Overwrites a file, keeping a copy of the pre-tool original in the backup folder.
        /// For callers that rewrite a whole file rather than patching lines.
        /// </summary>
        public static void WriteWithBackup(string projectRelativePath, string contents) =>
            WriteAll(projectRelativePath, contents);

        private static void WriteAll(string projectRelativePath, string contents)
        {
            if (_memory != null)
            {
                _memory[projectRelativePath] = contents;
                return;
            }

            Backup(projectRelativePath);
            File.WriteAllText(MeticaPaths.ToAbsolute(projectRelativePath), contents);
        }

        private static void Backup(string projectRelativePath)
        {
            try
            {
                var target = Path.Combine(MeticaPaths.BackupRoot, projectRelativePath).Replace('\\', '/');
                if (File.Exists(target)) return; // keep the pre-tool original, not the latest state

                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? MeticaPaths.BackupRoot);
                File.Copy(MeticaPaths.ToAbsolute(projectRelativePath), target);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Metica Integration] Could not back up {projectRelativePath}: {e.Message}");
            }
        }

        private static string DetectNewline(string text) => text.Contains("\r\n") ? "\r\n" : "\n";
    }
}
