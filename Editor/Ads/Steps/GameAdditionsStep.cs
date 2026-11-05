using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The lines the game added to its GD SDK AppLovin ad units — an Adjust ad-revenue call, its
    /// own AdsManager hooks, logs — listed against the original release, so the ones Metica
    /// needs can be carried into the Metica units. With Metica on, the AppLovin units don't run,
    /// so whatever was only there stops. See <see cref="GameAdditions"/> for how lines are
    /// found and placed.
    ///
    /// <para>Only shown when the game added something. Nothing is carried unticked, nothing is
    /// changed in the AppLovin units, and AdMob's units aren't read: AdMob keeps running.</para>
    /// </summary>
    public sealed class GameAdditionsStep : MeticaStep
    {
        public override string Title => "Game additions to carry over";

        public override bool Optional => true;

        public override string Summary => "Carry the lines the game added to its AppLovin ad units over to the Metica units.";

        public override string Why =>
            "With Metica on, the AppLovin units don't run — anything the game added only there stops: an Adjust " +
            "ad-revenue call, its own AdsManager hooks, logs. Each AppLovin unit is compared with the original " +
            "GD SDK release, method by method, and every added line is listed with where it goes in the Metica " +
            "unit: show → show, hidden → hidden, reward → reward, revenue → revenue, loaded / failed → loaded / " +
            "failed, displayed → OnAdShowSuccess (the handler is added when the unit has none). Lines using MAX's " +
            "ad info are adapted — arg2.NetworkName becomes meticaAd.networkName, a whole AdInfo becomes " +
            "MeticaMaxAdInfo.From(meticaAd) — and go in with a TODO to check. A stock line the game changed, or a " +
            "line the tool can't place, is listed to add by hand. Nothing goes in unticked; log lines are tagged. " +
            "The Metica units are backed up first; the AppLovin units are never touched. Skip the step when " +
            "Metica doesn't need any of it.";

        public override string ActionLabel => "Carry ticked lines";

        internal override bool Applies => MeticaPaths.HasGDSdk && Scan().Units.Any(u => u.Additions.Count > 0);

        public override IEnumerable<string> TouchedPaths =>
            Scan().Units.Where(u => u.Additions.Count > 0).Select(u => u.MeticaPath)
                .Concat(MeticaPaths.FileExists(HelperPath) ? new[] { HelperPath } : Array.Empty<string>());

        public override string ReviewHint =>
            "Lines added to the Metica units, each marked \"" + GameAdditions.Marker.Trim() + "\" — adapted ones with a TODO. " +
            "MeticaMaxAdInfo.cs is new when an adapted line needs it.";

        private static string HelperPath => MeticaPaths.Combine(MeticaPaths.AdsMeticaFolder, GameAdditions.HelperClass + ".cs");

        // ── Scanning ───────────────────────────────────────────────────────────

        internal sealed class UnitScan
        {
            public string AppLovin, Metica, Format, AppLovinPath, MeticaPath;
            public bool MeticaWritten;
            public List<Addition> Additions = new List<Addition>();
        }

        internal sealed class ScanResult
        {
            public string Problem;
            public string Version;
            public readonly List<UnitScan> Units = new List<UnitScan>();
        }

        private static ScanResult _cache;
        private static string _cacheKey;

        /// <summary>
        /// Every AppLovin unit against its stock copy. Cached until one of the eight files or the
        /// GD SDK version changes — the step re-verifies on every repaint.
        /// </summary>
        internal static ScanResult Scan()
        {
            if (!MeticaPaths.HasGDSdk) return new ScanResult { Problem = "No GD SDK in this project." };

            var stock = StockFiles.Packaged;
            if (stock == null) return new ScanResult { Problem = "The original GD SDK copies are missing from this tool." };

            var version = StockVersion(stock);
            var units = GameAdditions.Units.Select(u => new UnitScan
            {
                AppLovin = u.appLovin,
                Metica = u.metica,
                Format = u.format,
                AppLovinPath = MeticaPaths.Combine(MeticaPaths.RuntimeScripts, "Ads/Applovin/" + u.appLovin + ".cs"),
                MeticaPath = MeticaPaths.Combine(MeticaPaths.AdsMeticaFolder, u.metica + ".cs")
            }).ToList();

            var key = version + "|" + string.Join("|", units.SelectMany(u => new[] { Stamp(u.AppLovinPath), Stamp(u.MeticaPath) }));
            if (_cache != null && key == _cacheKey) return _cache;

            var result = new ScanResult { Version = version };
            if (version == null)
                result.Problem = "The GD SDK version matches no original release — pick it in the Compare step first.";
            else
            {
                foreach (var unit in units)
                {
                    if (!MeticaPaths.FileExists(unit.AppLovinPath)) continue;
                    var original = stock.Get(version, "Runtime/Scripts/Ads/Applovin/" + unit.AppLovin + ".cs");
                    if (original == null) continue;

                    unit.MeticaWritten = MeticaPaths.FileExists(unit.MeticaPath);
                    try
                    {
                        unit.Additions = GameAdditions.Find(unit.AppLovin, unit.Metica, unit.Format, original,
                            SourcePatcher.ReadAll(unit.AppLovinPath), unit.MeticaWritten ? SourcePatcher.ReadAll(unit.MeticaPath) : null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Metica Integration] Couldn't read {unit.AppLovinPath}: {e.Message}");
                    }
                    result.Units.Add(unit);
                }
            }

            _cacheKey = key;
            return _cache = result;
        }

        private static string Stamp(string path)
        {
            var absolute = MeticaPaths.ToAbsolute(path);
            return absolute != null && File.Exists(absolute) ? File.GetLastWriteTimeUtc(absolute).Ticks.ToString() : "-";
        }

        /// <summary>The release the project's GD SDK is — declared, or picked by hand in the Compare step.</summary>
        private static string StockVersion(StockFiles stock)
        {
            var versionText = MeticaPaths.FileExists(MeticaPaths.InitializeOnLoad) ? SourcePatcher.ReadAll(MeticaPaths.InitializeOnLoad) : null;
            var version = ModifiedFileCheck.DetectVersion(stock, versionText);
            var chosen = ModifiedFileCheck.ChosenVersion;
            return version ?? (chosen != null && stock.Versions.Contains(chosen) ? chosen : null);
        }

        /// <summary>Game-added lines that pass for carried: not by hand, and not in the Metica unit yet.</summary>
        private static IEnumerable<Addition> Open(UnitScan unit) =>
            unit.Additions.Where(a => a.Fit != AdditionFit.ByHand && !a.Present);

        // ── Ticks, per project ─────────────────────────────────────────────────

        private static string TicksKey => $"GameDistrict.MeticaIntegrationTools.CarryTicks.{Application.dataPath.GetHashCode():X8}";

        private static HashSet<string> Ticks =>
            new HashSet<string>(EditorPrefs.GetString(TicksKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private static void SetTicks(HashSet<string> ticks) => EditorPrefs.SetString(TicksKey, string.Join("\n", ticks));

        private static void Tick(IEnumerable<Addition> additions, bool on)
        {
            var ticks = Ticks;
            foreach (var addition in additions)
                if (on) ticks.Add(addition.Id);
                else ticks.Remove(addition.Id);
            SetTicks(ticks);
        }

        // ── Verify / Apply ─────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var scan = Scan();
            if (scan.Problem != null) return result.Problem(scan.Problem).Seal();

            var units = scan.Units.Where(u => u.Additions.Count > 0).ToList();
            var unwritten = units.FirstOrDefault(u => !u.MeticaWritten);
            if (unwritten != null) return result.Problem($"{unwritten.Metica}.cs is missing — run the Wrapper files step first.").Seal();

            var ticks = Ticks;
            var all = units.SelectMany(u => u.Additions).ToList();
            var open = units.SelectMany(Open).ToList();
            var tickedOpen = open.Count(a => ticks.Contains(a.Id));
            var untickedCode = open.Count(a => !ticks.Contains(a.Id) && !a.IsLog);
            var untickedLogs = open.Count(a => !ticks.Contains(a.Id) && a.IsLog);
            var byHand = all.Count(a => a.Fit == AdditionFit.ByHand && !a.Present);

            result.Note($"GD SDK {scan.Version}: {Count(all.Count, "line")} the game added to its AppLovin units, " +
                        $"{all.Count(a => a.Present)} already in the Metica units");

            if (tickedOpen > 0)
                result.Problem($"{Count(tickedOpen, "ticked line")} not in the Metica units yet — press Carry ticked lines.");
            if (untickedCode > 0)
                result.Problem($"{Count(untickedCode, "game line")} not carried — tick the ones Metica needs and carry them, " +
                               "or skip this step if it needs none.");
            if (untickedLogs > 0) result.Note($"{Count(untickedLogs, "log line")} left out — optional");
            if (byHand > 0) result.Warning($"{Count(byHand, "line")} to add by hand — open the units below.");

            if (all.Any(a => a.Present && a.UsesHelper) && !MeticaPaths.FileExists(HelperPath))
                result.Problem($"{GameAdditions.HelperClass}.cs is missing, and a carried line uses it — press Carry ticked lines to write it.");

            return result.Seal();
        }

        private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

        public override void Apply()
        {
            var scan = Scan();
            if (scan.Problem != null) return;

            var ticks = Ticks;
            var changed = false;
            var needsHelper = false;
            foreach (var unit in scan.Units.Where(u => u.MeticaWritten))
            {
                var chosen = Open(unit).Where(a => ticks.Contains(a.Id)).ToList();
                if (chosen.Count == 0) continue;

                var text = SourcePatcher.ReadAll(unit.MeticaPath);
                var log = new List<string>();
                var carried = GameAdditions.Carry(text, unit.Format, chosen, log);
                MeticaIntegrationLog.Record(Title, log);
                if (carried == text) continue;

                SourcePatcher.WriteWithBackup(unit.MeticaPath, carried);
                foreach (var line in AddedUsings(unit)) SourcePatcher.EnsureUsing(unit.MeticaPath, line);
                needsHelper |= chosen.Any(a => a.UsesHelper);
                changed = true;
            }

            // Ticked lines that are already there need the helper as much as new ones.
            needsHelper |= scan.Units.SelectMany(u => u.Additions).Any(a => a.UsesHelper && (a.Present || ticks.Contains(a.Id)));
            if (needsHelper && !MeticaPaths.FileExists(HelperPath))
            {
                TemplateWriter.Write(GameAdditions.HelperClass + ".cs.txt", HelperPath, false, out var message);
                MeticaIntegrationLog.Record(Title, message);
                changed = true;
            }

            if (!changed) MeticaIntegrationLog.Record(Title, "Nothing to carry — tick the lines first.");
            else AssetDatabase.Refresh();
        }

        private static readonly Regex UsingLine = new Regex(@"^\s*using\s+[\w\.]+\s*;\s*$", RegexOptions.Multiline);

        /// <summary>The usings the game added to the AppLovin unit — what its lines may need in the Metica one.</summary>
        private static IEnumerable<string> AddedUsings(UnitScan unit)
        {
            var stock = StockFiles.Packaged;
            var version = Scan().Version;
            var original = stock?.Get(version, "Runtime/Scripts/Ads/Applovin/" + unit.AppLovin + ".cs") ?? "";
            var had = new HashSet<string>(UsingLine.Matches(original).Cast<Match>().Select(m => m.Value.Trim()));
            return UsingLine.Matches(SourcePatcher.ReadAll(unit.AppLovinPath)).Cast<Match>()
                .Select(m => m.Value.Trim())
                .Where(line => !had.Contains(line) && line.IndexOf("MaxSdk", StringComparison.Ordinal) < 0)
                .Distinct();
        }

        // ── Controls ───────────────────────────────────────────────────────────

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            var scan = Scan();
            if (scan.Problem != null) yield break;

            var ticks = Ticks;
            foreach (var unit in scan.Units.Where(u => u.Additions.Count > 0))
            {
                var open = Open(unit).ToList();
                var tickable = open.Where(a => !ticks.Contains(a.Id)).ToList();
                var code = tickable.Where(a => !a.IsLog).ToList();
                var byHand = unit.Additions.Count(a => a.Fit == AdditionFit.ByHand && !a.Present);
                var present = unit.Additions.Count(a => a.Present);

                var status = new List<string>();
                if (open.Count > 0) status.Add($"{open.Count(a => ticks.Contains(a.Id))} of {open.Count} ticked");
                if (byHand > 0) status.Add($"{byHand} by hand");
                if (present > 0) status.Add($"{present} there");

                var rows = new List<StepControl>();
                foreach (var addition in unit.Additions)
                    rows.Add(Row(unit, addition, ticks.Contains(addition.Id)));

                var buttons = new List<StepControl>();
                if (code.Count > 0) buttons.Add(new StepButton("Tick all but logs", () => Tick(code, true)));
                if (open.Count > 0 && tickable.Count < open.Count) buttons.Add(new StepButton("Untick all", () => Tick(open, false)));

                // Done when all that's left out is logs nobody ticked.
                yield return new StepItem($"{unit.AppLovin} → {unit.Metica}",
                    open.All(a => a.IsLog && !ticks.Contains(a.Id)),
                    string.Join(" · ", status), buttons.Concat(rows).ToList(),
                    risk: "With Metica on, the lines left out don't run.");
            }
        }

        private static StepCheck Row(UnitScan unit, Addition addition, bool ticked)
        {
            var tags = new List<string>();
            if (addition.IsLog) tags.Add("log");
            if (addition.Present) tags.Add("carried");
            else if (addition.Fit == AdditionFit.Adapt) tags.Add("adapted");
            else if (addition.Fit == AdditionFit.ByHand) tags.Add("by hand");

            string hint;
            if (addition.Present) hint = $"{addition.Method}() — already in {addition.Where ?? addition.MeticaUnit}.";
            else if (addition.Fit == AdditionFit.ByHand) hint = $"{addition.Method}() — {addition.Note}";
            else hint = $"{addition.Method}() → {addition.Where}" + (addition.Note != null ? $" · {addition.Note}" : "");

            var canTick = addition.Fit != AdditionFit.ByHand && !addition.Present;
            return new StepCheck(addition.Code, hint, tags, ticked,
                canTick ? on => Tick(new[] { addition }, on) : (Action<bool>)null,
                () => InternalEditorUtility.OpenFileAtLineExternal(MeticaPaths.ToAbsolute(unit.AppLovinPath), addition.Line),
                !addition.Present && addition.Suggested != addition.Code ? addition.Suggested : null,
                addition.Present);
        }

        /// <summary>
        /// Game-added lines in <paramref name="appLovinPath"/> not yet in its Metica unit — what
        /// replacing the file with the original would lose. 0 for any other file.
        /// </summary>
        internal static int UncarriedIn(string appLovinPath) =>
            Scan().Units.Where(u => u.AppLovinPath == appLovinPath)
                .SelectMany(u => u.Additions)
                .Count(a => !a.Present);
    }
}
