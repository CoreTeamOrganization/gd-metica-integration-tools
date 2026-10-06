using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>One Metica SDK install the Metica SDK step has to remove before adding the target.</summary>
    internal sealed class MeticaInstall
    {
        public enum Source
        {
            /// <summary>A dependency in Packages/manifest.json (git or registry).</summary>
            PackageManager,

            /// <summary>A folder: the SDK under Assets, or an embedded package under Packages.</summary>
            Folder,

            /// <summary>A single file — v1's configuration asset.</summary>
            File
        }

        public Source Kind;

        /// <summary>Package name for <see cref="Source.PackageManager"/>; project path otherwise.</summary>
        public string Target;

        /// <summary>Version if known, else null.</summary>
        public string Version;

        public string Label =>
            Kind == Source.PackageManager
                ? $"Package Manager: {Target}{(Version == null ? "" : " " + Version)}"
                : $"{Target}{(Kind == Source.Folder ? "/" : "")}{(Version == null ? "" : $"  (Metica {Version})")}";
    }

    /// <summary>
    /// Every Metica SDK in a project other than the target version at Assets/MeticaSdk — in
    /// Assets, in the Package Manager, embedded under Packages — and their removal.
    ///
    /// <para>Only the SDK itself counts: the packages <see cref="SdkPackageNames"/> name (v1
    /// shipped as com.metica.unity, v2 as com.metica.sdk.unity) and v1's configuration asset.
    /// Packages that merely contain "metica" — com.metica.analytics.abstractions, which Genre
    /// analytics uses, and this tool, com.gamedistrict.metica-integration-tools — are not
    /// Metica SDK installs and are never touched.</para>
    /// </summary>
    internal static class MeticaInstalls
    {
        public static readonly string[] SdkPackageNames = { "com.metica.unity", "com.metica.sdk.unity" };

        /// <summary>Created by Metica v1 under Assets; its type goes away with the v1 package.</summary>
        public const string V1ConfigAsset = "Assets/Metica/Data/MeticaSdkConfiguration.asset";

        private const string Manifest = "Packages/manifest.json";

        private static readonly Regex PackageName = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"");
        private static readonly Regex PackageVersion = new Regex("\"version\"\\s*:\\s*\"([^\"]+)\"");

        /// <param name="targetVersion">
        /// The pinned version: Assets/MeticaSdk at exactly this version is the install the step
        /// wants, so it is not listed. Null (unreadable target) never lists Assets/MeticaSdk.
        /// </param>
        public static List<MeticaInstall> Find(string targetVersion)
        {
            var found = new List<MeticaInstall>();

            // Package Manager dependencies.
            var manifest = Read(Manifest);
            if (manifest != null)
                found.AddRange(FromManifest(manifest)
                    .Select(dependency => new MeticaInstall
                    {
                        Kind = MeticaInstall.Source.PackageManager,
                        Target = dependency.name,
                        Version = dependency.version
                    }));

            // Embedded packages: a folder under Packages/ holding the SDK's package.json.
            var packages = MeticaPaths.ToAbsolute("Packages");
            if (Directory.Exists(packages))
            {
                foreach (var folder in Directory.GetDirectories(packages))
                {
                    var (name, version) = ReadPackageJson(Path.Combine(folder, "package.json"));
                    if (SdkPackageNames.Contains(name))
                        found.Add(new MeticaInstall
                        {
                            Kind = MeticaInstall.Source.Folder,
                            Target = "Packages/" + Path.GetFileName(folder),
                            Version = version
                        });
                }
            }

            // The SDK imported under Assets, wherever it landed.
            foreach (var path in AssetDatabase.FindAssets("package", new[] { "Assets" })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => path.EndsWith("/package.json", StringComparison.Ordinal))
                         .Distinct())
            {
                var (name, version) = ReadPackageJson(MeticaPaths.ToAbsolute(path));
                if (!SdkPackageNames.Contains(name)) continue;

                var folder = path.Substring(0, path.Length - "/package.json".Length);
                // The target install itself — or, when the target can't be read, left alone
                // rather than deleted on a guess.
                if (folder == MeticaPaths.MeticaSdkRoot && (targetVersion == null || version == targetVersion)) continue;

                found.Add(new MeticaInstall { Kind = MeticaInstall.Source.Folder, Target = folder, Version = version });
            }

            if (MeticaPaths.FileExists(V1ConfigAsset))
                found.Add(new MeticaInstall { Kind = MeticaInstall.Source.File, Target = V1ConfigAsset, Version = "v1" });

            return found;
        }

        /// <summary>The SDK packages a manifest.json depends on, with the version a git URL pins.</summary>
        internal static List<(string name, string version)> FromManifest(string manifestJson)
        {
            var result = new List<(string, string)>();
            foreach (var name in SdkPackageNames)
            {
                var match = Regex.Match(manifestJson, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"");
                if (!match.Success) continue;

                var source = match.Groups[1].Value;
                var hash = source.LastIndexOf('#');
                var version = hash >= 0 ? source.Substring(hash + 1).TrimStart('v') : source;
                result.Add((name, version.Length == 0 ? null : version));
            }
            return result;
        }

        /// <summary>
        /// Asks once, listing everything, then removes it all: folders and files through the
        /// AssetDatabase (Assets) or from disk (embedded packages), Package Manager entries
        /// through the Package Manager, one after another. <paramref name="done"/> runs at the
        /// end — only if the developer agreed.
        /// </summary>
        public static void Remove(List<MeticaInstall> installs, string title, Action done)
        {
            if (installs.Count == 0) return;

            var lines = new List<string>();
            foreach (var install in installs)
            {
                lines.Add(install.Kind == MeticaInstall.Source.Folder ? install.Label + "  (the whole folder)" : install.Label);
                if (install.Kind == MeticaInstall.Source.Folder && install.Target.StartsWith("Assets/", StringComparison.Ordinal))
                    lines.AddRange(DeleteConfirm.Contents(install.Target).Select(path => "    " + path));
            }

            if (!DeleteConfirm.Ask(title, lines)) return;

            foreach (var install in installs.Where(i => i.Kind != MeticaInstall.Source.PackageManager))
            {
                if (install.Target.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(install.Target);
                    if (install.Kind == MeticaInstall.Source.File) DeleteEmptyParents(install.Target);
                }
                else
                {
                    FileUtil.DeleteFileOrDirectory(MeticaPaths.ToAbsolute(install.Target));
                }

                MeticaIntegrationLog.Record(title, $"Removed {install.Label}");
            }

            AssetDatabase.Refresh();

            var queue = new Queue<MeticaInstall>(installs.Where(i => i.Kind == MeticaInstall.Source.PackageManager));
            RemoveNext(queue, title, () =>
            {
                if (installs.Any(i => i.Target.StartsWith("Packages/", StringComparison.Ordinal))) Client.Resolve();
                done?.Invoke();
            });
        }

        private static void RemoveNext(Queue<MeticaInstall> queue, string title, Action done)
        {
            if (queue.Count == 0)
            {
                done();
                return;
            }

            var install = queue.Dequeue();
            PackageRequests.Track(Client.Remove(install.Target), $"Removing {install.Target}", request =>
            {
                MeticaIntegrationLog.Record(title, request.Status == StatusCode.Success
                    ? $"Removed {install.Label}"
                    : $"Could not remove {install.Target}: {request.Error?.message} — remove it in Window → Package Manager.");
                RemoveNext(queue, title, done);
            });
        }

        /// <summary>v1's config sat in Assets/Metica/Data; drop those folders once they are empty.</summary>
        private static void DeleteEmptyParents(string assetPath)
        {
            var folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            while (folder != null && folder != "Assets" && folder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                var absolute = MeticaPaths.ToAbsolute(folder);
                if (!Directory.Exists(absolute) || Directory.EnumerateFileSystemEntries(absolute).Any(e => !e.EndsWith(".meta"))) return;

                AssetDatabase.DeleteAsset(folder);
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }

        private static (string name, string version) ReadPackageJson(string absolute)
        {
            if (absolute == null || !System.IO.File.Exists(absolute)) return (null, null);
            var json = System.IO.File.ReadAllText(absolute);
            var name = PackageName.Match(json);
            var version = PackageVersion.Match(json);
            return (name.Success ? name.Groups[1].Value : null, version.Success ? version.Groups[1].Value : null);
        }

        private static string Read(string projectPath)
        {
            var absolute = MeticaPaths.ToAbsolute(projectPath);
            return absolute != null && System.IO.File.Exists(absolute) ? System.IO.File.ReadAllText(absolute) : null;
        }
    }
}
