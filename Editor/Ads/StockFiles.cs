using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The untouched GD SDK files this tool edits, one set per v5 release, as exported by
    /// Tools~/ExportStockFiles.ps1 into Editor/Ads/Stock/. Read-only.
    /// </summary>
    internal sealed class StockFiles
    {
        /// <summary>Where the GD SDK version is read from. Stored, but never patched.</summary>
        public const string VersionFile = "Runtime/Scripts/MonetizationInitializeOnLoad.cs";

        // Filled by the JSON deserializer.
#pragma warning disable 0649
        private sealed class Manifest
        {
            public string[] paths;
            public VersionEntry[] versions;
        }

        private sealed class VersionEntry
        {
            public string version;
            public string reports;
            public FileEntry[] files;
        }

        private sealed class FileEntry
        {
            public string path;
            public string file;
        }
#pragma warning restore 0649

        private readonly string _root;
        private readonly Manifest _manifest;
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();

        private StockFiles(string root, Manifest manifest)
        {
            _root = root;
            _manifest = manifest;
        }

        private static StockFiles _packaged;

        /// <summary>The copies shipped in this package, or null if they cannot be found.</summary>
        public static StockFiles Packaged
        {
            get
            {
                if (_packaged != null) return _packaged;
                if (MeticaPaths.ToolRoot == null) return null;
                return _packaged = Load(MeticaPaths.ToAbsolute(MeticaPaths.ToolRoot + "/Editor/Ads/Stock"));
            }
        }

        /// <summary>Loads a Stock folder by absolute path, or null if it has no manifest.</summary>
        public static StockFiles Load(string absoluteStockFolder)
        {
            var manifestPath = Path.Combine(absoluteStockFolder, "manifest.json");
            if (!File.Exists(manifestPath)) return null;

            var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath));
            return manifest?.versions == null ? null : new StockFiles(absoluteStockFolder, manifest);
        }

        /// <summary>Every tracked path, relative to the GD SDK root.</summary>
        public IReadOnlyList<string> Paths => _manifest.paths;

        /// <summary>Releases with a stock set, oldest first ("5.0.0" … "5.5.0").</summary>
        public IEnumerable<string> Versions => _manifest.versions.Select(v => v.version);

        /// <summary>Releases whose MonetizationInitializeOnLoad reports this Version string.</summary>
        public IEnumerable<string> VersionsReporting(string reported) =>
            _manifest.versions.Where(v => v.reports == reported).Select(v => v.version);

        /// <summary>
        /// The normalized stock text of <paramref name="path"/> in <paramref name="version"/>,
        /// or null when that release does not have the file.
        /// </summary>
        public string Get(string version, string path)
        {
            var file = _manifest.versions.FirstOrDefault(v => v.version == version)?
                .files?.FirstOrDefault(f => f.path == path)?.file;
            if (string.IsNullOrEmpty(file)) return null;

            if (!_texts.TryGetValue(file, out var text))
            {
                var absolute = Path.Combine(_root, "Files", file);
                text = File.Exists(absolute) ? FileText.Normalize(File.ReadAllText(absolute)) : null;
                _texts[file] = text;
            }

            return text;
        }
    }
}
