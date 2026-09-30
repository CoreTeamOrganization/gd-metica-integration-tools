using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Creates Resources/Configurations/MeticaSettings.asset with this game's Metica keys.
    ///
    /// <para>The App ID and API Key are per game and per platform, from the Metica dashboard.
    /// They are typed into the step and written into the asset when it is created (or later,
    /// with Save); iOS keys are only asked for when Enable iOS is on in the Metica SDK step.
    /// Blank keys are a warning, never a block.</para>
    /// </summary>
    public sealed class MeticaSettingsStep : MeticaStep
    {
        private const string TypeName = "Monetization.Runtime.Configurations.MeticaConfiguration";

        /// <summary>MeticaConfiguration's serialized field names, App ID above API Key.</summary>
        private static readonly (string field, string label)[] AndroidKeys =
        {
            ("AndroidAppID", "Android App ID"),
            ("AndroidApiKey", "Android API Key")
        };

        private static readonly (string field, string label)[] IosKeys =
        {
            ("iOSAppID", "iOS App ID"),
            ("iOSApiKey", "iOS API Key")
        };

        private readonly AssetKeyFields _keys = new AssetKeyFields();

        private static IEnumerable<(string field, string label)> ShownKeys =>
            ImportMeticaSdkStep.EnableIos ? AndroidKeys.Concat(IosKeys) : AndroidKeys;

        public override string Title => "Metica settings asset";

        public override string Summary => "Create MeticaSettings.asset with this game's Metica keys.";

        public override string Why =>
            "The App ID and API Key come from the Metica dashboard, per game and per platform — they are " +
            "not your MAX keys. Type them here, or leave them for later: blank keys never block this step, " +
            "but a platform left blank starts Metica with an empty key. iOS keys appear when Enable iOS is " +
            "on in the Metica SDK step.";

        // Nothing left to do once the asset exists; the keys are edited in the fields.
        public override string ActionLabel =>
            MeticaPaths.FileExists(MeticaPaths.MeticaSettingsAsset) ? null : "Create MeticaSettings.asset";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.MeticaSettingsAsset };

        public override string ReviewHint => "One new asset. Check the keys are this game's, under the right platform.";

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

            var asset = LoadAsset();
            if (asset == null)
            {
                result.Problem("MeticaSettings.asset didn't load — delete it and try again.");
                return result.Seal();
            }

            var warning = AssetKeyFields.EmptyWarning(ShownKeys
                .Where(key => string.IsNullOrEmpty(AssetKeyFields.Read(asset, key.field)))
                .Select(key => key.label));

            if (warning != null) result.Warning(warning);
            else result.Note("Keys set");

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
            WriteKeys(instance);
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (MeticaPaths.MeticaSettingsAsset == null || !TemplateWriter.TypeIsLoaded(TypeName)) yield break;

            var asset = LoadAsset();
            foreach (var (field, label) in ShownKeys)
                yield return new StepText(label, _keys.Value(asset, field), value => _keys.Set(field, value),
                    secret: field.EndsWith("ApiKey"));

            if (asset == null) yield break;

            yield return new StepButton("Save keys", () => WriteKeys(LoadAsset()),
                showWhen: () => _keys.HasChanges(LoadAsset()));
            yield return new StepButton("Select MeticaSettings", () => SelectAndPing(LoadAsset()),
                icon: StepIcon.File);
        }

        private void WriteKeys(ScriptableObject asset)
        {
            if (asset == null) return;

            var changed = _keys.WriteTo(asset);
            MeticaIntegrationLog.Record(Title, changed == 0
                ? "No key changes to save"
                : $"Saved {changed} key{(changed == 1 ? "" : "s")} to {MeticaPaths.MeticaSettingsAsset}");
        }

        private static ScriptableObject LoadAsset() =>
            MeticaPaths.MeticaSettingsAsset == null
                ? null
                : AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.MeticaSettingsAsset);
    }
}
