using System;
using System.Text.RegularExpressions;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The GD Monetization SDK version this project has, and whether the tool supports it.
    ///
    /// <para>5.3.0 is the floor. Before it, the ad-unit base classes the Metica wrapper files
    /// build on are different (no interstitial close callback, no banner reposition, no
    /// MRecPosition), so the wrappers do not compile — checked by compiling each v5 release
    /// with the tool's changes in Unity.</para>
    /// </summary>
    internal static class GdSdkVersion
    {
        public static readonly Version Minimum = new Version(5, 3, 0);

        private static readonly Regex Declared = new Regex("Version\\s*=\\s*\"([^\"]+)\"");
        private static readonly Regex Numeric = new Regex("^[0-9]+(?:\\.[0-9]+)*");

        /// <summary>The Version string MonetizationInitializeOnLoad reports, or null.</summary>
        public static string Reported()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.InitializeOnLoad)) return null;

            var match = Declared.Match(SourcePatcher.ReadAll(MeticaPaths.InitializeOnLoad));
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// False only when the version can be read and is below <see cref="Minimum"/>. An
        /// unreadable version is not blocked — the tool cannot tell, and the modified-file
        /// check asks for it instead.
        /// </summary>
        public static bool IsSupported(string reported)
        {
            var version = Parse(reported);
            return version == null || version >= Minimum;
        }

        /// <summary>"5.3.1" → 5.3.1; "5.0.0-beta5" → 5.0.0; anything else → null.</summary>
        public static Version Parse(string reported)
        {
            if (string.IsNullOrEmpty(reported)) return null;

            var match = Numeric.Match(reported);
            if (!match.Success) return null;

            var text = match.Value.Contains(".") ? match.Value : match.Value + ".0";
            return Version.TryParse(text, out var version) ? version : null;
        }
    }
}
