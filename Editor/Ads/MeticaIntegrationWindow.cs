using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Step-by-step Metica integration for a game on GD Monetization SDK v5 or older.
    ///
    /// The wizard never trusts stored progress: every step is re-verified against the real
    /// project, and a step stays locked until the one before it verifies. So it is safe to
    /// close the window, let Unity recompile, or come back tomorrow.
    /// </summary>
    public sealed class MeticaIntegrationWindow : EditorWindow
    {
        /// <summary>
        /// The run for a project that has the GD Monetization SDK: Metica is wired into an
        /// ads layer that already exists.
        ///
        /// <para>In both runs the integration comes first and the Gradle steps last. Both
        /// Gradle steps are optional and neither can be judged until there is a build to
        /// judge, so they sit where you reach them with a failing build in hand rather than
        /// blocking the work that produces it.</para>
        /// </summary>
        private static readonly MeticaStep[] GDSdkSteps =
        {
            new ImportMeticaSdkStep(),
            new ResolveLibrariesStep(),
            new WrapperFilesStep(),
            new PatchCoreFilesStep(),
            new RemoteSwitchStep(),
            new MeticaSettingsStep(),
            new AdUnitsStep(),
            new AsyncCleanupStep(),
            new FinishUpStep(),
            new DependenciesStep(),
            new GradleVersionStep(),
            new KotlinTemplateStep(),
            new RemoveToolStep()
        };

        /// <summary>
        /// The run for a project with no GD Monetization SDK: Metica is installed on its own,
        /// so there is nothing to patch, no remote flag and no async path to clean up.
        /// </summary>
        private static readonly MeticaStep[] StandaloneSteps =
        {
            new ImportMeticaSdkStep(),
            new ResolveLibrariesStep(),
            new StandaloneRuntimeStep(),
            new StandaloneConfigStep(),
            new DependenciesStep(),
            new GradleVersionStep(),
            new KotlinTemplateStep(),
            new RemoveToolStep()
        };

        /// <summary>
        /// The run this project gets. Chosen in <see cref="RefreshAll"/> from whether the GD
        /// SDK is present, so dropping the SDK in or taking it out switches the run on the
        /// next check — and never mid-repaint, which would renumber the list under the
        /// drawing code.
        /// </summary>
        private MeticaStep[] _steps = GDSdkSteps;

        private static readonly int MaxSteps = Mathf.Max(GDSdkSteps.Length, StandaloneSteps.Length);

        private readonly VerifyResult[] _results = new VerifyResult[MaxSteps];
        private readonly bool[] _expanded = new bool[MaxSteps];

        private Vector2 _scroll;
        private bool _showLog;

        /// <summary>
        /// Work requested by a button. Steps open dialogs, import packages and trigger
        /// reimports, none of which is safe inside OnGUI, so the press only records what to
        /// do and the work runs on the next editor tick.
        /// </summary>
        private int _pendingApply = -1;
        private bool _pendingRefresh;

        [MenuItem("GameDistrict/Metica/Ads Integration...", false, 10)]
        public static void Open()
        {
            var window = GetWindow<MeticaIntegrationWindow>("Metica Integration");
            window.minSize = new Vector2(520, 480);
            window.RefreshAll();
            window.ExpandCurrent();
        }

        private void OnEnable()
        {
            AssemblyReloadEvents.afterAssemblyReload += RefreshAll;
            RefreshAll();
            ExpandCurrent();
        }

        private void OnDisable() => AssemblyReloadEvents.afterAssemblyReload -= RefreshAll;

        private void OnFocus() => QueueRefresh();

        /// <summary>Queues a re-check for the next editor tick, outside the GUI pass.</summary>
        private void QueueRefresh()
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
        private void QueueApply(int index)
        {
            if (_pendingApply >= 0) return;

            _pendingApply = index;
            var step = _steps[index];

            EditorApplication.delayCall += () =>
            {
                _pendingApply = -1;

                try
                {
                    step.Apply();
                    AssetDatabase.SaveAssets();
                }
                catch (System.Exception e)
                {
                    MeticaIntegrationLog.Record(step.Title, $"Failed: {e.Message}");
                }

                RefreshAll();
                ExpandCurrent();
            };
        }

        private void RefreshAll()
        {
            MeticaPaths.ForgetCache();
            _steps = MeticaPaths.HasGDSdk ? GDSdkSteps : StandaloneSteps;

            for (var i = 0; i < _steps.Length; i++)
            {
                try
                {
                    _results[i] = _steps[i].Verify();
                }
                catch (System.Exception e)
                {
                    _results[i] = VerifyResult.Fail($"Verification threw: {e.Message}");
                }

                // If a step stops verifying — someone reverted, or a later step broke it —
                // the earlier sign-off no longer means anything.
                if (!_results[i].Ok) MeticaIntegrationProgress.ClearReviewed(_steps[i].Id);
            }

            Repaint();
        }

        /// <summary>Opens the first step that still needs work and collapses the rest.</summary>
        private void ExpandCurrent()
        {
            var current = CurrentStepIndex();
            for (var i = 0; i < _expanded.Length; i++) _expanded[i] = i == current;

            Repaint();
        }

        /// <summary>
        /// A step is finished only when it verifies AND you have signed it off, so the run
        /// stops after every step and waits while you read the diff.
        /// </summary>
        private bool IsComplete(int index) =>
            IsSkipped(index)
            || (_results[index] != null
                && _results[index].Ok
                && MeticaIntegrationProgress.IsReviewed(_steps[index].Id));

        /// <summary>A step passed over on purpose. Only optional steps can be in this state.</summary>
        private bool IsSkipped(int index) =>
            _steps[index].Optional && MeticaIntegrationProgress.IsSkipped(_steps[index].Id);

        /// <summary>Index of the first unfinished step, or _steps.Length when done.</summary>
        private int CurrentStepIndex()
        {
            for (var i = 0; i < _steps.Length; i++)
                if (!IsComplete(i)) return i;

            return _steps.Length;
        }

        private void OnGUI()
        {
            var current = CurrentStepIndex();

            DrawHeader(current);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _steps.Length; i++) DrawStep(i, current);
            EditorGUILayout.EndScrollView();

            DrawLog();
        }

        private void DrawHeader(int current)
        {
            EditorGUILayout.Space(6);
            var standalone = !ReferenceEquals(_steps, GDSdkSteps);

            EditorGUILayout.LabelField(
                standalone ? "Metica ads — standalone install" : "Metica integration for GDSDK v5",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                (standalone
                    ? "No GD Monetization SDK here, so Metica is installed on its own: a self-contained " +
                      "ads runtime your game drives through MeticaAdsManager.\n"
                    : "Adds Metica for ads (Smart Floors through MAX). Ads only — nothing here touches " +
                      "analytics.\n") +
                "Each step stops once it verifies and waits for you to sign it off, so you can read the " +
                "diff before the next one runs. A step you have finished or skipped stays open — expand " +
                "it any time to see where it stands now and run it again.",
                EditorStyles.wordWrappedMiniLabel);

            var skipped = Enumerable.Range(0, _steps.Length).Count(IsSkipped);
            var fraction = (float)current / _steps.Length;
            var rect = EditorGUILayout.GetControlRect(false, 18);
            EditorGUI.ProgressBar(rect, fraction,
                current >= _steps.Length
                    ? skipped == 0
                        ? "All steps verified"
                        : $"All steps done — {skipped} skipped, not verified"
                    : $"Step {current + 1} of {_steps.Length} — {_steps[current].Title}");

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
                MeticaIntegrationProgress.ClearAll(
                    GDSdkSteps.Concat(StandaloneSteps).Select(step => step.Id).Distinct().ToArray());
                QueueRefresh();
            }
            EditorGUILayout.EndHorizontal();

            if (current >= _steps.Length)
            {
                var names = Enumerable.Range(0, _steps.Length)
                    .Where(IsSkipped)
                    .Select(i => $"{i + 1}. {_steps[i].Title}")
                    .ToArray();

                EditorGUILayout.HelpBox(
                    names.Length == 0
                        ? "Every step verifies. Build to a device and confirm the Metica logs before shipping."
                        : "Build to a device and confirm the Metica logs before shipping.\n\n" +
                          "Skipped, so never verified — come back to these if the build complains:\n" +
                          string.Join("\n", names),
                    MessageType.Info);
            }

            DrawModeSelector();

            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Only shown to a project that already has the async plumbing. Anywhere else there
        /// is no decision to make — callback is simply how the integration gets built.
        /// </summary>
        private void DrawModeSelector()
        {
            if (!MeticaIntegrationMode.ProjectHasAsyncPath) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("This project already initializes Metica through Tasks",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "IAsyncAdNetworkService only existed because Metica shipped InitializeAsync alone at " +
                "first. Its callback API removed that need, so converting is the better end state — but " +
                "it touches Ads/Core, so it is your call. Everything else the tool does is the same " +
                "either way.",
                EditorStyles.wordWrappedMiniLabel);

            var mode = (InitMode)EditorGUILayout.EnumPopup("Existing async", MeticaIntegrationMode.Current);
            if (mode != MeticaIntegrationMode.Current)
            {
                MeticaIntegrationMode.Current = mode;

                // Only the async cleanup step changes, but a sign-off on it no longer describes what it will
                // do, so the simplest correct thing is to ask for the run to be reviewed again.
                MeticaIntegrationProgress.ClearAll(
                    GDSdkSteps.Concat(StandaloneSteps).Select(step => step.Id).Distinct().ToArray());
                QueueRefresh();
            }

            EditorGUILayout.LabelField(
                MeticaIntegrationMode.IsCallback
                    ? "Callback — the async cleanup step removes IAsyncAdNetworkService and the AdNetworkController "
                      + "overload built for it."
                    : "Async — the async cleanup step leaves the async plumbing alone.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawStep(int index, int current)
        {
            var step = _steps[index];
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
