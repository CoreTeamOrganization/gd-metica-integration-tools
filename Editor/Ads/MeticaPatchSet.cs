using System.Collections.Generic;
using System.Linq;

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
        /// The remote switches a patched project can have: MainAdNetwork only or with the old
        /// UseMetica kept, read from a Firebase parameter or from the AdsSettings JSON. A file
        /// counts as the tool's when it matches any of them.
        /// </summary>
        public static readonly (RemoteSwitchForm form, RemoteSwitchSource source)[] RemoteSwitches =
            new[] { RemoteSwitchForm.New, RemoteSwitchForm.Both }
                .SelectMany(form => new[] { RemoteSwitchSource.Parameter, RemoteSwitchSource.Json }.Select(source => (form, source)))
                .ToArray();

        /// <param name="form">The remote switch's keys: New or Both.</param>
        /// <param name="source">Where it reads them: Parameter or Json.</param>
        public static List<string> ApplyAll(RemoteSwitchForm form, RemoteSwitchSource source)
        {
            var log = new List<string>();
            log.AddRange(PatchCoreFilesStep.ApplyPatches(form, source));
            log.AddRange(RemoteSwitchStep.ApplyPatches());
            log.AddRange(FinishUpStep.ApplyPatches());
            return log;
        }
    }
}
