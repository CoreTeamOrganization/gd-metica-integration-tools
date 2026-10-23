using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Members a game added by hand to the GD SDK's base classes — an abstract
    /// <c>RefershBanner()</c> on BannerAdUnit, a method on IAdNetworkService — that the tool's
    /// wrapper classes, written for the original SDK, don't have. Without them nothing compiles
    /// (CS0534 / CS0535).
    ///
    /// <para><see cref="Find"/> walks a wrapper class's bases in the project: abstract methods
    /// of base classes not overridden lower down, and methods of interfaces with no body.
    /// <see cref="AddStubs"/> gives each one a body that compiles and does nothing, marked
    /// with <see cref="Marker"/> so the step can keep pointing at it until it's filled in —
    /// only the game knows what its own member should do. Methods only: an abstract property
    /// or event is reported, not stubbed.</para>
    /// </summary>
    internal static class BaseMemberStubs
    {
        public const string Marker = "Metica Integration Tools: stub for the game's own";

        internal sealed class Member
        {
            public string Owner;       // the base class or interface declaring it
            public string Name;
            public string Declaration; // the stub's first line, without the body
            public string ReturnType;
            public List<string> OutParameters = new List<string>();
            public bool Stubbable = true;
        }

        internal sealed class TypeInfo
        {
            public string Name;
            public bool IsInterface;
            public bool IsAbstract;
            public List<string> Bases;
            public string Body; // masked
            public string File;
        }

        private static readonly Regex TypeHead = new Regex(
            @"(?<mods>(?:\b(?:public|private|protected|internal|abstract|sealed|static|partial)\s+)*)" +
            @"\b(?<kind>class|interface)\s+(?<name>[A-Za-z_]\w*)(?:\s*<[^>{]*>)?\s*(?::\s*(?<bases>[^{]+))?\{",
            RegexOptions.Compiled);

        private static readonly Regex AbstractMethod = new Regex(
            @"(?<access>(?:\b(?:public|protected|internal)\s+)+)abstract\s+(?<ret>[^;{}()=]+?)\s+(?<name>[A-Za-z_]\w*)\s*" +
            @"\((?<params>[^;{}()]*)\)\s*;",
            RegexOptions.Compiled);

        private static readonly Regex AbstractOther = new Regex(
            @"\babstract\s+(?:event\s+)?[^;{}()=]+?\s+(?<name>[A-Za-z_]\w*)\s*(?:\{|;|=>)",
            RegexOptions.Compiled);

        private static readonly Regex InterfaceMethod = new Regex(
            @"(?<=^|[;{}\]])\s*(?<ret>[A-Za-z_][\w\.]*(?:\s*<[^;{}()=]*?>)?(?:\s*\[\s*\])*\??)\s+(?<name>[A-Za-z_]\w*)\s*" +
            @"\((?<params>[^;{}()]*)\)\s*;",
            RegexOptions.Compiled);

        /// <summary>The abstract classes and interfaces of a folder's scripts, read once for several <see cref="Find"/>s.</summary>
        internal sealed class Bases
        {
            internal Dictionary<string, TypeInfo> Types;
        }

        public static Bases ScanBases(string folder) =>
            new Bases { Types = folder == null ? new Dictionary<string, TypeInfo>() : ProjectTypes(folder) };

        /// <summary>The members <paramref name="className"/> (in <paramref name="classText"/>) is missing.</summary>
        public static List<Member> Find(string classText, string className, Bases bases)
        {
            var missing = new List<Member>();
            var self = Parse(CSharpSource.Mask(classText), null).FirstOrDefault(t => t.Name == className);
            if (self == null) return missing;

            var types = bases.Types;
            var declared = new HashSet<string>(Declared(self.Body));
            var seen = new HashSet<string>();

            void Walk(IEnumerable<string> bases)
            {
                foreach (var name in bases)
                {
                    if (!seen.Add(name) || !types.TryGetValue(name, out var type)) continue;

                    if (type.IsInterface)
                    {
                        foreach (Match m in InterfaceMethod.Matches(type.Body))
                            if (!declared.Contains(m.Groups["name"].Value))
                                missing.Add(Stub(type.Name, "public", m, isOverride: false));
                    }
                    else
                    {
                        var abstracts = AbstractMethod.Matches(type.Body).Cast<Match>().ToList();
                        foreach (var m in abstracts)
                            if (!declared.Contains(m.Groups["name"].Value))
                                missing.Add(Stub(type.Name, m.Groups["access"].Value.Trim(), m, isOverride: true));

                        var others = AbstractOther.Matches(type.Body).Cast<Match>().ToList();
                        foreach (var m in others)
                            if (!declared.Contains(m.Groups["name"].Value))
                                missing.Add(new Member { Owner = type.Name, Name = m.Groups["name"].Value, Stubbable = false });

                        // What this base declares — with a body, or abstract and so required of the
                        // class already — answers for the interfaces and bases above it.
                        declared.UnionWith(Declared(type.Body));
                        declared.UnionWith(abstracts.Concat(others).Select(m => m.Groups["name"].Value));
                    }

                    Walk(type.Bases);
                }
            }

            Walk(self.Bases);
            return missing.GroupBy(m => m.Name).Select(g => g.First()).ToList();
        }

        /// <summary>The class with a stub for every stubbable member, before its closing brace.</summary>
        public static string AddStubs(string classText, string className, IEnumerable<Member> members)
        {
            var masked = CSharpSource.Mask(classText);
            var type = CSharpSource.Types(masked).FirstOrDefault(t => t.Name == className);
            if (type == null) return classText;

            var nl = CSharpSource.NewlineOf(classText);
            var indent = CSharpSource.IndentAt(classText, type.Open) + "    ";
            var stubs = new StringBuilder();
            foreach (var member in members.Where(m => m.Stubbable))
            {
                stubs.Append(nl)
                    .Append(indent).Append($"// {Marker} {member.Owner}.{member.Name}() — fill in what it should do for Metica").Append(nl)
                    .Append(indent).Append(member.Declaration).Append(nl)
                    .Append(indent).Append('{').Append(nl);
                foreach (var output in member.OutParameters)
                    stubs.Append(indent).Append("    ").Append($"{output} = default;").Append(nl);
                if (member.ReturnType != "void")
                    stubs.Append(indent).Append("    ").Append("return default;").Append(nl);
                stubs.Append(indent).Append('}').Append(nl);
            }

            if (stubs.Length == 0) return classText;

            // Before the line holding the class's closing brace.
            var close = type.Close;
            var lineStart = classText.LastIndexOf('\n', close - 1) + 1;
            return classText.Substring(0, lineStart) + stubs.ToString().Substring(nl.Length) + classText.Substring(lineStart);
        }

        /// <summary>"Base.Name" for every stub still carrying the marker in the text.</summary>
        public static List<string> OpenStubs(string text) =>
            Regex.Matches(text, Regex.Escape(Marker) + @" (?<member>[\w\.]+)\(\)").Cast<Match>()
                .Select(m => m.Groups["member"].Value).ToList();

        /// <summary>A script in <paramref name="searchFolder"/>, other than <paramref name="except"/>, that overrides or implements the member.</summary>
        public static string ExampleOf(string memberName, string searchFolder, string except)
        {
            var pattern = new Regex(@"\b(?:override|public)\b[^;{}()=]*\b" + Regex.Escape(memberName) + @"\s*\([^;{}]*\)\s*(?:\{|=>)");
            return Scripts(searchFolder)
                .Where(path => !SamePath(path, except))
                .FirstOrDefault(path =>
                {
                    var text = File.ReadAllText(MeticaPaths.ToAbsolute(path));
                    return text.Contains(memberName) && pattern.IsMatch(CSharpSource.Mask(text));
                });
        }

        // ── Parsing ────────────────────────────────────────────────────────────

        private static Member Stub(string owner, string access, Match m, bool isOverride)
        {
            var ret = Regex.Replace(m.Groups["ret"].Value.Trim(), @"\s+", " ");
            var parameters = m.Groups["params"].Value.Trim();
            var member = new Member
            {
                Owner = owner,
                Name = m.Groups["name"].Value,
                ReturnType = ret,
                Declaration = $"{access}{(isOverride ? " override" : string.Empty)} {ret} {m.Groups["name"].Value}({parameters})"
            };

            foreach (var parameter in CSharpSource.SplitTopLevel(parameters, ','))
            {
                var words = parameter.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 3 && words[0] == "out") member.OutParameters.Add(words.Last().Split('=')[0].Trim());
            }

            return member;
        }

        /// <summary>Names of the methods a type body declares (with a body, or overriding).</summary>
        /// <summary>
        /// Names of the methods (with a body) and properties / events (with accessors or =>) a
        /// type body declares. Loose on purpose: a name too many only means a member isn't
        /// reported.
        /// </summary>
        private static IEnumerable<string> Declared(string body) =>
            Regex.Matches(body, @"(?<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:where\s[^{;=]*)?(?:\{|=>)")
                .Cast<Match>()
                .Concat(Regex.Matches(body, @"(?<name>[A-Za-z_]\w*)\s*(?:\{|=>)").Cast<Match>())
                .Concat(Regex.Matches(body, @"\bevent\s+[^;{}()=]+?\s+(?<name>[A-Za-z_]\w*)\s*;").Cast<Match>())
                .Select(m => m.Groups["name"].Value);

        private static IEnumerable<TypeInfo> Parse(string masked, string file)
        {
            foreach (Match m in TypeHead.Matches(masked))
            {
                var open = m.Index + m.Length - 1;
                var close = CSharpSource.Match(masked, open);
                if (close <= open) continue;

                yield return new TypeInfo
                {
                    Name = m.Groups["name"].Value,
                    IsInterface = m.Groups["kind"].Value == "interface",
                    IsAbstract = Regex.IsMatch(m.Groups["mods"].Value, @"\babstract\b"),
                    Bases = m.Groups["bases"].Success
                        ? CSharpSource.SplitTopLevel(Regex.Replace(m.Groups["bases"].Value, @"\bwhere\b.*$", ""), ',')
                            .Select(b => Regex.Replace(b.Trim(), @"<.*$", ""))
                            .Select(b => b.Substring(b.LastIndexOf('.') + 1))
                            .Where(b => b.Length > 0)
                            .ToList()
                        : new List<string>(),
                    Body = TopLevel(masked, open, close),
                    File = file
                };
            }
        }

        /// <summary>The body with nested type and method bodies blanked, so only its own members show.</summary>
        private static string TopLevel(string masked, int open, int close)
        {
            var body = masked.Substring(open + 1, close - open - 1).ToCharArray();
            for (var i = 0; i < body.Length; i++)
            {
                if (body[i] != '{') continue;
                var end = CSharpSource.Match(new string(body), i);
                if (end <= i) break;
                for (var j = i + 1; j < end; j++) if (body[j] != '\n') body[j] = ' ';
                i = end;
            }

            return new string(body);
        }

        /// <summary>Abstract classes and interfaces in the folder's scripts, by simple name.</summary>
        private static Dictionary<string, TypeInfo> ProjectTypes(string folder)
        {
            var types = new Dictionary<string, TypeInfo>();
            foreach (var path in Scripts(folder))
            {
                var text = File.ReadAllText(MeticaPaths.ToAbsolute(path));
                if (!text.Contains("abstract") && !text.Contains("interface")) continue;

                foreach (var type in Parse(CSharpSource.Mask(text), path))
                    if ((type.IsInterface || type.IsAbstract) && !types.ContainsKey(type.Name))
                        types[type.Name] = type;
            }

            return types;
        }

        private static IEnumerable<string> Scripts(string folder)
        {
            var absolute = MeticaPaths.ToAbsolute(folder).Replace('\\', '/').TrimEnd('/');
            if (!Directory.Exists(absolute)) return Enumerable.Empty<string>();

            // Back to the folder's own form (project-relative in the editor).
            return Directory.GetFiles(absolute, "*.cs", SearchOption.AllDirectories)
                .Select(path => folder.TrimEnd('/', '\\') + "/" + path.Replace('\\', '/').Substring(absolute.Length + 1));
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(a?.Replace('\\', '/'), b?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
}
