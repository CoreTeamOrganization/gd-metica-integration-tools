using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Version floors Metica publishes for the mediation stack it drives
    /// (https://docs.metica.com/api/unity-sdk/unity-sdk-2):
    ///
    ///   • AppLovin MAX Unity plugin — 8.1.0 or newer.
    ///   • Moloco SDK and its adapters — 4.3.1 or newer, both platforms. Older Moloco builds
    ///     cause dependency-resolution failures next to Metica's mediation setup. Checked and
    ///     fixed per platform even though the floor is currently the same on both, since
    ///     Metica's docs have published different floors per platform before.
    ///
    /// <para>The MAX floor is the one Metica's docs publish today, and it moves with the
    /// Metica SDK — an older Metica release can pair with an older MAX than this one lists.
    /// The tool cannot know which pairing you are on, so a MAX below the floor is reported
    /// but can be accepted: tick the box and it becomes a note. Nothing else about the step
    /// changes, and the acknowledgement is remembered per project and per MAX version, so
    /// upgrading or downgrading MAX asks again.</para>
    ///
    /// <para>MAX itself is upgraded through AppLovin's Integration Manager, which downloads
    /// binaries that match the versions it declares — this step never edits it. Moloco is
    /// different: its floor can be fixed directly by rewriting the declared version in
    /// Dependencies.xml. That only changes what gets asked for, not what is on disk, so
    /// Resolve libraries still has to run afterwards to fetch the new artifact.</para>
    ///
    /// <para>Only the active build target's Moloco floor blocks moving on — the other
    /// platform's is reported but not enforced, since you are not building it right now. The
    /// MAX floor blocks regardless of platform, since it is one Unity package either way.</para>
    /// </summary>
    public sealed class DependenciesStep : MeticaStep
    {
        private static readonly Version MinMaxPlugin = new Version(8, 1, 0);
        private static readonly Version MinMolocoAndroid = new Version(4, 3, 1);
        private static readonly Version MinMolocoIos = new Version(4, 3, 1);

        private static readonly Regex AndroidMolocoVersion =
            new Regex("moloco-adapter:([0-9]+(?:\\.[0-9]+)*)", RegexOptions.IgnoreCase);

        private static readonly Regex IosMolocoVersion =
            new Regex("MolocoAdapter\"\\s+version\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)\"", RegexOptions.IgnoreCase);

        /// <summary>What the last check read, so the body can offer to accept exactly that.</summary>
        private static Version InstalledMax;

        /// <summary>Whether the last check found each platform below its Moloco floor.</summary>
        private static bool AndroidMolocoBelowFloor;
        private static bool IosMolocoBelowFloor;

        private const string DocsUrl = "https://docs.metica.com/api/unity-sdk/unity-sdk-2";

        /// <summary>Keyed on the MAX version too, so changing MAX re-asks.</summary>
        private static string AcceptedKey(Version max) =>
            $"GameDistrict.MeticaIntegrationTools.MaxFloorAccepted.{Application.dataPath.GetHashCode():X8}.{max}";

        public override string Title => "Dependencies and Moloco version";

        public override string Summary =>
            "Checks the mediation versions Metica requires: AppLovin MAX Unity plugin 8.1.0+, Moloco " +
            "4.3.1+ on both platforms. Only the active build target's Moloco floor blocks moving on. " +
            "The MAX floor moves with the Metica SDK, so a lower MAX can be accepted.";

        public override string ActionLabel => "Open AppLovin Integration Manager";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.MolocoDependencies };

        public override string ReviewHint =>
            "MAX is read-only here. If you used a Fix Moloco button, check Dependencies.xml gained the " +
            "new version, then re-run Resolve libraries so the artifact itself is fetched.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            CheckMaxPlugin(result);
            CheckMoloco(result);

            result.Note($"Source for these floors: {DocsUrl}");
            return result.Seal();
        }

        public override void Apply()
        {
            // AppLovin has moved this menu around; try the known paths in turn.
            var opened = new[]
                {
                    "AppLovin/Integration Manager",
                    "AppLovin/Integration Manager...",
                    "AppLovin/MAX/Integration Manager"
                }
                .Any(EditorApplication.ExecuteMenuItem);

            if (opened)
            {
                MeticaIntegrationLog.Record(Title, "Opened the AppLovin Integration Manager");
                return;
            }

            MeticaIntegrationLog.Record(Title,
                "Could not open the AppLovin Integration Manager from here. Open it from the AppLovin " +
                "menu to upgrade MAX, then re-run Resolve libraries.");
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "AppLovin MAX is upgraded through the Integration Manager, not by editing Dependencies.xml " +
                "— the manager downloads binaries that match the versions it declares.\n\n" +
                "Moloco can be fixed directly: the buttons below only rewrite the declared version in " +
                "Dependencies.xml as the floor version with a .0 adapter revision — confirm that build " +
                "actually exists once you resolve. After either, re-run Resolve libraries so the new " +
                "artifact is actually fetched.",
                MessageType.Info);

            if (GUILayout.Button("Open Metica's requirements page"))
                Application.OpenURL(DocsUrl);

            DrawMaxFloorOverride();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Moloco", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!AndroidMolocoBelowFloor))
            {
                // Writes a file and triggers a reimport, so it runs on the next editor tick
                // rather than inside OnGUI — same reasoning as the window's own QueueApply.
                if (GUILayout.Button($"Fix Android Moloco → {MinMolocoAndroid}.0"))
                    EditorApplication.delayCall += () =>
                        FixMolocoVersion("Android", AndroidMolocoVersion, MinMolocoAndroid);
            }

            using (new EditorGUI.DisabledScope(!IosMolocoBelowFloor))
            {
                if (GUILayout.Button($"Fix iOS Moloco → {MinMolocoIos}.0"))
                    EditorApplication.delayCall += () =>
                        FixMolocoVersion("iOS", IosMolocoVersion, MinMolocoIos);
            }

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Select Dependencies.xml"))
                PingMolocoDependencies();
        }

        /// <summary>
        /// Offered only when MAX is below the published floor. The floor comes from one page
        /// that describes one Metica version; an older Metica pairs with an older MAX, and the
        /// developer is the one who can check that pairing.
        /// </summary>
        private static void DrawMaxFloorOverride()
        {
            if (InstalledMax == null || InstalledMax >= MinMaxPlugin) return;

            var key = AcceptedKey(InstalledMax);
            var accepted = EditorPrefs.GetBool(key, false);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"MAX {InstalledMax} is below the documented {MinMaxPlugin}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "That floor is what Metica's page lists for the current SDK, and it moves between Metica " +
                "releases — an older Metica version can support an older MAX than this one lists. Check " +
                "your Metica version's own requirements; if this MAX is supported there, tick this and " +
                "the step passes.",
                EditorStyles.wordWrappedMiniLabel);

            var now = EditorGUILayout.ToggleLeft(
                $"My Metica version supports MAX {InstalledMax}", accepted);

            if (now != accepted)
            {
                EditorPrefs.SetBool(key, now);
                MeticaIntegrationLog.Record("Dependencies and Moloco version",
                    now
                        ? $"Accepted AppLovin MAX {InstalledMax}, below the documented {MinMaxPlugin}"
                        : $"Withdrew acceptance of AppLovin MAX {InstalledMax}");
            }

            EditorGUILayout.EndVertical();
        }

        // ── AppLovin MAX Unity plugin ──────────────────────────────────────────

        private static void CheckMaxPlugin(VerifyResult result)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MaxSdkVersionFile))
            {
                result.Problem($"Could not read {MeticaPaths.MaxSdkVersionFile} to determine the " +
                               "AppLovin MAX plugin version.");
                return;
            }

            var text = SourcePatcher.ReadAll(MeticaPaths.MaxSdkVersionFile);
            var match = Regex.Match(text, "_version\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)*)\"");

            if (!match.Success || !TryParse(match.Groups[1].Value, out var version))
            {
                result.Problem("Could not parse the AppLovin MAX plugin version from MaxSdk.cs. " +
                               $"Confirm it is {MinMaxPlugin} or newer yourself.");
                return;
            }

            InstalledMax = version;

            if (version >= MinMaxPlugin)
            {
                result.Note($"AppLovin MAX Unity plugin {version} (minimum {MinMaxPlugin})");
                return;
            }

            if (EditorPrefs.GetBool(AcceptedKey(version), false))
            {
                result.Note($"AppLovin MAX Unity plugin {version} — below the {MinMaxPlugin} the docs " +
                            "list, accepted as supported by your Metica version.");
                return;
            }

            result.Problem($"AppLovin MAX Unity plugin is {version}, below the {MinMaxPlugin} Metica's " +
                           "docs list. Upgrade it in the Integration Manager — or, if your Metica " +
                           "version supports this one, tick the box below.");
        }

        // ── Moloco SDK and adapters ────────────────────────────────────────────

        private static void CheckMoloco(VerifyResult result)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MolocoDependencies))
            {
                AndroidMolocoBelowFloor = false;
                IosMolocoBelowFloor = false;
                result.Note("Moloco adapter is not installed — nothing to check. If you add it later it " +
                            $"must be {MinMolocoAndroid} or newer on Android, {MinMolocoIos} or newer on iOS.");
                return;
            }

            var xml = SourcePatcher.ReadAll(MeticaPaths.MolocoDependencies);
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;

            // Android: spec="com.applovin.mediation:moloco-adapter:4.5.0.0"
            AndroidMolocoBelowFloor = CheckMolocoVersion(result, "Android", MinMolocoAndroid,
                AndroidMolocoVersion.Match(xml), activeTarget == BuildTarget.Android);

            // iOS: <iosPod name="AppLovinMediationMolocoAdapter" version="4.5.0.0" />
            IosMolocoBelowFloor = CheckMolocoVersion(result, "iOS", MinMolocoIos,
                IosMolocoVersion.Match(xml), activeTarget == BuildTarget.iOS);
        }

        /// <summary>Reports the platform's Moloco version and returns whether it is below floor.</summary>
        private static bool CheckMolocoVersion(VerifyResult result, string platform, Version minimum,
            Match match, bool blocking)
        {
            if (!match.Success || !TryParse(match.Groups[1].Value, out var version))
            {
                result.Problem($"Could not read the {platform} Moloco adapter version from " +
                               $"{MeticaPaths.MolocoDependencies}. Confirm it is {minimum} or newer yourself.");
                return false;
            }

            // Adapter versions carry a fourth component for the adapter revision
            // (4.5.0.0 wraps Moloco SDK 4.5.0); the first three are the SDK version.
            var sdkVersion = new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

            if (sdkVersion >= minimum)
            {
                result.Note($"{platform} Moloco adapter {version} (minimum SDK {minimum})");
                return false;
            }

            var message = $"{platform} Moloco adapter is {version}, i.e. Moloco SDK {sdkVersion}. Metica " +
                          $"requires {minimum} or newer on {platform} — older Moloco builds cause " +
                          "dependency-resolution failures alongside Metica.";

            if (blocking)
                result.Problem(message + " Fix it with the Moloco button below, then re-run Resolve libraries.");
            else
                result.Note(message + $" Not blocking here — the active build target is not {platform}.");

            return true;
        }

        // ── Fixing Moloco's declared version ────────────────────────────────────

        /// <summary>
        /// Rewrites Dependencies.xml's declared Moloco version for one platform. Only the
        /// declaration changes — the actual artifact still has to be fetched by Resolve
        /// libraries, same as any other Dependencies.xml edit.
        /// </summary>
        private static void FixMolocoVersion(string platform, Regex pattern, Version minimum)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MolocoDependencies))
            {
                MeticaIntegrationLog.Record("Dependencies and Moloco version",
                    $"{MeticaPaths.MolocoDependencies} not found — nothing to fix.");
                return;
            }

            var text = SourcePatcher.ReadAll(MeticaPaths.MolocoDependencies);
            var match = pattern.Match(text);
            if (!match.Success)
            {
                MeticaIntegrationLog.Record("Dependencies and Moloco version",
                    $"Could not find the {platform} Moloco adapter version in " +
                    $"{MeticaPaths.MolocoDependencies}. Edit it by hand.");
                return;
            }

            var target = $"{minimum}.0";
            var group = match.Groups[1];

            if (group.Value == target)
            {
                MeticaIntegrationLog.Record("Dependencies and Moloco version",
                    $"{platform} Moloco adapter is already {target}");
                return;
            }

            var updated = text.Substring(0, group.Index) + target + text.Substring(group.Index + group.Length);
            SourcePatcher.WriteWithBackup(MeticaPaths.MolocoDependencies, updated);
            AssetDatabase.Refresh();

            MeticaIntegrationLog.Record("Dependencies and Moloco version",
                $"Set the {platform} Moloco adapter to {target} in Dependencies.xml. Re-run Resolve " +
                "libraries to fetch it — if that build does not exist on the registry, Resolve will say so.");
        }

        private static void PingMolocoDependencies()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MolocoDependencies))
            {
                MeticaIntegrationLog.Record("Dependencies and Moloco version",
                    $"{MeticaPaths.MolocoDependencies} does not exist yet.");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(MeticaPaths.MolocoDependencies);
            if (asset == null) return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static bool TryParse(string raw, out Version version)
        {
            // Version.TryParse rejects a bare "8", so pad to at least major.minor.
            if (!raw.Contains('.')) raw += ".0";
            return Version.TryParse(raw, out version);
        }
    }
}
