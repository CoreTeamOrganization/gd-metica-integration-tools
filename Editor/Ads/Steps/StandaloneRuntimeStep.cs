using System.Collections.Generic;
using System.IO;
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
        internal static readonly string[] Files =
        {
            // Support: what the GD SDK used to provide.
            EnumFile,
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

        /// <summary>
        /// The MainAdNetwork enum and MainAdNetworkPref (global namespace, the same ones a
        /// game's own remote config code uses), which MeticaRemoteConfig stores through.
        /// </summary>
        internal const string EnumFile = "MainAdNetwork";

        private const string RemoteConfigFile = "MeticaRemoteConfig";

        /// <summary>The pref class MeticaRemoteConfig uses once it is on the enum.</summary>
        private const string PrefType = "MainAdNetworkPref";

        /// <summary>
        /// Why MainAdNetwork.cs is left out, or null when it is written: the game has a
        /// MainAdNetwork of its own (outside this folder), which the runtime then compiles
        /// against; or the runtime here was written before the enum, and its
        /// MeticaRemoteConfig.cs (never overwritten) doesn't use it.
        /// </summary>
        internal static string EnumFileSkipped
        {
            get
            {
                if (MeticaPaths.FileExists(FilePath(EnumFile))) return null;

                if (TemplateWriter.TypeIsLoaded(EnumFile))
                    return "The game has its own MainAdNetwork — MainAdNetwork.cs not written; the runtime uses the game's.";

                if (MeticaPaths.FileExists(FilePath(RemoteConfigFile)) && !RemoteConfigUsesEnum())
                    return "MeticaRemoteConfig.cs predates the MainAdNetwork enum — MainAdNetwork.cs not needed. " +
                           "Delete MeticaRemoteConfig.cs and write the runtime again to move to the enum.";

                return null;
            }
        }

        /// <summary><see cref="Files"/>, less MainAdNetwork when <see cref="EnumFileSkipped"/> says so.</summary>
        internal static IEnumerable<string> FilesToWrite =>
            EnumFileSkipped == null ? Files : Files.Where(name => name != EnumFile).ToArray();

        private static string FilePath(string name) => $"{MeticaPaths.StandaloneRoot}/{name}.cs";

        private static bool RemoteConfigUsesEnum()
        {
            try
            {
                return File.ReadAllText(MeticaPaths.ToAbsolute(FilePath(RemoteConfigFile))).Contains(PrefType);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Compiling is the real test, so verification waits for these to exist as types.</summary>
        private static readonly string[] MustCompile =
        {
            "MeticaIntegration.MeticaAdsManager",
            "MeticaIntegration.MeticaAdsConfig",
            "MeticaIntegration.MeticaAdsHooks",
            "MeticaIntegration.AdNetworkMetica"
        };

        public override string Title => "Metica ads runtime";

        public override string Summary => $"Write the Metica ads runtime to {MeticaPaths.StandaloneRoot}.";

        public override string Why =>
            "No GD SDK here, so Metica gets its own small runtime. The ad units and initializer are the GD " +
            "SDK's; what they relied on the SDK for is written alongside, so the folder compiles on its " +
            "own. Your game calls MeticaAdsManager only — e.g. MeticaAdsManager.Initialize(), " +
            "ShowInterstitial(\"level_end\"), ShowRewarded(\"double_coins\", ok => …). Edit MeticaAdsHooks " +
            "to plug in your analytics and consent; existing files are never overwritten.";

        public override string ActionLabel => "Write the Metica ads runtime";

        public override IEnumerable<string> TouchedPaths =>
            FilesToWrite.Select(FilePath);

        public override string ReviewHint => "New files in one new folder. Nothing existing changes.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var files = FilesToWrite.ToArray();
            var missing = files
                .Where(name => !MeticaPaths.FileExists($"{MeticaPaths.StandaloneRoot}/{name}.cs"))
                .ToArray();

            if (missing.Length == files.Length)
            {
                result.Problem("Not written yet.");
                return result.Seal();
            }

            if (missing.Length > 0)
            {
                result.Problem($"{missing.Length} of {files.Length} files missing.");
                foreach (var name in missing)
                    result.Problem($"Missing: {name}.cs");
                return result.Seal();
            }

            // Present but not compiling is the failure that matters, and the only one that
            // can be seen from here.
            // A runtime on the enum also needs MainAdNetworkPref — this folder's or the game's.
            var mustCompile = RemoteConfigUsesEnum() ? MustCompile.Concat(new[] { PrefType }) : MustCompile;
            var uncompiled = mustCompile.Where(name => !TemplateWriter.TypeIsLoaded(name)).ToArray();
            if (uncompiled.Length > 0 && ScriptCompile.Pending(MeticaPaths.StandaloneRoot))
            {
                result.Wait();
                return result.Seal();
            }

            if (uncompiled.Length > 0)
            {
                result.Problem("Runtime hasn't compiled — check the Console.");
                foreach (var name in uncompiled)
                    result.Problem($"Not compiled: {name.Split('.').Last()}");
                return result.Seal();
            }

            result.Note($"{files.Length} files, compiled");
            var skipped = EnumFileSkipped;
            if (skipped != null) result.Note(skipped);
            return result.Seal();
        }

        public override void Apply()
        {
            var skipped = EnumFileSkipped;
            var pairs = FilesToWrite.Select(name => ($"Standalone/{name}.cs.txt", FilePath(name)));

            // Never overwrite: MeticaAdsHooks is meant to be edited, and a second run must
            // not throw that away.
            var messages = TemplateWriter.WriteAll(pairs, false, out _);
            if (skipped != null) messages.Add(skipped);
            MeticaIntegrationLog.Record(Title, messages);
        }

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (!MeticaPaths.DirectoryExists(MeticaPaths.StandaloneRoot)) yield break;

            yield return new StepButton("Select the folder",
                () => SelectAndPing(AssetDatabase.LoadAssetAtPath<Object>(MeticaPaths.StandaloneRoot)),
                icon: StepIcon.Folder);
        }
    }
}
