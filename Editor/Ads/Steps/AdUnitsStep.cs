using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Fills the Metica section of AdUnitsSettings by copying the Applovin section.
    ///
    /// <para>That is not a shortcut: Metica mediates through MAX, so its App Key <em>is</em>
    /// the AppLovin MAX SDK key and its ad unit IDs <em>are</em> the MAX ad unit IDs. The
    /// values are already in the project, one field across.</para>
    ///
    /// <para>Reads through SerializedProperty so it works with both the v5 single-key layout
    /// (AppKey / AdUnitId) and the v6 per-platform layout (AndroidAppKey / iOSAppKey,
    /// Android / iOS).</para>
    /// </summary>
    public sealed class AdUnitsStep : MeticaStep
    {
        /// <summary>AdFormat values Metica serves. AppOpen (2) is deliberately absent.</summary>
        private static readonly (int value, string name)[] RequiredFormats =
        {
            (0, "Interstitial"),
            (1, "Rewarded"),
            (3, "Banner"),
            (4, "MRec")
        };

        public override string Title => "Metica ad units";

        public override string Summary =>
            "Copies the Applovin App Key and ad unit IDs into the Metica section of AdUnitsSettings. " +
            "Metica runs through MAX, so those are exactly the values it needs.";

        public override string ActionLabel => "Copy the Applovin section into Metica";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.AdUnitsSettingsAsset };

        public override string ReviewHint =>
            "The Metica block should now hold the same App Key and the same four ad unit IDs as the " +
            "Applovin block above it. Nothing else in the asset should have moved.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var asset = LoadAsset();
            if (asset == null)
            {
                result.Problem($"Could not load {MeticaPaths.AdUnitsSettingsAsset}");
                return result.Seal();
            }

            var serialized = new SerializedObject(asset);
            var metica = serialized.FindProperty("Metica");
            if (metica == null)
            {
                result.Problem("AdUnitsSettings has no Metica section. Run the patch step and let Unity recompile — " +
                               "the field is added to AdUnitsConfiguration there.");
                return result.Seal();
            }

            foreach (var (label, value) in ReadAppKeys(metica))
                if (string.IsNullOrWhiteSpace(value))
                    result.Problem($"Metica {label} is empty.");

            var units = metica.FindPropertyRelative("AdUnitsInfo");
            if (units == null)
            {
                result.Problem("Metica section has no AdUnitsInfo list.");
                return result.Seal();
            }

            foreach (var (formatValue, formatName) in RequiredFormats)
            {
                var entry = FindEntry(units, formatValue);
                if (entry == null)
                {
                    result.Problem($"No Metica ad unit configured for {formatName}.");
                    continue;
                }

                foreach (var (label, id) in ReadAdUnitIds(entry))
                    if (string.IsNullOrWhiteSpace(id))
                        result.Problem($"Metica {formatName} {label} is empty.");
            }

            if (result.Problems.Count == 0)
                result.Note("App Key and all four ad units set");

            result.Note("Metica has no App-Open unit — App-Open ads keep coming from Admob.");
            return result.Seal();
        }

        public override void Apply()
        {
            var asset = LoadAsset();
            if (asset == null) return;

            var type = asset.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var source = type.GetField("Applovin", flags);
            var target = type.GetField("Metica", flags);

            if (source == null || target == null)
            {
                MeticaIntegrationLog.Record(Title,
                    "Could not find both an Applovin and a Metica field on AdUnitsConfiguration — fill the " +
                    "Metica section in by hand.");
                return;
            }

            var value = source.GetValue(asset);

            // AdNetworkInfo holds its ad units by reference. Clone the array, or editing
            // Metica's units would silently edit Applovin's too.
            var unitsField = value.GetType().GetField("AdUnitsInfo", flags);
            if (unitsField?.GetValue(value) is Array units)
                unitsField.SetValue(value, units.Clone());

            target.SetValue(asset, value);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            MeticaIntegrationLog.Record(Title,
                "Copied the Applovin App Key and ad units into the Metica section. Metica does not serve " +
                "App-Open, so that entry — if there was one — is simply ignored.");
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "Metica's App Key is the AppLovin MAX SDK key and its ad unit IDs are the MAX ad unit IDs, " +
                "so copying the Applovin section is exactly right rather than an approximation. Review the " +
                "values afterwards.\n\n" +
                "The per-game Metica API Key and App ID are a different thing and belong in MeticaSettings " +
                "(the Metica settings asset step) — the tool does not touch those.",
                MessageType.Info);

            if (MeticaPaths.FileExists(MeticaPaths.AdUnitsSettingsAsset)
                && GUILayout.Button("Select AdUnitsSettings in the Project window"))
            {
                Selection.activeObject = LoadAsset();
            }
        }

        // ── Reading ────────────────────────────────────────────────────────────

        private static ScriptableObject LoadAsset() =>
            MeticaPaths.AdUnitsSettingsAsset == null
                ? null
                : AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.AdUnitsSettingsAsset);

        /// <summary>App key fields, whichever layout this SDK version uses.</summary>
        private static IEnumerable<(string label, string value)> ReadAppKeys(SerializedProperty network)
        {
            var single = network.FindPropertyRelative("AppKey");
            if (single != null)
            {
                yield return ("App Key", single.stringValue);
                yield break;
            }

            var android = network.FindPropertyRelative("AndroidAppKey");
            var ios = network.FindPropertyRelative("iOSAppKey");
            if (android != null) yield return ("Android App Key", android.stringValue);
            if (ios != null) yield return ("iOS App Key", ios.stringValue);
        }

        /// <summary>Ad unit id fields, whichever layout this SDK version uses.</summary>
        private static IEnumerable<(string label, string value)> ReadAdUnitIds(SerializedProperty entry)
        {
            var single = entry.FindPropertyRelative("AdUnitId");
            if (single != null)
            {
                yield return ("ad unit ID", single.stringValue);
                yield break;
            }

            var android = entry.FindPropertyRelative("Android");
            var ios = entry.FindPropertyRelative("iOS");
            if (android != null) yield return ("Android ad unit ID", android.stringValue);
            if (ios != null) yield return ("iOS ad unit ID", ios.stringValue);
        }

        private static SerializedProperty FindEntry(SerializedProperty units, int adFormatValue)
        {
            for (var i = 0; i < units.arraySize; i++)
            {
                var entry = units.GetArrayElementAtIndex(i);
                var type = entry.FindPropertyRelative("AdType");
                if (type != null && type.enumValueIndex == adFormatValue) return entry;
            }

            return null;
        }
    }
}
