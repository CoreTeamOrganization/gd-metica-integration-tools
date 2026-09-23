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

        public override string Summary =>
            "Creates MeticaSettings.asset. Filling in the API Key and App ID is yours to do — they are " +
            "per-game values from the Metica dashboard, and not the same as your MAX keys.";

        public override string ActionLabel => "Create MeticaSettings.asset";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.MeticaSettingsAsset };

        public override string ReviewHint =>
            "One new asset, created empty. Enter your Metica API Key and App ID before shipping — the " +
            "tool deliberately does not.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.MeticaSettingsAsset == null)
            {
                result.Problem("GD Monetization SDK root not resolved — go back to the Metica SDK step.");
                return result.Seal();
            }

            if (!TemplateWriter.TypeIsLoaded(TypeName))
            {
                result.Problem("MeticaConfiguration has not compiled yet. Finish the wrapper files and " +
                               "patch steps and let Unity recompile, then Re-check.");
                return result.Seal();
            }

            if (!MeticaPaths.FileExists(MeticaPaths.MeticaSettingsAsset))
            {
                result.Problem($"Missing {MeticaPaths.MeticaSettingsAsset}");
                return result.Seal();
            }

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.MeticaSettingsAsset);
            if (asset == null)
            {
                result.Problem($"{MeticaPaths.MeticaSettingsAsset} exists but did not load as a " +
                               "MeticaConfiguration. Delete it and press the button again.");
                return result.Seal();
            }

            result.Note($"{MeticaPaths.MeticaSettingsAsset} exists");
            result.Note("Fill in the API Key and App ID for every platform you ship, from the Metica " +
                        "dashboard. MeticaConfiguration returns string.Empty for a platform whose pair is " +
                        "blank, so Metica would initialize there with an empty key.");

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

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "The tool only creates the asset. Open it and enter the API Key and App ID Metica issued " +
                "for this game — per platform.",
                MessageType.Info);

            if (!MeticaPaths.FileExists(MeticaPaths.MeticaSettingsAsset)) return;

            if (GUILayout.Button("Open MeticaSettings in the Inspector"))
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.MeticaSettingsAsset);
        }
    }
}
