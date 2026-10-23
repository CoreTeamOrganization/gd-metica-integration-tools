using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>Reads and adds dependencies in Packages/manifest.json, as text.</summary>
    internal static class ManifestDependencies
    {
        /// <summary>The dependency's value ("file:…", a git URL, a version), or null.</summary>
        public static string Value(string packageName)
        {
            if (!MeticaPaths.FileExists(MeticaPaths.PackagesManifest)) return null;

            var match = Regex.Match(File.ReadAllText(MeticaPaths.ToAbsolute(MeticaPaths.PackagesManifest)),
                "\"" + Regex.Escape(packageName) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Adds <c>"name": "value"</c> as the first entry of "dependencies". False when the
        /// manifest has no dependencies block, or already names the package.
        /// </summary>
        public static bool Add(string packageName, string value, out string error)
        {
            error = null;
            var path = MeticaPaths.ToAbsolute(MeticaPaths.PackagesManifest);
            if (!File.Exists(path)) { error = "Packages/manifest.json not found."; return false; }
            if (Value(packageName) != null) return true;

            var added = AddTo(File.ReadAllText(path), packageName, value);
            if (added == null) { error = "No \"dependencies\" block in Packages/manifest.json."; return false; }

            File.WriteAllText(path, added);
            return true;
        }

        /// <summary>The manifest text with the entry added first in "dependencies"; null without that block.</summary>
        internal static string AddTo(string manifest, string packageName, string value)
        {
            var block = Regex.Match(manifest, "\"dependencies\"\\s*:\\s*\\{");
            if (!block.Success) return null;

            var newline = manifest.Contains("\r\n") ? "\r\n" : "\n";
            var entry = $"{newline}    \"{packageName}\": \"{value}\",";
            var at = block.Index + block.Length;

            // An empty block needs no trailing comma.
            if (Regex.IsMatch(manifest.Substring(at), "^\\s*\\}")) entry = entry.TrimEnd(',');

            return manifest.Insert(at, entry);
        }
    }

    /// <summary>
    /// com.metica.analytics.abstractions — the interfaces Metica's analytics code compiles
    /// against once METICA_ANALYTICS is on. Not on any registry: it ships as a .tgz, kept in
    /// the project's Packages folder and referenced by a file: dependency.
    /// </summary>
    internal static class AnalyticsAbstractions
    {
        public const string PackageName = "com.metica.analytics.abstractions";
        public const string AssemblyName = "MeticaAnalyticsAbstractions";
        public const string FileName = "com.metica.analytics.abstractions-1.0.0.tgz";

        // The same 1.0.0 .tgz in two Drive places, tried in order.
        private static readonly string[] Urls =
        {
            "https://drive.usercontent.google.com/u/0/uc?id=1H6WYJqsIjQBls3H3giZoEKnIdDje4-Lj&export=download",
            "https://drive.usercontent.google.com/u/0/uc?id=19ck0x0HWz79SthgR8ZY0fS48QUJ4xVij&export=download"
        };

        /// <summary>The Drive folder with the .tgz, for downloading it by hand.</summary>
        public const string FolderUrl = "https://drive.google.com/drive/folders/1yWxZ03FbBNMoCovxQcoLqCKf-DwQiCNi";

        public static string ProjectFile => "Packages/" + FileName;

        public static bool IsLoaded => AssemblyLoaded(AssemblyName);

        /// <summary>
        /// The manifest names it, or something else in the project already defines its
        /// assembly — GD SDK 6.2.2+ carries its own copy, and a second one would not compile.
        /// </summary>
        public static bool Present => ManifestDependencies.Value(PackageName) != null || IsLoaded;

        /// <summary>Downloads the .tgz into Packages/, checks it, and adds it to the manifest.</summary>
        public static void Install(string logTitle)
        {
            var temp = Path.Combine(Path.GetTempPath(), FileName);
            string error = null;
            foreach (var url in Urls)
            {
                // A Drive link that stopped being shared serves a sign-in page, not the .tgz.
                if (Downloads.TryDownload(url, temp, "Downloading Metica analytics abstractions", out error)
                    && PackageNameIn(temp) == PackageName)
                {
                    InstallFrom(temp, logTitle);
                    return;
                }

                error = error ?? "the download was not the package";
            }

            MeticaIntegrationLog.Record(logTitle,
                $"Download failed: {error}. Download it from {FolderUrl} and use Pick the .tgz.");
        }

        /// <summary>Same, from a .tgz the developer picked.</summary>
        public static void InstallFromPickedFile(string logTitle)
        {
            var picked = EditorUtility.OpenFilePanel("Metica analytics abstractions (.tgz)", "", "tgz");
            if (string.IsNullOrEmpty(picked)) return;
            InstallFrom(picked, logTitle);
        }

        private static void InstallFrom(string tgz, string logTitle)
        {
            var name = PackageNameIn(tgz);
            if (name != PackageName)
            {
                MeticaIntegrationLog.Record(logTitle, name == null
                    ? "The file isn't a Unity package (.tgz with package/package.json) — nothing added."
                    : $"The file is {name}, not {PackageName} — nothing added.");
                return;
            }

            var target = MeticaPaths.ToAbsolute(ProjectFile);
            File.Copy(tgz, target, true);

            if (!ManifestDependencies.Add(PackageName, "file:" + FileName, out var error))
            {
                MeticaIntegrationLog.Record(logTitle, error);
                return;
            }

            MeticaIntegrationLog.Record(logTitle, $"Added {ProjectFile} and its manifest entry");
            Client.Resolve();
        }

        /// <summary>The "name" in the package/package.json inside a .tgz, or null.</summary>
        internal static string PackageNameIn(string tgzPath)
        {
            try
            {
                using (var file = File.OpenRead(tgzPath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var tar = new MemoryStream())
                {
                    gzip.CopyTo(tar);
                    var bytes = tar.ToArray();

                    // Tar: 512-byte headers, name at 0 (100 bytes), size in octal at 124 (12 bytes).
                    for (var offset = 0; offset + 512 <= bytes.Length;)
                    {
                        var entry = Encoding.ASCII.GetString(bytes, offset, 100).TrimEnd('\0');
                        if (entry.Length == 0) break;

                        var sizeText = Encoding.ASCII.GetString(bytes, offset + 124, 12).Trim('\0', ' ');
                        var size = string.IsNullOrEmpty(sizeText) ? 0 : Convert.ToInt32(sizeText, 8);

                        if (entry == "package/package.json")
                        {
                            var json = Encoding.UTF8.GetString(bytes, offset + 512, size);
                            var match = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                            return match.Success ? match.Groups[1].Value : null;
                        }

                        offset += 512 + (size + 511) / 512 * 512;
                    }
                }
            }
            catch
            {
                // Not gzip, or not a tar inside — not a package.
            }

            return null;
        }

        internal static bool AssemblyLoaded(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name);
    }

    /// <summary>
    /// com.gamedistrict.metica-genre-creator — writes a game's genre files (scripts and the
    /// ScriptableObject) from an Excel event schema. The tool installs it and hands it the game
    /// name and the Excel file; the files themselves are always made by its own code.
    ///
    /// <para>That code has no public entry point other than its window, so it is called by name
    /// (reflection): GenreExcelParser.GetSheetNames / Parse and GenreAssetCreator.CreateFiles,
    /// the same calls its window makes. Checked against 1.0.0 and 2.0.0. If a later version
    /// renames them, <see cref="ApiProblem"/> says so instead of guessing.</para>
    /// </summary>
    internal static class GenreCreatorPackage
    {
        public const string PackageName = "com.gamedistrict.metica-genre-creator";
        public const string Version = "2.0.0";
        public const string GitUrl = "https://github.com/CoreTeamOrganization/gd-analytics-genre-creator.git#v" + Version;

        public const string RuntimeAssembly = "GameDistrict.MeticaAnalytics.Runtime";
        public const string EditorAssembly = "GameDistrict.MeticaAnalytics.Editor";

        private const string Namespace = "GameDistrict.MeticaAnalytics.Editor.";

        /// <summary>In the manifest, or embedded in Packages/ (as some projects have it).</summary>
        public static bool Present =>
            ManifestDependencies.Value(PackageName) != null || MeticaPaths.DirectoryExists("Packages/" + PackageName);

        public static bool IsLoaded =>
            AnalyticsAbstractions.AssemblyLoaded(RuntimeAssembly) && AnalyticsAbstractions.AssemblyLoaded(EditorAssembly);

        /// <summary>The installed version, from Package Manager, or null.</summary>
        public static string InstalledVersion
        {
            get
            {
                var assembly = EditorAssemblyLoaded();
                return assembly == null ? null : UnityEditor.PackageManager.PackageInfo.FindForAssembly(assembly)?.version;
            }
        }

        public static void Install(string logTitle)
        {
            MeticaIntegrationLog.Record(logTitle, $"Adding the genre creator {Version} from {GitUrl}");
            PackageRequests.Track(Client.Add(GitUrl), "Adding the Metica genre creator", request =>
            {
                MeticaIntegrationLog.Record(logTitle, request.Status == StatusCode.Success
                    ? $"Added the genre creator {Version}"
                    : $"Couldn't add the genre creator: {request.Error?.message} — Unity needs git on the PATH " +
                      "and access to the CoreTeamOrganization GitHub org.");
            });
        }

        // ── Its code, called by name ───────────────────────────────────────────

        /// <summary>Null when the calls this tool makes are all there.</summary>
        public static string ApiProblem
        {
            get
            {
                if (!IsLoaded) return "The genre creator isn't installed.";
                if (Method("GenreExcelParser", "GetSheetNames") == null || Method("GenreExcelParser", "Parse") == null
                    || Method("GenreAssetCreator", "CreateFiles") == null)
                    return $"Unsupported genre creator version ({InstalledVersion ?? "unknown"}) — " +
                           $"this tool works with {Version}.";
                return null;
            }
        }

        public static List<string> SheetNames(string xlsxPath) =>
            (Method("GenreExcelParser", "GetSheetNames")?.Invoke(null, new object[] { xlsxPath }) as List<string>)
            ?? new List<string>();

        /// <summary>
        /// Reads one sheet's events. Returns the genre creator's own event list (to hand back to
        /// <see cref="CreateFiles"/>) and how many events it holds.
        /// </summary>
        public static (object events, int count) Parse(string xlsxPath, int sheetIndex)
        {
            var tuple = Invoke(Method("GenreExcelParser", "Parse"), xlsxPath, sheetIndex);
            var events = tuple?.GetType().GetField("Item2")?.GetValue(tuple);
            return (events, (events as ICollection)?.Count ?? 0);
        }

        public static void CreateFiles(string genre, object events) =>
            Invoke(Method("GenreAssetCreator", "CreateFiles"), genre, events);

        private static MethodInfo Method(string type, string name) =>
            EditorAssemblyLoaded()?.GetType(Namespace + type)
                ?.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>Invokes, and rethrows the genre creator's own exception rather than the wrapper.</summary>
        private static object Invoke(MethodInfo method, params object[] args)
        {
            if (method == null) throw new InvalidOperationException(ApiProblem ?? "Genre creator call not found.");

            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw e.InnerException;
            }
        }

        private static Assembly EditorAssemblyLoaded() =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == EditorAssembly);
    }
}
