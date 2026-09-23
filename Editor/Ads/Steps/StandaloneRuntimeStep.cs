using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Writes a self-contained Metica ads runtime into a project that has no GD Monetization
    /// SDK to wire Metica into.
    ///
    /// <para>The ad units, the ad network and the initializer are the GD SDK's, unchanged in
    /// everything that talks to Metica — the event wiring, the retry backoff, the revenue
    /// payload. What the GD SDK supplied around them is replaced by the smallest possible
    /// stand-ins written alongside, so the whole folder compiles on its own with nothing to
    /// edit. The one exception is MeticaAdsHooks, which is where the game's own analytics
    /// and consent plug in.</para>
    /// </summary>
    public sealed class StandaloneRuntimeStep : MeticaStep
    {
        /// <summary>Written into <see cref="MeticaPaths.StandaloneRoot"/>, in this order.</summary>
        private static readonly string[] Files =
        {
            // Support: what the GD SDK used to provide.
            "AdTypes",
            "AdUnitBase",
            "MeticaAdsLog",
            "MeticaAdsRunner",
            "MeticaAdsConfig",
            "MeticaAdsHooks",
            "MeticaRemoteConfig",

            // The Metica code itself, as it is in the GD SDK.
            "MeticaInitializer",
            "AdNetworkMetica",
            "MeticaInterstitial",
            "MeticaRewarded",
            "MeticaBanner",
            "MeticaMRec",

            // The front door.
            "MeticaAdsManager"
        };

        /// <summary>Compiling is the real test, so verification waits for these to exist as types.</summary>
        private static readonly string[] MustCompile =
        {
            "MeticaIntegration.MeticaAdsManager",
            "MeticaIntegration.MeticaAdsConfig",
            "MeticaIntegration.MeticaAdsHooks",
            "MeticaIntegration.AdNetworkMetica"
        };

        public override string Title => "Metica ads runtime";

        public override string Summary =>
            "Writes a standalone Metica ads runtime to " + MeticaPaths.StandaloneRoot + ". The ad units " +
            "and initializer are the GD SDK's; everything they leaned on the SDK for is written alongside, " +
            "so the folder compiles on its own.";

        public override string ActionLabel => "Write the Metica ads runtime";

        public override IEnumerable<string> TouchedPaths =>
            Files.Select(name => $"{MeticaPaths.StandaloneRoot}/{name}.cs");

        public override string ReviewHint =>
            "All new files, in one new folder — nothing existing is touched. MeticaAdsManager is the only " +
            "class your game calls; MeticaAdsHooks is the only one you edit.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var missing = Files
                .Where(name => !MeticaPaths.FileExists($"{MeticaPaths.StandaloneRoot}/{name}.cs"))
                .ToArray();

            if (missing.Length > 0)
            {
                result.Problem(missing.Length == Files.Length
                    ? $"Nothing written to {MeticaPaths.StandaloneRoot} yet."
                    : $"Missing from {MeticaPaths.StandaloneRoot}: {string.Join(", ", missing)}");
                return result.Seal();
            }

            result.Note($"{Files.Length} files in {MeticaPaths.StandaloneRoot}");

            // Present but not compiling is the failure that matters, and the only one that
            // can be seen from here.
            var uncompiled = MustCompile.Where(name => !TemplateWriter.TypeIsLoaded(name)).ToArray();
            if (uncompiled.Length > 0)
            {
                result.Problem(
                    "The files are there but have not compiled: " +
                    string.Join(", ", uncompiled.Select(n => n.Split('.').Last())) + ". " +
                    "Check the Console — a missing Metica SDK is the usual cause — then Re-check.");
                return result.Seal();
            }

            result.Note("The runtime compiles");
            return result.Seal();
        }

        public override void Apply()
        {
            var pairs = Files.Select(name =>
                ($"Standalone/{name}.cs.txt", $"{MeticaPaths.StandaloneRoot}/{name}.cs"));

            // Never overwrite: MeticaAdsHooks is meant to be edited, and a second run must
            // not throw that away.
            var messages = TemplateWriter.WriteAll(pairs, false, out _);
            MeticaIntegrationLog.Record(Title, messages);
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "No GD Monetization SDK in this project, so Metica is installed on its own.\n\n" +
                "Your game calls MeticaAdsManager and nothing else:\n" +
                "    MeticaAdsManager.Initialize();\n" +
                "    MeticaAdsManager.ShowInterstitial(\"level_end\");\n" +
                "    MeticaAdsManager.ShowRewarded(\"double_coins\", ok => { if (ok) Grant(); });\n\n" +
                "Existing files are never overwritten, so editing MeticaAdsHooks is safe — running " +
                "this step again will not undo it.",
                MessageType.Info);

            if (!MeticaPaths.DirectoryExists(MeticaPaths.StandaloneRoot)) return;

            if (GUILayout.Button("Select the folder in the Project window"))
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<Object>(MeticaPaths.StandaloneRoot);
        }
    }
}
