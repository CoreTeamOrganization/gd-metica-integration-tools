using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    public enum FileState
    {
        /// <summary>Same as the original: the GD SDK release's copy, or our template.</summary>
        Stock,

        /// <summary>The original plus this tool's patches, and nothing else.</summary>
        Patched,

        /// <summary>Anything else — changed by hand.</summary>
        Modified,

        Missing
    }

    internal sealed class FileStatus
    {
        /// <summary>Project-relative path.</summary>
        public string Path;

        /// <summary>Written by this tool from a template, rather than a GD SDK file it patches.</summary>
        public bool IsWrapper;

        public FileState State;

        public string Name => System.IO.Path.GetFileName(Path);
    }

    internal sealed class FileCheck
    {
        /// <summary>The GD SDK release compared against, or null when it is not known.</summary>
        public string GdSdkVersion;

        /// <summary>True when <see cref="GdSdkVersion"/> was picked by hand, not detected.</summary>
        public bool VersionChosenByHand;

        /// <summary>The Version string MonetizationInitializeOnLoad reports, if it can be read.</summary>
        public string ReportedVersion;

        public readonly List<FileStatus> Files = new List<FileStatus>();

        public IEnumerable<FileStatus> Modified => Files.Where(f => f.State == FileState.Modified);
    }

    /// <summary>
    /// Tells, for every file the Ads flow patches or writes, whether it is stock, patched by
    /// this tool, modified by hand, or missing. Compares against the stock copies in
    /// Editor/Ads/Stock (GD SDK files) or this tool's templates (wrapper files), ignoring line
    /// endings and trailing whitespace. Reads only; never changes a file.
    /// </summary>
    internal static class ModifiedFileCheck
    {
        /// <summary>Meant to be edited by the game (its analytics and consent hooks), so never flagged.</summary>
        private const string EditableStandaloneFile = "MeticaAdsHooks";

        private static readonly Regex VersionPattern = new Regex("Version\\s*=\\s*\"([^\"]+)\"");

        private static string ChosenVersionKey =>
            $"GameDistrict.MeticaIntegrationTools.GdSdkVersion.{Application.dataPath.GetHashCode():X8}";

        /// <summary>
        /// The release picked by hand when it cannot be detected (version file missing,
        /// modified, or reporting an unknown version). Per project; null when never picked.
        /// </summary>
        public static string ChosenVersion
        {
            get
            {
                var value = EditorPrefs.GetString(ChosenVersionKey, string.Empty);
                return value.Length == 0 ? null : value;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) EditorPrefs.DeleteKey(ChosenVersionKey);
                else EditorPrefs.SetString(ChosenVersionKey, value);
            }
        }

        public static FileCheck Run()
        {
            var check = new FileCheck();

            if (MeticaPaths.HasGDSdk)
            {
                var stock = StockFiles.Packaged;
                if (stock != null) CheckGdSdkFiles(stock, check);
                CheckWrappers(WrapperFilesStep.Files(), check);
            }
            else
            {
                CheckWrappers(StandaloneRuntimeStep.Files
                    .Where(name => name != EditableStandaloneFile)
                    .Select(name => ($"Standalone/{name}.cs.txt", $"{MeticaPaths.StandaloneRoot}/{name}.cs")), check);
            }

            return check;
        }

        // ── GD SDK files ───────────────────────────────────────────────────────

        private static void CheckGdSdkFiles(StockFiles stock, FileCheck check)
        {
            var versionText = ReadProjectFile(MeticaPaths.InitializeOnLoad);
            check.ReportedVersion = ReadReportedVersion(versionText);
            check.GdSdkVersion = DetectVersion(stock, versionText);

            if (check.GdSdkVersion == null && ChosenVersion != null && stock.Versions.Contains(ChosenVersion))
            {
                check.GdSdkVersion = ChosenVersion;
                check.VersionChosenByHand = true;
            }

            if (check.GdSdkVersion == null) return;

            var paths = stock.Paths.Where(p => p != StockFiles.VersionFile).ToList();
            var original = paths.ToDictionary(p => p, p => stock.Get(check.GdSdkVersion, p));
            var project = paths.ToDictionary(p => p, p => ReadProjectFile(MeticaPaths.Combine(MeticaPaths.GDRoot, p)));

            foreach (var (path, state) in Classify(original, project, MeticaIntegrationMode.IsCallback))
                check.Files.Add(new FileStatus { Path = MeticaPaths.Combine(MeticaPaths.GDRoot, path), State = state });
        }

        /// <summary>
        /// The release whose stock MonetizationInitializeOnLoad matches this one exactly, or null
        /// when the file is missing, modified, or reports a version with no stock set.
        /// </summary>
        internal static string DetectVersion(StockFiles stock, string versionFileText)
        {
            var reported = ReadReportedVersion(versionFileText);
            if (reported == null) return null;

            var matches = stock.VersionsReporting(reported)
                .Where(v => FileText.Same(stock.Get(v, StockFiles.VersionFile), versionFileText))
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        internal static string ReadReportedVersion(string versionFileText)
        {
            if (versionFileText == null) return null;
            var match = VersionPattern.Match(versionFileText);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Classifies each file (keyed by path relative to the GD SDK root; null = absent).
        /// "Patched" means the project file is the original with this tool's patches — all
        /// of them, or some, since patching the project copy in memory then lands on the same
        /// result as patching the original. A file with any other change is "modified", even
        /// when it happens to contain this tool's marker strings.
        /// </summary>
        internal static List<(string path, FileState state)> Classify(
            Dictionary<string, string> original, Dictionary<string, string> project, bool callback)
        {
            var patchedOriginal = PatchInMemory(original, callback);
            var patchedProject = PatchInMemory(project, callback);

            var states = new List<(string, FileState)>();
            foreach (var path in original.Keys)
            {
                original.TryGetValue(path, out var stockText);
                project.TryGetValue(path, out var projectText);

                FileState state;
                if (projectText == null) state = stockText == null ? FileState.Stock : FileState.Missing;
                else if (stockText == null) state = FileState.Modified;
                else if (FileText.Same(projectText, stockText)) state = FileState.Stock;
                else if (FileText.Same(projectText, patchedOriginal[path])
                         || FileText.Same(patchedProject[path], patchedOriginal[path])) state = FileState.Patched;
                else state = FileState.Modified;

                states.Add((path, state));
            }

            return states;
        }

        /// <summary>Runs every patch on copies of <paramref name="files"/> in memory.</summary>
        private static Dictionary<string, string> PatchInMemory(Dictionary<string, string> files, bool callback)
        {
            var memory = files
                .Where(file => file.Value != null)
                .ToDictionary(file => MeticaPaths.Combine(MeticaPaths.GDRoot, file.Key), file => file.Value);

            using (SourcePatcher.InMemory(memory))
                MeticaPatchSet.ApplyAll(callback);

            return files.Keys.ToDictionary(path => path,
                path => memory.TryGetValue(MeticaPaths.Combine(MeticaPaths.GDRoot, path), out var text) ? text : null);
        }

        // ── Wrapper files ──────────────────────────────────────────────────────

        private static void CheckWrappers(IEnumerable<(string template, string target)> files, FileCheck check)
        {
            foreach (var (template, target) in files)
            {
                var current = ReadProjectFile(target);
                var original = MeticaPaths.TemplatesRoot == null
                    ? null
                    : ReadProjectFile(MeticaPaths.TemplatesRoot + "/" + template);

                check.Files.Add(new FileStatus
                {
                    Path = target,
                    IsWrapper = true,
                    State = current == null ? FileState.Missing
                        : original != null && FileText.Same(current, original) ? FileState.Stock
                        : FileState.Modified
                });
            }
        }

        private static string ReadProjectFile(string projectRelativePath) =>
            MeticaPaths.FileExists(projectRelativePath)
                ? File.ReadAllText(MeticaPaths.ToAbsolute(projectRelativePath))
                : null;
    }
}
