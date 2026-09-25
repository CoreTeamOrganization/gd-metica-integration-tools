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

        public override string Summary => "Remove the old async init path, if the project has one.";

        public override string Why =>
            "Projects that used Metica's older async init still carry IAsyncAdNetworkService and an " +
            "AdNetworkController overload for it. Nothing uses them once Metica starts through its " +
            "callback API. Leaving them is harmless. Nothing is removed while Admob or AppLovin still " +
            "implement the interface — that would break the build.";

        public override string ActionLabel => "Remove the async init path";

        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.IAdNetworkService,
            MeticaPaths.AdNetworkController
        };

        public override string ReviewHint =>
            "Often an empty diff. Otherwise, AdNetworkController.Initialize() should call the sync path only.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            if (MeticaPaths.RuntimeScripts == null)
            {
                result.Problem("GD SDK not found.");
                return result.Seal();
            }

            if (!MeticaIntegrationMode.IsCallback)
            {
                result.Note("Async mode — nothing to clean up");
                return result.Seal();
            }

            var leftovers = Leftovers().ToList();
            if (leftovers.Count == 0)
            {
                result.Note("Nothing to remove");
                return result.Seal();
            }

            var stillImplementing = StillImplementing().ToList();
            if (stillImplementing.Count > 0)
            {
                result.Note($"Kept — {string.Join(" and ", stillImplementing)} still use it");
                return result.Seal();
            }

            foreach (var leftover in leftovers)
                result.Problem($"Unused: {leftover}.");

            return result.Seal();
        }

        public override void Apply()
        {
            if (!MeticaIntegrationMode.IsCallback)
            {
                MeticaIntegrationLog.Record(Title, "Async mode — leaving the async init path in place.");
                return;
            }

            MeticaIntegrationLog.Record(Title, ApplyPatches());
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// The callback-mode edits, through <see cref="SourcePatcher"/> only. The mode check
        /// stays in <see cref="Apply"/>, since it reads an editor preference.
        /// </summary>
        internal static List<string> ApplyPatches()
        {
            var log = new List<string>();

            var stillImplementing = StillImplementing().ToList();
            if (stillImplementing.Count > 0)
            {
                log.Add($"Left the async path in place — {string.Join(" and ", stillImplementing)} still " +
                        "implement it. Removing it would break the build.");
                return log;
            }

            RemoveControllerAsyncPath(log);
            RemoveAsyncInterface(log);

            return log;
        }

        // ── Detection ──────────────────────────────────────────────────────────

        /// <summary>Async-only declarations that no longer have a user.</summary>
        private static IEnumerable<string> Leftovers()
        {
            if (SourcePatcher.Contains(MeticaPaths.IAdNetworkService, "interface " + AsyncInterface))
                yield return $"{AsyncInterface} interface";

            if (SourcePatcher.Contains(MeticaPaths.AdNetworkController, "asyncAdNetwork"))
                yield return "AdNetworkController async constructor and field";
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
