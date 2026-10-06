using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The untouched GD SDK code files of every v5 and v6 release, as exported by
    /// Tools~/ExportStockFiles.ps1 into Editor/Ads/Stock/. Read-only.
    ///
    /// <para>Paths are relative to the GD SDK root; the Monetization services folder next to
    /// it is "../Monetization/...". Each unique file is stored once, named by the hash of its
    /// normalized text (<see cref="HashName"/>), so comparing a project file with a release
    /// is comparing two names.</para>
    /// </summary>
    internal sealed class StockFiles
    {
        /// <summary>Where the GD SDK version is read from. Stored, but never patched.</summary>
        public const string VersionFile = "Runtime/Scripts/MonetizationInitializeOnLoad.cs";

        /// <summary>The services folder's path prefix: it sits next to the GD SDK root.</summary>
        public const string ServicesPrefix = "../Monetization/";

        /// <summary>The file types stored. Same list as $Extensions in the export script.</summary>
        public static readonly string[] Extensions =
            { ".cs", ".asmdef", ".asmref", ".java", ".kt", ".xml", ".json", ".gradle", ".m", ".mm", ".h", ".txt", ".md" };

        // Filled by the JSON deserializer.
#pragma warning disable 0649
        private sealed class Manifest
        {
            public int format;
            public string[] patched;
            public VersionEntry[] versions;

            /// <summary>Path → stored file name → releases that have that copy.</summary>
            public Dictionary<string, Dictionary<string, string[]>> files;
        }

        private sealed class VersionEntry
        {
            public string version;
            public string reports;
        }
#pragma warning restore 0649

        private readonly string _root;
        private readonly Manifest _manifest;

        /// <summary>Release → path → stored file name.</summary>
        private readonly Dictionary<string, Dictionary<string, string>> _byVersion;

        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();

        private StockFiles(string root, Manifest manifest)
        {
            _root = root;
            _manifest = manifest;
            _byVersion = manifest.versions.ToDictionary(v => v.version, v => new Dictionary<string, string>());

            foreach (var path in manifest.files)
            foreach (var copy in path.Value)
            foreach (var version in copy.Value)
                if (_byVersion.TryGetValue(version, out var files))
                    files[path.Key] = copy.Key;
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

        /// <summary>Loads a Stock folder by absolute path, or null if it has no usable manifest.</summary>
        public static StockFiles Load(string absoluteStockFolder)
        {
            var manifestPath = Path.Combine(absoluteStockFolder, "manifest.json");
            if (!File.Exists(manifestPath)) return null;

            var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath));
            return manifest?.format == 2 && manifest.versions != null && manifest.files != null
                ? new StockFiles(absoluteStockFolder, manifest)
                : null;
        }

        /// <summary>The files the tool patches, plus the version file — the modified-file check's set.</summary>
        public IReadOnlyList<string> PatchedPaths => _manifest.patched;

        /// <summary>Every path any release has.</summary>
        public IEnumerable<string> AllPaths => _manifest.files.Keys;

        /// <summary>Releases, oldest first ("5.0.0" … "6.2.5").</summary>
        public IEnumerable<string> Versions => _manifest.versions.Select(v => v.version);

        /// <summary>The Version string a release's MonetizationInitializeOnLoad declares.</summary>
        public string Reports(string version) =>
            _manifest.versions.FirstOrDefault(v => v.version == version)?.reports;

        /// <summary>Releases declaring this Version string. One at most: each release declares its own.</summary>
        public IEnumerable<string> VersionsReporting(string reported) =>
            _manifest.versions.Where(v => v.reports == reported).Select(v => v.version);

        /// <summary>The paths <paramref name="version"/> has.</summary>
        public IEnumerable<string> PathsIn(string version) =>
            _byVersion.TryGetValue(version, out var files) ? files.Keys : Enumerable.Empty<string>();

        /// <summary>
        /// The stored name of <paramref name="path"/> in <paramref name="version"/> — equal to
        /// <see cref="HashName"/> of the stock text — or null when that release lacks the file.
        /// </summary>
        public string StoredName(string version, string path) =>
            _byVersion.TryGetValue(version, out var files) && files.TryGetValue(path, out var name) ? name : null;

        /// <summary>
        /// The normalized stock text of <paramref name="path"/> in <paramref name="version"/>,
        /// or null when that release does not have the file.
        /// </summary>
        public string Get(string version, string path)
        {
            var file = StoredName(version, path);
            if (file == null) return null;

            if (!_texts.TryGetValue(file, out var text))
            {
                var absolute = Path.Combine(_root, "Files", file);
                text = File.Exists(absolute) ? FileText.Normalize(File.ReadAllText(absolute)) : null;
                _texts[file] = text;
            }

            return text;
        }

        /// <summary>
        /// The name a file's text is stored under: the first 16 hex digits of the SHA-256 of
        /// its normalized UTF-8 text, plus ".txt". Same as the export script.
        /// </summary>
        public static string HashName(string text)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(FileText.Normalize(text)));
                var hex = new StringBuilder(16);
                for (var i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
                return hex + ".txt";
            }
        }

        /// <summary>Whether the compare covers this file type.</summary>
        public static bool IsCovered(string path) =>
            Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }
}
