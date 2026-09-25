using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    public enum StepState
    {
        /// <summary>Verified and signed off.</summary>
        Done,

        /// <summary>The first unfinished step, not verifying yet.</summary>
        Current,

        /// <summary>The first unfinished step, verifying and waiting for "Reviewed".</summary>
        Review,

        /// <summary>An optional step passed over on purpose.</summary>
        Skipped,

        /// <summary>After the current step; opens once everything before it is done.</summary>
        Locked
    }

    /// <summary>
    /// One wizard run — Ads Integration or Genre Creator: its steps, what the project says
    /// about each, and the sign-offs. No UI; the window draws it.
    ///
    /// <para>Stored progress is never trusted: <see cref="Refresh"/> re-verifies every step
    /// against the project, and a step is done only when it verifies AND has been signed off.
    /// A step that stops verifying loses its sign-off. So it is safe to close the window, let
    /// Unity recompile, or come back tomorrow.</para>
    /// </summary>
    internal sealed class MeticaFlow
    {
        private readonly Func<MeticaStep[]> _steps;
        private readonly Func<IEnumerable<string>> _allStepIds;
        private VerifyResult[] _results = Array.Empty<VerifyResult>();

        /// <param name="steps">
        /// The steps this run shows, read on every refresh — Ads Integration switches lists
        /// depending on whether the GD SDK is in the project.
        /// </param>
        /// <param name="allStepIds">
        /// Every step id any of its lists can show, so Reset sign-offs clears them all.
        /// </param>
        public MeticaFlow(string name, Func<MeticaStep[]> steps, Func<IEnumerable<string>> allStepIds)
        {
            Name = name;
            _steps = steps;
            _allStepIds = allStepIds;
        }

        public string Name { get; }

        public MeticaStep[] Steps { get; private set; } = Array.Empty<MeticaStep>();

        public void Refresh()
        {
            Steps = _steps();
            _results = new VerifyResult[Steps.Length];

            for (var i = 0; i < Steps.Length; i++)
            {
                try
                {
                    _results[i] = Steps[i].Verify();
                }
                catch (Exception e)
                {
                    _results[i] = VerifyResult.Fail($"Verification threw: {e.Message}");
                }

                // A step that no longer verifies — someone reverted, or a later step broke
                // it — cannot keep a sign-off that described a different state.
                if (!_results[i].Ok) MeticaIntegrationProgress.ClearReviewed(Steps[i].Id);
            }
        }

        public VerifyResult Result(int index) => _results[index];

        public bool IsVerified(int index) => _results[index] != null && _results[index].Ok;

        public bool IsReviewed(int index) => MeticaIntegrationProgress.IsReviewed(Steps[index].Id);

        public bool IsSkipped(int index) =>
            Steps[index].Optional && MeticaIntegrationProgress.IsSkipped(Steps[index].Id);

        public bool IsComplete(int index) => IsSkipped(index) || (IsVerified(index) && IsReviewed(index));

        /// <summary>The first unfinished step, or <see cref="Steps"/>.Length when all are.</summary>
        public int CurrentIndex
        {
            get
            {
                for (var i = 0; i < Steps.Length; i++)
                    if (!IsComplete(i)) return i;
                return Steps.Length;
            }
        }

        public bool Finished => CurrentIndex >= Steps.Length;

        public int SkippedCount => Enumerable.Range(0, Steps.Length).Count(IsSkipped);

        public int DoneCount => Enumerable.Range(0, Steps.Length).Count(i => !IsSkipped(i) && IsComplete(i));

        /// <summary>Anything signed off or skipped yet.</summary>
        public bool Started => Enumerable.Range(0, Steps.Length).Any(i => IsReviewed(i) || IsSkipped(i));

        public StepState StateOf(int index)
        {
            if (IsSkipped(index)) return StepState.Skipped;
            if (IsComplete(index)) return StepState.Done;

            var current = CurrentIndex;
            if (index == current) return IsVerified(index) ? StepState.Review : StepState.Current;
            return StepState.Locked;
        }

        /// <summary>
        /// Runs a step's action. Steps open dialogs, import packages and trigger reimports,
        /// none of which is safe inside a UI callback, so call this from a delayed callback.
        /// </summary>
        public void Apply(int index)
        {
            var step = Steps[index];
            try
            {
                step.Apply();
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                MeticaIntegrationLog.Record(step.Title, $"Failed: {e.Message}");
            }
        }

        public void MarkReviewed(int index) => MeticaIntegrationProgress.MarkReviewed(Steps[index].Id);

        public void Skip(int index) => MeticaIntegrationProgress.MarkSkipped(Steps[index].Id);

        public void Unskip(int index) => MeticaIntegrationProgress.ClearSkipped(Steps[index].Id);

        public void ResetSignOffs() => MeticaIntegrationProgress.ClearAll(_allStepIds().Distinct().ToArray());

        public int IndexOf(string stepId) => Array.FindIndex(Steps, s => s.Id == stepId);
    }
}
