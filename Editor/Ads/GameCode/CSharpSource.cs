using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Just enough C# reading to patch a game's ads code by position: the text with comments
    /// and literals blanked (same length, so every index still points at the original), its
    /// methods with their bodies, and the statements of a block.
    ///
    /// <para>Not a compiler: it trusts the file to compile, and anything it cannot place with
    /// confidence is reported back for a hand edit instead of guessed.</para>
    /// </summary>
    internal static class CSharpSource
    {
        // ── Masking ────────────────────────────────────────────────────────────

        /// <summary>
        /// The text with comments and the contents of string and char literals replaced by
        /// spaces — quotes, newlines and every other character kept in place.
        /// </summary>
        public static string Mask(string text)
        {
            var masked = new StringBuilder(text);
            var i = 0;
            var lineStart = true;
            while (i < text.Length)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '\n') { lineStart = true; i++; continue; }
                if (c == ' ' || c == '\t' || c == '\r') { i++; continue; }

                if (lineStart && c == '#')
                {
                    // A preprocessor line (#if, #region …) is not code to split statements on.
                    while (i < text.Length && text[i] != '\n') Blank(masked, text, i++);
                    continue;
                }
                lineStart = false;

                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n') Blank(masked, text, i++);
                }
                else if (c == '/' && next == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 2;
                    while (i < end) Blank(masked, text, i++);
                }
                else if (c == '"' || ((c == '@' || c == '$') && (next == '"' || ((next == '@' || next == '$') && i + 2 < text.Length && text[i + 2] == '"'))))
                {
                    i = MaskString(text, masked, i);
                }
                else if (c == '\'')
                {
                    i = MaskChar(text, masked, i);
                }
                else
                {
                    i++;
                }
            }

            return masked.ToString();
        }

        private static void Blank(StringBuilder masked, string text, int i)
        {
            if (text[i] != '\n' && text[i] != '\r') masked[i] = ' ';
        }

        /// <summary>Masks one string literal (regular, verbatim, interpolated) starting at <paramref name="start"/>; returns the index after it.</summary>
        private static int MaskString(string text, StringBuilder masked, int start)
        {
            var i = start;
            var verbatim = false;
            var interpolated = false;
            while (text[i] != '"')
            {
                if (text[i] == '@') verbatim = true;
                if (text[i] == '$') interpolated = true;
                i++;
            }

            i++; // opening quote
            while (i < text.Length)
            {
                var c = text[i];
                if (!verbatim && c == '\\') { Blank(masked, text, i); if (i + 1 < text.Length) Blank(masked, text, i + 1); i += 2; continue; }
                if (verbatim && c == '"' && i + 1 < text.Length && text[i + 1] == '"') { Blank(masked, text, i); Blank(masked, text, i + 1); i += 2; continue; }
                if (c == '"') return i + 1;
                if (!verbatim && c == '\n') return i; // unterminated: stop at the line end

                if (interpolated && c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{') { Blank(masked, text, i); Blank(masked, text, i + 1); i += 2; continue; }

                    // An interpolation hole: code, possibly with its own strings — skip it whole.
                    var depth = 0;
                    while (i < text.Length)
                    {
                        if (text[i] == '"' || (text[i] == '$' || text[i] == '@') && i + 1 < text.Length && text[i + 1] == '"')
                        {
                            var after = MaskString(text, masked, i);
                            for (var k = i; k < after; k++) Blank(masked, text, k);
                            i = after;
                            continue;
                        }
                        if (text[i] == '{') depth++;
                        if (text[i] == '}') depth--;
                        Blank(masked, text, i);
                        i++;
                        if (depth == 0) break;
                    }
                    continue;
                }

                Blank(masked, text, i);
                i++;
            }

            return i;
        }

        private static int MaskChar(string text, StringBuilder masked, int start)
        {
            // 'x', '\n', '\'', 'A' — up to the closing quote on the same line.
            var i = start + 1;
            while (i < text.Length && text[i] != '\n')
            {
                if (text[i] == '\\') { Blank(masked, text, i); if (i + 1 < text.Length) Blank(masked, text, i + 1); i += 2; continue; }
                if (text[i] == '\'') return i + 1;
                Blank(masked, text, i);
                i++;
            }
            return i;
        }

        // ── Brackets ───────────────────────────────────────────────────────────

        /// <summary>The index of the bracket that closes the one at <paramref name="open"/>, or -1.</summary>
        public static int Match(string masked, int open)
        {
            var opener = masked[open];
            var closer = opener == '{' ? '}' : opener == '(' ? ')' : opener == '[' ? ']' : '\0';
            var depth = 0;
            for (var i = open; i < masked.Length; i++)
            {
                if (masked[i] == opener) depth++;
                else if (masked[i] == closer && --depth == 0) return i;
            }
            return -1;
        }

        // ── Types and methods ──────────────────────────────────────────────────

        internal sealed class TypeDecl
        {
            public string Name;
            public string Namespace;
            public int Open;
            public int Close;

            public string FullName => string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;
        }

        internal sealed class Method
        {
            public string Name;
            public string ReturnType;
            public string Modifiers;
            public string Parameters;
            public int Start;     // first character of the declaration (its modifiers)
            public int Open;      // the body's '{'
            public int Close;     // the body's '}'
            public TypeDecl Type;

            public bool IsStatic => Regex.IsMatch(Modifiers, @"\bstatic\b");
            public bool IsAsync => Regex.IsMatch(Modifiers, @"\basync\b");
            public string Label => $"{Name}({Parameters.Trim()})";
        }

        private static readonly HashSet<string> NotNames = new HashSet<string>
        {
            "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new", "else",
            "do", "try", "finally", "throw", "await", "sizeof", "typeof", "nameof", "default", "checked", "unchecked",
            "when", "get", "set", "add", "remove", "base", "this"
        };

        private static readonly HashSet<string> Modifiers = new HashSet<string>
        {
            "public", "private", "protected", "internal", "static", "virtual", "override", "async", "sealed",
            "new", "unsafe", "extern", "abstract", "partial", "readonly"
        };

        private static readonly Regex MethodDecl = new Regex(
            @"(?<mods>(?:\b(?:public|private|protected|internal|static|virtual|override|async|sealed|new|unsafe|extern|partial)\s+)*)" +
            @"(?<ret>[A-Za-z_][\w\.]*(?:\s*<[^;{}()=]*?>)?(?:\s*\[\s*\])*\??)\s+(?<name>[A-Za-z_]\w*)\s*" +
            @"\((?<params>[^;{}]*?)\)\s*(?:where\s[^{;=]*)?\{",
            RegexOptions.Compiled);

        private static readonly Regex TypeDeclRegex = new Regex(@"\b(?:class|struct)\s+(?<name>[A-Za-z_]\w*)[^{;]*\{", RegexOptions.Compiled);
        private static readonly Regex NamespaceBlock = new Regex(@"\bnamespace\s+(?<name>[\w\.]+)\s*\{", RegexOptions.Compiled);
        private static readonly Regex NamespaceFile = new Regex(@"\bnamespace\s+(?<name>[\w\.]+)\s*;", RegexOptions.Compiled);

        public static List<TypeDecl> Types(string masked)
        {
            var namespaces = NamespaceBlock.Matches(masked).Cast<Match>()
                .Select(m => (name: m.Groups["name"].Value, open: m.Index + m.Length - 1))
                .Select(n => (n.name, n.open, close: Match(masked, n.open)))
                .ToList();
            var fileNamespace = NamespaceFile.Match(masked);

            var types = new List<TypeDecl>();
            foreach (Match m in TypeDeclRegex.Matches(masked))
            {
                var open = m.Index + m.Length - 1;
                var ns = namespaces.Where(n => n.open < open && n.close > open)
                    .Select(n => n.name).LastOrDefault() ?? (fileNamespace.Success ? fileNamespace.Groups["name"].Value : null);
                types.Add(new TypeDecl { Name = m.Groups["name"].Value, Namespace = ns, Open = open, Close = Match(masked, open) });
            }

            // Nested types: Outer.Inner, so the full name resolves.
            foreach (var type in types)
            {
                var outer = types.Where(t => t != type && t.Open < type.Open && t.Close > type.Close).OrderBy(t => t.Close - t.Open).FirstOrDefault();
                if (outer != null) type.Name = outer.Name + "." + type.Name;
            }

            return types;
        }

        /// <summary>The methods with a block body, outermost only (local functions are part of their method).</summary>
        public static List<Method> Methods(string masked)
        {
            var types = Types(masked);
            var found = new List<Method>();

            foreach (Match m in MethodDecl.Matches(masked))
            {
                var name = m.Groups["name"].Value;
                var ret = m.Groups["ret"].Value.Trim();
                if (NotNames.Contains(name) || NotNames.Contains(ret) || Modifiers.Contains(ret)) continue;

                var open = m.Index + m.Length - 1;
                var close = Match(masked, open);
                if (close < 0) continue;

                var type = types.Where(t => t.Open < m.Index && t.Close > m.Index).OrderBy(t => t.Close - t.Open).FirstOrDefault();
                if (type == null) continue;

                found.Add(new Method
                {
                    Name = name,
                    ReturnType = ret,
                    Modifiers = m.Groups["mods"].Value,
                    Parameters = m.Groups["params"].Value,
                    Start = m.Index,
                    Open = open,
                    Close = close,
                    Type = type
                });
            }

            // Drop anything inside another method's body.
            return found.Where(method => !found.Any(other => other != method && other.Open < method.Start && other.Close > method.Close))
                .ToList();
        }

        /// <summary>The names in a parameter list, with ref / out / in kept for passing them on.</summary>
        public static List<string> Arguments(string parameters)
        {
            var arguments = new List<string>();
            foreach (var raw in SplitTopLevel(parameters, ','))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var eq = part.IndexOf('=');
                if (eq >= 0) part = part.Substring(0, eq).Trim();
                var name = Regex.Match(part, @"([A-Za-z_]\w*)\s*$").Groups[1].Value;
                var modifier = Regex.Match(part, @"^\s*(ref|out|in)\b").Groups[1].Value;
                arguments.Add(modifier.Length > 0 ? modifier + " " + name : name);
            }
            return arguments;
        }

        /// <summary>Splits on <paramref name="separator"/> outside brackets.</summary>
        public static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '(' || c == '<' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == '>' || c == ']' || c == '}') depth--;
                else if (c == separator && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        // ── Statements ─────────────────────────────────────────────────────────

        internal sealed class Statement
        {
            /// <summary>Where the statement's own text starts (its first code character).</summary>
            public int Start;

            /// <summary>Just after its last character (';' or '}').</summary>
            public int End;

            /// <summary>Where the leading whitespace and comments that belong to it start.</summary>
            public int LeadStart;

            /// <summary>The first word: if, for, try, var, an identifier …</summary>
            public string Keyword;

            public bool IsControl => ControlKeywords.Contains(Keyword) || Keyword == "{";
        }

        private static readonly HashSet<string> ControlKeywords = new HashSet<string>
        {
            "if", "else", "for", "foreach", "while", "do", "try", "switch", "lock", "using", "fixed", "unsafe",
            "checked", "unchecked"
        };

        /// <summary>
        /// The statements directly inside the block whose braces are at <paramref name="open"/> and
        /// <paramref name="close"/> — an if with its else, a try with its catch and finally, a
        /// statement with a lambda body up to its ';', each counted once.
        /// </summary>
        public static List<Statement> Statements(string masked, int open, int close)
        {
            var statements = new List<Statement>();
            var i = open + 1;
            var leadStart = i;

            while (i < close)
            {
                while (i < close && char.IsWhiteSpace(masked[i])) i++;
                if (i >= close) break;

                var start = i;
                var keyword = masked[i] == '{' ? "{" : Regex.Match(masked.Substring(i, Math.Min(32, close - i)), @"^[A-Za-z_]\w*").Value;
                var control = ControlKeywords.Contains(keyword) || keyword == "{";
                var end = StatementEnd(masked, start, close, control, keyword);

                statements.Add(new Statement { Start = start, End = end, LeadStart = LineStart(masked, leadStart, start), Keyword = keyword });
                i = end;
                leadStart = end;
            }

            return statements;
        }

        /// <summary>Back to the start of the first line after the previous statement, so a moved statement keeps its comments and indentation.</summary>
        private static int LineStart(string masked, int previousEnd, int start)
        {
            var newline = masked.IndexOf('\n', previousEnd);
            return newline >= 0 && newline < start ? newline + 1 : previousEnd;
        }

        private static int StatementEnd(string masked, int start, int limit, bool control, string keyword)
        {
            var paren = 0;
            var brace = 0;
            var bracket = 0;
            for (var i = start; i < limit; i++)
            {
                var c = masked[i];
                if (c == '(') paren++;
                else if (c == ')') paren--;
                else if (c == '[') bracket++;
                else if (c == ']') bracket--;
                else if (c == '{') brace++;
                else if (c == '}')
                {
                    brace--;
                    if (brace == 0 && paren == 0 && bracket == 0)
                    {
                        var after = NextToken(masked, i + 1, limit);
                        if (after == ";") return masked.IndexOf(';', i + 1) + 1;
                        if (after == "else" || after == "catch" || after == "finally" || (keyword == "do" && after == "while")) continue;
                        if (control) return i + 1;
                    }
                }
                else if (c == ';' && paren == 0 && brace == 0 && bracket == 0)
                {
                    // `if (x) a(); else b();` — the else still belongs to this statement.
                    if (keyword == "if" && NextToken(masked, i + 1, limit) == "else") continue;
                    return i + 1;
                }
            }
            return limit;
        }

        private static string NextToken(string masked, int from, int limit)
        {
            var i = from;
            while (i < limit && char.IsWhiteSpace(masked[i])) i++;
            if (i >= limit) return "";
            if (!char.IsLetter(masked[i]) && masked[i] != '_') return masked[i].ToString();
            return Regex.Match(masked.Substring(i, Math.Min(16, limit - i)), @"^[A-Za-z_]\w*").Value;
        }

        /// <summary>The '{' … '}' blocks directly inside a statement (an if's branches, a try's parts).</summary>
        public static List<(int open, int close)> InnerBlocks(string masked, Statement statement)
        {
            var blocks = new List<(int, int)>();
            var i = statement.Start;
            while (i < statement.End)
            {
                if (masked[i] == '(') { i = Match(masked, i) + 1; continue; }
                if (masked[i] == '{')
                {
                    var close = Match(masked, i);
                    if (close < 0) break;
                    blocks.Add((i, close));
                    i = close + 1;
                    continue;
                }
                i++;
            }
            return blocks;
        }

        /// <summary>The indentation of the line <paramref name="index"/> is on.</summary>
        public static string IndentAt(string text, int index)
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            var i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(lineStart, i - lineStart);
        }

        public static string NewlineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";
    }
}
