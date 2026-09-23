using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>What to do about a project that already initializes Metica through Tasks.</summary>
    public enum InitMode
    {
        /// <summary>
        /// Convert to Metica's callback API: AdNetworkMetica becomes a plain
        /// IAdNetworkService and the async plumbing comes out.
        /// </summary>
        Callback,

        /// <summary>Leave the existing async plumbing exactly as it is.</summary>
        Async
    }

    /// <summary>
    /// What to do about async plumbing a project already has.
    ///
    /// <para>The tool only ever writes the callback wiring — nothing needs async
    /// <em>added</em>. A v5 project has no Metica and gets callback; a v6 project already
    /// has async and only needs the remote switch. So the single question, asked only where
    /// async exists, is whether to convert it.</para>
    ///
    /// <para>Converting is the better end state — <c>IAsyncAdNetworkService</c> only existed
    /// because Metica shipped <c>InitializeAsync</c> alone at first — but it touches
    /// Ads/Core, so it stays a choice.</para>
    /// </summary>
    public static class MeticaIntegrationMode
    {
        private const string Key = "GameDistrict.MeticaIntegrationTools.InitMode";

        private static string ProjectKey => $"{Key}.{Application.dataPath.GetHashCode():X8}";

        public static InitMode Current
        {
            get => (InitMode)EditorPrefs.GetInt(ProjectKey, (int)InitMode.Callback);
            set => EditorPrefs.SetInt(ProjectKey, (int)value);
        }

        /// <summary>
        /// True when the project already carries the async plumbing. Only then is there a
        /// decision to make — otherwise callback is simply how it gets built.
        /// </summary>
        public static bool ProjectHasAsyncPath =>
            SourcePatcher.Contains(MeticaPaths.IAdNetworkService, "IAsyncAdNetworkService")
            || SourcePatcher.Contains(MeticaPaths.AdNetworkController, "asyncAdNetwork");

        public static bool IsCallback => Current == InitMode.Callback;
    }
}
