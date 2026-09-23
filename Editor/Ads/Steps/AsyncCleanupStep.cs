using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Only relevant to a project that already carried the older async Metica integration.
    ///
    /// <c>IAsyncAdNetworkService</c> existed for one reason: Metica used to ship
    /// <c>InitializeAsync</c> alone. Its callback API removed that need, so once
    /// AdNetworkMetica is a plain <see cref="MeticaStep"/>-installed IAdNetworkService the
    /// async interface and the AdNetworkController overload built for it are dead weight.
    ///
    /// Nothing is removed while something still implements the interface — a project that
    /// also converted AdNetworkAdmob or AdNetworkAppLovin to it keeps the whole path, and
    /// this step says so instead of breaking the build.
    /// </summary>
    public sealed class AsyncCleanupStep : MeticaStep
    {
        private const string AsyncInterface = "IAsyncAdNetworkService";

        public override string Title => "Remove the unused async init path";

        public override string Summary =>
            "A project that previously used Metica's async initialization still carries " +
            "IAsyncAdNetworkService and an AdNetworkController overload for it. Neither is used once " +
            "Metica initializes through its callback API. On a project that never had them, this step " +
            "has nothing to do.";

        public override string ActionLabel => "Remove the async init path";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.IAdNetworkService,
            MeticaPaths.AdNetworkController
        };

        public override string ReviewHint =>
            "On a project that never had the async integration this changes nothing and the diff is " +
            "empty. If it did remove something, check AdNetworkController still compiles and its " +
            "Initialize() calls the synchronous path.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.RuntimeScripts == null)
            {
                result.Problem("GD Monetization SDK root not resolved — go back to the Metica SDK step.");
                return result.Seal();
            }

            if (!MeticaIntegrationMode.IsCallback)
            {
                result.Note("Async mode — the async init path is in use, so there is nothing to clean up.");
                return result.Seal();
            }

            var leftovers = Leftovers().ToList();
            if (leftovers.Count == 0)
            {
                result.Note("No async init leftovers — nothing to remove.");
                return result.Seal();
            }

            var stillImplementing = StillImplementing().ToList();
            if (stillImplementing.Count > 0)
            {
                result.Note($"{AsyncInterface} is still implemented by {string.Join(" and ", stillImplementing)}, " +
                            "so the async path stays. That is fine — Metica does not use it. Revert those to " +
                            "the synchronous IAdNetworkService first if you want it gone.");
                return result.Seal();
            }

            foreach (var leftover in leftovers)
                result.Problem($"{leftover} is left over from the async integration and nothing uses it.");

            return result.Seal();
        }

        public override void Apply()
        {
            if (!MeticaIntegrationMode.IsCallback)
            {
                MeticaIntegrationLog.Record(Title, "Async mode — leaving the async init path in place.");
                return;
            }

            var log = new List<string>();

            var stillImplementing = StillImplementing().ToList();
            if (stillImplementing.Count > 0)
            {
                MeticaIntegrationLog.Record(Title,
                    $"Left the async path in place — {string.Join(" and ", stillImplementing)} still " +
                    "implement it. Removing it would break the build.");
                return;
            }

            RemoveControllerAsyncPath(log);
            RemoveAsyncInterface(log);

            MeticaIntegrationLog.Record(Title, log);
            AssetDatabase.Refresh();
        }

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "Optional cleanup. Leaving the async path in place is harmless — Metica no longer uses " +
                "it. Originals are backed up before any change.",
                MessageType.Info);
        }

        // ── Detection ──────────────────────────────────────────────────────────

        /// <summary>Async-only declarations that no longer have a user.</summary>
        private static IEnumerable<string> Leftovers()
        {
            if (SourcePatcher.Contains(MeticaPaths.IAdNetworkService, "interface " + AsyncInterface))
                yield return $"The {AsyncInterface} interface";

            if (SourcePatcher.Contains(MeticaPaths.AdNetworkController, "asyncAdNetwork"))
                yield return "The AdNetworkController async constructor and field";
        }

        /// <summary>Ad networks that would stop compiling if the interface went away.</summary>
        private static IEnumerable<string> StillImplementing()
        {
            if (SourcePatcher.Contains(MeticaPaths.AdNetworkAdmob, ": " + AsyncInterface))
                yield return "AdNetworkAdmob";

            if (SourcePatcher.Contains(MeticaPaths.AdNetworkAppLovin, ": " + AsyncInterface))
                yield return "AdNetworkAppLovin";
        }

        // ── Removal ────────────────────────────────────────────────────────────

        private static void RemoveControllerAsyncPath(List<string> log)
        {
            var path = MeticaPaths.AdNetworkController;
            if (!SourcePatcher.Contains(path, "asyncAdNetwork"))
            {
                log.Add("AdNetworkController has no async path — nothing to remove.");
                return;
            }

            // 1. The constructor overload built for the async interface.
            if (SourcePatcher.RemoveBlockContaining(path, AsyncInterface + " asyncAdNetwork"))
                log.Add("Removed the AdNetworkController async constructor");
            else
                log.Add("Could not find the async constructor in AdNetworkController — remove the " +
                        "overload taking IAsyncAdNetworkService by hand.");

            // 2. Initialize() back to a synchronous body. The logging and try/catch the async
            //    work introduced are worth keeping — they are an improvement on the bare v5
            //    one-liner, and a project that migrated by hand will already have this shape.
            if (SourcePatcher.ReplaceBlockBody(path, "public void Initialize()",
                    "            Message.Log(Tag.SDK, \"AdNetwork Initialization started...\");\n" +
                    "            try\n" +
                    "            {\n" +
                    "                adNetwork.Initialize(adUnitInfo.AppKey, InvokeCompletionOnMainThread);\n" +
                    "            }\n" +
                    "            catch (System.Exception ex)\n" +
                    "            {\n" +
                    "                IsInitialized = false;\n" +
                    "                Message.LogError(Tag.SDK, $\"AdNetwork initialization failed: {ex.Message}\");\n" +
                    "            }"))
                log.Add("Rewrote AdNetworkController.Initialize() to call the synchronous path only");
            else
                log.Add("Could not rewrite AdNetworkController.Initialize() — make it call " +
                        "adNetwork.Initialize(adUnitInfo.AppKey, InvokeCompletionOnMainThread) and drop " +
                        "the asyncAdNetwork branch by hand.");

            // 3. The field, last, so the checks above still had it to find.
            var fields = SourcePatcher.RemoveLinesContaining(path, AsyncInterface + " asyncAdNetwork;");
            if (fields > 0) log.Add("Removed the AdNetworkController asyncAdNetwork field");
        }

        private static void RemoveAsyncInterface(List<string> log)
        {
            var path = MeticaPaths.IAdNetworkService;
            if (!SourcePatcher.Contains(path, "interface " + AsyncInterface))
            {
                log.Add($"{AsyncInterface} is already gone.");
                return;
            }

            if (!SourcePatcher.RemoveBlockContaining(path, "interface " + AsyncInterface))
            {
                log.Add($"Could not remove {AsyncInterface} from IAdNetworkService.cs — delete the " +
                        "interface by hand.");
                return;
            }

            log.Add($"Removed {AsyncInterface} from IAdNetworkService.cs");

            // The Tasks using was only there for the async signature.
            if (!SourcePatcher.Contains(path, "Task"))
                SourcePatcher.RemoveLinesContaining(path, "using System.Threading.Tasks;");
        }
    }
}
