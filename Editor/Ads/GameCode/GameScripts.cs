using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The game's own C# scripts — Assets/ without the SDKs, Editor folders and this tool's
    /// output — read once per change and kept with their masked text.
    /// </summary>
    internal static class GameScripts
    {
        internal sealed class Script
        {
            public string Path;
            public string Text;
            public string Masked;
        }

        /// <summary>SDK and generated folders: not the game's code.</summary>
        private static readonly string[] Skipped =
        {
            "Assets/MaxSdk/", "Assets/MeticaSdk/", "Assets/MeticaAds/", "Assets/MeticaGenres/", "Assets/GoogleMobileAds/",
            "Assets/ExternalDependencyManager/", "Assets/Plugins/", "Assets/Firebase/", "Assets/Adjust/", "Assets/AppMetrica/",
            "Assets/GameAnalytics/", "Assets/MeticaIntegrationToolsSettings/", "Assets/Metica/", "Assets/GDMonetization/"
        };

        private static readonly Dictionary<string, (DateTime written, Script script)> Cache =
            new Dictionary<string, (DateTime, Script)>();

        /// <summary>Overrides the project root (tests).</summary>
        internal static string RootOverride { get; set; }

        private static string AssetsFolder => RootOverride != null ? System.IO.Path.Combine(RootOverride, "Assets") : Application.dataPath;

        /// <summary>Every game script whose text contains <paramref name="needle"/>.</summary>
        public static List<Script> Containing(string needle)
        {
            var found = new List<Script>();
            var assets = AssetsFolder;
            if (!Directory.Exists(assets)) return found;

            foreach (var file in Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories))
            {
                var relative = "Assets/" + file.Substring(assets.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (Skipped.Any(folder => relative.StartsWith(folder, StringComparison.Ordinal))) continue;
                if (relative.Contains("/Editor/")) continue;

                var stamp = File.GetLastWriteTimeUtc(file);
                if (!Cache.TryGetValue(file, out var cached) || cached.written != stamp)
                {
                    var text = File.ReadAllText(file);
                    cached = (stamp, new Script { Path = relative, Text = text, Masked = CSharpSource.Mask(text) });
                    Cache[file] = cached;
                }

                if (cached.script.Masked.IndexOf(needle, StringComparison.Ordinal) >= 0) found.Add(cached.script);
            }

            return found.OrderBy(s => s.Path, StringComparer.Ordinal).ToList();
        }

        /// <summary>One script, fresh from disk (or memory), or null.</summary>
        public static Script Read(string path)
        {
            if (!SourcePatcher.Exists(path)) return null;
            var text = SourcePatcher.ReadAll(path);
            return new Script { Path = path, Text = text, Masked = CSharpSource.Mask(text) };
        }

        /// <summary>The .asmdef that compiles <paramref name="path"/>, or null (Assembly-CSharp).</summary>
        public static string AsmdefFor(string path)
        {
            var folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) && folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                var absolute = MeticaPaths.ToAbsolute(folder);
                if (Directory.Exists(absolute))
                {
                    var asmdef = Directory.GetFiles(absolute, "*.asmdef").FirstOrDefault();
                    if (asmdef != null) return folder + "/" + System.IO.Path.GetFileName(asmdef);
                }

                folder = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }

            return null;
        }

        /// <summary>
        /// Why <paramref name="path"/> cannot call the Metica runtime, or null. The runtime in
        /// Assets/MeticaAds compiles into Assembly-CSharp, which an assembly definition cannot
        /// reference.
        /// </summary>
        public static string RuntimeUnreachable(string path)
        {
            var asmdef = AsmdefFor(path);
            return asmdef == null
                ? null
                : $"{path} is compiled by {asmdef}, which can't see the Metica runtime in {MeticaPaths.StandaloneRoot} " +
                  "(Assembly-CSharp) — patch it by hand, or move the script out of that assembly.";
        }
    }
}
