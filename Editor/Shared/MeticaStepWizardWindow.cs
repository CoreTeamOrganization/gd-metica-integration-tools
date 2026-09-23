using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The generic step-by-step wizard shell: verify-then-sign-off steps, a review gate with
    /// a diff command scoped to what each step touched, and a log of what ran. Both the Ads
    /// Integration and Genre Creator windows are this shell plus their own step list and
    /// header — extracted here once both needed it, so the two do not drift apart the way
    /// the tool itself did when it lived as two hand-copied folders.
    ///
    /// <para>The wizard never trusts stored progress: every step is re-verified against the
    /// real project, and a step stays locked until the one before it verifies. So it is safe
    /// to close the window, let Unity recompile, or come back tomorrow.</para>
    /// </summary>
    public abstract class MeticaStepWizardWindow : EditorWindow
    {
        /// <summary>
        /// The steps this run shows, in order. Read fresh each time rather than cached once,
        /// so a subclass can switch step lists between refreshes (Ads Integration does, based
        /// on whether the GD SDK is present) without the base needing to know why.
        /// </summary>
        protected abstract MeticaStep[] Steps { get; }

        private VerifyResult[] _results = Array.Empty<VerifyResult>();
        private bool[] _expanded = Array.Empty<bool>();

        private Vector2 _scroll;
        private bool _showLog;

        /// <summary>
        /// Work requested by a button. Steps open dialogs, import packages and trigger
        /// reimports, none of which is safe inside OnGUI, so the press only records what to
        /// do and the work runs on the next editor tick.
        /// </summary>
        private int _pendingApply = -1;
        private bool _pendingRefresh;

        protected virtual void OnEnable()
        {
            AssemblyReloadEvents.afterAssemblyReload += RefreshAll;
            RefreshAll();
            ExpandCurrent();
        }

        protected virtual void OnDisable() => AssemblyReloadEvents.afterAssemblyReload -= RefreshAll;

        private void OnFocus() => QueueRefresh();

        /// <summary>Queues a re-check for the next editor tick, outside the GUI pass.</summary>
        protected void QueueRefresh()
        {
            if (_pendingRefresh) return;

            _pendingRefresh = true;
            EditorApplication.delayCall += () =>
            {
                _pendingRefresh = false;
                RefreshAll();
            };
        }

        /// <summary>Queues a step's action for the next editor tick, then a re-check.</summary>
        protected void QueueApply(int index)
        {
            if (_pendingApply >= 0) return;

            _pendingApply = index;
            var step = Steps[index];

            EditorApplication.delayCall += () =>
            {
                _pendingApply = -1;

                try
                {
                    step.Apply();
                    AssetDatabase.SaveAssets();
                }
                catch (Exception e)
                {
                    MeticaIntegrationLog.Record(step.Title, $"Failed: {e.Message}");
                }

                RefreshAll();
                ExpandCurrent();
            };
        }

        protected virtual void RefreshAll()
        {
            MeticaPaths.ForgetCache();

            var steps = Steps;
            if (_results.Length != steps.Length)
            {
                _results = new VerifyResult[steps.Length];
                _expanded = new bool[steps.Length];
            }

            for (var i = 0; i < steps.Length; i++)
            {
                try
                {
                    _results[i] = steps[i].Verify();
                }
                catch (Exception e)
                {
                    _results[i] = VerifyResult.Fail($"Verification threw: {e.Message}");
                }

                // If a step stops verifying — someone reverted, or a later step broke it —
                // the earlier sign-off no longer means anything.
                if (!_results[i].Ok) MeticaIntegrationProgress.ClearReviewed(steps[i].Id);
            }

            Repaint();
        }

        /// <summary>Opens the first step that still needs work and collapses the rest.</summary>
        protected void ExpandCurrent()
        {
            var current = CurrentStepIndex();
            for (var i = 0; i < _expanded.Length; i++) _expanded[i] = i == current;

            Repaint();
        }

        /// <summary>
        /// A step is finished only when it verifies AND you have signed it off, so the run
        /// stops after every step and waits while you read the diff.
        /// </summary>
        protected bool IsComplete(int index) =>
            IsSkipped(index)
            || (_results[index] != null
                && _results[index].Ok
                && MeticaIntegrationProgress.IsReviewed(Steps[index].Id));

        /// <summary>A step passed over on purpose. Only optional steps can be in this state.</summary>
        protected bool IsSkipped(int index) =>
            Steps[index].Optional && MeticaIntegrationProgress.IsSkipped(Steps[index].Id);

        /// <summary>Index of the first unfinished step, or Steps.Length when done.</summary>
        protected int CurrentStepIndex()
        {
            var steps = Steps;
            for (var i = 0; i < steps.Length; i++)
                if (!IsComplete(i)) return i;

            return steps.Length;
        }

        private void OnGUI()
        {
            var current = CurrentStepIndex();

            DrawHeader(current);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < Steps.Length; i++) DrawStep(i, current);
            EditorGUILayout.EndScrollView();

            DrawLog();
        }

        /// <summary>
        /// Title, description and any flow-specific controls above the step list. A subclass
        /// draws its own, then typically calls <see cref="DrawProgressAndControls"/> for the
        /// shared progress bar and buttons.
        /// </summary>
        protected abstract void DrawHeader(int current);

        /// <summary>
        /// The progress bar, Re-check/Reveal backups/Reset sign-offs row, and the "all done"
        /// box — the part of the header that does not vary between flows. All the ids ever
        /// signed off or skipped across every step list a subclass might show have to be
        /// passed in, so Reset sign-offs clears all of them, not just the ones on screen right
        /// now (Ads Integration can switch step lists between GDSDK and standalone runs).
        /// </summary>
        protected void DrawProgressAndControls(int current, IEnumerable<string> allStepIds)
        {
            var steps = Steps;
            var skipped = Enumerable.Range(0, steps.Length).Count(IsSkipped);
            var fraction = steps.Length == 0 ? 1f : (float)current / steps.Length;
            var rect = EditorGUILayout.GetControlRect(false, 18);
            EditorGUI.ProgressBar(rect, fraction,
                current >= steps.Length
                    ? skipped == 0
                        ? "All steps verified"
                        : $"All steps done — {skipped} skipped, not verified"
                    : $"Step {current + 1} of {steps.Length} — {steps[current].Title}");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-check everything")) QueueRefresh();

            using (new EditorGUI.DisabledScope(!MeticaPaths.DirectoryExists(MeticaPaths.BackupRoot)))
            {
                if (GUILayout.Button("Reveal backups"))
                    EditorUtility.RevealInFinder(MeticaPaths.BackupRoot + "/");
            }

            if (GUILayout.Button("Reset sign-offs")
                && EditorUtility.DisplayDialog("Reset sign-offs",
                    "Forget which steps you have reviewed or skipped? Nothing in the project changes — " +
                    "the run just asks you to sign each step off again.", "Reset", "Cancel"))
            {
                MeticaIntegrationProgress.ClearAll(allStepIds.Distinct().ToArray());
                QueueRefresh();
            }
            EditorGUILayout.EndHorizontal();

            if (current >= steps.Length)
            {
                var names = Enumerable.Range(0, steps.Length)
                    .Where(IsSkipped)
                    .Select(i => $"{i + 1}. {steps[i].Title}")
                    .ToArray();

                EditorGUILayout.HelpBox(
                    names.Length == 0
                        ? "Every step verifies. Build to a device and confirm the Metica logs before shipping."
                        : "Build to a device and confirm the Metica logs before shipping.\n\n" +
                          "Skipped, so never verified — come back to these if the build complains:\n" +
                          string.Join("\n", names),
                    MessageType.Info);
            }
        }

        private void DrawStep(int index, int current)
        {
            var step = Steps[index];
            var result = _results[index];
            var verified = result != null && result.Ok;
            var reviewed = MeticaIntegrationProgress.IsReviewed(step.Id);
            var skipped = IsSkipped(index);
            var passed = !skipped && verified && reviewed;
            var locked = index > current;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── Title row ──────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            var mark = skipped ? "–" : passed ? "✔" : locked ? "•" : verified ? "?" : "▸";
            var header = $"{mark}  {index + 1}. {step.Title}";

            using (new EditorGUI.DisabledScope(locked))
            {
                _expanded[index] = EditorGUILayout.Foldout(_expanded[index], header, true, EditorStyles.foldoutHeader);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                skipped ? "skipped" : passed ? "done" : locked ? "locked" : verified ? "review" : "to do",
                EditorStyles.miniLabel, GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();

            if (!_expanded[index] || locked)
            {
                if (locked && _expanded[index])
                    EditorGUILayout.LabelField($"Locked until step {current + 1} passes.",
                        EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
                return;
            }

            // ── Body ───────────────────────────────────────────────────────────
            EditorGUILayout.LabelField(step.Summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(2);

            if (result != null)
            {
                foreach (var problem in result.Problems)
                    EditorGUILayout.HelpBox(problem, MessageType.Error);

                foreach (var note in result.Notes)
                    EditorGUILayout.LabelField("• " + note, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(2);
            step.DrawBody(result);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_pendingApply >= 0))
            {
                if (step.ActionLabel != null && GUILayout.Button(step.ActionLabel, GUILayout.Height(24)))
                    QueueApply(index);

                if (GUILayout.Button("Re-check", GUILayout.Width(90), GUILayout.Height(24)))
                    QueueRefresh();
            }

            EditorGUILayout.EndHorizontal();

            if (step.Optional) DrawSkipControl(step, skipped);

            if (!skipped && verified && !reviewed) DrawReviewGate(step);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// Skip / un-skip for an optional step. Skipping unlocks the next step without the
        /// project satisfying this one, and is always reversible.
        /// </summary>
        private void DrawSkipControl(MeticaStep step, bool skipped)
        {
            EditorGUILayout.Space(2);

            if (skipped)
            {
                EditorGUILayout.HelpBox("Skipped. Nothing was changed for this step.", MessageType.None);

                if (GUILayout.Button("Un-skip this step"))
                {
                    MeticaIntegrationProgress.ClearSkipped(step.Id);
                    QueueRefresh();
                }

                return;
            }

            if (GUILayout.Button("Skip this step"))
            {
                MeticaIntegrationProgress.MarkSkipped(step.Id);
                QueueRefresh();
            }
        }

        /// <summary>
        /// Shown once a step verifies: what to look at, a diff command scoped to the files it
        /// touched, and the sign-off that unlocks the next step.
        /// </summary>
        private void DrawReviewGate(MeticaStep step)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Verified — review before continuing", EditorStyles.boldLabel);

            if (step.ReviewHint != null)
                EditorGUILayout.LabelField(step.ReviewHint, EditorStyles.wordWrappedMiniLabel);

            var paths = step.TouchedPaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (paths.Length > 0)
            {
                var command = "git diff -- " + string.Join(" ", paths.Select(p => $"\"{p}\""));

                EditorGUILayout.Space(2);
                EditorGUILayout.SelectableLabel(command,
                    EditorStyles.textArea, GUILayout.Height(paths.Length > 3 ? 54 : 38));

                if (GUILayout.Button("Copy the diff command"))
                {
                    EditorGUIUtility.systemCopyBuffer = command;
                    ShowNotification(new GUIContent("Diff command copied"));
                }
            }
            else
            {
                EditorGUILayout.LabelField("This step changed no files — nothing to diff.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Reviewed — unlock the next step", GUILayout.Height(26)))
            {
                MeticaIntegrationProgress.MarkReviewed(step.Id);
                QueueRefresh();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLog()
        {
            IReadOnlyList<string> entries = MeticaIntegrationLog.All;
            if (entries.Count == 0) return;

            EditorGUILayout.Space(4);
            _showLog = EditorGUILayout.Foldout(_showLog, $"What this tool changed ({entries.Count})", true);
            if (!_showLog) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (var i = entries.Count - 1; i >= 0; i--)
                EditorGUILayout.LabelField(entries[i], EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("Clear")) MeticaIntegrationLog.Clear();
            EditorGUILayout.EndVertical();
        }
    }
}
