using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Step-by-step Genre Creator setup: the Android toolchain Metica's native SDK needs
    /// (shared with Ads Integration, so there is one place that owns
    /// baseProjectTemplate.gradle, not two), the scripting-define symbol, the optional
    /// Performance Tracker package, and the genre-authoring window itself. The generic
    /// engine — verify, sign off, review gate, log — lives in
    /// <see cref="MeticaStepWizardWindow"/>; this class is the Genre-specific step list and
    /// header.
    /// </summary>
    public sealed class GenreWizardWindow : MeticaStepWizardWindow
    {
        private static readonly MeticaStep[] GenreSteps =
        {
            new GradleVersionStep(),
            new GradleJdkStep(),
            new KotlinTemplateStep(),
            new PerformanceTrackerStep(),
            new GenreDefinitionStep()
        };

        protected override MeticaStep[] Steps => GenreSteps;

        [MenuItem("GameDistrict/Metica/Genre Creator...", false, 20)]
        public static void Open()
        {
            var window = GetWindow<GenreWizardWindow>("Genre Creator");
            window.minSize = new Vector2(520, 480);
            window.RefreshAll();
            window.ExpandCurrent();
        }

        protected override void DrawHeader(int current)
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("Metica Genre Creator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Needs the Metica SDK installed. Run each step, check its diff, sign it off.",
                EditorStyles.wordWrappedMiniLabel);

            DrawProgressAndControls(current, GenreSteps.Select(step => step.Id));

            EditorGUILayout.Space(4);
        }
    }
}
