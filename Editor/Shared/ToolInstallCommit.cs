using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Adding (or updating) this tool through the Package Manager changes two project files —
    /// Packages/manifest.json gets its entry, Packages/packages-lock.json its pinned commit — and
    /// no step commits them. This finds those changes for Home's "Commit the tool install".
    ///
    /// <para>git commits whole files, so anything else uncommitted in the two files — another
    /// package added or bumped — is listed first, for the developer to decide.</para>
    /// </summary>
    internal static class ToolInstallCommit
    {
        public const string PackageName = "com.gamedistrict.metica-integration-tools";

        private const string Manifest = "Packages/manifest.json";
        private const string Lock = "Packages/packages-lock.json";

        internal sealed class State
        {
            /// <summary>The files to commit, as git reports them.</summary>
            public List<StepCommit.Change> Changes;

            /// <summary>Other packages changed in the same files ("manifest.json: com.unity.ads"); empty when none.</summary>
            public List<string> Others;

            public string Summary;
            public string Description;
        }

        /// <summary>The uncommitted tool install, or null when there is none (or no git to ask).</summary>
        public static State Read()
        {
            if (!StepCommit.TryGetChanges(new[] { Manifest, Lock }, out var changes, out _)) return null;

            // Only the two files — never a .meta (Packages/ has none, but the spec asks for them).
            changes = changes.Where(c => c.ProjectPath == Manifest || c.ProjectPath == Lock).ToList();
            if (changes.Count == 0) return null;

            var headManifest = Parse(StepCommit.HeadText(Manifest));
            var manifest = Parse(ReadProject(Manifest));
            var headLock = Parse(StepCommit.HeadText(Lock));
            var lockFile = Parse(ReadProject(Lock));

            var toolEntryChanged = !JToken.DeepEquals(Dependency(headManifest, PackageName), Dependency(manifest, PackageName))
                                   || !JToken.DeepEquals(Dependency(headLock, PackageName), Dependency(lockFile, PackageName));
            if (!toolEntryChanged) return null; // someone else's change, not the tool's

            // What the tool pulls in with it is part of its install too.
            var ownDependencies = new HashSet<string> { PackageName };
            if (Dependency(lockFile, PackageName)?["dependencies"] is JObject pulled)
                foreach (var property in pulled.Properties()) ownDependencies.Add(property.Name);

            var others = Differences("manifest.json", headManifest, manifest, new HashSet<string> { PackageName })
                .Concat(Differences("packages-lock.json", headLock, lockFile, ownDependencies))
                .ToList();

            var info = PackageInfo.FindForAssembly(typeof(ToolInstallCommit).Assembly);
            var version = info?.version;
            var hash = Dependency(lockFile, PackageName)?["hash"]?.ToString();
            var shortHash = string.IsNullOrEmpty(hash) ? null : hash.Substring(0, Math.Min(7, hash.Length));
            var added = Dependency(headManifest, PackageName) == null;
            var source = Dependency(manifest, PackageName)?.ToString();

            var summary = (added ? "Add Metica Integration Tools" : "Update Metica Integration Tools to") +
                          (version != null ? " " + version : "") +
                          (shortHash != null ? $" ({shortHash})" : "");

            var description = new List<string>();
            if (source != null) description.Add($"{Manifest}: {PackageName} → {source}");
            if (hash != null) description.Add($"{Lock}: pinned at {hash}");
            description.Add("Committed from the Metica Integration Tools window.");

            return new State
            {
                Changes = changes,
                Others = others,
                Summary = summary,
                Description = string.Join("\n", description)
            };
        }

        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JObject.Parse(json);
            }
            catch (Exception)
            {
                return null; // mid-edit or broken: compared as missing
            }
        }

        private static JToken Dependency(JObject file, string name) => (file?["dependencies"] as JObject)?[name];

        /// <summary>"file: key" for every dependency (and every other top-level setting) that differs, except <paramref name="skip"/>.</summary>
        private static IEnumerable<string> Differences(string file, JObject head, JObject current, HashSet<string> skip)
        {
            var headDeps = head?["dependencies"] as JObject;
            var deps = current?["dependencies"] as JObject;
            var names = (headDeps?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>())
                .Union(deps?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>());
            foreach (var name in names.Where(n => !skip.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
                if (!JToken.DeepEquals(headDeps?[name], deps?[name]))
                    yield return $"{file}: {name}";

            var keys = (head?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>())
                .Union(current?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>())
                .Where(k => k != "dependencies");
            foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
                if (!JToken.DeepEquals(head?[key], current?[key]))
                    yield return $"{file}: {key}";
        }

        private static string ReadProject(string projectPath)
        {
            var absolute = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", projectPath);
            try
            {
                return File.Exists(absolute) ? File.ReadAllText(absolute) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
