using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Creates Resources/Configurations/MeticaSettings.asset, empty.
    ///
    /// <para>The API Key and App ID are per-game values from the Metica dashboard, so the
    /// tool does not fill or check them — that is the developer's step. Creating the asset
    /// empty is also the safe default: a prefilled one is how a game ends up reporting into
    /// somebody else's Metica app.</para>
    /// </summary>
    public sealed class MeticaSettingsStep : MeticaStep
    {
        private const string TypeName = "Monetization.Runtime.Configurations.MeticaConfiguration";

        public override string Title => "Metica settings asset";

        public override string Summary => "Create MeticaSettings.asset — you fill in its keys.";

        public override string Why =>
            "The API Key and App ID come from the Metica dashboard, per game and per platform — they are " +
            "not your MAX keys. The asset is created empty on purpose: a prefilled one is how a game ends " +
            "up reporting into somebody else's Metica app. A platform left blank starts Metica with an " +
            "empty key.";

        public override string ActionLabel => "Create MeticaSettings.asset";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.MeticaSettingsAsset };

        public override string ReviewHint => "One new, empty asset. Fill in the API Key and App ID before shipping.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.MeticaSettingsAsset == null)
            {
                result.Problem("GD SDK not found.");
                return result.Seal();
            }

            if (!TemplateWriter.TypeIsLoaded(TypeName))
            {
                result.Problem("MeticaConfiguration hasn't compiled yet — finish the earlier steps.");
                return result.Seal();
            }

            if (!MeticaPaths.FileExists(MeticaPaths.MeticaSettingsAsset))
            {
                result.Problem("MeticaSettings.asset missing.");
                return result.Seal();
            }

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.MeticaSettingsAsset);
            if (asset == null)
            {
                result.Problem("MeticaSettings.asset didn't load — delete it and try again.");
                return result.Seal();
            }

            result.Note("Created — fill in the API Key and App ID");

            return result.Seal();
        }

        public override void Apply()
        {
            var type = TemplateWriter.FindType(TypeName);
            if (type == null)
            {
                MeticaIntegrationLog.Record(Title, "MeticaConfiguration is not compiled yet — cannot create the asset.");
                return;
            }

            if (MeticaPaths.FileExists(MeticaPaths.MeticaSettingsAsset))
            {
                MeticaIntegrationLog.Record(Title, $"{MeticaPaths.MeticaSettingsAsset} already exists");
                return;
            }

            Directory.CreateDirectory(MeticaPaths.ToAbsolute(MeticaPaths.ResourcesConfigurations));
            AssetDatabase.Refresh();

            var instance = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(instance, MeticaPaths.MeticaSettingsAsset);
            AssetDatabase.SaveAssets();

            MeticaIntegrationLog.Record(Title, $"Created {MeticaPaths.MeticaSettingsAsset}");
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.MeticaSettingsAsset)) yield break;

            yield return new StepButton("Open MeticaSettings", () =>
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.MeticaSettingsAsset),
                icon: StepIcon.File);
        }
    }
}
