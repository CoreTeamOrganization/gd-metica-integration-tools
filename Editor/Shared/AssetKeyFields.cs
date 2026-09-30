using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The key fields a step shows for an asset it creates (App ID, API Key…). What the
    /// developer types is kept here until it is written into the asset — on create or on
    /// Save — so a window rebuild never loses it; until then a field shows the asset's value.
    ///
    /// <para>Fields are read and written through SerializedObject, by serialized field name:
    /// the tool's assembly cannot reference the classes it writes into the game's.</para>
    /// </summary>
    internal sealed class AssetKeyFields
    {
        private readonly Dictionary<string, string> _typed = new Dictionary<string, string>();

        /// <summary>What the field shows: the typed value if any, else the asset's.</summary>
        public string Value(Object asset, string field) =>
            _typed.TryGetValue(field, out var typed) ? typed : Read(asset, field);

        public void Set(string field, string value) => _typed[field] = value;

        /// <summary>True when anything typed differs from what the asset holds.</summary>
        public bool HasChanges(Object asset) =>
            _typed.Any(entry => (entry.Value ?? string.Empty).Trim() != Read(asset, entry.Key));

        /// <summary>Writes every typed value into <paramref name="asset"/> and saves it. Returns how many changed.</summary>
        public int WriteTo(Object asset)
        {
            if (asset == null || _typed.Count == 0) return 0;

            var serialized = new SerializedObject(asset);
            var changed = 0;

            foreach (var entry in _typed)
            {
                var property = serialized.FindProperty(entry.Key);
                var value = (entry.Value ?? string.Empty).Trim();
                if (property == null || property.stringValue == value) continue;

                property.stringValue = value;
                changed++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            _typed.Clear();

            if (changed > 0)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            return changed;
        }

        public static string Read(Object asset, string field) =>
            asset == null ? string.Empty : new SerializedObject(asset).FindProperty(field)?.stringValue ?? string.Empty;

        /// <summary>
        /// "App ID is empty — fill it in before you build." for one, "App ID and API Key are
        /// empty — fill them in before you build." for several; null when none are empty.
        /// </summary>
        public static string EmptyWarning(IEnumerable<string> emptyLabels)
        {
            var labels = emptyLabels.ToList();
            if (labels.Count == 0) return null;
            if (labels.Count == 1) return $"{labels[0]} is empty — fill it in before you build.";

            var list = string.Join(", ", labels.Take(labels.Count - 1)) + " and " + labels.Last();
            return $"{list} are empty — fill them in before you build.";
        }
    }
}
