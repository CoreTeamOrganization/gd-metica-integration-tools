using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// What the project already knows that the genre asset asks for, so the fields start filled:
    /// a game name from Player Settings, the Adjust app token, and the Metica App ID / API Key
    /// the Ads integration stored. Each is a suggestion — what the developer types wins.
    /// </summary>
    internal static class GameKeys
    {
        /// <summary>
        /// Player Settings' product name as one word for class names: each word capitalised and
        /// joined, everything but letters and digits dropped, leading digits too
        /// ("Anti Stress Game iOS" → "AntiStressGameIOS"). Empty when nothing is left.
        /// </summary>
        public static string GameName() => ClassName(PlayerSettings.productName);

        internal static string ClassName(string product)
        {
            var words = Regex.Split(product ?? "", "[^A-Za-z0-9]+").Where(w => w.Length > 0);
            var joined = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
            return Regex.Replace(joined, "^[^A-Za-z]+", "");
        }

        /// <summary>
        /// The Adjust app token: GD SDK's AdjustConfiguration asset, else a string literal passed
        /// to <c>new AdjustConfig("…"</c> in game code, else an Adjust component's appToken in a
        /// prefab or scene. Empty when none is found.
        /// </summary>
        public static string AdjustAppToken()
        {
            var token = AssetField("AdjustConfiguration", "AppToken");
            if (token.Length > 0) return token;

            token = Cached("AdjustToken.Code", () => FirstMatch("*.cs", new Regex(@"new\s+AdjustConfig\s*\(\s*""([^""]+)"""), skipAdjust: true));
            if (token.Length > 0) return token;

            return Cached("AdjustToken.Yaml", () =>
                FirstMatch("*.prefab", AppTokenYaml, skipAdjust: true) is var prefab && prefab.Length > 0
                    ? prefab
                    : FirstMatch("*.unity", AppTokenYaml, skipAdjust: true));
        }

        private static readonly Regex AppTokenYaml = new Regex(@"^\s*appToken:\s*([A-Za-z0-9]+)\s*$", RegexOptions.Multiline);

        /// <summary>
        /// The Metica App ID the Ads integration stored for the platform being built: GD SDK's
        /// MeticaSettings asset (iOS or Android field), else the standalone MeticaAdsConfig.
        /// </summary>
        public static string MeticaAppId() => MeticaKey("iOSAppID", "AndroidAppID", "AppId");

        public static string MeticaApiKey() => MeticaKey("iOSApiKey", "AndroidApiKey", "ApiKey");

        private static string MeticaKey(string iosField, string androidField, string standaloneField)
        {
            var settings = MeticaPaths.MeticaSettingsAsset;
            if (settings != null && MeticaPaths.FileExists(settings))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(settings);
                var ios = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
                var value = Read(asset, ios ? iosField : androidField);
                if (value.Length > 0) return value;
            }

            return AssetField("MeticaAdsConfig", standaloneField);
        }

        // ── Reading ────────────────────────────────────────────────────────────

        /// <summary>A string field of the first asset of a ScriptableObject type (by class name) that has it set.</summary>
        private static string AssetField(string typeName, string field)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeName))
            {
                var value = Read(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid)), field);
                if (value.Length > 0) return value;
            }

            return "";
        }

        private static string Read(ScriptableObject asset, string field) =>
            asset == null ? "" : new SerializedObject(asset).FindProperty(field)?.stringValue?.Trim() ?? "";

        /// <summary>The first capture of <paramref name="pattern"/> in Assets' files of a kind.</summary>
        private static string FirstMatch(string filePattern, Regex pattern, bool skipAdjust)
        {
            var assets = Application.dataPath;
            foreach (var file in Directory.EnumerateFiles(assets, filePattern, SearchOption.AllDirectories))
            {
                var relative = file.Substring(assets.Length).Replace('\\', '/').TrimStart('/');
                if (skipAdjust && relative.StartsWith("Adjust/", StringComparison.Ordinal)) continue;

                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }

                var match = pattern.Match(text);
                if (match.Success) return match.Groups[1].Value.Trim();
            }

            return "";
        }

        /// <summary>
        /// Scans of the whole project, once per editor session: the step's fields are redrawn
        /// often, and a token in code or a scene rarely changes while it is open.
        /// </summary>
        private static string Cached(string key, Func<string> scan)
        {
            var sessionKey = "GameDistrict.MeticaIntegrationTools.GameKeys." + key;
            if (SessionState.GetBool(sessionKey + ".done", false)) return SessionState.GetString(sessionKey, "");

            var value = scan() ?? "";
            SessionState.SetString(sessionKey, value);
            SessionState.SetBool(sessionKey + ".done", true);
            return value;
        }

        /// <summary>The suggestion for one of the genre asset's fields (appId, apiKey, appToken).</summary>
        public static string ForGenreField(string field)
        {
            switch (field)
            {
                case "appId": return MeticaAppId();
                case "apiKey": return MeticaApiKey();
                case "appToken": return AdjustAppToken();
                default: return "";
            }
        }

        /// <summary>Every suggestion for the genre asset's fields, by field name.</summary>
        public static Dictionary<string, string> ForGenreAsset() => new[] { "appId", "apiKey", "appToken" }
            .ToDictionary(field => field, ForGenreField);
    }
}
