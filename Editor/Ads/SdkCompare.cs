using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    internal enum ChangeKind
    {
        /// <summary>In both, but different.</summary>
        Changed,

        /// <summary>In the project only.</summary>
        Added,

        /// <summary>In the original only.</summary>
        Missing
    }

    internal sealed class SdkFileChange
    {
        /// <summary>Relative to the GD SDK root; the services folder is "../Monetization/...".</summary>
        public string Path;

        /// <summary>Project-relative ("Assets/GDMonetization/..."), whether or not it exists.</summary>
        public string ProjectPath;

        public ChangeKind Kind;

        /// <summary>An added or removed line mentions Metica (an added file: anywhere in it).</summary>
        public bool Metica;

        /// <summary>Exactly what this tool's patches or templates produce — an expected change.</summary>
        public bool ByTool;

        public int AddedLines, RemovedLines;

        /// <summary>Every line of the original and project file, marked same / added / removed.</summary>
        public List<LineDiff.Line> Diff;

        public string Name => System.IO.Path.GetFileName(Path);
    }

    internal sealed class SdkCompareResult
    {
        /// <summary>Set when there is nothing to compare; the rest is then empty.</summary>
        public string Problem;

        /// <summary>The Version string the project's MonetizationInitializeOnLoad declares.</summary>
        public string Declared;

        /// <summary>v6's BaseVersion — the non-Metica GD SDK it is built on. Shown, never used to choose.</summary>
        public string BaseVersion;

        /// <summary>The release compared against, or null when it could not be determined.</summary>
        public string Version;

        /// <summary>The release was picked by hand, because the declared Version matches none.</summary>
        public bool ChosenByHand;

        /// <summary>Whether Monetization/Scripts/Services was compared (it is absent in some projects).</summary>
        public bool ServicesCompared;

        public int Compared, Identical;

        public readonly List<SdkFileChange> Changes = new List<SdkFileChange>();
    }

    /// <summary>
    /// Compares the project's GD SDK code with the original release it declares, file by file.
    /// Reads only; never changes a file.
    ///
    /// <para>The release is the one whose Version string the project's
    /// MonetizationInitializeOnLoad declares — nothing else decides it. When the declared
    /// string matches no release, the release picked by hand
    /// (<see cref="ModifiedFileCheck.ChosenVersion"/>) is used, if any.</para>
    ///
    /// <para>Covered: the code and text files of <see cref="StockFiles.Extensions"/> under the
    /// GD SDK root and Monetization/Scripts/Services next to it. Assets, prefabs and scenes
    /// hold each game's own ids and settings, so they are not compared.</para>
    /// </summary>
    internal static class SdkCompare
    {
        private const string ServicesFolder = "Scripts/Services";

        /// <summary>Compares the open project.</summary>
        /// <param name="details">
        /// False counts changed files only — no diffs, no Metica / tool tags. Cheap enough for
        /// the Home screen, which refreshes on every focus.
        /// </param>
        public static SdkCompareResult Run(bool details = true)
        {
            var stock = StockFiles.Packaged;
            if (stock == null) return new SdkCompareResult { Problem = "The original GD SDK copies are missing from this tool." };
            if (!MeticaPaths.HasGDSdk) return new SdkCompareResult { Problem = "No GD SDK in this project." };

            var gdRoot = MeticaPaths.GDRoot;
            if (!details)
                return Run(stock, MeticaPaths.ToAbsolute(gdRoot), gdRoot, ModifiedFileCheck.ChosenVersion,
                    MeticaIntegrationMode.IsCallback, null, false);

            var templates = WrapperFilesStep.Files()
                .Where(file => file.target.StartsWith(gdRoot + "/", StringComparison.Ordinal))
                .ToDictionary(
                    file => file.target.Substring(gdRoot.Length + 1),
                    file => MeticaPaths.TemplatesRoot == null
                        ? null
                        : ReadOrNull(MeticaPaths.ToAbsolute(MeticaPaths.TemplatesRoot + "/" + file.template)));

            return Run(stock, MeticaPaths.ToAbsolute(gdRoot), gdRoot, ModifiedFileCheck.ChosenVersion,
                MeticaIntegrationMode.IsCallback, templates);
        }

        /// <param name="gdRootAbsolute">The GD SDK root on disk.</param>
        /// <param name="gdRoot">The same, project-relative ("Assets/GDMonetization").</param>
        /// <param name="chosenVersion">Used only when the declared Version matches no release.</param>
        /// <param name="callback">Which async-init choice the tool's patches follow.</param>
        /// <param name="templates">The tool's wrapper templates by path relative to the GD SDK root.</param>
        /// <param name="details">False: count changed files only, see <see cref="Run(bool)"/>.</param>
        internal static SdkCompareResult Run(StockFiles stock, string gdRootAbsolute, string gdRoot,
            string chosenVersion, bool callback, IReadOnlyDictionary<string, string> templates, bool details = true)
        {
            var result = new SdkCompareResult();

            var versionText = ReadOrNull(Path.Combine(gdRootAbsolute, StockFiles.VersionFile));
            result.Declared = ModifiedFileCheck.ReadReportedVersion(versionText);
            result.BaseVersion = ModifiedFileCheck.ReadBaseVersion(versionText);
            result.Version = ModifiedFileCheck.DetectVersion(stock, versionText);

            if (result.Version == null && chosenVersion != null && stock.Versions.Contains(chosenVersion))
            {
                result.Version = chosenVersion;
                result.ChosenByHand = true;
            }

            if (result.Version == null)
            {
                result.Problem = result.Declared == null
                    ? "The GD SDK version can't be read — MonetizationInitializeOnLoad.cs has no Version."
                    : $"No original GD SDK declares \"{result.Declared}\" — pick the release to compare with.";
                return result;
            }

            var version = result.Version;
            var project = ProjectFiles(gdRootAbsolute, out var servicesAbsolute);
            result.ServicesCompared = servicesAbsolute != null;

            var stockPaths = stock.PathsIn(version)
                .Where(path => result.ServicesCompared || !path.StartsWith(StockFiles.ServicesPrefix, StringComparison.Ordinal))
                .ToList();

            // Which patched files are exactly "original + this tool's patches".
            var tooled = new HashSet<string>();
            if (details)
            {
                var patched = stock.PatchedPaths.Where(p => p != StockFiles.VersionFile && stockPaths.Contains(p)).ToList();
                var originals = patched.ToDictionary(p => p, p => stock.Get(version, p));
                var current = patched.ToDictionary(p => p, p => project.TryGetValue(p, out var file) ? ReadOrNull(file) : null);

                tooled.UnionWith(ModifiedFileCheck.Classify(originals, current, callback)
                    .Where(c => c.state == FileState.Patched)
                    .Select(c => c.path));

                // Earlier versions of this tool added a using at the top instead of after the
                // last one; the same edit in another using order is still the tool's.
                var patchedOriginals = ModifiedFileCheck.PatchInMemory(originals, callback);
                var patchedCurrent = ModifiedFileCheck.PatchInMemory(current, callback);
                foreach (var path in patched.Where(p => !tooled.Contains(p) && current[p] != null && patchedOriginals[p] != null))
                    if (SameIgnoringUsingOrder(current[path], patchedOriginals[path])
                        || SameIgnoringUsingOrder(patchedCurrent[path], patchedOriginals[path]))
                        tooled.Add(path);
            }

            foreach (var path in stockPaths.Union(project.Keys).OrderBy(p => p, StringComparer.Ordinal))
            {
                result.Compared++;
                var original = stock.StoredName(version, path);
                project.TryGetValue(path, out var file);
                var text = file == null ? null : ReadOrNull(file);

                if (original != null && text != null && StockFiles.HashName(text) == original)
                {
                    result.Identical++;
                    continue;
                }

                var change = new SdkFileChange
                {
                    Path = path,
                    ProjectPath = ProjectPath(gdRoot, path),
                    Kind = text == null ? ChangeKind.Missing : original == null ? ChangeKind.Added : ChangeKind.Changed
                };
                result.Changes.Add(change);
                if (!details) continue;

                change.Diff = LineDiff.Compare(original == null ? null : stock.Get(version, path), text);

                change.AddedLines = change.Diff.Count(l => l.Op == LineDiff.Op.Added);
                change.RemovedLines = change.Diff.Count(l => l.Op == LineDiff.Op.Removed);
                change.Metica = change.Diff.Any(l => l.Op != LineDiff.Op.Same
                                                     && l.Text.IndexOf("metica", StringComparison.OrdinalIgnoreCase) >= 0);
                change.ByTool = tooled.Contains(path)
                                || (text != null && templates != null && templates.TryGetValue(path, out var template)
                                    && template != null && FileText.Same(template, text));
            }

            return result;
        }

        /// <summary>
        /// The project's covered files, keyed like the stock paths. The services folder is only
        /// included when the project has it.
        /// </summary>
        private static Dictionary<string, string> ProjectFiles(string gdRootAbsolute, out string servicesAbsolute)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            Collect(gdRootAbsolute, string.Empty, files);

            var parent = Path.GetDirectoryName(gdRootAbsolute.TrimEnd('/', '\\'));
            servicesAbsolute = parent == null ? null : Path.Combine(parent, "Monetization", ServicesFolder);
            if (servicesAbsolute != null && Directory.Exists(servicesAbsolute))
                Collect(servicesAbsolute, StockFiles.ServicesPrefix + ServicesFolder + "/", files);
            else
                servicesAbsolute = null;

            return files;
        }

        private static void Collect(string folder, string prefix, Dictionary<string, string> files)
        {
            if (!Directory.Exists(folder)) return;

            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!StockFiles.IsCovered(file)) continue;
                var relative = file.Substring(folder.Length).TrimStart('/', '\\').Replace('\\', '/');
                files[prefix + relative] = file;
            }
        }

        /// <summary>
        /// Equal once the using lines at the top of each file are sorted — the rest must match
        /// line for line. Using order never changes what the code does.
        /// </summary>
        internal static bool SameIgnoringUsingOrder(string a, string b)
        {
            string Canonical(string text)
            {
                var lines = LineDiff.LinesOf(text);
                var head = 0;
                while (head < lines.Length && (lines[head].StartsWith("using ", StringComparison.Ordinal)
                                               || lines[head].Trim().Length == 0))
                    head++;

                var usings = lines.Take(head).Where(l => l.Trim().Length > 0).OrderBy(l => l, StringComparer.Ordinal);
                return string.Join("\n", usings.Concat(lines.Skip(head)));
            }

            return a != null && b != null && Canonical(a) == Canonical(b);
        }

        /// <summary>Project-relative path of a stock path.</summary>
        internal static string ProjectPath(string gdRoot, string path)
        {
            if (!path.StartsWith(StockFiles.ServicesPrefix, StringComparison.Ordinal))
                return gdRoot + "/" + path;

            var slash = gdRoot.LastIndexOf('/');
            var parent = slash < 0 ? string.Empty : gdRoot.Substring(0, slash + 1);
            return parent + "Monetization/" + path.Substring(StockFiles.ServicesPrefix.Length);
        }

        private static string ReadOrNull(string absolute)
        {
            try
            {
                return absolute != null && File.Exists(absolute) ? File.ReadAllText(absolute) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
