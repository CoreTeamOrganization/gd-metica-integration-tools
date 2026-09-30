using System;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The Moloco adapter versions MAX declares in Moloco's Dependencies.xml, against the floor
    /// Metica publishes (https://docs.metica.com/api/unity-sdk/unity-sdk-2): 4.3.1 on both
    /// platforms. Older Moloco builds break dependency resolution next to Metica.
    ///
    /// <para>Only relevant to a project that already has Moloco — one without it has nothing
    /// to add. Fixing only rewrites the declared version; resolving then fetches it.</para>
    /// </summary>
    internal static class MolocoVersions
    {
        public const string DocsUrl = "https://docs.metica.com/api/unity-sdk/unity-sdk-2";

        public static readonly Version Minimum = new Version(4, 3, 1);

        // Android: spec="com.applovin.mediation:moloco-adapter:4.5.0.0"
        private static readonly Regex Android = new Regex("moloco-adapter:([0-9]+(?:\\.[0-9]+)*)", RegexOptions.IgnoreCase);

        // iOS: <iosPod name="AppLovinMediationMolocoAdapter" version="4.5.0.0" />
        private static readonly Regex Ios =
            new Regex("MolocoAdapter\"\\s+version\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)\"", RegexOptions.IgnoreCase);

        public static bool Installed => MeticaPaths.FileExists(MeticaPaths.MolocoDependencies);

        /// <summary>The declared adapter version, e.g. 4.3.1.0, or null.</summary>
        public static Version AndroidVersion => Read(Android);

        public static Version IosVersion => Read(Ios);

        /// <summary>
        /// Adapter versions carry a fourth part for the adapter revision (4.5.0.0 wraps Moloco
        /// SDK 4.5.0); the first three are the SDK version compared against the floor.
        /// </summary>
        public static bool BelowFloor(Version adapter) =>
            adapter != null && new Version(adapter.Major, adapter.Minor, Math.Max(adapter.Build, 0)) < Minimum;

        public static void FixAndroid() => Fix("Android", Android);

        public static void FixIos() => Fix("iOS", Ios);

        private static Version Read(Regex pattern)
        {
            if (!Installed) return null;

            var match = pattern.Match(SourcePatcher.ReadAll(MeticaPaths.MolocoDependencies));
            if (!match.Success) return null;

            var raw = match.Groups[1].Value.Contains(".") ? match.Groups[1].Value : match.Groups[1].Value + ".0";
            return Version.TryParse(raw, out var version) ? version : null;
        }

        private static void Fix(string platform, Regex pattern)
        {
            const string title = "Dependencies & troubleshooting";
            if (!Installed) return;

            var text = SourcePatcher.ReadAll(MeticaPaths.MolocoDependencies);
            var match = pattern.Match(text);
            if (!match.Success)
            {
                MeticaIntegrationLog.Record(title,
                    $"Could not find the {platform} Moloco adapter version in {MeticaPaths.MolocoDependencies}. Edit it by hand.");
                return;
            }

            var target = $"{Minimum}.0";
            var group = match.Groups[1];
            if (group.Value == target) return;

            SourcePatcher.WriteWithBackup(MeticaPaths.MolocoDependencies,
                text.Substring(0, group.Index) + target + text.Substring(group.Index + group.Length));
            AssetDatabase.Refresh();

            MeticaIntegrationLog.Record(title,
                $"Set the {platform} Moloco adapter to {target}. Press Resolve Android dependencies to fetch it.");
        }
    }
}
