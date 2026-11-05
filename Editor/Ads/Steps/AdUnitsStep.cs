using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Fills the Metica section of AdUnitsSettings by copying the Applovin section — the
    /// four formats Metica serves only. App-Open, and any other format a game added, stays
    /// out of it.
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

        /// <summary>The AdFormat names Metica serves — matched by name, so a game's own enum order doesn't matter.</summary>
        private static readonly HashSet<string> MeticaFormats = new HashSet<string>(RequiredFormats.Select(f => f.name));

        public override string Title => "Metica ad units";

        public override string Summary => "Copy the Applovin App Key and ad units into Metica's section.";

        public override string Why =>
            "Metica mediates through MAX, so its App Key is the AppLovin MAX SDK key and its ad unit IDs " +
            "are the MAX ad unit IDs — copying the Applovin section is exactly right. Metica has no " +
            "App-Open unit, so App-Open ads keep coming from Admob: only Interstitial, Rewarded, Banner and MRec are " +
            "copied — App-Open, or any other format the game added, is left out (and taken out if an earlier copy " +
            "put it there). The per-game Metica API Key and App ID " +
            "are different values and live in MeticaSettings.";

        public override string ActionLabel => "Copy the Applovin section into Metica";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.AdUnitsSettingsAsset };

        public override string ReviewHint =>
            "The Metica block should match the Applovin block, minus App-Open and anything but the four formats. Nothing else moves.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var asset = LoadAsset();
            if (asset == null)
            {
                result.Problem("AdUnitsSettings.asset not found.");
                return result.Seal();
            }

            var serialized = new SerializedObject(asset);
            var metica = serialized.FindProperty("Metica");
            if (metica == null)
            {
                // The field arrives with the patch step's compile.
                if (ScriptCompile.Pending(MeticaPaths.AdUnitsConfiguration)) result.Wait();
                else result.Problem("No Metica section yet — run the patch step first.");
                return result.Seal();
            }

            var units = metica.FindPropertyRelative("AdUnitsInfo");
            if (units == null)
            {
                result.Problem("Metica section has no AdUnitsInfo list.");
                return result.Seal();
            }

            var empty = new List<string>();

            foreach (var (label, value) in ReadAppKeys(metica))
                if (string.IsNullOrWhiteSpace(value))
                    empty.Add(label);

            foreach (var (formatValue, formatName) in RequiredFormats)
            {
                var entry = FindEntry(units, formatValue);
                if (entry == null)
                {
                    empty.Add($"{formatName} unit");
                    continue;
                }

                foreach (var (label, id) in ReadAdUnitIds(entry))
                    if (string.IsNullOrWhiteSpace(id))
                        empty.Add($"{formatName} {label}");
            }

            // Formats Metica doesn't serve — App-Open, from a copy made before they were left out.
            for (var i = 0; i < units.arraySize; i++)
            {
                var name = FormatName(units.GetArrayElementAtIndex(i));
                if (name != null && !MeticaFormats.Contains(name))
                    result.Problem($"Metica has a {name} unit — Metica doesn't serve {name}. Copy again to take it out.");
            }

            if (empty.Count == 0)
            {
                if (result.Problems.Count == 0) result.Note("App Key and 4 ad units set");
                return result.Seal();
            }

            result.Problem($"{empty.Count} Metica fields empty.");
            foreach (var label in empty)
                result.Problem($"Empty: {label}");

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

            // AdNetworkInfo holds its ad units by reference: a new array, or editing Metica's
            // units would silently edit Applovin's too. Only the formats Metica serves.
            var unitsField = value.GetType().GetField("AdUnitsInfo", flags);
            var skipped = new List<string>();
            if (unitsField?.GetValue(value) is Array units)
            {
                var elementType = units.GetType().GetElementType();
                var adType = elementType?.GetField("AdType", flags);
                var kept = new List<object>();
                foreach (var unit in units)
                {
                    var format = adType?.GetValue(unit)?.ToString();
                    if (format == null || MeticaFormats.Contains(format)) kept.Add(unit);
                    else skipped.Add(format);
                }

                var copy = Array.CreateInstance(elementType, kept.Count);
                for (var i = 0; i < kept.Count; i++) copy.SetValue(kept[i], i);
                unitsField.SetValue(value, copy);
            }

            target.SetValue(asset, value);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            MeticaIntegrationLog.Record(Title,
                "Copied the Applovin App Key and ad units into the Metica section" +
                (skipped.Count > 0
                    ? $" — left out {string.Join(", ", skipped.Distinct())}: Metica serves only Interstitial, Rewarded, Banner and MRec."
                    : "."));
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (MeticaPaths.FileExists(MeticaPaths.AdUnitsSettingsAsset))
                yield return new StepButton("Select AdUnitsSettings", () => SelectAndPing(LoadAsset()),
                    icon: StepIcon.File);
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

        /// <summary>The entry's AdType by name, or null when it can't be read.</summary>
        private static string FormatName(SerializedProperty entry)
        {
            var type = entry.FindPropertyRelative("AdType");
            if (type == null || type.propertyType != SerializedPropertyType.Enum) return null;
            var index = type.enumValueIndex;
            return index >= 0 && index < type.enumNames.Length ? type.enumNames[index] : null;
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
