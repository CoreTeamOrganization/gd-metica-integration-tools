using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Creates Resources/MeticaAdsConfig.asset — every id Metica ads need, in one place.
    ///
    /// <para>Created empty on purpose. The API Key, App ID, MAX SDK key and ad unit ids are
    /// this game's, from the Metica and AppLovin dashboards; a prefilled asset is how a game
    /// ends up reporting revenue into somebody else's account. The step passes once the asset
    /// exists — blank keys are only noted, never blocked on, since filling them in is the
    /// developer's own pace to set, not this wizard's.</para>
    /// </summary>
    public sealed class StandaloneConfigStep : MeticaStep
    {
        private const string TypeName = "MeticaIntegration.MeticaAdsConfig";

        public override string Title => "Metica ads config";

        public override string Summary =>
            "Creates MeticaAdsConfig.asset in Resources: the Metica API Key and App ID, your MAX SDK " +
            "key, and the ad unit ids per platform. Filling them in is yours to do at your own pace.";

        public override string ActionLabel => "Create MeticaAdsConfig.asset";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.StandaloneConfigAsset };

        public override string ReviewHint =>
            "One new asset. Check the API Key and App ID are this game's Metica values, and that the " +
            "ad unit ids are on the right platform — an id in the wrong column silently serves nothing.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (!TemplateWriter.TypeIsLoaded(TypeName))
            {
                result.Problem("MeticaAdsConfig has not compiled yet. Finish the runtime step and let " +
                               "Unity recompile, then Re-check.");
                return result.Seal();
            }

            if (!MeticaPaths.FileExists(MeticaPaths.StandaloneConfigAsset))
            {
                result.Problem($"Missing {MeticaPaths.StandaloneConfigAsset}");
                return result.Seal();
            }

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.StandaloneConfigAsset);
            if (asset == null)
            {
                result.Problem($"{MeticaPaths.StandaloneConfigAsset} exists but did not load as a " +
                               "MeticaAdsConfig. Delete it and press the button again.");
                return result.Seal();
            }

            CheckKeys(asset, result);
            return result.Seal();
        }

        /// <summary>
        /// Read through SerializedObject rather than the type, because the tool's assembly
        /// cannot reference a class it just wrote into the game's.
        ///
        /// <para>Blank keys are not this step's problem to raise. The asset exists once
        /// created, and filling it in is deliberately left to the developer at their own
        /// pace — see MeticaAdsConfig's own class doc.</para>
        /// </summary>
        private static void CheckKeys(ScriptableObject asset, VerifyResult result)
        {
            var serialized = new SerializedObject(asset);
            var blank = new List<string>();

            foreach (var field in new[] { "ApiKey", "AppId", "MaxSdkKey" })
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                {
                    result.Problem($"MeticaAdsConfig has no {field} field — the asset may be from an " +
                                   "older version of this tool.");
                    continue;
                }

                if (string.IsNullOrEmpty(property.stringValue)) blank.Add(field);
            }

            if (blank.Count > 0)
                result.Note($"{string.Join(", ", blank)} still blank — Metica cannot initialize without " +
                            "them, but that is yours to fill in from the dashboards, not this step's to block on.");

            var units = new[]
            {
                "AndroidInterstitial", "AndroidRewarded", "AndroidBanner", "AndroidMRec",
                "IosInterstitial", "IosRewarded", "IosBanner", "IosMRec"
            };

            var filled = 0;
            foreach (var unit in units)
            {
                var property = serialized.FindProperty(unit);
                if (property != null && !string.IsNullOrEmpty(property.stringValue)) filled++;
            }

            result.Note(filled == 0
                ? "No ad unit ids yet — with none, Metica initializes but no ad ever serves."
                : $"{filled} of {units.Length} ad unit ids filled in (an empty format is simply off)");
        }

        public override void Apply()
        {
            var type = TemplateWriter.FindType(TypeName);
            if (type == null)
            {
                MeticaIntegrationLog.Record(Title,
                    "MeticaAdsConfig is not compiled yet — cannot create the asset.");
                return;
            }

            if (MeticaPaths.FileExists(MeticaPaths.StandaloneConfigAsset))
            {
                MeticaIntegrationLog.Record(Title, $"{MeticaPaths.StandaloneConfigAsset} already exists");
                return;
            }

            var folder = Path.GetDirectoryName(MeticaPaths.ToAbsolute(MeticaPaths.StandaloneConfigAsset));
            Directory.CreateDirectory(folder ?? ".");
            AssetDatabase.Refresh();

            var instance = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(instance, MeticaPaths.StandaloneConfigAsset);
            AssetDatabase.SaveAssets();

            Selection.activeObject = instance;
            MeticaIntegrationLog.Record(Title, $"Created {MeticaPaths.StandaloneConfigAsset}");
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "The tool creates the asset; the values are yours to enter.\n\n" +
                "API Key and App ID come from the Metica dashboard. The MAX SDK key is the one your " +
                "game already uses — Metica serves through MAX, so it is the same key. Ad unit ids are " +
                "your MAX ad unit ids, and they differ between Android and iOS.\n\n" +
                "Leave a format's id blank to turn that format off.",
                MessageType.Info);

            if (!MeticaPaths.FileExists(MeticaPaths.StandaloneConfigAsset)) return;

            if (GUILayout.Button("Open MeticaAdsConfig in the Inspector"))
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.StandaloneConfigAsset);
        }
    }
}
