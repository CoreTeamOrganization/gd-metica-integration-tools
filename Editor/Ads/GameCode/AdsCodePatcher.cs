using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The edits that move a non-GD-SDK game's own ads code onto Metica, as text in → text out
    /// so they can be checked without Unity:
    ///
    /// <list type="bullet">
    /// <item>Init: the chosen method decides Metica or AppLovin MAX once per session
    /// (<c>UseMetica</c>, from the remote switch) and starts one or the other; its MAX code moves
    /// to <c>&lt;Method&gt;_Max</c>, its AdMob init to <c>&lt;Method&gt;_AdMob</c> (both paths).</item>
    /// <item>Ad formats: each MAX ready / show / hide / load call in the chosen methods is put
    /// behind <c>UseMetica</c>, with the Metica call on the other side — the game's own checks
    /// around the call stay. The game's MAX close / reward handlers are called from Metica's
    /// callbacks.</item>
    /// <item>Revenue: <c>MeticaAdsHooks.OnAdRevenue</c> subscribed to an empty handler the
    /// developer fills in.</item>
    /// </list>
    /// </summary>
    internal static class AdsCodePatcher
    {
        public const string UseMetica = "UseMetica";

        private static readonly Regex MaxCode = new Regex(@"\bMaxSdk(?:Callbacks|Base)?\b");
        private static readonly Regex AdMobCode = new Regex(@"\bMobileAds\s*\.");
        private static readonly Regex LocalDeclaration =
            new Regex(@"^(?!(?:return|throw|await|yield|goto|case|else|new|using|await)\b)(?:const\s+)?(?:var|[A-Za-z_][\w\.]*(?:\s*<[^;=(){}]*>)?(?:\s*\[\s*\])*\??)\s+([A-Za-z_]\w*)\s*(?:=|;)");

        // ── Init ───────────────────────────────────────────────────────────────

        internal sealed class Result
        {
            public string Text;
            public string Problem;
            public readonly List<string> Notes = new List<string>();
        }

        /// <summary>Finds <paramref name="methodLabel"/> (from <see cref="CSharpSource.Method.Label"/>) in a script.</summary>
        public static CSharpSource.Method FindMethod(string masked, string methodLabel) =>
            CSharpSource.Methods(masked).FirstOrDefault(m => m.Label == methodLabel)
            ?? CSharpSource.Methods(masked).FirstOrDefault(m => m.Name == methodLabel);

        /// <summary>
        /// The init patch: <c>UseMetica</c> on the method's class, its MAX statements moved to
        /// <c>&lt;Method&gt;_Max</c>, its AdMob statements to <c>&lt;Method&gt;_AdMob</c>, and in
        /// their place "Metica + AdMob" or "MAX + AdMob". Everything else stays where it is.
        /// </summary>
        public static Result PatchInit(string text, string methodLabel)
        {
            var result = new Result();
            var masked = CSharpSource.Mask(text);
            var method = FindMethod(masked, methodLabel);
            if (method == null) return Fail(result, $"Method {methodLabel} not found.");

            var body = masked.Substring(method.Open, method.Close - method.Open + 1);
            if (body.Contains(UseMetica)) return Fail(result, $"{method.Name} is already patched.");
            if (method.ReturnType != "void") return Fail(result, $"{method.Name} returns {method.ReturnType} — only a void method can be split; patch it by hand.");
            if (Regex.IsMatch(body, @"\byield\b")) return Fail(result, $"{method.Name} is a coroutine — patch it by hand.");

            var statements = CSharpSource.Statements(masked, method.Open, method.Close);
            var kinds = statements.Select(s =>
            {
                var code = masked.Substring(s.Start, s.End - s.Start);
                return MaxCode.IsMatch(code) ? Kind.Max : AdMobCode.IsMatch(code) ? Kind.AdMob : Kind.Stay;
            }).ToList();

            for (var i = 0; i < statements.Count; i++)
            {
                var code = masked.Substring(statements[i].Start, statements[i].End - statements[i].Start);
                if (kinds[i] == Kind.Max && AdMobCode.IsMatch(code))
                    result.Notes.Add($"A statement uses both MAX and AdMob — it moves to {method.Name}_Max: {OneLine(text, statements[i])}");
                if (kinds[i] != Kind.Stay && Regex.IsMatch(code, @"^\s*return\b"))
                    return Fail(result, $"{method.Name} returns from inside its ads code — patch it by hand.");
                if (kinds[i] != Kind.Stay && method.IsAsync && Regex.IsMatch(code, @"\bawait\b"))
                    return Fail(result, $"{method.Name} awaits inside its ads code — patch it by hand.");
            }

            if (!kinds.Contains(Kind.Max)) return Fail(result, $"No AppLovin MAX code in {method.Name}.");

            // Code inside #if … #endif can't move to another method without its condition.
            var conditional = ConditionalRegions(text, method.Open, method.Close);
            if (statements.Where((s, i) => kinds[i] != Kind.Stay).Any(s => conditional.Any(r => r.start < s.Start && s.Start < r.end)))
                return Fail(result, $"{method.Name} has ads code inside #if … #endif — patch it by hand.");

            var localProblem = KeepLocalsTogether(masked, statements, kinds);
            if (localProblem != null) return Fail(result, localProblem);

            var maxName = method.Name + "_Max";
            var adMobName = method.Name + "_AdMob";
            var methods = CSharpSource.Methods(masked);
            if (methods.Any(m => m.Type.Open == method.Type.Open && (m.Name == maxName || m.Name == adMobName)))
                return Fail(result, $"{method.Type.Name} already has {maxName} or {adMobName}.");

            var newline = CSharpSource.NewlineOf(text);
            var memberIndent = CSharpSource.IndentAt(text, method.Start);
            var bodyIndent = statements.Count > 0 ? CSharpSource.IndentAt(text, statements[0].Start) : memberIndent + "    ";
            var unit = bodyIndent.Length > memberIndent.Length ? bodyIndent.Substring(memberIndent.Length) : "    ";
            var arguments = string.Join(", ", CSharpSource.Arguments(method.Parameters));
            var hasAdMob = kinds.Contains(Kind.AdMob);
            var staticWord = method.IsStatic ? "static " : "";

            // The new body: the switch first, then the method's own statements, with the choice
            // where its ads code was.
            var newBody = new StringBuilder();
            newBody.Append(text, method.Open + 1, statements[0].LeadStart - method.Open - 1);
            newBody.Append(bodyIndent).Append("// Metica Integration Tools: Metica or AppLovin MAX for this session, from the remote switch.").Append(newline);
            newBody.Append(bodyIndent).Append($"{UseMetica} = MeticaIntegration.MeticaRemoteConfig.ShouldInitialize(MeticaIntegration.MeticaAdsConfig.Load());").Append(newline);

            // Each statement with the gap before it (its newline and comments); a moved
            // statement takes its gap with it, the first one leaves the choice in its place.
            var choiceWritten = false;
            var cursor = statements[0].LeadStart;
            for (var i = 0; i < statements.Count; i++)
            {
                var gap = text.Substring(cursor, statements[i].LeadStart - cursor);
                cursor = statements[i].End;

                if (kinds[i] == Kind.Stay)
                {
                    newBody.Append(gap).Append(text, statements[i].LeadStart, statements[i].End - statements[i].LeadStart);
                    continue;
                }
                if (choiceWritten) continue;
                choiceWritten = true;

                newBody.Append(gap);
                newBody.Append(bodyIndent).Append($"if ({UseMetica})").Append(newline);
                newBody.Append(bodyIndent).Append('{').Append(newline);
                newBody.Append(bodyIndent).Append(unit).Append("MeticaIntegration.MeticaAdsManager.Initialize();").Append(newline);
                if (hasAdMob) newBody.Append(bodyIndent).Append(unit).Append($"{adMobName}({arguments});").Append(newline);
                newBody.Append(bodyIndent).Append('}').Append(newline);
                newBody.Append(bodyIndent).Append("else").Append(newline);
                newBody.Append(bodyIndent).Append('{').Append(newline);
                newBody.Append(bodyIndent).Append(unit).Append($"{maxName}({arguments});").Append(newline);
                if (hasAdMob) newBody.Append(bodyIndent).Append(unit).Append($"{adMobName}({arguments});").Append(newline);
                newBody.Append(bodyIndent).Append('}');
            }
            newBody.Append(text, statements[statements.Count - 1].End, method.Close - statements[statements.Count - 1].End);

            // The moved code, as methods after this one.
            var moved = new StringBuilder();
            moved.Append(newline).Append(newline);
            moved.Append(MovedMethod(text, statements, kinds, Kind.Max, maxName, method, staticWord, memberIndent, newline,
                "AppLovin MAX init, moved here by the Metica Integration Tools — runs only when Metica is off."));
            if (hasAdMob)
            {
                moved.Append(newline).Append(newline);
                moved.Append(MovedMethod(text, statements, kinds, Kind.AdMob, adMobName, method, staticWord, memberIndent, newline,
                    "AdMob init, moved here by the Metica Integration Tools — runs with Metica and with MAX."));
            }

            var patched = new StringBuilder(text);
            patched.Insert(method.Close + 1, moved.ToString());
            patched.Remove(method.Open + 1, method.Close - method.Open - 1);
            patched.Insert(method.Open + 1, newBody.ToString());

            if (!masked.Substring(method.Type.Open, method.Type.Close - method.Type.Open).Contains($"bool {UseMetica}"))
            {
                patched.Insert(method.Type.Open + 1,
                    newline +
                    $"{memberIndent}/// <summary>" + newline +
                    $"{memberIndent}/// True when this session serves ads through Metica, false for AppLovin MAX — from the" + newline +
                    $"{memberIndent}/// remote switch, set in {method.Name}. Added by the Metica Integration Tools." + newline +
                    $"{memberIndent}/// </summary>" + newline +
                    $"{memberIndent}public static bool {UseMetica} {{ get; private set; }}" + newline);
            }

            result.Text = patched.ToString();
            result.Notes.Add($"{kinds.Count(k => k == Kind.Max)} MAX statement(s) → {maxName}" +
                             (hasAdMob ? $", {kinds.Count(k => k == Kind.AdMob)} AdMob → {adMobName}" : "") +
                             $", {kinds.Count(k => k == Kind.Stay)} stay in {method.Name}");
            return result;
        }

        private enum Kind { Stay, Max, AdMob }

        /// <summary>The spans between each #if and its #endif inside a method body.</summary>
        private static List<(int start, int end)> ConditionalRegions(string text, int open, int close)
        {
            var regions = new List<(int, int)>();
            var stack = new Stack<int>();
            foreach (Match m in Regex.Matches(text.Substring(open, close - open), @"(?m)^[ \t]*#\s*(if|endif)\b"))
            {
                if (m.Groups[1].Value == "if") stack.Push(open + m.Index);
                else if (stack.Count > 0) regions.Add((stack.Pop(), open + m.Index));
            }
            return regions;
        }

        private static string MovedMethod(string text, List<CSharpSource.Statement> statements, List<Kind> kinds, Kind kind,
            string name, CSharpSource.Method method, string staticWord, string indent, string newline, string summary)
        {
            var body = new StringBuilder();
            for (var i = 0; i < statements.Count; i++)
                if (kinds[i] == kind)
                    body.Append(TrimLeadingBlankLines(text.Substring(statements[i].LeadStart, statements[i].End - statements[i].LeadStart), body.Length == 0))
                        .Append(newline);

            return $"{indent}/// <summary>{summary}</summary>" + newline +
                   $"{indent}private {staticWord}void {name}({method.Parameters.Trim()})" + newline +
                   $"{indent}{{" + newline +
                   body +
                   $"{indent}}}";
        }

        private static string TrimLeadingBlankLines(string segment, bool first)
        {
            var lines = segment.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 1 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            var joined = string.Join("\n", lines);
            return segment.Contains("\r\n") ? joined.Replace("\n", "\r\n") : joined;
        }

        /// <summary>
        /// Moved code must not lose a local it uses: a declaration only the moving group uses
        /// moves with it; one used on both sides cannot be split.
        /// </summary>
        private static string KeepLocalsTogether(string masked, List<CSharpSource.Statement> statements, List<Kind> kinds)
        {
            for (var pass = 0; pass < statements.Count; pass++)
            {
                var changed = false;
                for (var d = 0; d < statements.Count; d++)
                {
                    var declaration = LocalDeclaration.Match(masked.Substring(statements[d].Start, statements[d].End - statements[d].Start));
                    if (!declaration.Success || statements[d].IsControl) continue;

                    var name = declaration.Groups[1].Value;
                    var users = Enumerable.Range(0, statements.Count)
                        .Where(u => u != d && Regex.IsMatch(masked.Substring(statements[u].Start, statements[u].End - statements[u].Start), $@"\b{Regex.Escape(name)}\b"))
                        .ToList();
                    var userKinds = users.Select(u => kinds[u]).Distinct().ToList();

                    if (userKinds.Count > 1)
                        return $"Local '{name}' is used by the ads code and by other code — patch it by hand.";
                    if (userKinds.Count == 1 && userKinds[0] != kinds[d])
                    {
                        if (kinds[d] != Kind.Stay)
                            return $"Local '{name}' comes from the ads code but is used by other code — patch it by hand.";
                        kinds[d] = userKinds[0];
                        changed = true;
                    }
                }
                if (!changed) return null;
            }
            return null;
        }

        // ── Ad formats ─────────────────────────────────────────────────────────

        internal enum Verb { None, Show, Hide, Load }

        internal sealed class Format
        {
            public string Name;              // "interstitial"
            public string Callbacks;         // MaxSdkCallbacks.<Callbacks>
            public string ReadyCall;         // MaxSdk.<ReadyCall>(…), or null
            public string MeticaReady;       // the Metica side of the ready check
            public Dictionary<string, Verb> Calls;
            public bool HasReward;
        }

        public static readonly Format Interstitial = new Format
        {
            Name = "interstitial",
            Callbacks = "Interstitial",
            ReadyCall = "IsInterstitialReady",
            MeticaReady = "MeticaIntegration.MeticaAdsManager.HasInterstitial(false)",
            Calls = new Dictionary<string, Verb> { { "ShowInterstitial", Verb.Show }, { "LoadInterstitial", Verb.Load } }
        };

        public static readonly Format Rewarded = new Format
        {
            Name = "rewarded",
            Callbacks = "Rewarded",
            ReadyCall = "IsRewardedAdReady",
            MeticaReady = "MeticaIntegration.MeticaAdsManager.HasRewarded(false)",
            Calls = new Dictionary<string, Verb> { { "ShowRewardedAd", Verb.Show }, { "LoadRewardedAd", Verb.Load } },
            HasReward = true
        };

        public static readonly Format Banner = new Format
        {
            Name = "banner",
            Callbacks = "Banner",
            Calls = new Dictionary<string, Verb>
            {
                { "ShowBanner", Verb.Show }, { "HideBanner", Verb.Hide },
                // MAX-only: the Metica runtime creates, places and refreshes its own banner.
                { "CreateBanner", Verb.None }, { "DestroyBanner", Verb.None }, { "UpdateBannerPosition", Verb.None },
                { "SetBannerBackgroundColor", Verb.None }, { "SetBannerWidth", Verb.None }, { "SetBannerPlacement", Verb.None },
                { "StartBannerAutoRefresh", Verb.None }, { "StopBannerAutoRefresh", Verb.None }, { "SetBannerExtraParameter", Verb.None }
            }
        };

        public static readonly Format MRec = new Format
        {
            Name = "MREC",
            Callbacks = "MRec",
            Calls = new Dictionary<string, Verb>
            {
                { "ShowMRec", Verb.Show }, { "HideMRec", Verb.Hide },
                { "CreateMRec", Verb.None }, { "DestroyMRec", Verb.None }, { "UpdateMRecPosition", Verb.None },
                { "SetMRecPlacement", Verb.None }, { "StartMRecAutoRefresh", Verb.None }, { "StopMRecAutoRefresh", Verb.None },
                { "SetMRecExtraParameter", Verb.None }
            }
        };

        private static Regex StatementCall(Format format) =>
            new Regex($@"\bMaxSdk\s*\.\s*(?<call>{string.Join("|", format.Calls.Keys)})\s*\(");

        private static Regex ReadyCall(Format format) =>
            format.ReadyCall == null ? null : new Regex($@"\bMaxSdk\s*\.\s*{format.ReadyCall}\s*\(");

        /// <summary>The methods of a script that make this format's MAX calls.</summary>
        public static List<CSharpSource.Method> Candidates(string masked, Format format)
        {
            var statement = StatementCall(format);
            var ready = ReadyCall(format);
            return CSharpSource.Methods(masked).Where(m =>
            {
                var body = masked.Substring(m.Open, m.Close - m.Open + 1);
                return statement.IsMatch(body) || (ready != null && ready.IsMatch(body));
            }).ToList();
        }

        /// <summary>This format's MAX calls in the method that are not behind <c>UseMetica</c> yet.</summary>
        public static int Unpatched(string masked, CSharpSource.Method method, Format format, string gate)
        {
            var guarded = GuardedSpans(masked, method, gate);
            var count = 0;
            foreach (Match call in StatementCall(format).Matches(masked.Substring(method.Open, method.Close - method.Open + 1)))
                if (!guarded.Any(span => span.start <= method.Open + call.Index && method.Open + call.Index < span.end)) count++;

            var ready = ReadyCall(format);
            if (ready != null)
                foreach (Match call in ready.Matches(masked.Substring(method.Open, method.Close - method.Open + 1)))
                {
                    var before = masked.Substring(Math.Max(0, method.Open + call.Index - 200), Math.Min(200, method.Open + call.Index));
                    if (!Regex.IsMatch(before, $@"{Regex.Escape(gate)}\s*\?[^;]*:\s*$")) count++;
                }

            return count;
        }

        /// <summary>The MAX side of every <c>if (gate) … else …</c> / <c>if (!gate) …</c> in the method.</summary>
        private static List<(int start, int end)> GuardedSpans(string masked, CSharpSource.Method method, string gate)
        {
            var spans = new List<(int, int)>();
            var pattern = new Regex($@"\bif\s*\(\s*(?<not>!?)\s*{Regex.Escape(gate)}\s*\)");
            foreach (Match m in pattern.Matches(masked, method.Open))
            {
                if (m.Index > method.Close) break;
                var thenOpen = masked.IndexOf('{', m.Index + m.Length);
                if (thenOpen < 0) continue;
                var thenClose = CSharpSource.Match(masked, thenOpen);
                if (thenClose < 0) continue;
                if (m.Groups["not"].Value == "!") { spans.Add((thenOpen, thenClose)); continue; }

                var afterThen = Regex.Match(masked.Substring(thenClose + 1), @"^\s*else\s*\{");
                if (!afterThen.Success) continue;
                var elseOpen = thenClose + 1 + afterThen.Length - 1;
                spans.Add((elseOpen, CSharpSource.Match(masked, elseOpen)));
            }
            return spans;
        }

        /// <summary>
        /// The game's MAX handlers for one callback, subscribed by name in this script's class —
        /// only ones <paramref name="caller"/> can call (a static method can't call an instance one).
        /// </summary>
        public static List<string> Handlers(string masked, CSharpSource.Method caller, Format format, string callbackEvent)
        {
            var type = caller.Type;
            var pattern = new Regex($@"\bMaxSdkCallbacks\s*\.\s*{format.Callbacks}\s*\.\s*{callbackEvent}\s*\+=\s*(?<handler>[A-Za-z_][\w\.]*)\s*;");
            var methods = CSharpSource.Methods(masked).Where(m => m.Type.Open == type.Open).ToList();
            return pattern.Matches(masked, type.Open).Cast<Match>()
                .Where(m => m.Index < type.Close)
                .Select(m => m.Groups["handler"].Value)
                .Select(h => h.StartsWith("this.") ? h.Substring(5) : h)
                .Where(h => methods.Any(m => m.Name == h && (!caller.IsStatic || m.IsStatic)))
                .Distinct()
                .ToList();
        }

        /// <summary>Lambda subscriptions — their code can't be called from Metica's callbacks.</summary>
        private static bool HasLambdaHandler(string masked, Format format, string callbackEvent) =>
            Regex.IsMatch(masked, $@"\bMaxSdkCallbacks\s*\.\s*{format.Callbacks}\s*\.\s*{callbackEvent}\s*\+=\s*(?:\(|[A-Za-z_]\w*\s*=>|delegate)");

        /// <summary>
        /// Puts this format's MAX calls in the given methods behind <paramref name="gate"/>
        /// (<c>Type.UseMetica</c>): ready checks become a choice, and each run of MAX statements
        /// becomes <c>if (gate) { Metica } else { MAX }</c>.
        /// </summary>
        public static Result PatchFormat(string text, IEnumerable<string> methodLabels, Format format, string gate)
        {
            var result = new Result { Text = text };
            foreach (var label in methodLabels)
            {
                var masked = CSharpSource.Mask(result.Text);
                var method = FindMethod(masked, label);
                if (method == null) { result.Notes.Add($"{label}: not found."); continue; }
                if (Unpatched(masked, method, format, gate) == 0) continue;

                // A call in a branch without braces can't be wrapped — leave the whole function,
                // so its ready check and its show call never point at different networks.
                var dryRun = new List<string>();
                CollectGroups(masked, method.Open, method.Close, format, dryRun, method.Name, GuardedSpans(masked, method, gate),
                    new List<List<CSharpSource.Statement>>(), "");
                var braceless = dryRun.FirstOrDefault(n => n.Contains("without braces"));
                if (braceless != null) { result.Notes.Add(braceless); continue; }

                result.Text = PatchReadyChecks(result.Text, label, format, gate);
                result.Text = PatchStatements(result.Text, label, format, gate, result.Notes);
            }
            return result;
        }

        private static string PatchReadyChecks(string text, string label, Format format, string gate)
        {
            var ready = ReadyCall(format);
            if (ready == null) return text;

            var masked = CSharpSource.Mask(text);
            var method = FindMethod(masked, label);
            var calls = ready.Matches(masked.Substring(method.Open, method.Close - method.Open + 1)).Cast<Match>()
                .Select(m => method.Open + m.Index)
                .Where(start => !Regex.IsMatch(masked.Substring(Math.Max(0, start - 200), Math.Min(200, start)), $@"{Regex.Escape(gate)}\s*\?[^;]*:\s*$"))
                .OrderByDescending(start => start)
                .ToList();

            var patched = new StringBuilder(text);
            foreach (var start in calls)
            {
                var open = masked.IndexOf('(', start);
                var close = CSharpSource.Match(masked, open);
                var call = text.Substring(start, close - start + 1);
                patched.Remove(start, close - start + 1);
                patched.Insert(start, $"({gate} ? {format.MeticaReady} : {call})");
            }
            return patched.ToString();
        }

        private static string PatchStatements(string text, string label, Format format, string gate, List<string> notes)
        {
            var masked = CSharpSource.Mask(text);
            var method = FindMethod(masked, label);
            var groups = new List<List<CSharpSource.Statement>>();
            var guarded = GuardedSpans(masked, method, gate);
            CollectGroups(masked, method.Open, method.Close, format, notes, method.Name, guarded, groups, "");
            if (groups.Count == 0) return text;

            var newline = CSharpSource.NewlineOf(text);
            var memberIndent = CSharpSource.IndentAt(text, method.Start);
            var hidden = Handlers(masked, method, format, "OnAdHiddenEvent");
            var rewarded = format.HasReward ? Handlers(masked, method, format, "OnAdReceivedRewardEvent") : new List<string>();
            var needsAdInfo = false;

            // Every replacement first, against the original positions; then applied from the end of
            // the file backwards, so no edit moves another one's position.
            var edits = new List<(int start, int length, string with)>();
            foreach (var group in groups)
            {
                var first = group[0];
                var last = group[group.Count - 1];
                var indent = CSharpSource.IndentAt(text, first.Start);
                var unit = UnitOf(text, memberIndent);
                var original = text.Substring(first.LeadStart, last.End - first.LeadStart);
                var meticaLines = MeticaLines(text, masked, group, format, hidden, rewarded, notes, method.Name, ref needsAdInfo);

                var wrapped = new StringBuilder();
                wrapped.Append(indent).Append($"// Metica Integration Tools: {format.Name} through Metica or AppLovin MAX.").Append(newline);
                if (meticaLines.Count > 0)
                {
                    wrapped.Append(indent).Append($"if ({gate})").Append(newline);
                    wrapped.Append(indent).Append('{').Append(newline);
                    foreach (var line in meticaLines) wrapped.Append(indent).Append(unit).Append(line).Append(newline);
                    wrapped.Append(indent).Append('}').Append(newline);
                    wrapped.Append(indent).Append("else").Append(newline);
                }
                else
                {
                    wrapped.Append(indent).Append($"if (!{gate})").Append(newline);
                }
                wrapped.Append(indent).Append('{').Append(newline);
                wrapped.Append(Reindent(TrimLeadingBlankLines(original, true), unit, newline)).Append(newline);
                wrapped.Append(indent).Append('}');

                edits.Add((first.LeadStart, last.End - first.LeadStart, wrapped.ToString()));
            }

            if (needsAdInfo && !masked.Substring(method.Type.Open, method.Type.Close - method.Type.Open).Contains("MeticaAdInfo("))
            {
                // Before the class's closing brace, on its own lines.
                var closeLine = text.LastIndexOf('\n', method.Type.Close) + 1;
                edits.Add((closeLine, 0,
                    newline +
                    $"{memberIndent}/// <summary>A MAX ad info for the game's MAX handlers, when Metica showed the ad. Added by the Metica Integration Tools.</summary>" + newline +
                    $"{memberIndent}private static MaxSdkBase.AdInfo MeticaAdInfo(string adUnitId) =>" + newline +
                    $"{memberIndent}{UnitOf(text, memberIndent)}new MaxSdkBase.AdInfo(new System.Collections.Generic.Dictionary<string, object> {{ {{ \"adUnitId\", adUnitId }} }});" + newline));
            }

            var patched = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.start))
            {
                patched.Remove(edit.start, edit.length);
                patched.Insert(edit.start, edit.with);
            }

            return patched.ToString();
        }

        /// <summary>Runs of consecutive statements that make this format's MAX calls, in this block and the blocks inside it.</summary>
        private static void CollectGroups(string masked, int open, int close, Format format, List<string> notes, string methodName,
            List<(int start, int end)> guarded, List<List<CSharpSource.Statement>> groups, string conditions)
        {
            var call = StatementCall(format);
            List<CSharpSource.Statement> run = null;

            foreach (var statement in CSharpSource.Statements(masked, open, close))
            {
                var code = masked.Substring(statement.Start, statement.End - statement.Start);

                // `if (!ready) return;` guards everything after it in this block.
                if (statement.Keyword == "if" && Regex.IsMatch(code, @"\)\s*(?:\{\s*)?return\b[^;]*;\s*\}?\s*$"))
                    conditions += " " + code;

                if (statement.IsControl)
                {
                    run = null;
                    var blocks = CSharpSource.InnerBlocks(masked, statement);
                    foreach (var (innerOpen, innerClose) in blocks)
                        if (!guarded.Any(g => g.start == innerOpen)) // already the MAX side of a choice
                            CollectGroups(masked, innerOpen, innerClose, format, notes, methodName, guarded, groups,
                                conditions + " " + masked.Substring(statement.Start, innerOpen - statement.Start));

                    // A call in a branch without braces can't be wrapped safely.
                    var inBlocks = blocks.Sum(b => call.Matches(masked.Substring(b.open, b.close - b.open)).Count);
                    if (call.Matches(code).Count > inBlocks)
                        notes.Add($"{methodName}: an AppLovin MAX {format.Name} call in an if/else without braces — the function was left unchanged; patch it by hand.");
                    continue;
                }

                if (!call.IsMatch(code)) { run = null; continue; }
                if (run == null) { run = new List<CSharpSource.Statement>(); groups.Add(run); }
                run.Add(statement);
                WarnMaxOnlyGuards(conditions, format, methodName, notes);
            }
        }

        /// <summary>
        /// A flag like isMaxInitialized around a patched call: MAX sets it in its own init
        /// callback, which never runs on the Metica path — so the Metica call behind it is never
        /// reached. Reported, not changed: only the game knows what the flag means.
        /// </summary>
        private static void WarnMaxOnlyGuards(string conditions, Format format, string methodName, List<string> notes)
        {
            var flags = Regex.Matches(conditions, @"[A-Za-z_]\w*").Cast<Match>().Select(m => m.Value)
                .Where(t => Regex.IsMatch(t, "(?i)max") && !t.StartsWith("MaxSdk") && t != "Max" && t != "Mathf" && t != "Math")
                .Distinct();
            foreach (var flag in flags)
            {
                var note = $"{methodName}: '{flag}' guards the {format.Name} call — if only AppLovin MAX sets it, the Metica path never gets there. Check it.";
                if (!notes.Contains(note)) notes.Add(note);
            }
        }

        private static List<string> MeticaLines(string text, string masked, List<CSharpSource.Statement> group, Format format,
            List<string> hidden, List<string> rewarded, List<string> notes, string methodName, ref bool needsAdInfo)
        {
            var lines = new List<string>();
            var call = StatementCall(format);
            var lastVerb = Verb.None;

            foreach (var statement in group)
            foreach (Match m in call.Matches(masked.Substring(statement.Start, statement.End - statement.Start)))
            {
                var verb = format.Calls[m.Groups["call"].Value];
                if (verb == Verb.None || verb == lastVerb) continue;
                lastVerb = verb;

                var open = masked.IndexOf('(', statement.Start + m.Index);
                var close = CSharpSource.Match(masked, open);
                var args = CSharpSource.SplitTopLevel(text.Substring(open + 1, close - open - 1), ',').Select(a => a.Trim()).ToList();
                var adUnit = args.Count > 0 && args[0].Length > 0 ? args[0] : "\"\"";
                var placement = args.Count > 1 ? args[1] : "\"\"";

                switch (verb)
                {
                    case Verb.Load:
                        lines.Add(format == Rewarded ? "MeticaIntegration.MeticaAdsManager.LoadRewarded();" : "MeticaIntegration.MeticaAdsManager.LoadInterstitial();");
                        break;
                    case Verb.Hide:
                        lines.Add(format == Banner ? "MeticaIntegration.MeticaAdsManager.HideBanner();" : "MeticaIntegration.MeticaAdsManager.HideMRec();");
                        break;
                    case Verb.Show when format == Banner:
                        lines.Add("MeticaIntegration.MeticaAdsManager.ShowBanner();");
                        break;
                    case Verb.Show when format == MRec:
                        lines.Add("MeticaIntegration.MeticaAdsManager.ShowMRec();");
                        break;
                    case Verb.Show when format == Interstitial:
                    {
                        if (hidden.Count == 0) notes.Add(NoHandlerNote(masked, methodName, format, "OnAdHiddenEvent", "close"));
                        if (hidden.Count > 0) needsAdInfo = true;
                        var onClosed = hidden.Count == 0 ? "" : $", shown => {{ if (shown) {{ {Calls(hidden, $"{adUnit}, MeticaAdInfo({adUnit})")} }} }}";
                        lines.Add($"MeticaIntegration.MeticaAdsManager.ShowInterstitial({placement}{onClosed});");
                        break;
                    }
                    case Verb.Show when format == Rewarded:
                    {
                        if (rewarded.Count == 0) notes.Add(NoHandlerNote(masked, methodName, format, "OnAdReceivedRewardEvent", "reward"));
                        if (hidden.Count == 0) notes.Add(NoHandlerNote(masked, methodName, format, "OnAdHiddenEvent", "close"));
                        if (rewarded.Count + hidden.Count > 0) needsAdInfo = true;
                        var onReward = rewarded.Count == 0
                            ? "earned => { }"
                            : $"earned => {{ if (earned) {{ {Calls(rewarded, $"{adUnit}, default(MaxSdkBase.Reward), MeticaAdInfo({adUnit})")} }} }}";
                        var onClosed = hidden.Count == 0 ? "" : $", () => {{ {Calls(hidden, $"{adUnit}, MeticaAdInfo({adUnit})")} }}";
                        lines.Add($"MeticaIntegration.MeticaAdsManager.ShowRewarded({placement}, {onReward}{onClosed});");
                        break;
                    }
                }
            }

            return lines;
        }

        private static string Calls(List<string> handlers, string arguments) =>
            string.Join(" ", handlers.Select(h => $"{h}({arguments});"));

        private static string NoHandlerNote(string masked, string methodName, Format format, string callbackEvent, string what) =>
            HasLambdaHandler(masked, format, callbackEvent)
                ? $"{methodName}: the {format.Name} {what} handler is a lambda (MaxSdkCallbacks.{format.Callbacks}.{callbackEvent}) — call its code from the Metica callback by hand."
                : $"{methodName}: no MAX {format.Name} {what} handler (MaxSdkCallbacks.{format.Callbacks}.{callbackEvent}) in this class — add the game's {what} logic to the Metica callback by hand if it needs one.";

        private static string UnitOf(string text, string memberIndent) => memberIndent.Contains("\t") ? "\t" : "    ";

        private static string Reindent(string segment, string unit, string newline)
        {
            var lines = segment.Replace("\r\n", "\n").Split('\n');
            return string.Join(newline, lines.Select(l => l.Trim().Length == 0 ? l : unit + l));
        }

        // ── Revenue ────────────────────────────────────────────────────────────

        public const string RevenueHandler = "OnMeticaAdRevenue";

        private static readonly Regex MeticaInitialize = new Regex(@"\bMeticaAdsManager\s*\.\s*Initialize\s*\(\s*\)\s*;");

        public static bool StartsMetica(string masked) => MeticaInitialize.IsMatch(masked);

        public static bool SubscribesRevenue(string masked) => Regex.IsMatch(masked, @"\bMeticaAdsHooks\s*\.\s*OnAdRevenue\s*\+?=");

        /// <summary>
        /// Subscribes <c>MeticaAdsHooks.OnAdRevenue</c> right before <c>MeticaAdsManager.Initialize()</c>
        /// to an empty <c>OnMeticaAdRevenue(AdRevenueInfo)</c> added to the same class.
        /// </summary>
        public static Result PatchRevenue(string text)
        {
            var result = new Result();
            var masked = CSharpSource.Mask(text);
            var init = MeticaInitialize.Match(masked);
            if (!init.Success) return Fail(result, "MeticaAdsManager.Initialize() not found.");
            if (SubscribesRevenue(masked)) return Fail(result, "OnAdRevenue is already subscribed.");

            var method = CSharpSource.Methods(masked).FirstOrDefault(m => m.Open < init.Index && init.Index < m.Close);
            if (method == null) return Fail(result, "MeticaAdsManager.Initialize() isn't inside a method.");
            if (CSharpSource.Methods(masked).Any(m => m.Type.Open == method.Type.Open && m.Name == RevenueHandler))
                return Fail(result, $"{method.Type.Name} already has {RevenueHandler}.");

            var newline = CSharpSource.NewlineOf(text);
            var memberIndent = CSharpSource.IndentAt(text, method.Start);
            var unit = UnitOf(text, memberIndent);
            var lineStart = text.LastIndexOf('\n', init.Index) + 1;
            var indent = CSharpSource.IndentAt(text, init.Index);

            var handler =
                newline + newline +
                $"{memberIndent}/// <summary>" + newline +
                $"{memberIndent}/// Metica ad revenue, once per paid impression — report it to the game's analytics here." + newline +
                $"{memberIndent}/// Interstitial and rewarded revenue arrive on Metica's native thread: keep Unity calls" + newline +
                $"{memberIndent}/// (PlayerPrefs, MonoBehaviours) inside MeticaIntegration.ThreadDispatcher.Enqueue(() => …)." + newline +
                $"{memberIndent}/// Banner and MREC revenue arrive on the main thread. Added by the Metica Integration Tools." + newline +
                $"{memberIndent}/// </summary>" + newline +
                $"{memberIndent}private static void {RevenueHandler}(MeticaIntegration.AdRevenueInfo info)" + newline +
                $"{memberIndent}{{" + newline +
                $"{memberIndent}{unit}// TODO: report info.Revenue (USD), info.Format, info.NetworkName, info.AdUnitId, info.Placement." + newline +
                $"{memberIndent}}}";

            var patched = new StringBuilder(text);
            patched.Insert(method.Close + 1, handler);
            patched.Insert(lineStart, $"{indent}MeticaIntegration.MeticaAdsHooks.OnAdRevenue += {RevenueHandler};{newline}");

            result.Text = patched.ToString();
            result.Notes.Add($"{RevenueHandler} added to {method.Type.Name}, subscribed in {method.Name}");
            return result;
        }

        // ── Renaming ───────────────────────────────────────────────────────────

        /// <summary>The method's name without its "Max" / "MAX" part, or null when it has none.</summary>
        public static string GeneralName(string name)
        {
            var general = Regex.Replace(name, @"(?<![A-Z])(?:Max|MAX)(?=[A-Z_0-9]|$)", "");
            general = general.Trim('_');
            return general.Length == 0 || general == name || !char.IsLetter(general[0]) ? null : general;
        }

        /// <summary>
        /// Renames a method everywhere in the given scripts: calls, method groups, nameof and the
        /// string forms Invoke / StartCoroutine / SendMessage use. Returns the scripts' new text.
        /// </summary>
        public static Dictionary<string, string> Rename(Dictionary<string, string> scripts, string oldName, string newName)
        {
            var renamed = new Dictionary<string, string>();
            var identifier = new Regex($@"\b{Regex.Escape(oldName)}\b");
            var literal = $"\"{oldName}\"";

            foreach (var pair in scripts)
            {
                var text = pair.Value;
                var masked = CSharpSource.Mask(text);
                var patched = new StringBuilder(text);
                var edits = new List<(int at, int length, string with)>();

                foreach (Match m in identifier.Matches(masked)) edits.Add((m.Index, m.Length, newName));

                var at = text.IndexOf(literal, StringComparison.Ordinal);
                while (at >= 0)
                {
                    if (masked[at] == '"') edits.Add((at, literal.Length, $"\"{newName}\""));
                    at = text.IndexOf(literal, at + literal.Length, StringComparison.Ordinal);
                }

                if (edits.Count == 0) continue;
                foreach (var edit in edits.OrderByDescending(e => e.at))
                {
                    patched.Remove(edit.at, edit.length);
                    patched.Insert(edit.at, edit.with);
                }
                renamed[pair.Key] = patched.ToString();
            }

            return renamed;
        }

        // ── Shared ─────────────────────────────────────────────────────────────

        private static Result Fail(Result result, string problem)
        {
            result.Problem = problem;
            result.Text = null;
            return result;
        }

        private static string OneLine(string text, CSharpSource.Statement statement)
        {
            var line = text.Substring(statement.Start, statement.End - statement.Start).Replace("\r", "").Split('\n')[0].Trim();
            return line.Length <= 80 ? line : line.Substring(0, 77) + "…";
        }
    }
}
