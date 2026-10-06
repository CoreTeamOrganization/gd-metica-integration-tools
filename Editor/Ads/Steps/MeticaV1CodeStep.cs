using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Lists every line of game code that still uses the Metica v1 API, and passes once there
    /// are none. It never edits the game's code — the developer does, with the list and what
    /// replaces each call.
    ///
    /// <para>Before the Metica SDK step on purpose: v1's API is gone in 2.x, so this code stops
    /// compiling the moment v1 is removed. Taking it out first, while v1 is still installed,
    /// keeps the project compiling at every point — the game runs on MAX alone, then the next
    /// steps swap v1 for the target version and add Metica the 2.x way.</para>
    ///
    /// <para>Only shows in a project that has, or had, Metica v1.</para>
    /// </summary>
    public sealed class MeticaV1CodeStep : MeticaStep
    {
        private const int LinesShownPerFile = 25;

        public override string Title => "Metica v1 code";

        public override string Summary => "Take the old Metica v1 calls out of the game's code.";

        public override string Why => MeticaPaths.HasGDSdk
            ? "Metica v1 is in this project. Its API is gone in Metica 2.x, so the code below stops " +
              "compiling once v1 is removed. In a GD SDK project v1 lives in the AppLovin scripts: " +
              "Compare with original GD SDK shows exactly what v1 changed there. Put those parts back " +
              "to the original, keeping the game's own changes; the later steps add Metica the 2.x way. " +
              "The tool lists the lines; it does not edit them."
            : "Metica v1 is in this project. Its API is gone in Metica 2.x (Metica.ADS → Metica.Ads, no " +
              "per-user IsMeticaAdsEnabled, no NotifyAd* calls), so the code below stops compiling once " +
              "v1 is removed. Take the v1 calls out and keep the game's own MAX path — the game then runs " +
              "on MAX alone. After the Metica SDK, runtime and config steps, call MeticaAdsManager where " +
              "the old Metica branches were: Initialize() at boot, IsEnabled to choose Metica or MAX, " +
              "HasInterstitial / ShowInterstitial, HasRewarded / ShowRewarded, ShowBanner / ShowMRec, and " +
              "MeticaAdsHooks.OnAdRevenue for revenue. The tool lists the lines; it does not edit them.";

        // Nothing to run: the developer edits the code, and Re-check re-scans.
        public override string ActionLabel => null;

        public override IEnumerable<string> TouchedPaths => RememberedFiles;

        public override string ReviewHint =>
            "Only Metica v1 lines should have gone; the game should still compile and run on MAX alone.";

        internal override bool Applies =>
            RememberedFiles.Count > 0
            || MeticaInstalls.Find(null).Any(i => i.Target == "com.metica.unity" || i.Target == MeticaInstalls.V1ConfigAsset)
            || MeticaV1Code.ScanProject().Count > 0;

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            var found = MeticaV1Code.ScanProject();
            Remember(found.Select(r => r.Path));

            if (found.Count == 0)
            {
                result.Note("No Metica v1 code left");
            }
            else
            {
                var files = found.GroupBy(r => r.Path).ToList();
                result.Problem($"{found.Count} Metica v1 line{(found.Count == 1 ? "" : "s")} in " +
                               $"{files.Count} file{(files.Count == 1 ? "" : "s")} — take them out.");
                foreach (var file in files)
                    result.Problem($"{file.Key} — {file.Count()} line{(file.Count() == 1 ? "" : "s")}");
            }

            foreach (var scene in ScenesWithV1Object())
                result.Warning($"{scene} has a MeticaSdk object — v1 needed it. Delete it once v1 is removed.");

            return result.Seal();
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (MeticaPaths.HasGDSdk)
                yield return new StepButton("Compare with original GD SDK…", SdkCompareWindow.Open);

            foreach (var file in MeticaV1Code.ScanProject().GroupBy(r => r.Path))
            {
                var lines = file.ToList();
                var actions = lines.Take(LinesShownPerFile)
                    .Select(r => (StepControl)new StepButton($"Line {r.Line}: {Shorten(r.Text)} — {r.Hint}",
                        () => OpenAt(r.Path, r.Line)))
                    .ToList();
                if (lines.Count > LinesShownPerFile)
                    actions.Add(new StepButton($"Open the file ({lines.Count - LinesShownPerFile} more lines)",
                        () => OpenAt(file.Key, lines[LinesShownPerFile].Line), icon: StepIcon.File));

                yield return new StepItem(file.Key, false,
                    $"{lines.Count} Metica v1 line{(lines.Count == 1 ? "" : "s")}", actions);
            }
        }

        private static void OpenAt(string path, int line) =>
            InternalEditorUtility.OpenFileAtLineExternal(MeticaPaths.ToAbsolute(path), line);

        private static string Shorten(string text) => text.Length <= 60 ? text : text.Substring(0, 57) + "…";

        // ── Files v1 was found in, kept per project ────────────────────────────

        /// <summary>
        /// Every file v1 code was ever found in, so the step stays in the run once the code is
        /// fixed (and the commit section knows which files to commit).
        /// </summary>
        private static List<string> RememberedFiles =>
            EditorPrefs.GetString(RememberedKey, string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static void Remember(IEnumerable<string> paths)
        {
            var all = RememberedFiles.Union(paths).OrderBy(p => p, StringComparer.Ordinal).ToList();
            EditorPrefs.SetString(RememberedKey, string.Join("|", all));
        }

        private static string RememberedKey =>
            $"GameDistrict.MeticaIntegrationTools.MeticaV1Files.{Application.dataPath.GetHashCode():X8}";

        // ── v1's scene object ──────────────────────────────────────────────────

        /// <summary>
        /// Scenes with a root object named MeticaSdk — v1 needed one; without the v1 package it
        /// is a missing script. Only looked for while this step shows.
        /// </summary>
        private static IEnumerable<string> ScenesWithV1Object()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var absolute = MeticaPaths.ToAbsolute(path);
                bool has;
                try
                {
                    var written = File.GetLastWriteTimeUtc(absolute);
                    if (!SceneCache.TryGetValue(absolute, out var cached) || cached.written != written)
                    {
                        cached = (written, File.ReadLines(absolute).Any(l => l.Trim() == "m_Name: MeticaSdk"));
                        SceneCache[absolute] = cached;
                    }
                    has = cached.has;
                }
                catch (IOException)
                {
                    continue;
                }

                if (has) yield return path;
            }
        }

        /// <summary>Scenes can be large; each is read again only when it changes.</summary>
        private static readonly Dictionary<string, (DateTime written, bool has)> SceneCache =
            new Dictionary<string, (DateTime, bool)>();
    }
}
