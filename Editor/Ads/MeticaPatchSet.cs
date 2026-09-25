using System.Collections.Generic;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Every edit the Ads flow makes to existing GD SDK files, in step order. Run inside
    /// <see cref="SourcePatcher.InMemory"/> on stock copies, the result is what a correctly
    /// patched file looks like; that is how the modified-file check tells "patched by this
    /// tool" apart from "changed by hand".
    /// </summary>
    internal static class MeticaPatchSet
    {
        /// <param name="callback">
        /// Whether the async init path is removed (<see cref="MeticaIntegrationMode.IsCallback"/>).
        /// </param>
        public static List<string> ApplyAll(bool callback)
        {
            var log = new List<string>();
            log.AddRange(PatchCoreFilesStep.ApplyPatches());
            log.AddRange(RemoteSwitchStep.ApplyPatches());
            if (callback) log.AddRange(AsyncCleanupStep.ApplyPatches());
            log.AddRange(FinishUpStep.ApplyPatches());
            return log;
        }
    }
}
