using System.Text.RegularExpressions;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The Gradle property AppLovin MAX writes into gradle.properties at build time
    /// (AppLovinPostProcessAndroid.PropertyDexingArtifactTransform). Gradle 8 removed
    /// <c>android.enableDexingArtifactTransform</c>; the replacement is
    /// <c>android.useFullClasspathForDexingTransform</c>. MAX switched in 8.2.1, so 8.1.0 and
    /// 8.2.0 still write the old one and fail a Gradle 8 build.
    ///
    /// <para>This edits a file inside the MAX plugin, so re-importing MAX can undo it — the
    /// Gradle row shows it again if so.</para>
    /// </summary>
    internal static class MaxDexingProperty
    {
        public const string Required = "android.useFullClasspathForDexingTransform";

        public const string File = "Assets/MaxSdk/Scripts/IntegrationManager/Editor/AppLovinPostProcessAndroid.cs";

        private static readonly Regex Declared = new Regex("PropertyDexingArtifactTransform\\s*=\\s*\"([^\"]+)\"");

        /// <summary>The value MAX declares, or null when the file or the constant is not there.</summary>
        public static string Current()
        {
            if (!MeticaPaths.FileExists(File)) return null;
            var match = Declared.Match(SourcePatcher.ReadAll(File));
            return match.Success ? match.Groups[1].Value : null;
        }

        public static bool NeedsFix
        {
            get
            {
                var current = Current();
                return current != null && current != Required;
            }
        }

        public static void Fix()
        {
            var current = Current();
            if (current == null || current == Required) return;

            var outcome = SourcePatcher.ReplaceFirst(File, $"\"{Required}\"", $"\"{current}\"", $"\"{Required}\"");
            AssetDatabase.Refresh();

            MeticaIntegrationLog.Record("Dependencies & troubleshooting", SourcePatcher.IsSatisfied(outcome)
                ? $"Set MAX's dexing property to {Required} (was {current})"
                : $"Could not change the dexing property in {File} — set it to {Required} by hand.");
        }
    }
}
