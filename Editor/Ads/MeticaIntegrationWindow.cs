using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Step-by-step Metica ads integration, standalone or into GD Monetization SDK v5 or
    /// older. The generic engine — verify, sign off, review gate, log — lives in
    /// <see cref="MeticaStepWizardWindow"/>; this class is the Ads-specific step lists and
    /// header.
    /// </summary>
    public sealed class MeticaIntegrationWindow : MeticaStepWizardWindow
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
            new KotlinTemplateStep()
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
            new KotlinTemplateStep()
        };

        /// <summary>
        /// The run this project gets. Chosen in <see cref="RefreshAll"/> from whether the GD
        /// SDK is present, so dropping the SDK in or taking it out switches the run on the
        /// next check — and never mid-repaint, which would renumber the list under the
        /// drawing code.
        /// </summary>
        private MeticaStep[] _steps = GDSdkSteps;

        protected override MeticaStep[] Steps => _steps;

        [MenuItem("GameDistrict/Metica/Ads Integration...", false, 10)]
        public static void Open()
        {
            var window = GetWindow<MeticaIntegrationWindow>("Metica Integration");
            window.minSize = new Vector2(520, 480);
            window.RefreshAll();
            window.ExpandCurrent();
        }

        protected override void RefreshAll()
        {
            _steps = MeticaPaths.HasGDSdk ? GDSdkSteps : StandaloneSteps;
            base.RefreshAll();
        }

        protected override void DrawHeader(int current)
        {
            EditorGUILayout.Space(6);
            var standalone = !ReferenceEquals(_steps, GDSdkSteps);

            EditorGUILayout.LabelField(
                standalone ? "Metica ads — standalone" : "Metica ads — GD Monetization SDK",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Run each step, check its diff, sign it off.",
                EditorStyles.wordWrappedMiniLabel);

            DrawProgressAndControls(current, GDSdkSteps.Concat(StandaloneSteps).Select(step => step.Id));

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

            var mode = (InitMode)EditorGUILayout.EnumPopup("Existing async init", MeticaIntegrationMode.Current);
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
                    ? "Callback: the async cleanup step removes it."
                    : "Async: the async cleanup step leaves it alone.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }
    }
}
