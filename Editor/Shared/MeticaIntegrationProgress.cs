using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Remembers which steps you have reviewed and signed off.
    ///
    /// <para>Verification alone says a step's changes are present; it cannot say you have
    /// looked at them. So a step unlocks the next only once it both verifies AND you
    /// acknowledge it, which is what makes it possible to stop after every step and read the
    /// diff before anything else moves.</para>
    ///
    /// <para>Stored in EditorPrefs, keyed per project, so it survives a domain reload and
    /// closing the window. It is a review record, not a substitute for verification — the
    /// project is still re-read every time.</para>
    /// </summary>
    public static class MeticaIntegrationProgress
    {
        private const string Prefix = "GameDistrict.MeticaIntegrationTools.Reviewed";
        private const string SkipPrefix = "GameDistrict.MeticaIntegrationTools.Skipped";
        private const string CommitPrefix = "GameDistrict.MeticaIntegrationTools.Committed";

        private static string Key(string stepId) => $"{Prefix}.{ProjectId}.{stepId}";

        private static string SkipKey(string stepId) => $"{SkipPrefix}.{ProjectId}.{stepId}";

        private static string CommitKey(string stepId) => $"{CommitPrefix}.{ProjectId}.{stepId}";

        private static string ProjectId => Application.dataPath.GetHashCode().ToString("X8");

        public static bool IsReviewed(string stepId) => EditorPrefs.GetBool(Key(stepId), false);

        public static void MarkReviewed(string stepId) => EditorPrefs.SetBool(Key(stepId), true);

        public static void ClearReviewed(string stepId) => EditorPrefs.DeleteKey(Key(stepId));

        /// <summary>
        /// Whether an optional step was deliberately passed over. Unlike a sign-off this is
        /// not cleared when the step fails to verify — a skipped step is expected to fail,
        /// that is what skipping it means.
        /// </summary>
        public static bool IsSkipped(string stepId) => EditorPrefs.GetBool(SkipKey(stepId), false);

        public static void MarkSkipped(string stepId) => EditorPrefs.SetBool(SkipKey(stepId), true);

        public static void ClearSkipped(string stepId) => EditorPrefs.DeleteKey(SkipKey(stepId));

        /// <summary>
        /// The commit a step's changes went into, from the window's commit section; null if
        /// none. Once set the section is not offered again for that step. Not cleared when the
        /// step stops verifying — the commit still exists.
        /// </summary>
        public static string CommittedAs(string stepId)
        {
            var sha = EditorPrefs.GetString(CommitKey(stepId), string.Empty);
            return sha.Length == 0 ? null : sha;
        }

        public static void MarkCommitted(string stepId, string sha) => EditorPrefs.SetString(CommitKey(stepId), sha);

        public static void ClearCommitted(string stepId) => EditorPrefs.DeleteKey(CommitKey(stepId));

        public static void ClearAll(params string[] stepIds)
        {
            foreach (var stepId in stepIds)
            {
                ClearReviewed(stepId);
                ClearSkipped(stepId);
                ClearCommitted(stepId);
            }
        }
    }
}
