using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Optional last step: Claude reviews every change the integration made — the tool's and
    /// anyone else's — against how this project's own code works. The tool can only check
    /// what it knows to look for; integrations differ per GD SDK version, per fork and per
    /// game, and a build like GD SDK 5.0.0-beta5 (whose AdsManager never set the IsTopPriority
    /// the Metica banner waited for) passed every step and still never showed a banner.
    ///
    /// <para>The step builds a bundle (<see cref="ReviewBundle"/>), installs the
    /// metica-integration-review skill into the project and runs Claude Code on it
    /// (<see cref="ClaudeReview"/>), then shows result.json. It passes when the review found
    /// no blockers, or each blocker has been acknowledged.</para>
    /// </summary>
    public sealed class IntegrationReviewStep : MeticaStep
    {
        public const string StepTitle = "Integration review";

        private string _gitBase = string.Empty;
        private string _startError;

        public override string Title => StepTitle;

        public override bool Optional => true;

        public override string Summary => "Have Claude check every change the integration made — or skip.";

        public override string Why =>
            "Optional, and best run last. The steps check what they know to look for; this reads everything. It " +
            "writes a bundle to MeticaIntegrationReview/<time>/ — the GD SDK version and code shape, the template " +
            "each wrapper came from, every step's own check, the GD SDK against its original release, and the git " +
            "diff since before the integration — then runs Claude Code with the metica-integration-review skill " +
            "(installed into .claude/skills). For each ad format it traces the Metica path end to end, finds who " +
            "sets every value the Metica units wait for, and compares them with the game's AppLovin units. It " +
            "writes result.json and changes nothing else. Needs the Claude Code CLI; without it, Copy prompt and " +
            "paste it into Claude Code in this project's folder. Unknown GD SDK builds (betas, forks) should " +
            "always be reviewed.";

        public override string ActionLabel => ClaudeReview.IsRunning ? null : "Build bundle and run review";

        public override IEnumerable<string> TouchedPaths => new[] { ClaudeReview.SkillTargetRelative };

        public override string ReviewHint => "Only the review skill under .claude/skills — the bundle itself is git-ignored.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (UnknownBuild(out var declared))
                result.Warning($"Unknown GD SDK build \"{declared}\" — no release declares it, so the tool's patches " +
                               "weren't made for it. Run the review.");

            if (_startError != null) result.Problem(_startError);

            if (ClaudeReview.IsRunning)
            {
                result.Wait($"Claude is reviewing {ClaudeReview.RunningBundle} ({ClaudeReview.Elapsed:mm\\:ss}). " +
                            "You can keep working; this updates when it's done.");
                return result.Seal();
            }

            var bundle = ReviewBundle.Latest;
            if (bundle == null)
            {
                result.Problem("Not reviewed yet — build the bundle and run the review, or skip.");
                if (ClaudeReview.FindCli() == null)
                    result.Note("Claude Code CLI not found: Build bundle, then Copy prompt into Claude Code in this folder.");
                return result.Seal();
            }

            result.Note($"Latest bundle: {bundle}");

            var review = ReviewResult.Load(bundle, out var error);
            if (error != null)
            {
                result.Problem(error + " Run the review again.");
                return result.Seal();
            }

            if (review == null)
            {
                result.Problem($"{bundle} has no {ReviewBundle.ResultFile} yet — run the review " +
                               "(or paste the copied prompt into Claude Code).");
                return result.Seal();
            }

            if (!string.IsNullOrEmpty(review.summary)) result.Note(review.summary);

            var acknowledged = ReviewResult.Acknowledged(bundle);
            var open = review.Blockers.Where(f => !acknowledged.Contains(f.id)).ToList();
            foreach (var blocker in open)
                result.Problem($"{blocker.title}{(blocker.Where != null ? $" ({blocker.Where})" : string.Empty)}");

            var warnings = review.Warnings.ToList();
            if (warnings.Count > 0)
                result.Warning($"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")} — open them below.");

            if (open.Count == 0)
                result.Note(review.Blockers.Any()
                    ? "All blockers acknowledged"
                    : $"No blockers ({review.findings.Length} finding{(review.findings.Length == 1 ? "" : "s")})");

            return result.Seal();
        }

        public override void Apply()
        {
            _startError = null;
            try
            {
                EditorUtility.DisplayProgressBar(StepTitle, "Building the review bundle…", 0.5f);
                AssetDatabase.SaveAssets();
                var bundle = ReviewBundle.Build(_gitBase, AdsFlow.CurrentSteps, this);
                MeticaIntegrationLog.Record(Title, $"Review bundle written: {bundle}");
                _startError = ClaudeReview.Start(bundle);
            }
            catch (Exception e)
            {
                _startError = "Couldn't build the review bundle: " + e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (ClaudeReview.IsRunning)
            {
                yield return new StepButton("Stop the review", ClaudeReview.Cancel);
                yield break;
            }

            yield return new StepText("Compare with commit (optional)", _gitBase, value => _gitBase = value ?? string.Empty);

            yield return new StepButton("Build bundle only", BuildOnly);

            var bundle = ReviewBundle.Latest;
            if (bundle != null)
            {
                yield return new StepButton("Copy prompt", () => CopyPrompt(bundle));
                yield return new StepButton("Open bundle folder", () => EditorUtility.RevealInFinder(
                    Path.Combine(ReviewBundle.Absolute(bundle), ReviewBundle.IndexFile)), icon: StepIcon.Folder);

                var review = ReviewResult.Load(bundle, out _);
                if (review != null)
                {
                    var acknowledged = ReviewResult.Acknowledged(bundle);
                    foreach (var finding in review.findings
                                 .OrderBy(f => f.IsBlocker ? 0 : f.Severity == "warning" ? 1 : 2))
                        yield return Item(bundle, finding, acknowledged.Contains(finding.id));
                }
            }
        }

        private StepItem Item(string bundle, ReviewFinding finding, bool acknowledged)
        {
            var actions = new List<StepControl>();
            if (!string.IsNullOrEmpty(finding.detail)) actions.Add(new StepCaption(finding.detail));
            if (!string.IsNullOrEmpty(finding.fix)) actions.Add(new StepCaption("Fix: " + finding.fix));
            if (finding.Where != null)
                actions.Add(new StepButton("Open " + Path.GetFileName(finding.file), () => Open(finding), icon: StepIcon.File));
            if (finding.IsBlocker)
                actions.Add(new StepToggle("Acknowledged — ship with it", acknowledged,
                    value => ReviewResult.SetAcknowledged(bundle, finding.id, value)));

            var done = !finding.IsBlocker || acknowledged;
            var status = finding.IsBlocker ? (acknowledged ? "Blocker — acknowledged" : "Blocker") :
                finding.Severity == "warning" ? "Warning" : "Info";
            return new StepItem(finding.title ?? finding.id, done, status, actions, detail: finding.Where);
        }

        private void BuildOnly()
        {
            _startError = null;
            try
            {
                AssetDatabase.SaveAssets();
                var bundle = ReviewBundle.Build(_gitBase, AdsFlow.CurrentSteps, this);
                var skill = ClaudeReview.InstallSkill();
                MeticaIntegrationLog.Record(Title, $"Review bundle written: {bundle}" + (skill != null ? $" — {skill}" : string.Empty));
                CopyPrompt(bundle);
            }
            catch (Exception e)
            {
                _startError = "Couldn't build the review bundle: " + e.Message;
            }
        }

        private static void CopyPrompt(string bundle)
        {
            var skill = ClaudeReview.SkillIsCurrent() ? null : ClaudeReview.InstallSkill();
            EditorGUIUtility.systemCopyBuffer = ClaudeReview.Prompt(bundle);
            MeticaIntegrationLog.Record(StepTitle,
                skill ?? "Prompt copied — paste it into Claude Code started in this project's folder.");
        }

        private static void Open(ReviewFinding finding)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finding.file);
            if (asset != null) AssetDatabase.OpenAsset(asset, Math.Max(finding.line, 1));
            else EditorUtility.RevealInFinder(Path.Combine(ReviewBundle.ProjectDir, finding.file));
        }

        /// <summary>A GD SDK whose declared version no original release has — a beta, fork or removed release.</summary>
        internal static bool UnknownBuild(out string declared)
        {
            declared = null;
            if (!MeticaPaths.HasGDSdk) return false;
            try
            {
                var compare = SdkCompare.Run(false);
                declared = compare.Declared;
                return compare.Declared != null && (compare.Version == null || compare.ChosenByHand);
            }
            catch
            {
                return false;
            }
        }
    }
}
