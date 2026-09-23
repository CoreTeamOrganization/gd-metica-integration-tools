using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>One published Metica Unity package.</summary>
    public sealed class MeticaRelease
    {
        public string Tag;
        public string Name;
        public bool PreRelease;
        public string PublishedOn;
        public string DownloadUrl;     // the .unitypackage asset, null when a release has none
        public string PageUrl;

        /// <summary>Tag without the leading v, to compare against package.json.</summary>
        public string Version => Tag?.TrimStart('v', 'V');

        public string Label =>
            $"{Tag}{(PreRelease ? "  (pre-release)" : string.Empty)}" +
            $"{(DownloadUrl == null ? "  — no package asset" : string.Empty)}" +
            $"{(string.IsNullOrEmpty(PublishedOn) ? string.Empty : "   " + PublishedOn)}";
    }

    /// <summary>
    /// Reads the published releases of meticalabs/metica-unity-package and downloads one.
    ///
    /// <para>Only the ten most recent are offered. Anything older is far enough behind that
    /// replacing it is the honest answer, which is what the Metica SDK step asks for.</para>
    ///
    /// <para>Asset names are not assumed: whatever the release attaches, the first file
    /// ending in .unitypackage is the one taken. A release with no such asset is still
    /// listed, and its page can be opened to download by hand.</para>
    /// </summary>
    public static class MeticaReleases
    {
        public const string Repository = "meticalabs/metica-unity-package";

        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

        private const string Api = "https://api.github.com/repos/" + Repository + "/releases?per_page=10";

        /// <summary>Cached for the session — the API allows only 60 unauthenticated calls an hour.</summary>
        private static List<MeticaRelease> _cache;

        public static IReadOnlyList<MeticaRelease> Cached => _cache;

        public static string LastError { get; private set; }

        public static bool TryFetch(out List<MeticaRelease> releases, bool forceRefresh = false)
        {
            if (!forceRefresh && _cache != null)
            {
                releases = _cache;
                return true;
            }

            LastError = null;

            using (var request = UnityWebRequest.Get(Api))
            {
                // GitHub rejects requests with no user agent.
                request.SetRequestHeader("User-Agent", "GameDistrict-MeticaIntegrationTools");
                request.SetRequestHeader("Accept", "application/vnd.github+json");

                if (!Send(request, "Reading Metica releases"))
                {
                    LastError = request.error;
                    releases = null;
                    return false;
                }

                try
                {
                    _cache = Parse(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    LastError = $"Could not read the release list: {e.Message}";
                    releases = null;
                    return false;
                }
            }

            releases = _cache;
            return true;
        }

        /// <summary>Downloads the release's package to a temp file and returns its path.</summary>
        public static bool TryDownload(MeticaRelease release, out string path)
        {
            path = null;
            LastError = null;

            if (release?.DownloadUrl == null)
            {
                LastError = "That release has no .unitypackage attached — download it from the release page.";
                return false;
            }

            var target = Path.Combine(Path.GetTempPath(), $"MeticaSdk-{release.Tag}.unitypackage");

            using (var request = UnityWebRequest.Get(release.DownloadUrl))
            {
                request.SetRequestHeader("User-Agent", "GameDistrict-MeticaIntegrationTools");
                request.downloadHandler = new DownloadHandlerFile(target) { removeFileOnAbort = true };

                if (!Send(request, $"Downloading Metica {release.Tag}"))
                {
                    LastError = request.error;
                    return false;
                }
            }

            path = target;
            return true;
        }

        // ── Plumbing ───────────────────────────────────────────────────────────

        /// <summary>
        /// Editor code has no coroutines, so the request is pumped here behind a cancellable
        /// progress bar rather than blocking the editor outright.
        /// </summary>
        private static bool Send(UnityWebRequest request, string title)
        {
            var operation = request.SendWebRequest();

            try
            {
                while (!operation.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(title, request.url, request.downloadProgress))
                    {
                        request.Abort();
                        LastError = "Cancelled.";
                        return false;
                    }

                    System.Threading.Thread.Sleep(50);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return request.result == UnityWebRequest.Result.Success;
        }

        /// <summary>
        /// Pulls the handful of fields that matter straight out of the JSON. JsonUtility
        /// cannot deserialize a top-level array, and the shape here is small and stable
        /// enough that regex is less trouble than a wrapper type per field.
        /// </summary>
        private static List<MeticaRelease> Parse(string json)
        {
            var releases = new List<MeticaRelease>();

            // Split on the object boundary that starts each release entry.
            foreach (var block in SplitReleases(json))
            {
                var release = new MeticaRelease
                {
                    Tag = Field(block, "tag_name"),
                    Name = Field(block, "name"),
                    PageUrl = Field(block, "html_url"),
                    PublishedOn = Field(block, "published_at")?.Split('T').FirstOrDefault(),
                    PreRelease = Regex.IsMatch(block, "\"prerelease\"\\s*:\\s*true"),
                    DownloadUrl = UnityPackageAsset(block)
                };

                if (release.Tag != null) releases.Add(release);
            }

            return releases;
        }

        private static IEnumerable<string> SplitReleases(string json)
        {
            // Each release object opens with "url" immediately followed by "assets_url" —
            // unique to the release itself, since every attached asset also opens with its
            // own "url" field but never has "assets_url" after it. Splitting on the bare
            // "url" key instead would cut a release apart at each of its own assets, taking
            // "browser_download_url" out of the chunk that carries "tag_name".
            var starts = Regex.Matches(json, "\\{\\s*\"url\"\\s*:\\s*\"[^\"]*\"\\s*,\\s*\"assets_url\"")
                .Cast<Match>()
                .Select(match => match.Index)
                .ToList();

            for (var i = 0; i < starts.Count; i++)
            {
                var from = starts[i];
                var to = i + 1 < starts.Count ? starts[i + 1] : json.Length;
                var block = json.Substring(from, to - from);

                if (block.Contains("\"tag_name\"")) yield return block;
            }
        }

        private static string Field(string block, string name)
        {
            var match = Regex.Match(block, $"\"{name}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
        }

        /// <summary>First attached asset whose name ends in .unitypackage.</summary>
        private static string UnityPackageAsset(string block)
        {
            foreach (Match match in Regex.Matches(block,
                "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                var url = Regex.Unescape(match.Groups[1].Value);
                if (url.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) return url;
            }

            return null;
        }
    }
}
