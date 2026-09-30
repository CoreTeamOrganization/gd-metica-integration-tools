using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Creates Resources/MeticaAdsConfig.asset — every id Metica ads need, in one place.
    ///
    /// <para>The App ID and API Key are typed into the step and written into the asset when
    /// it is created (or later, with Save). The MAX SDK key is the one the game already has
    /// in AppLovinSettings, so it is copied from there rather than asked for. Blank keys are
    /// a warning, never a block: the step passes once the asset exists.</para>
    /// </summary>
    public sealed class StandaloneConfigStep : MeticaStep
    {
        private const string TypeName = "MeticaIntegration.MeticaAdsConfig";

        private const string AppId = "AppId";
        private const string ApiKey = "ApiKey";
        private const string MaxSdkKey = "MaxSdkKey";
        private const string UseRemoteSwitch = "UseRemoteSwitch";
        private const string DefaultUseMetica = "DefaultUseMetica";

        private readonly AssetKeyFields _keys = new AssetKeyFields();

        public override string Title => "Metica ads config";

        public override string Summary => "Create MeticaAdsConfig.asset with this game's Metica keys.";

        public override string Why =>
            "One asset holds every id Metica ads need. App ID and API Key come from the Metica dashboard, " +
            "for this game — type them here, or leave them for later. The MAX SDK key is copied from " +
            "AppLovinSettings: Metica serves through MAX, so it is the same key. Ad unit ids are your MAX " +
            "ad unit ids and differ per platform — fill them in the asset; a blank one turns that format " +
            "off. Blank keys never block this step.";

        // Nothing left to do once the asset exists; the keys are edited in the fields.
        public override string ActionLabel =>
            MeticaPaths.FileExists(MeticaPaths.StandaloneConfigAsset) ? null : "Create MeticaAdsConfig.asset";

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.StandaloneConfigAsset };

        public override string ReviewHint =>
            "One new asset. Check each ad unit id is under the right platform. Metica stays off until " +
            "the game calls MeticaRemoteConfig.Apply(true).";

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

            var asset = LoadAsset();
            if (asset == null)
            {
                result.Problem("MeticaAdsConfig.asset didn't load — delete it and try again.");
                return result.Seal();
            }

            CheckKeys(asset, result);
            result.Note(SwitchState(asset));
            return result.Seal();
        }

        /// <summary>The remote switch as the asset has it, in words.</summary>
        private static string SwitchState(ScriptableObject asset)
        {
            var serialized = new SerializedObject(asset);
            var gated = serialized.FindProperty(UseRemoteSwitch);
            var fallback = serialized.FindProperty(DefaultUseMetica);
            if (gated == null || fallback == null) return "Remote switch: unknown (no switch fields in the asset)";

            if (!gated.boolValue) return "Remote switch off — Metica always on";

            return fallback.boolValue
                ? "Remote switch on, default on — Metica on until the backend turns it off"
                : "Remote switch on, default off — Metica off until MeticaRemoteConfig.Apply(true), from the next session";
        }

        private static void CheckKeys(ScriptableObject asset, VerifyResult result)
        {
            var serialized = new SerializedObject(asset);

            foreach (var field in new[] { AppId, ApiKey, MaxSdkKey })
                if (serialized.FindProperty(field) == null)
                    result.Problem($"No {field} field — the asset is from an older tool version.");

            var empty = new[] { (AppId, "App ID"), (ApiKey, "API Key"), (MaxSdkKey, "MAX SDK key") }
                .Where(key => string.IsNullOrEmpty(serialized.FindProperty(key.Item1)?.stringValue))
                .Select(key => key.Item2);

            var warning = AssetKeyFields.EmptyWarning(empty);
            if (warning != null) result.Warning(warning);

            var units = new[]
            {
                "AndroidInterstitial", "AndroidRewarded", "AndroidBanner", "AndroidMRec",
                "IosInterstitial", "IosRewarded", "IosBanner", "IosMRec"
            };

            var filled = units.Count(unit => !string.IsNullOrEmpty(serialized.FindProperty(unit)?.stringValue));
            result.Note($"{filled} of {units.Length} ad unit ids set");
        }

        public override void Apply()
        {
            var type = TemplateWriter.FindType(TypeName);
            if (type == null)
            {
                MeticaIntegrationLog.Record(Title, "MeticaAdsConfig is not compiled yet — cannot create the asset.");
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

            // Metica off until the backend turns it on — set here as well as in the field
            // initializers, so a new asset is off whatever the template says.
            var serialized = new SerializedObject(instance);
            var gated = serialized.FindProperty(UseRemoteSwitch);
            var fallback = serialized.FindProperty(DefaultUseMetica);
            if (gated != null) gated.boolValue = true;
            if (fallback != null) fallback.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(instance, MeticaPaths.StandaloneConfigAsset);
            AssetDatabase.SaveAssets();

            MeticaIntegrationLog.Record(Title, $"Created {MeticaPaths.StandaloneConfigAsset}");
            WriteKeys(instance);
            Selection.activeObject = instance;
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (!TemplateWriter.TypeIsLoaded(TypeName)) yield break;

            var asset = LoadAsset();
            yield return new StepText("App ID", _keys.Value(asset, AppId), value => _keys.Set(AppId, value));
            yield return new StepText("API Key", _keys.Value(asset, ApiKey), value => _keys.Set(ApiKey, value), secret: true);

            if (asset == null) yield break;

            yield return new StepButton("Save keys", () => WriteKeys(LoadAsset()),
                showWhen: () => _keys.HasChanges(LoadAsset()));
            yield return new StepButton("Select MeticaAdsConfig", () => SelectAndPing(LoadAsset()),
                icon: StepIcon.File);
        }

        /// <summary>The typed keys, plus the MAX SDK key from AppLovinSettings when the asset has none.</summary>
        private void WriteKeys(ScriptableObject asset)
        {
            if (asset == null) return;

            if (string.IsNullOrEmpty(AssetKeyFields.Read(asset, MaxSdkKey)))
            {
                var maxKey = ReadMaxSdkKey();
                if (!string.IsNullOrEmpty(maxKey)) _keys.Set(MaxSdkKey, maxKey);
            }

            var changed = _keys.WriteTo(asset);
            MeticaIntegrationLog.Record(Title, changed == 0
                ? "No key changes to save"
                : $"Saved {changed} key{(changed == 1 ? "" : "s")} to {MeticaPaths.StandaloneConfigAsset}");
        }

        /// <summary>The SDK key the AppLovin Integration Manager keeps, or null.</summary>
        private static string ReadMaxSdkKey()
        {
            var path = AssetDatabase.FindAssets("t:AppLovinSettings")
                           .Select(AssetDatabase.GUIDToAssetPath)
                           .FirstOrDefault()
                       ?? "Assets/MaxSdk/Resources/AppLovinSettings.asset";

            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            var key = AssetKeyFields.Read(settings, "sdkKey");
            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static ScriptableObject LoadAsset() =>
            AssetDatabase.LoadAssetAtPath<ScriptableObject>(MeticaPaths.StandaloneConfigAsset);
    }
}
