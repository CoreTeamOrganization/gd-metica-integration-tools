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

        public override string Summary => "Create MeticaAdsConfig.asset — you fill in its keys.";

        public override string Why =>
            "One asset holds every id Metica ads need. API Key and App ID come from the Metica dashboard. " +
            "The MAX SDK key is the one your game already uses — Metica serves through MAX. Ad unit ids " +
            "are your MAX ad unit ids and differ per platform; leave one blank to turn that format off. " +
            "The asset is created empty on purpose, and blank keys never block this step.";

        public override string ActionLabel => "Create MeticaAdsConfig.asset";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.StandaloneConfigAsset };

        public override string ReviewHint => "One new asset. Check each ad unit id is under the right platform.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (!TemplateWriter.TypeIsLoaded(TypeName))
            {
                result.Problem("MeticaAdsConfig hasn't compiled yet — finish the runtime step.");
                return result.Seal();
            }

            if (!MeticaPaths.FileExists(MeticaPaths.StandaloneConfigAsset))
            {
                result.Problem("MeticaAdsConfig.asset missing.");
                return result.Seal();
            }

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.StandaloneConfigAsset);
            if (asset == null)
            {
                result.Problem("MeticaAdsConfig.asset didn't load — delete it and try again.");
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
                    result.Problem($"No {field} field — the asset is from an older tool version.");
                    continue;
                }

                if (string.IsNullOrEmpty(property.stringValue)) blank.Add(field);
            }

            if (blank.Count > 0)
                result.Note($"Still blank: {string.Join(", ", blank)}");

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

            result.Note($"{filled} of {units.Length} ad unit ids set");
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
            if (!MeticaPaths.FileExists(MeticaPaths.StandaloneConfigAsset)) return;

            if (GUILayout.Button("Open MeticaAdsConfig"))
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.StandaloneConfigAsset);
        }
    }
}
