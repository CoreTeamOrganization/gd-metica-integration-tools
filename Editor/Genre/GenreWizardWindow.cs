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

            EditorGUILayout.LabelField("Metica Genre Creator setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Gets Metica's Android toolchain in place, then opens the Genre Creator to author " +
                "analytics genre files. Requires the Metica Unity SDK already installed — this wizard " +
                "does not install it.\n" +
                "Each step stops once it verifies and waits for you to sign it off, so you can read the " +
                "diff before the next one runs. A step you have finished or skipped stays open — expand " +
                "it any time to see where it stands now and run it again.",
                EditorStyles.wordWrappedMiniLabel);

            DrawProgressAndControls(current, GenreSteps.Select(step => step.Id));

            EditorGUILayout.Space(4);
        }
    }
}
