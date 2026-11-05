using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameDistrict.MeticaIntegrationTools
{
    internal enum AdditionFit
    {
        /// <summary>Goes into the Metica unit as the game wrote it.</summary>
        Carry,

        /// <summary>Goes in with MAX names swapped for Metica ones, and a TODO to check it.</summary>
        Adapt,

        /// <summary>The tool can't place it; listed so it can be added by hand.</summary>
        ByHand
    }

    /// <summary>One statement the game added to an AppLovin ad unit, and where it goes in the Metica unit.</summary>
    internal sealed class Addition
    {
        /// <summary>Stable across scans: what a tick is kept under.</summary>
        public string Id;

        public string Unit;         // "ApplovinInterstitial"
        public string MeticaUnit;   // "MeticaInterstitial"
        public string Method;       // the AppLovin method it was added to

        /// <summary>The Metica method it goes to; null when it has to be made (<see cref="MeticaEvent"/>) or there is none.</summary>
        public string Target;

        /// <summary>The Metica event whose handler is added for it, when the unit does not listen to it yet.</summary>
        public string MeticaEvent;

        /// <summary>As the game wrote it, on one line.</summary>
        public string Code;

        /// <summary>What goes into the Metica unit, on one line (equal to <see cref="Code"/> unless adapted).</summary>
        public string Suggested;

        /// <summary>1-based, in the AppLovin unit.</summary>
        public int Line;

        public bool IsLog;
        public AdditionFit Fit;

        /// <summary>Why it is by hand, or what was adapted.</summary>
        public string Note;

        /// <summary>Already in the Metica unit (carried, or there to begin with).</summary>
        public bool Present;

        /// <summary>Uses <see cref="GameAdditions.HelperClass"/>, so the helper file has to exist.</summary>
        public bool UsesHelper;

        // Placement, all in compare keys (code with whitespace and comments removed).
        internal string Context;    // "" the method body, "main" a ThreadDispatcher.Enqueue lambda, else a container path
        internal string Before;     // the statement before it in the game's code; null when it comes first
        internal string Next;       // the statement after it; null when it comes last
        internal string Insert;     // the adapted statement, its lines re-indented to column 0
        internal string Where;      // the hint: "MeticaInterstitial.ShowInterstitial()"
    }

    /// <summary>
    /// Finds what a game added to its GD SDK AppLovin ad units — Adjust calls, its own hooks,
    /// logs — by comparing each unit with the original release, statement by statement per
    /// method, and plans each addition into the matching Metica unit method: show → show,
    /// hidden → hidden, reward → reward, revenue → revenue, loaded / failed → loaded / failed,
    /// displayed → the Metica unit's OnAdShowSuccess (added when it does not listen to it).
    ///
    /// <para>Text in, text out — no Unity, no files — so it can be checked against every stock
    /// release outside the editor. Comments, whitespace, usings and Metica v1 code are not
    /// additions. A stock statement the game changed, a line in a method or block the Metica
    /// unit lacks, or one using MAX-only types the tool can't swap, is reported for a hand
    /// edit, never guessed.</para>
    /// </summary>
    internal static class GameAdditions
    {
        /// <summary>The marker on every carried line, and on what the tool added for one.</summary>
        public const string Marker = "Metica Integration Tools: from ";

        public const string AdaptedMarker = "TODO(Metica Integration Tools): adapted from ";

        /// <summary>Builds a MaxSdkBase.AdInfo from a MeticaAd, for carried lines that take one.</summary>
        public const string HelperClass = "MeticaMaxAdInfo";

        /// <summary>AppLovin unit → Metica unit, by class (and file) name.</summary>
        public static readonly (string appLovin, string metica, string format)[] Units =
        {
            ("ApplovinInterstitial", "MeticaInterstitial", "Interstitial"),
            ("ApplovinRewarded", "MeticaRewarded", "Rewarded"),
            ("ApplovinBanner", "MeticaBanner", "Banner"),
            ("ApplovinMRec", "MeticaMRec", "Mrec")
        };

        // MAX event → Metica event.
        private static readonly Dictionary<string, string> Events = new Dictionary<string, string>
        {
            { "OnAdLoadedEvent", "OnAdLoadSuccess" },
            { "OnAdLoadFailedEvent", "OnAdLoadFailed" },
            { "OnAdDisplayedEvent", "OnAdShowSuccess" },
            { "OnAdDisplayFailedEvent", "OnAdShowFailed" },
            { "OnAdHiddenEvent", "OnAdHidden" },
            { "OnAdClickedEvent", "OnAdClicked" },
            { "OnAdRevenuePaidEvent", "OnAdRevenuePaid" },
            { "OnAdReceivedRewardEvent", "OnAdRewarded" }
        };

        // What MeticaAdsCallbacks has per format (Metica SDK 2.45).
        private static readonly Dictionary<string, string[]> FormatEvents = new Dictionary<string, string[]>
        {
            { "Banner", new[] { "OnAdLoadSuccess", "OnAdLoadFailed", "OnAdClicked", "OnAdRevenuePaid" } },
            { "Mrec", new[] { "OnAdLoadSuccess", "OnAdLoadFailed", "OnAdClicked", "OnAdRevenuePaid" } },
            {
                "Interstitial", new[]
                    { "OnAdLoadSuccess", "OnAdLoadFailed", "OnAdShowSuccess", "OnAdShowFailed", "OnAdHidden", "OnAdClicked", "OnAdRevenuePaid" }
            },
            {
                "Rewarded", new[]
                {
                    "OnAdLoadSuccess", "OnAdLoadFailed", "OnAdShowSuccess", "OnAdShowFailed", "OnAdHidden", "OnAdClicked",
                    "OnAdRewarded", "OnAdRevenuePaid"
                }
            }
        };

        private static readonly Regex MaxSubscription = new Regex(@"\bMaxSdkCallbacks\s*\.\s*(\w+)\s*\.\s*(\w+)\s*\+=\s*([A-Za-z_]\w*)\s*;");
        private static readonly Regex MeticaSubscription = new Regex(@"\bMeticaAdsCallbacks\s*\.\s*(\w+)\s*\.\s*(\w+)\s*\+=\s*([A-Za-z_]\w*)\s*;");
        private static readonly Regex LogCall = new Regex(@"^(?:UnityEngine\.)?(?:Debug|Message)\.Log\w*\(|^print\(|^Console\.Write");
        private static readonly Regex MaxName = new Regex(@"\bMaxSdk\w*\b");

        // ── Finding ────────────────────────────────────────────────────────────

        /// <summary>
        /// The additions in <paramref name="appLovinText"/> against <paramref name="stockText"/>,
        /// planned into <paramref name="meticaText"/> (null when the Metica unit isn't written yet).
        /// </summary>
        public static List<Addition> Find(string unit, string meticaUnit, string format,
            string stockText, string appLovinText, string meticaText)
        {
            var additions = new List<Addition>();
            var game = new Source(appLovinText, unit);
            var stock = new Source(stockText, unit);
            var metica = meticaText == null ? null : new Source(meticaText, meticaUnit);
            if (game.Type == null || stock.Type == null) return additions;

            var gameHandlers = MaxSubscription.Matches(game.Masked).Cast<Match>()
                .GroupBy(m => m.Groups[3].Value).ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
            var meticaHandlers = metica == null
                ? new Dictionary<string, string>()
                : MeticaSubscription.Matches(metica.Masked).Cast<Match>()
                    .GroupBy(m => m.Groups[2].Value).ToDictionary(g => g.Key, g => g.First().Groups[3].Value);

            var onlyAppLovin = game.MemberNames()
                .Except(metica?.MemberNames() ?? Enumerable.Empty<string>())
                .Except(gameHandlers.Keys)
                .ToList();

            // Class-level members the game added (fields, properties, its own methods).
            var stockMembers = new HashSet<string>(stock.Members().Select(m => m.key));
            foreach (var member in game.Members().Where(m => !stockMembers.Contains(m.key) && !IsMetica(m.key)))
            {
                var method = game.Methods.FirstOrDefault(m => m.Start == member.start);
                if (method != null && (gameHandlers.ContainsKey(method.Name) || stock.Methods.Any(s => s.Name == method.Name)))
                    continue; // a handler the game added, or a stock method: its statements are read below

                additions.Add(Make(unit, meticaUnit, method?.Name ?? "(class)", game, member.start, member.end,
                    AdditionFit.ByHand, method != null
                        ? $"The game's own method {method.Name}() — add it to {meticaUnit} by hand if the lines need it."
                        : $"A member the game added to {unit} — add it to {meticaUnit} by hand if the lines need it."));
            }

            foreach (var method in game.Methods)
            {
                var stockMethod = stock.Methods.FirstOrDefault(m => m.Name == method.Name);
                var handled = gameHandlers.TryGetValue(method.Name, out var maxEvent);
                if (stockMethod == null && !handled) continue; // reported above, whole

                var gameLeaves = game.Leaves(method);
                var stockLeaves = stockMethod == null ? new List<Leaf>() : stock.Leaves(stockMethod);

                // Where this method's lines go.
                string target = null, meticaEvent = null, noTarget = null;
                if (handled)
                {
                    if (!Events.TryGetValue(maxEvent, out var mapped))
                        noTarget = $"MAX's {maxEvent} has no Metica counterpart.";
                    else if (!FormatEvents[format].Contains(mapped))
                        noTarget = $"Metica has no {mapped} for {format.ToLowerInvariant()} ads.";
                    else if (meticaHandlers.TryGetValue(mapped, out var handler)) target = handler;
                    else meticaEvent = mapped;
                }
                else if (metica != null && metica.Methods.Any(m => m.Name == method.Name)) target = method.Name;
                else noTarget = $"{meticaUnit} has no {method.Name}().";

                if (metica == null) noTarget = $"{meticaUnit}.cs isn't written yet.";

                var meticaMethod = target == null ? null : metica?.Methods.FirstOrDefault(m => m.Name == target);
                var meticaLeaves = meticaMethod == null ? null : metica.Leaves(meticaMethod);
                var swaps = Swaps(method, meticaMethod, meticaEvent);

                foreach (var (leaf, edited) in Added(stockLeaves, gameLeaves))
                {
                    if (MaxSubscription.IsMatch(leaf.Key + " ")) continue; // the subscriptions are the mapping
                    var addition = Make(unit, meticaUnit, method.Name, game, leaf.Start, leaf.End, AdditionFit.Carry, null);
                    addition.Context = leaf.Context;
                    addition.Before = SiblingKey(gameLeaves, leaf, -1);
                    addition.Next = SiblingKey(gameLeaves, leaf, +1);
                    addition.Target = target;
                    addition.MeticaEvent = meticaEvent;
                    addition.Where = target != null ? $"{meticaUnit}.{target}()"
                        : meticaEvent != null ? $"a new {meticaUnit} handler for {format}.{meticaEvent}" : null;

                    var adapted = Adapt(game, leaf.Start, leaf.End, swaps, out var swapped, out var leftover);
                    addition.Insert = adapted;
                    addition.Suggested = OneLine(adapted);
                    addition.UsesHelper = adapted.Contains(HelperClass + ".");

                    var foreign = onlyAppLovin.FirstOrDefault(name =>
                        Regex.IsMatch(CSharpSource.Mask(adapted, true),$@"(?<![\w.])(?:this\s*\.\s*)?{Regex.Escape(name)}\b"));

                    if (Regex.IsMatch(leaf.Key, @"^MaxSdkCallbacks\."))
                        Hand(addition, "A MAX event subscription the tool can't map — subscribe to the Metica event by hand if it's needed.");
                    else if (edited) Hand(addition, "Changes a stock line, not just adds one — carry it by hand.");
                    else if (noTarget != null) Hand(addition, noTarget);
                    else if (leftover != null) Hand(addition, $"Uses {leftover} — there's no Metica equivalent the tool can swap in.");
                    else if (foreign != null) Hand(addition, $"Uses {foreign}, which only {unit} has.");
                    else if (!FitsContext(leaf.Context, meticaEvent != null, meticaLeaves))
                        Hand(addition, $"Sits in a block {addition.Where} doesn't have.");
                    else if (swapped.Count > 0)
                    {
                        addition.Fit = AdditionFit.Adapt;
                        addition.Note = "Adapted: " + string.Join(", ", swapped.Distinct()) + ".";
                    }

                    additions.Add(addition);
                }
            }

            foreach (var addition in additions) addition.Present = IsPresent(addition, metica, meticaHandlers);

            // The same statement twice in one method: tell them apart.
            foreach (var group in additions.GroupBy(a => a.Id).Where(g => g.Count() > 1))
            {
                var n = 0;
                foreach (var addition in group) addition.Id += "#" + n++;
            }

            return additions.OrderBy(a => a.Line).ToList();
        }

        private static void Hand(Addition addition, string note)
        {
            addition.Fit = AdditionFit.ByHand;
            addition.Note = note;
        }

        private static Addition Make(string unit, string meticaUnit, string method, Source game, int start, int end,
            AdditionFit fit, string note)
        {
            var key = Source.Key(game.Text, game.Masked, start, end);
            var code = Source.Display(game.Text, game.Masked, start, end);
            return new Addition
            {
                Id = unit + "|" + method + "|" + key,
                Unit = unit,
                MeticaUnit = meticaUnit,
                Method = method,
                Code = code,
                Suggested = code,
                Insert = Reindent(game.Text.Substring(start, end - start), CSharpSource.IndentAt(game.Text, start)),
                Line = game.LineOf(start),
                IsLog = LogCall.IsMatch(key),
                Fit = fit,
                Note = note,
                Context = ""
            };
        }

        /// <summary>
        /// The game's leaves that are not in the stock method, each with whether its run of
        /// changes also removed a stock leaf (an edit, not an addition). A container added
        /// whole is one addition; its own statements are not listed again.
        /// </summary>
        private static IEnumerable<(Leaf leaf, bool edited)> Added(List<Leaf> stock, List<Leaf> game)
        {
            string Id(Leaf leaf) => leaf.Context + "\u0001" + leaf.Key;

            // Longest common subsequence of the two leaf lists.
            var n = stock.Count;
            var m = game.Count;
            var table = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                table[i, j] = Id(stock[i]) == Id(game[j]) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);

            var added = new HashSet<Leaf>();
            var run = new List<Leaf>();
            var runRemoves = false;
            var result = new List<(Leaf, bool)>();

            void Flush()
            {
                foreach (var leaf in run)
                {
                    added.Add(leaf);
                    if (leaf.Ignored || Ancestors(leaf).Any(added.Contains)) continue;
                    result.Add((leaf, runRemoves));
                }
                run.Clear();
                runRemoves = false;
            }

            int a = 0, b = 0;
            while (a < n || b < m)
            {
                if (a < n && b < m && Id(stock[a]) == Id(game[b])) { Flush(); a++; b++; }
                else if (b < m && (a >= n || table[a, b + 1] >= table[a + 1, b])) run.Add(game[b++]);
                else
                {
                    if (!stock[a].Ignored) runRemoves = true;
                    a++;
                }
            }
            Flush();
            return result;
        }

        private static IEnumerable<Leaf> Ancestors(Leaf leaf)
        {
            for (var parent = leaf.Parent; parent != null; parent = parent.Parent) yield return parent;
        }

        /// <summary>
        /// The key of the statement before (<paramref name="direction"/> -1) or after (+1)
        /// <paramref name="leaf"/> in its block, or null when there is none.
        /// </summary>
        private static string SiblingKey(List<Leaf> leaves, Leaf leaf, int direction)
        {
            var siblings = leaves.Where(l => l.Parent == leaf.Parent && l.Context == leaf.Context && !l.Ignored).OrderBy(l => l.Start);
            var sibling = direction < 0
                ? siblings.LastOrDefault(l => l.Start < leaf.Start)
                : siblings.FirstOrDefault(l => l.Start > leaf.Start);

            // As the Metica unit would have it.
            return sibling?.Key.Replace("Tag.Applovin", "Tag.Metica").Replace("AdPlatforms.APPLOVIN", "AdPlatforms.METICA");
        }

        private static bool FitsContext(string context, bool newHandler, List<Leaf> meticaLeaves)
        {
            if (context.Length == 0 || context == "main") return true;
            if (newHandler || meticaLeaves == null) return false;
            return meticaLeaves.Any(l => l.Container && Enumerable.Range(0, l.Blocks.Count).Any(i => l.ChildContext(i) == context));
        }

        private static bool IsPresent(Addition addition, Source metica, Dictionary<string, string> meticaHandlers)
        {
            if (metica?.Type == null) return false;

            var key = Source.Key(addition.Insert, Source.MaskOf(addition.Insert), 0, addition.Insert.Length);
            var targetName = addition.Target ?? (addition.MeticaEvent != null && meticaHandlers.TryGetValue(addition.MeticaEvent, out var h) ? h : null);
            var method = targetName == null ? null : metica.Methods.FirstOrDefault(m => m.Name == targetName);

            var (start, end) = method != null ? (method.Open, method.Close + 1) : (metica.Type.Open, metica.Type.Close + 1);
            if (addition.Fit != AdditionFit.ByHand && method == null) return false;
            return Source.Key(metica.Text, metica.Masked, start, end).Contains(key);
        }

        // ── Adapting ───────────────────────────────────────────────────────────

        /// <summary>
        /// What to swap in a line from <paramref name="method"/>: AppLovin's tag and platform, and
        /// the MAX handler's parameters for the Metica handler's.
        /// </summary>
        private static List<(Regex pattern, Func<Match, string> replace, string label)> Swaps(
            CSharpSource.Method method, CSharpSource.Method meticaMethod, string meticaEvent)
        {
            var swaps = new List<(Regex, Func<Match, string>, string)>
            {
                // "~": cosmetic — the line still goes in as carried, only the log's tag changes.
                (new Regex(@"\bTag\s*\.\s*Applovin\b"), _ => "Tag.Metica", "~Tag.Metica"),
                (new Regex(@"\bAdPlatforms\s*\.\s*APPLOVIN\b"), _ => "AdPlatforms.METICA", "AdPlatforms.METICA")
            };

            // The Metica handler's parameters: its own, or the ones the tool will give it.
            List<(string type, string name)> meticaParams;
            if (meticaMethod != null) meticaParams = Parameters(meticaMethod.Parameters);
            else if (meticaEvent != null) meticaParams = Parameters(HandlerParameters(meticaEvent));
            else meticaParams = new List<(string type, string name)>();
            var ad = meticaParams.FirstOrDefault(p => p.type.EndsWith("MeticaAd")).name;
            var error = meticaParams.FirstOrDefault(p => p.type.EndsWith("MeticaAdError")).name;

            var meticaNames = new HashSet<string>(meticaParams.Select(p => p.name));
            foreach (var (type, name) in Parameters(method.Parameters))
            {
                if (meticaMethod != null && meticaNames.Contains(name) && !type.StartsWith("MaxSdk")) continue; // same parameter both sides
                var pattern = new Regex($@"(?<![\w.]){Regex.Escape(name)}\b(?:\s*\.\s*(\w+))?");

                if (type == "string" && ad != null)
                    swaps.Add((pattern, m => m.Groups[1].Success ? null : ad + ".adUnitId", $"{name} → {ad}.adUnitId"));
                else if (type.EndsWith("AdInfo") && ad != null)
                    swaps.Add((pattern, m => AdInfoMember(ad, m.Groups[1].Success ? m.Groups[1].Value : null), $"{name} → {ad}"));
                else if (type.EndsWith("ErrorInfo") && error != null)
                    swaps.Add((pattern, m => m.Groups[1].Value == "Message" ? error + ".message" : null, $"{name}.Message → {error}.message"));
                else
                    swaps.Add((pattern, _ => null, null)); // unmapped: reported as leftover
            }

            return swaps;
        }

        /// <summary>The parameter list of a handler for a MeticaAdsCallbacks event.</summary>
        private static string HandlerParameters(string meticaEvent) =>
            meticaEvent == "OnAdLoadFailed" ? "MeticaAdError meticaAdError"
            : meticaEvent == "OnAdShowFailed" ? "MeticaAd meticaAd, MeticaAdError meticaAdError"
            : "MeticaAd meticaAd";

        private static string AdInfoMember(string ad, string member)
        {
            switch (member)
            {
                case null: return $"{HelperClass}.From({ad})";
                case "AdUnitIdentifier": return ad + ".adUnitId";
                case "NetworkName": return ad + ".networkName";
                case "Placement": return ad + ".placementTag";
                case "Revenue": return $"({ad}.revenue ?? 0.0)";
                default: return $"{HelperClass}.From({ad}).{member}";
            }
        }

        private static List<(string type, string name)> Parameters(string parameters) =>
            CSharpSource.SplitTopLevel(parameters ?? "", ',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(p => Regex.Match(p, @"^(?:(?:ref|out|in|params|this)\s+)?(?<type>[\w\.<>\[\],\s\?]+?)\s+(?<name>[A-Za-z_]\w*)\s*(?:=.*)?$"))
                .Where(m => m.Success)
                .Select(m => (Regex.Replace(m.Groups["type"].Value, @"\s+", ""), m.Groups["name"].Value))
                .ToList();

        /// <summary>
        /// The statement with every swap applied (on code only, never inside strings), re-indented
        /// to column 0. <paramref name="leftover"/> is the first MAX name left, or null.
        /// </summary>
        private static string Adapt(Source game, int start, int end,
            List<(Regex pattern, Func<Match, string> replace, string label)> swaps, out List<string> swapped, out string leftover)
        {
            var text = game.Text.Substring(start, end - start);
            var masked = CSharpSource.Mask(text, true); // a hole's code is renamed too: $"{arg2.Revenue}"
            swapped = new List<string>();
            leftover = null;

            var edits = new List<(int index, int length, string with)>();
            foreach (var (pattern, replace, label) in swaps)
            foreach (Match match in pattern.Matches(masked))
            {
                if (edits.Any(e => match.Index < e.index + e.length && e.index < match.Index + match.Length)) continue;
                var with = replace(match);
                if (with == null)
                {
                    leftover = leftover ?? match.Value.Trim();
                    continue;
                }
                edits.Add((match.Index, match.Length, with));
                if (label != null && !label.StartsWith("~")) swapped.Add(label);
            }

            foreach (var (index, length, with) in edits.OrderByDescending(e => e.index))
                text = text.Substring(0, index) + with + text.Substring(index + length);

            var max = MaxName.Match(CSharpSource.Mask(text, true));
            if (max.Success) leftover = leftover ?? max.Value;

            return Reindent(text, CSharpSource.IndentAt(game.Text, start));
        }

        // ── Carrying ───────────────────────────────────────────────────────────

        /// <summary>
        /// <paramref name="meticaText"/> with each addition inserted where it was planned, in
        /// order. An addition already there, or one that can no longer be placed, is skipped
        /// and said so in <paramref name="log"/>.
        /// </summary>
        public static string Carry(string meticaText, string format, IEnumerable<Addition> additions, List<string> log)
        {
            var text = meticaText;
            foreach (var addition in additions.Where(a => a.Fit != AdditionFit.ByHand).OrderBy(a => a.Line))
            {
                var source = new Source(text, addition.MeticaUnit);
                if (source.Type == null) { log.Add($"{addition.MeticaUnit}: class not found."); break; }

                var handlers = MeticaSubscription.Matches(source.Masked).Cast<Match>()
                    .GroupBy(m => m.Groups[2].Value).ToDictionary(g => g.Key, g => g.First().Groups[3].Value);
                if (IsPresent(addition, source, handlers)) { log.Add($"{addition.MeticaUnit}: already there — {addition.Suggested}"); continue; }

                var targetName = addition.Target;
                if (targetName == null && addition.MeticaEvent != null)
                {
                    if (!handlers.TryGetValue(addition.MeticaEvent, out targetName))
                    {
                        text = AddHandler(source, format, addition, out targetName, log);
                        if (targetName == null) continue;
                        source = new Source(text, addition.MeticaUnit);
                    }
                }

                var method = source.Methods.FirstOrDefault(m => m.Name == targetName);
                if (method == null) { log.Add($"{addition.MeticaUnit}: no {targetName}() — add by hand: {addition.Suggested}"); continue; }

                var inserted = InsertInto(source, method, addition);
                if (inserted == null) { log.Add($"{addition.MeticaUnit}.{targetName}: couldn't place — add by hand: {addition.Suggested}"); continue; }

                text = inserted;
                log.Add($"{addition.MeticaUnit}.{targetName}: carried {addition.Suggested}");
            }

            return text;
        }

        /// <summary>Adds a handler for the addition's Metica event and subscribes it next to the unit's other subscriptions.</summary>
        private static string AddHandler(Source source, string format, Addition addition, out string name, List<string> log)
        {
            name = null;
            var subscriptions = MeticaSubscription.Matches(source.Masked).Cast<Match>().ToList();
            if (subscriptions.Count == 0)
            {
                log.Add($"{addition.MeticaUnit}: no MeticaAdsCallbacks subscriptions to add {addition.MeticaEvent} next to — add by hand.");
                return source.Text;
            }

            var word = addition.MeticaUnit.StartsWith("Metica") ? addition.MeticaUnit.Substring("Metica".Length) : format;
            var eventWord = addition.MeticaEvent.StartsWith("OnAd") ? addition.MeticaEvent.Substring(4) : addition.MeticaEvent.Substring(2);
            var candidate = $"On{word}{eventWord}Event"; // OnInterstitialShowSuccessEvent
            while (source.Methods.Any(m => m.Name == candidate)) candidate += "Carried";

            var parameters = HandlerParameters(addition.MeticaEvent);

            var nl = CSharpSource.NewlineOf(source.Text);
            var text = source.Text;

            // The handler: after the last subscribed handler, else at the end of the class.
            var subscribed = new HashSet<string>(subscriptions.Select(m => m.Groups[3].Value));
            var after = source.Methods.Where(m => subscribed.Contains(m.Name)).OrderBy(m => m.Close).LastOrDefault();
            var indent = after != null ? CSharpSource.IndentAt(text, after.Start) : CSharpSource.IndentAt(text, source.Type.Open) + Unit(text);
            var handler = $"{nl}{nl}{indent}// {Marker}{addition.Unit}: Metica's {addition.MeticaEvent}, for the lines carried over{nl}" +
                          $"{indent}private void {candidate}({parameters}){nl}{indent}{{{nl}{indent}}}";
            var at = after != null ? after.Close + 1 : LineStartOf(text, source.Type.Close);
            text = text.Insert(at, after != null ? handler : handler.Substring(nl.Length) + nl);

            // The subscription: after the last one (it comes before the handler in the file, so its index still holds).
            var last = subscriptions.Last();
            var lineEnd = LineEndOf(text, last.Index + last.Length);
            var subIndent = CSharpSource.IndentAt(text, last.Index);
            var subscription = $"{nl}{subIndent}MeticaAdsCallbacks.{last.Groups[1].Value}.{addition.MeticaEvent} += {candidate}; // {Marker}{addition.Unit}";
            if (last.Index < at) text = text.Insert(lineEnd, subscription);
            else return source.Text; // unusual layout: leave it

            name = candidate;
            log.Add($"{addition.MeticaUnit}: added {candidate}() for MeticaAdsCallbacks.{last.Groups[1].Value}.{addition.MeticaEvent}");
            return text;
        }

        /// <summary>The text with the addition placed in <paramref name="method"/>, or null when its block isn't there.</summary>
        private static string InsertInto(Source source, CSharpSource.Method method, Addition addition)
        {
            var text = source.Text;
            var masked = source.Masked;
            var nl = CSharpSource.NewlineOf(text);

            // The block: the method body, or the block the game's line sat in.
            int open = method.Open, close = method.Close;
            var wrap = false;
            if (addition.Context.Length > 0)
            {
                var leaves = source.Leaves(method);
                var container = leaves.Select(l => (leaf: l, index: Enumerable.Range(0, l.Blocks.Count).Where(i => l.ChildContext(i) == addition.Context).DefaultIfEmpty(-1).First()))
                    .FirstOrDefault(c => c.leaf.Container && c.index >= 0);
                if (container.leaf != null) (open, close) = container.leaf.Blocks[container.index];
                else if (addition.Context == "main") wrap = true; // no ThreadDispatcher lambda here: bring one
                else return null;
            }

            var marker = (addition.Fit == AdditionFit.Adapt ? AdaptedMarker : Marker) + $"{addition.Unit}.{addition.Method}";

            // On lines of their own: the statement, its last line marked — inside a ThreadDispatcher
            // lambda of its own when the Metica method has none to put it in.
            var lines = addition.Insert.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            lines[lines.Count - 1] += " // " + marker;
            if (wrap)
                lines = new[] { "ThreadDispatcher.Enqueue(() =>", "{" }
                    .Concat(lines.Select(l => l.Length == 0 ? l : Unit(text) + l))
                    .Concat(new[] { "});" })
                    .ToList();

            // In a one-line block: inline, the marker a block comment.
            var inline = (wrap ? $"ThreadDispatcher.Enqueue(() => {{ {OneLine(addition.Insert)} }});" : OneLine(addition.Insert))
                         + $" /* {marker} */";

            var statements = CSharpSource.Statements(masked, open, close);
            CSharpSource.Statement Find(string key) => key == null ? null
                : statements.LastOrDefault(s => Source.Key(text, masked, s.Start, s.End) == key || Source.ShellKey(text, masked, s) == key);

            // After the statement it came after; else before the one it came before; else at the
            // start when it came first, or at the end (ahead of a final return).
            var after = Find(addition.Before);
            var ahead = after == null ? Find(addition.Next) : null;
            if (after == null && ahead == null)
            {
                if (addition.Before == null) after = null;
                else if (EndsWithReturn(statements)) ahead = statements.Last();
                else after = statements.LastOrDefault();
            }

            if (after != null)
            {
                var lineEnd = LineEndOf(text, after.End);
                return IsBlank(masked, after.End, lineEnd)
                    ? text.Insert(lineEnd, nl + Block(lines, CSharpSource.IndentAt(text, after.Start), nl))
                    : text.Insert(after.End, " " + inline);
            }

            if (ahead != null)
            {
                var lineStart = LineStartOf(text, ahead.Start);
                return IsBlank(masked, lineStart, ahead.Start)
                    ? text.Insert(lineStart, Block(lines, CSharpSource.IndentAt(text, ahead.Start), nl) + nl)
                    : text.Insert(ahead.Start, inline + " ");
            }

            if (addition.Before == null && statements.Count > 0)
            {
                // First in the block.
                var lineEnd = LineEndOf(text, open + 1);
                return IsBlank(masked, open + 1, lineEnd)
                    ? text.Insert(lineEnd, nl + Block(lines, CSharpSource.IndentAt(text, statements[0].Start), nl))
                    : text.Insert(open + 1, " " + inline);
            }

            // An empty block.
            var closeLine = LineStartOf(text, close);
            return IsBlank(masked, closeLine, close)
                ? text.Insert(closeLine, Block(lines, CSharpSource.IndentAt(text, close) + Unit(text), nl) + nl)
                : text.Insert(close, inline + " ");
        }

        private static bool EndsWithReturn(List<CSharpSource.Statement> statements) =>
            statements.Count > 0 && statements.Last().Keyword == "return";

        private static string Block(List<string> lines, string indent, string nl) =>
            string.Join(nl, lines.Select(l => l.Length == 0 ? l : indent + l));

        private static bool IsBlank(string masked, int from, int to)
        {
            for (var i = from; i < to && i < masked.Length; i++)
                if (!char.IsWhiteSpace(masked[i])) return false;
            return true;
        }

        /// <summary>The index of the line break ending the line <paramref name="index"/> is on (before a '\r').</summary>
        private static int LineEndOf(string text, int index)
        {
            var newline = text.IndexOf('\n', index);
            if (newline < 0) return text.Length;
            return newline > 0 && text[newline - 1] == '\r' ? newline - 1 : newline;
        }

        private static int LineStartOf(string text, int index) => text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;

        private static string Unit(string text) => text.Contains("\n\t") ? "\t" : "    ";

        // ── Text helpers ───────────────────────────────────────────────────────

        private static bool IsMetica(string key) => key.IndexOf("metica", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The statement's lines with <paramref name="baseIndent"/> (its first line's) taken off each.</summary>
        private static string Reindent(string statement, string baseIndent)
        {
            var lines = statement.Replace("\r\n", "\n").Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var strip = 0;
                while (strip < baseIndent.Length && strip < line.Length && (line[strip] == ' ' || line[strip] == '\t')) strip++;
                lines[i] = line.Substring(strip).TrimEnd();
            }
            lines[0] = lines[0].Trim();
            return string.Join("\n", lines);
        }

        internal static string OneLine(string statement)
        {
            var masked = Source.MaskOf(statement);
            return Source.Display(statement, masked, 0, statement.Length);
        }

        // ── Reading a unit ─────────────────────────────────────────────────────

        internal sealed class Leaf
        {
            public int Start, End;
            public string Key;
            public string Context;
            public Leaf Parent;
            public bool Ignored;
            public List<(int open, int close)> Blocks;
            public bool Container => Blocks.Count > 0;
            public bool IsEnqueue;

            /// <summary>The context of the statements in block <paramref name="index"/>.</summary>
            public string ChildContext(int index)
            {
                if (IsEnqueue) return Context.Length == 0 ? "main" : Context + "/main";
                return Context + "/" + Key + "#" + index;
            }
        }

        /// <summary>One file: its text, masked text, and the class it is about.</summary>
        private sealed class Source
        {
            public readonly string Text;
            public readonly string Masked;
            public readonly CSharpSource.TypeDecl Type;
            public readonly List<CSharpSource.Method> Methods;

            public Source(string text, string className)
            {
                Text = text ?? "";
                Masked = CSharpSource.Mask(Text);
                Type = CSharpSource.Types(Masked).FirstOrDefault(t => t.Name == className);
                Methods = Type == null
                    ? new List<CSharpSource.Method>()
                    : CSharpSource.Methods(Masked).Where(m => m.Type.Name == Type.Name).ToList();
            }

            public static string MaskOf(string text) => CSharpSource.Mask(text);

            public int LineOf(int index)
            {
                var line = 1;
                for (var i = 0; i < index && i < Text.Length; i++)
                    if (Text[i] == '\n') line++;
                return line;
            }

            /// <summary>Every statement of a method, flattened: blocks first, then what is inside them.</summary>
            public List<Leaf> Leaves(CSharpSource.Method method)
            {
                var leaves = new List<Leaf>();
                Collect(method.Open, method.Close, "", null, leaves);
                return leaves;
            }

            private void Collect(int open, int close, string context, Leaf parent, List<Leaf> into)
            {
                foreach (var statement in CSharpSource.Statements(Masked, open, close))
                {
                    var blocks = statement.IsControl
                        ? CSharpSource.InnerBlocks(Masked, statement)
                        : LambdaBlocks(Masked, statement.Start, statement.End);
                    var leaf = new Leaf
                    {
                        Start = statement.Start,
                        End = statement.End,
                        Context = context,
                        Parent = parent,
                        Blocks = blocks
                    };
                    leaf.Key = blocks.Count == 0 ? Key(Text, Masked, statement.Start, statement.End) : ShellKey(Text, Masked, statement, blocks);
                    leaf.Ignored = IsMetica(leaf.Key);
                    leaf.IsEnqueue = !statement.IsControl && blocks.Count == 1 && leaf.Key.StartsWith("ThreadDispatcher.Enqueue(", StringComparison.Ordinal);
                    into.Add(leaf);

                    for (var i = 0; i < blocks.Count; i++)
                        Collect(blocks[i].open, blocks[i].close, leaf.ChildContext(i), leaf, into);
                }
            }

            /// <summary>The bodies of the lambdas and anonymous methods in a statement, outermost only.</summary>
            private static List<(int open, int close)> LambdaBlocks(string masked, int start, int end)
            {
                var blocks = new List<(int, int)>();
                for (var i = start; i < end; i++)
                {
                    if (masked[i] != '{') continue;
                    var before = masked.Substring(start, i - start).TrimEnd();
                    var isBody = before.EndsWith("=>")
                                 || Regex.IsMatch(before, @"\bdelegate$")
                                 || Regex.IsMatch(before, @"\bdelegate\s*\([^()]*\)$");
                    var close = CSharpSource.Match(masked, i);
                    if (close < 0 || close >= end) break;
                    if (isBody) blocks.Add((i, close));
                    i = close; // a block's own lambdas are found when it is read
                }
                return blocks;
            }

            /// <summary>The members of the class: fields, properties, events and methods, each with a compare key.</summary>
            public List<(int start, int end, string key)> Members()
            {
                var members = new List<(int, int, string)>();
                if (Type == null) return members;

                var i = Type.Open + 1;
                var start = -1;
                var paren = 0;
                while (i < Type.Close)
                {
                    var c = Masked[i];
                    if (start < 0)
                    {
                        if (char.IsWhiteSpace(c) || c == ';') { i++; continue; }
                        start = i;
                    }

                    if (c == '(') paren++;
                    else if (c == ')') paren--;
                    else if (c == ';' && paren == 0)
                    {
                        members.Add((start, i + 1, Key(Text, Masked, start, i + 1)));
                        start = -1;
                    }
                    else if (c == '{' && paren == 0)
                    {
                        var close = CSharpSource.Match(Masked, i);
                        if (close < 0) break;
                        var head = Masked.Substring(start, i - start);
                        var next = close + 1;
                        while (next < Type.Close && char.IsWhiteSpace(Masked[next])) next++;

                        // A property with an initializer ends at its ';'.
                        if (next < Type.Close && Masked[next] == '=' && !head.Contains("("))
                        {
                            var semicolon = Masked.IndexOf(';', next);
                            close = semicolon < 0 ? close : semicolon;
                        }

                        members.Add((start, close + 1, Key(Text, Masked, start, close + 1)));
                        start = -1;
                        i = close;
                    }
                    i++;
                }

                return members;
            }

            /// <summary>The names the class declares.</summary>
            public IEnumerable<string> MemberNames()
            {
                foreach (var (start, end, _) in Members())
                {
                    var head = Masked.Substring(start, end - start);
                    head = Regex.Replace(head, @"^\s*(?:\[[^\]]*\]\s*)*", "");
                    var cut = head.IndexOfAny(new[] { '(', '=', '{', ';' });
                    var name = Regex.Match(cut < 0 ? head : head.Substring(0, cut), @"([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*$");
                    if (name.Success) yield return name.Groups[1].Value;
                }
            }

            // ── Keys ──

            /// <summary>The code with every space and comment removed; strings and chars kept as written.</summary>
            public static string Key(string text, string masked, int start, int end) => Flatten(text, masked, start, end, false, null);

            /// <summary>Like <see cref="Key"/>, collapsed to single spaces instead: one readable line.</summary>
            public static string Display(string text, string masked, int start, int end) => Flatten(text, masked, start, end, true, null);

            public static string ShellKey(string text, string masked, CSharpSource.Statement statement)
            {
                var blocks = statement.IsControl
                    ? CSharpSource.InnerBlocks(masked, statement)
                    : LambdaBlocks(masked, statement.Start, statement.End);
                return ShellKey(text, masked, statement, blocks);
            }

            private static string ShellKey(string text, string masked, CSharpSource.Statement statement, List<(int open, int close)> blocks) =>
                Flatten(text, masked, statement.Start, statement.End, false, blocks);

            private static string Flatten(string text, string masked, int start, int end, bool spaced, List<(int open, int close)> skip)
            {
                var sb = new StringBuilder();
                var inString = false;
                var inChar = false;
                var pendingSpace = false;
                for (var i = start; i < end && i < text.Length; i++)
                {
                    if (skip != null && !inString && !inChar)
                    {
                        var block = skip.FirstOrDefault(b => b.open == i);
                        if (skip.Any(b => b.open == i))
                        {
                            sb.Append("{…}");
                            i = block.close;
                            continue;
                        }
                    }

                    var m = masked[i];
                    if (inString || inChar)
                    {
                        sb.Append(text[i]);
                        if (inString && m == '"') inString = false;
                        else if (inChar && m == '\'') inChar = false;
                        continue;
                    }

                    if (char.IsWhiteSpace(m))
                    {
                        pendingSpace = true;
                        continue;
                    }

                    if (pendingSpace && spaced && sb.Length > 0) sb.Append(' ');
                    pendingSpace = false;
                    sb.Append(text[i]);
                    if (m == '"') inString = true;
                    else if (m == '\'') inChar = true;
                }
                return sb.ToString();
            }
        }

        internal static string KeyOf(string code) => Source.Key(code, Source.MaskOf(code), 0, code.Length);
    }
}
