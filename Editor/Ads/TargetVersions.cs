using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Reads <see cref="MeticaTargetVersion"/>: the project's own override if it made one
    /// (Assets/MeticaIntegrationToolsSettings), otherwise the default this package ships.
    /// </summary>
    internal static class TargetVersions
    {
        public static MeticaTargetVersion Load()
        {
            var path = MeticaPaths.FileExists(MeticaPaths.TargetVersionAsset)
                ? MeticaPaths.TargetVersionAsset
                : MeticaPaths.PackagedTargetVersionAsset;

            return path == null || !MeticaPaths.FileExists(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<MeticaTargetVersion>(path);
        }

        /// <summary>The Metica SDK version to install, or null when unset.</summary>
        public static string MeticaSdk => NullIfEmpty(Load()?.Version);

        /// <summary>The AppLovin MAX version to install, or null when unset.</summary>
        public static string Max => NullIfEmpty(Load()?.MaxVersion);

        /// <summary>
        /// Selects this project's own target-version asset, copying the packaged default into
        /// the project first if there is none yet — the packaged one is shared across every
        /// project on this package version and is often read-only (git packages live in
        /// Library/PackageCache).
        /// </summary>
        public static void OpenOverride()
        {
            if (!MeticaPaths.FileExists(MeticaPaths.TargetVersionAsset))
            {
                if (MeticaPaths.PackagedTargetVersionAsset == null ||
                    !MeticaPaths.FileExists(MeticaPaths.PackagedTargetVersionAsset))
                {
                    MeticaIntegrationLog.Record("Target versions", "Could not find the packaged default to copy.");
                    return;
                }

                var folder = Path.GetDirectoryName(MeticaPaths.ToAbsolute(MeticaPaths.TargetVersionAsset));
                Directory.CreateDirectory(folder ?? ".");
                AssetDatabase.Refresh();

                AssetDatabase.CopyAsset(MeticaPaths.PackagedTargetVersionAsset, MeticaPaths.TargetVersionAsset);
                MeticaIntegrationLog.Record("Target versions", $"Created {MeticaPaths.TargetVersionAsset}");
            }

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.TargetVersionAsset);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
