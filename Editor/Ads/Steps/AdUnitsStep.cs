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
            "put it there). On GD SDK 6, which keeps an Android and an iOS value for each, only the build " +
            "target's platform is copied and checked; Metica's other-platform values are left as they are. " +
            "The per-game Metica API Key and App ID " +
            "are different values and live in MeticaSettings.";

        public override string ActionLabel => "Copy the Applovin section into Metica";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.AdUnitsSettingsAsset };

        public override string ReviewHint =>
            "The Metica block should match the Applovin block, minus App-Open and anything but the four formats " +
            "(GD SDK 6: the build target's platform only). Nothing else moves.";

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
            if (IsPerPlatform(metica)) result.Note($"Checked for {Platform} (the build target)");

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

            var value = source.GetValue(asset);   // AdNetworkInfo is a struct: a boxed copy
            var previous = target.GetValue(asset);

            // GD SDK 6 keeps an Android and an iOS value per field: only the build target's are
            // copied, Metica's other-platform values stay as they were.
            var other = OtherPlatform;
            var networkType = value.GetType();
            var otherKey = networkType.GetField(other + "AppKey", flags);
            var perPlatform = otherKey != null && networkType.GetField(Platform + "AppKey", flags) != null;
            if (perPlatform) otherKey.SetValue(value, otherKey.GetValue(previous));

            // AdNetworkInfo holds its ad units by reference: a new array, or editing Metica's
            // units would silently edit Applovin's too. Only the formats Metica serves.
            var unitsField = networkType.GetField("AdUnitsInfo", flags);
            var previousUnits = unitsField?.GetValue(previous) as Array;
            var skipped = new List<string>();
            if (unitsField?.GetValue(value) is Array units)
            {
                var elementType = units.GetType().GetElementType();
                var adType = elementType?.GetField("AdType", flags);
                var otherId = perPlatform ? elementType?.GetField(other, flags) : null;
                var kept = new List<object>();
                foreach (var unit in units)   // each a boxed copy of the AdUnitIdInfo struct
                {
                    var format = adType?.GetValue(unit)?.ToString();
                    if (format != null && !MeticaFormats.Contains(format))
                    {
                        skipped.Add(format);
                        continue;
                    }

                    if (otherId != null)
                        otherId.SetValue(unit, PreviousId(previousUnits, adType, otherId, format) ?? string.Empty);
                    kept.Add(unit);
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
                (perPlatform ? $" ({Platform} only — the build target)" : string.Empty) +
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

        /// <summary>
        /// The build target's platform, as GD SDK 6 names its per-platform fields: "iOS" on
        /// iOS, "Android" otherwise. Only its values are copied and checked.
        /// </summary>
        private static string Platform =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ? "iOS" : "Android";

        private static string OtherPlatform => Platform == "iOS" ? "Android" : "iOS";

        /// <summary>True for GD SDK 6's layout, an Android and an iOS value per field.</summary>
        private static bool IsPerPlatform(SerializedProperty network) =>
            network.FindPropertyRelative("AppKey") == null && network.FindPropertyRelative(Platform + "AppKey") != null;

        /// <summary>The other-platform id Metica had for this format before the copy; null when none.</summary>
        private static string PreviousId(Array previousUnits, FieldInfo adType, FieldInfo id, string format)
        {
            if (previousUnits == null || adType == null || format == null) return null;
            foreach (var unit in previousUnits)
                if (adType.GetValue(unit)?.ToString() == format)
                    return id.GetValue(unit) as string;
            return null;
        }

        /// <summary>App key fields, whichever layout this SDK version uses — the build target's only.</summary>
        private static IEnumerable<(string label, string value)> ReadAppKeys(SerializedProperty network)
        {
            var single = network.FindPropertyRelative("AppKey");
            if (single != null)
            {
                yield return ("App Key", single.stringValue);
                yield break;
            }

            var key = network.FindPropertyRelative(Platform + "AppKey");
            if (key != null) yield return ($"{Platform} App Key", key.stringValue);
        }

        /// <summary>Ad unit id fields, whichever layout this SDK version uses — the build target's only.</summary>
        private static IEnumerable<(string label, string value)> ReadAdUnitIds(SerializedProperty entry)
        {
            var single = entry.FindPropertyRelative("AdUnitId");
            if (single != null)
            {
                yield return ("ad unit ID", single.stringValue);
                yield break;
            }

            var id = entry.FindPropertyRelative(Platform);
            if (id != null) yield return ($"{Platform} ad unit ID", id.stringValue);
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
