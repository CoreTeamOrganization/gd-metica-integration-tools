using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>Per-project choices and records of the game-code steps, kept in EditorPrefs.</summary>
    internal static class GameCodePrefs
    {
        private static string Key(string name) =>
            $"GameDistrict.MeticaIntegrationTools.GameCode.{name}.{Application.dataPath.GetHashCode():X8}";

        public static string Get(string name) => EditorPrefs.GetString(Key(name), "");

        public static void Set(string name, string value) => EditorPrefs.SetString(Key(name), value ?? "");

        public static List<string> List(string name) =>
            Get(name).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        public static void SetList(string name, IEnumerable<string> values) =>
            Set(name, string.Join("|", values.Distinct()));

        public static void AddToList(string name, IEnumerable<string> values) => SetList(name, List(name).Concat(values));
    }

    /// <summary>
    /// Non-GD-SDK games, optional: makes the method that starts AppLovin MAX choose Metica or MAX
    /// once per session. The developer picks the script and the method; the tool adds
    /// <c>public static bool UseMetica</c> to that class, sets it from the remote switch, moves
    /// the method's MAX code to <c>&lt;Method&gt;_Max()</c> and its AdMob init to
    /// <c>&lt;Method&gt;_AdMob()</c>, and calls either Metica + AdMob or MAX + AdMob.
    /// </summary>
    public sealed class InitPatchStep : MeticaStep
    {
        public override string Title => "Patch: start Metica or MAX";

        public override bool Optional => true;

        public override string Summary => "Choose the script and function that start AppLovin MAX — the tool makes them start Metica or MAX.";

        public override string Why =>
            "Adds public static bool UseMetica to the class (other scripts read it), set first thing from " +
            "MeticaRemoteConfig.ShouldInitialize(MeticaAdsConfig.Load()) — the remote switch's stored value. The function's " +
            "AppLovin MAX statements move to <Function>_Max(), its AdMob statements to <Function>_AdMob(); in their place: " +
            "if (UseMetica) start Metica + AdMob, else MAX + AdMob. Everything else in the function stays where it is. " +
            "Code inside #if, a non-void or coroutine function, or a local shared by both sides is left for a hand edit. " +
            "Backed up first.";

        public override string ActionLabel => Ready(out _) ? $"Patch {MethodName}" : null;

        public override IEnumerable<string> TouchedPaths =>
            string.IsNullOrEmpty(ScriptPath) ? Array.Empty<string>() : new[] { ScriptPath };

        public override string ReviewHint => "UseMetica, the if/else where MAX started, and the new _Max / _AdMob functions.";

        // ── Choices ────────────────────────────────────────────────────────────

        private static List<GameScripts.Script> Scripts() => GameScripts.Containing("MaxSdk");

        private static string ScriptPath
        {
            get
            {
                var chosen = GameCodePrefs.Get("InitScript");
                var scripts = Scripts();
                if (scripts.Any(s => s.Path == chosen)) return chosen;
                return (scripts.FirstOrDefault(s => s.Masked.Contains("MaxSdk.InitializeSdk")) ?? scripts.FirstOrDefault())?.Path;
            }
        }

        private static string MethodName
        {
            get
            {
                var script = ScriptPath == null ? null : GameScripts.Read(ScriptPath);
                if (script == null) return null;
                var methods = CSharpSource.Methods(script.Masked);
                var chosen = GameCodePrefs.Get("InitMethod");
                if (methods.Any(m => m.Label == chosen)) return chosen;
                return (methods.FirstOrDefault(m => script.Masked.Substring(m.Open, m.Close - m.Open).Contains("MaxSdk.InitializeSdk"))
                        ?? methods.FirstOrDefault())?.Label;
            }
        }

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var started = GameScripts.Containing("MeticaAdsManager").FirstOrDefault(s => AdsCodePatcher.StartsMetica(s.Masked));
            if (started != null)
            {
                result.Note($"Metica is started in {started.Path}");
                if (!started.Masked.Contains($"bool {AdsCodePatcher.UseMetica}"))
                    result.Warning("Started by hand, without UseMetica — the ad-format steps need UseMetica to patch the show calls.");
                return result.Seal();
            }

            if (!Ready(out var problem)) return result.Problem(problem).Seal();

            var dryRun = AdsCodePatcher.PatchInit(SourcePatcher.ReadAll(ScriptPath), MethodName);
            if (dryRun.Problem != null) return result.Problem(dryRun.Problem).Seal();

            result.Problem($"Not patched yet — {ScriptPath}, {MethodName}.");
            foreach (var note in dryRun.Notes) result.Note(note);
            return result.Seal();
        }

        /// <summary>Everything in place to patch, or why not.</summary>
        private static bool Ready(out string problem)
        {
            problem = null;
            if (!TemplateWriter.TypeIsLoaded("MeticaIntegration.MeticaAdsManager"))
                problem = "Add the Metica runtime first (an earlier step).";
            else if (ScriptPath == null)
                problem = "No AppLovin MAX code found in the game's scripts.";
            else if (MethodName == null)
                problem = $"No functions found in {ScriptPath}.";
            else
                problem = GameScripts.RuntimeUnreachable(ScriptPath);
            return problem == null;
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            if (!Ready(out var problem)) { MeticaIntegrationLog.Record(Title, problem); return; }

            var path = ScriptPath;
            var result = AdsCodePatcher.PatchInit(SourcePatcher.ReadAll(path), MethodName);
            if (result.Problem != null) { MeticaIntegrationLog.Record(Title, result.Problem); return; }

            SourcePatcher.WriteWithBackup(path, result.Text);
            AssetDatabase.ImportAsset(path);
            MeticaIntegrationLog.Record(Title, new[] { $"Patched {path}, {MethodName}" }.Concat(result.Notes));
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            var scripts = Scripts();
            if (scripts.Count == 0 || GameScripts.Containing("MeticaAdsManager").Any(s => AdsCodePatcher.StartsMetica(s.Masked))) yield break;

            var paths = scripts.Select(s => s.Path).ToArray();
            yield return new StepChoice("Script", paths, Math.Max(0, Array.IndexOf(paths, ScriptPath)), index =>
            {
                GameCodePrefs.Set("InitScript", paths[index]);
                GameCodePrefs.Set("InitMethod", "");
            });

            var script = GameScripts.Read(ScriptPath);
            var methods = script == null ? new string[0] : CSharpSource.Methods(script.Masked).Select(m => m.Label).ToArray();
            if (methods.Length > 0)
                yield return new StepChoice("Function", methods, Math.Max(0, Array.IndexOf(methods, MethodName)),
                    index => GameCodePrefs.Set("InitMethod", methods[index]));
        }
    }

    /// <summary>
    /// Non-GD-SDK games, optional, one per ad format: puts each AppLovin MAX ready / show / hide /
    /// load call of the format behind <c>UseMetica</c>, with the Metica call on the other side.
    /// The game's checks around the call stay; its MAX close and reward handlers are called from
    /// Metica's callbacks. Patched functions with "Max" in their name are renamed
    /// (ShowMaxInterstitial → ShowInterstitial) unless that is unsafe.
    /// </summary>
    public abstract class AdFormatPatchStep : MeticaStep
    {
        internal abstract AdsCodePatcher.Format Format { get; }

        public override bool Optional => true;

        public override string Title => $"Patch: {Format.Name} calls";

        public override string Summary => $"Send the game's {Format.Name} calls to Metica when UseMetica is on, AppLovin MAX otherwise.";

        public override string Why =>
            $"Every function of the game that calls AppLovin MAX for {Format.Name} ads is listed; untick one to leave it on MAX. " +
            "A ready check becomes (UseMetica ? Metica : MAX); each run of MAX calls becomes if (UseMetica) { Metica } " +
            "else { MAX }, so the game's own checks around it (remove ads, cooldowns, fallbacks) still apply. Metica's close " +
            "and reward callbacks call the game's MAX handlers (MaxSdkCallbacks … OnAdHiddenEvent / OnAdReceivedRewardEvent). " +
            "Load, display and click callbacks are not mapped — the runtime loads and retries itself. A patched function " +
            "with Max in its name is renamed, with every call, unless the name is taken, used by another class, or wired " +
            "in a scene or prefab — then it is reported and kept. Backed up first.";

        public override string ActionLabel
        {
            get
            {
                var count = Pending().Count;
                return count == 0 || Gate() == null ? null : $"Patch {count} function{(count == 1 ? "" : "s")}";
            }
        }

        public override IEnumerable<string> TouchedPaths => GameCodePrefs.List(Id + ".Touched");

        public override string ReviewHint => "if (UseMetica) { Metica } else { MAX } around each call, and renamed functions.";

        // ── Reading the game ───────────────────────────────────────────────────

        internal sealed class Candidate
        {
            public string Path;
            public string Label;
            public string Display;
            public int Unpatched;
            public string Key => Path + "#" + Label;
        }

        /// <summary><c>Type.UseMetica</c>, from the class the init patch added it to, or null.</summary>
        internal static string Gate()
        {
            foreach (var script in GameScripts.Containing($"bool {AdsCodePatcher.UseMetica}"))
            {
                var at = script.Masked.IndexOf($"static bool {AdsCodePatcher.UseMetica}", StringComparison.Ordinal);
                if (at < 0) continue;
                var type = CSharpSource.Types(script.Masked).Where(t => t.Open < at && at < t.Close)
                    .OrderBy(t => t.Close - t.Open).FirstOrDefault();
                if (type != null) return type.FullName + "." + AdsCodePatcher.UseMetica;
            }
            return null;
        }

        internal List<Candidate> Candidates()
        {
            var gate = Gate() ?? "UseMetica";
            var found = new List<Candidate>();
            foreach (var script in GameScripts.Containing("MaxSdk"))
            foreach (var method in AdsCodePatcher.Candidates(script.Masked, Format))
                found.Add(new Candidate
                {
                    Path = script.Path,
                    Label = method.Label,
                    Display = $"{method.Type.Name}.{method.Name}",
                    Unpatched = AdsCodePatcher.Unpatched(script.Masked, method, Format, gate)
                });
            return found;
        }

        private HashSet<string> Excluded => new HashSet<string>(GameCodePrefs.List(Id + ".Excluded"));

        private List<Candidate> Pending() => Candidates().Where(c => c.Unpatched > 0 && !Excluded.Contains(c.Key)).ToList();

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var gate = Gate();
            if (gate == null) return result.Problem("Do the init patch first — it adds UseMetica, which these calls switch on.").Seal();

            var candidates = Candidates();
            if (candidates.Count == 0) return result.Note($"No AppLovin MAX {Format.Name} calls in the game's scripts").Seal();

            foreach (var path in candidates.Select(c => c.Path).Distinct())
            {
                var unreachable = GameScripts.RuntimeUnreachable(path);
                if (unreachable != null) result.Problem(unreachable);
            }

            var excluded = Excluded;
            var pending = candidates.Where(c => c.Unpatched > 0 && !excluded.Contains(c.Key)).ToList();
            if (pending.Count > 0)
                result.Problem($"{pending.Count} function{(pending.Count == 1 ? "" : "s")} still call AppLovin MAX directly: " +
                               string.Join(", ", pending.Select(c => c.Display)));

            var patched = candidates.Count(c => c.Unpatched == 0);
            if (patched > 0) result.Note($"{patched} function{(patched == 1 ? "" : "s")} patched");
            var left = candidates.Count(c => c.Unpatched > 0 && excluded.Contains(c.Key));
            if (left > 0) result.Note($"{left} left on AppLovin MAX (unticked)");

            foreach (var note in GameCodePrefs.List(Id + ".Notes")) result.Warning(note);
            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            var gate = Gate();
            if (gate == null) { MeticaIntegrationLog.Record(Title, "Do the init patch first."); return; }

            var pending = Pending();
            var notes = new List<string>();
            var touched = new List<string>();
            var renames = new List<(string path, string label, string oldName)>();

            foreach (var group in pending.GroupBy(c => c.Path))
            {
                var unreachable = GameScripts.RuntimeUnreachable(group.Key);
                if (unreachable != null) { notes.Add(unreachable); continue; }

                var result = AdsCodePatcher.PatchFormat(SourcePatcher.ReadAll(group.Key), group.Select(c => c.Label), Format, gate);
                notes.AddRange(result.Notes);
                if (result.Text == SourcePatcher.ReadAll(group.Key)) continue;

                SourcePatcher.WriteWithBackup(group.Key, result.Text);
                touched.Add(group.Key);

                // Only functions now fully patched get a general name — one left for a hand edit keeps its own.
                var masked = CSharpSource.Mask(result.Text);
                foreach (var candidate in group)
                {
                    var method = AdsCodePatcher.FindMethod(masked, candidate.Label);
                    if (method != null && AdsCodePatcher.Unpatched(masked, method, Format, gate) == 0)
                        renames.Add((candidate.Path, candidate.Label, method.Name));
                }
            }

            touched.AddRange(Rename(renames, notes));

            foreach (var path in touched.Distinct()) AssetDatabase.ImportAsset(path);
            GameCodePrefs.AddToList(Id + ".Touched", touched);
            GameCodePrefs.SetList(Id + ".Notes", notes);
            MeticaIntegrationLog.Record(Title, new[] { $"Patched {pending.Count} function(s) in {touched.Distinct().Count()} file(s)" }.Concat(notes));
        }

        /// <summary>
        /// ShowMaxInterstitial → ShowInterstitial, with every call — only when nothing else could
        /// be meant: the new name is free, no other method has the old one, and no scene or prefab
        /// wires it by name. Returns the files it changed.
        /// </summary>
        private List<string> Rename(List<(string path, string label, string oldName)> renames, List<string> notes)
        {
            var changed = new List<string>();
            foreach (var (path, _, oldName) in renames.Distinct())
            {
                var newName = AdsCodePatcher.GeneralName(oldName);
                if (newName == null) continue;

                var scripts = GameScripts.Containing(oldName);
                var declarations = scripts.Sum(s => CSharpSource.Methods(s.Masked).Count(m => m.Name == oldName));
                var script = GameScripts.Read(path);
                // Taken: a method of that name, or the name used unqualified (a field, a local, a
                // call) — `x.ShowInterstitial(` on another object is no clash.
                var taken = CSharpSource.Methods(script.Masked).Any(m => m.Name == newName)
                            || Regex.IsMatch(script.Masked, $@"(?<![\.\w]){Regex.Escape(newName)}\b");
                var wired = WiredInAssets(oldName);

                var reason = taken ? $"{newName} is already used in {path}"
                    : declarations > 1 ? $"another method is also called {oldName}"
                    : wired != null ? $"{wired} calls it by name (an Inspector event)"
                    : null;
                if (reason != null)
                {
                    notes.Add($"Kept the name {oldName} — {reason}.");
                    continue;
                }

                var renamed = AdsCodePatcher.Rename(scripts.ToDictionary(s => s.Path, s => SourcePatcher.ReadAll(s.Path)), oldName, newName);
                foreach (var pair in renamed)
                {
                    SourcePatcher.WriteWithBackup(pair.Key, pair.Value);
                    changed.Add(pair.Key);
                }
                notes.Add($"Renamed {oldName} → {newName} ({renamed.Count} file{(renamed.Count == 1 ? "" : "s")}).");
            }
            return changed;
        }

        /// <summary>The first scene, prefab or asset whose Inspector events call <paramref name="method"/>, or null.</summary>
        private static string WiredInAssets(string method)
        {
            var needle = "m_MethodName: " + method;
            foreach (var file in Directory.EnumerateFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".unity") || f.EndsWith(".prefab") || f.EndsWith(".asset")))
            {
                try
                {
                    foreach (var line in File.ReadLines(file))
                        if (line.Trim() == needle)
                            return "Assets/" + file.Substring(Application.dataPath.Length).TrimStart('/', '\\').Replace('\\', '/');
                }
                catch (IOException)
                {
                    // Locked or unreadable — nothing to learn from it.
                }
            }
            return null;
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (Gate() == null) yield break;

            var excluded = Excluded;
            foreach (var candidate in Candidates())
            {
                var c = candidate;
                var state = c.Unpatched == 0 ? "patched" : $"{c.Unpatched} MAX call{(c.Unpatched == 1 ? "" : "s")}";
                yield return new StepToggle($"{c.Display} — {c.Path} ({state})", !excluded.Contains(c.Key), on =>
                {
                    var list = GameCodePrefs.List(Id + ".Excluded");
                    if (on) list.Remove(c.Key); else list.Add(c.Key);
                    GameCodePrefs.SetList(Id + ".Excluded", list);
                });
            }
        }
    }

    public sealed class InterstitialPatchStep : AdFormatPatchStep
    {
        internal override AdsCodePatcher.Format Format => AdsCodePatcher.Interstitial;
    }

    public sealed class RewardedPatchStep : AdFormatPatchStep
    {
        internal override AdsCodePatcher.Format Format => AdsCodePatcher.Rewarded;
    }

    public sealed class BannerPatchStep : AdFormatPatchStep
    {
        internal override AdsCodePatcher.Format Format => AdsCodePatcher.Banner;
    }

    public sealed class MRecPatchStep : AdFormatPatchStep
    {
        internal override AdsCodePatcher.Format Format => AdsCodePatcher.MRec;
    }

    /// <summary>
    /// Non-GD-SDK games, optional: subscribes <c>MeticaAdsHooks.OnAdRevenue</c>, right before
    /// <c>MeticaAdsManager.Initialize()</c>, to an empty <c>OnMeticaAdRevenue(AdRevenueInfo)</c>
    /// the developer fills with the game's own revenue reporting.
    /// </summary>
    public sealed class RevenueLoggingStep : MeticaStep
    {
        public override string Title => "Patch: Metica ad revenue";

        public override bool Optional => true;

        public override string Summary => "Add an OnMeticaAdRevenue(AdRevenueInfo) handler to fill with the game's revenue reporting.";

        public override string Why =>
            "Adds MeticaAdsHooks.OnAdRevenue += OnMeticaAdRevenue right before MeticaAdsManager.Initialize(), and an empty " +
            "OnMeticaAdRevenue(AdRevenueInfo info) in the same class with a TODO. Fill it the way the game reports MAX revenue " +
            "(Firebase, Adjust, AppMetrica …). Interstitial and rewarded revenue arrive on Metica's native thread — Unity calls " +
            "go through ThreadDispatcher.Enqueue. Backed up first.";

        public override string ActionLabel => StartScript() != null && Subscribed() == null ? "Add the revenue handler" : null;

        public override IEnumerable<string> TouchedPaths =>
            StartScript() is GameScripts.Script script ? new[] { script.Path } : Array.Empty<string>();

        public override string ReviewHint => "One subscription line, and OnMeticaAdRevenue with a TODO.";

        private static GameScripts.Script StartScript() =>
            GameScripts.Containing("MeticaAdsManager").FirstOrDefault(s => AdsCodePatcher.StartsMetica(s.Masked));

        private static GameScripts.Script Subscribed() =>
            GameScripts.Containing("MeticaAdsHooks").FirstOrDefault(s => AdsCodePatcher.SubscribesRevenue(s.Masked));

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var subscribed = Subscribed();
            if (subscribed != null)
            {
                result.Note($"OnAdRevenue is handled in {subscribed.Path}");
                if (Regex.IsMatch(subscribed.Masked, $@"void\s+{AdsCodePatcher.RevenueHandler}\s*\([^)]*\)\s*\{{\s*\}}"))
                    result.Warning($"{AdsCodePatcher.RevenueHandler} is still empty — add the game's revenue reporting.");
                return result.Seal();
            }

            var start = StartScript();
            if (start == null) return result.Problem("Do the init patch first — MeticaAdsManager.Initialize() not found.").Seal();
            return result.Problem($"Not added yet — {start.Path}.").Seal();
        }

        public override void Apply()
        {
            var start = StartScript();
            if (start == null) { MeticaIntegrationLog.Record(Title, "MeticaAdsManager.Initialize() not found."); return; }

            var result = AdsCodePatcher.PatchRevenue(SourcePatcher.ReadAll(start.Path));
            if (result.Problem != null) { MeticaIntegrationLog.Record(Title, result.Problem); return; }

            SourcePatcher.WriteWithBackup(start.Path, result.Text);
            AssetDatabase.ImportAsset(start.Path);
            MeticaIntegrationLog.Record(Title, result.Notes);
        }
    }
}
