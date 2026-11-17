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
        /// <summary>
        /// The two remote switches a patched project can have — "MainAdNetwork" only, or with
        /// the old "UseMetica" key kept. A file counts as the tool's when it matches either.
        /// </summary>
        public static readonly RemoteSwitchForm[] RemoteSwitchForms = { RemoteSwitchForm.New, RemoteSwitchForm.Both };

        /// <param name="remoteSwitch">The remote switch to patch to: New or Both.</param>
        public static List<string> ApplyAll(RemoteSwitchForm remoteSwitch)
        {
            var log = new List<string>();
            log.AddRange(PatchCoreFilesStep.ApplyPatches(remoteSwitch));
            log.AddRange(RemoteSwitchStep.ApplyPatches());
            log.AddRange(FinishUpStep.ApplyPatches());
            return log;
        }
    }
}
