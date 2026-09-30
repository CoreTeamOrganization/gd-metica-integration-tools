using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine.Networking;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Downloads a file to disk, and unzips an archive, each behind a cancellable progress
    /// bar. Streams straight to the file, so a 200 MB Gradle distribution never sits in memory.
    /// </summary>
    internal static class Downloads
    {
        public static bool TryDownload(string url, string targetPath, string title, out string error)
        {
            error = null;
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(targetPath) { removeFileOnAbort = true };
                var operation = request.SendWebRequest();

                try
                {
                    while (!operation.isDone)
                    {
                        var progress = request.downloadProgress;
                        var megabytes = request.downloadedBytes / (1024f * 1024f);
                        if (EditorUtility.DisplayCancelableProgressBar(title, $"{megabytes:0} MB — {url}", progress))
                        {
                            request.Abort();
                            error = "Cancelled.";
                            return false;
                        }

                        System.Threading.Thread.Sleep(50);
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }

                if (request.result == UnityWebRequest.Result.Success) return true;

                error = $"{request.error} ({url})";
                return false;
            }
        }

        /// <summary>Extracts every entry of <paramref name="zipPath"/> under <paramref name="folder"/>.</summary>
        public static bool TryUnzip(string zipPath, string folder, string title, out string error)
        {
            error = null;
            var root = Path.GetFullPath(folder);

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    for (var i = 0; i < archive.Entries.Count; i++)
                    {
                        var entry = archive.Entries[i];
                        var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

                        // Never write outside the chosen folder, whatever the archive says.
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                        if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(title, entry.FullName,
                                (float)i / archive.Entries.Count))
                        {
                            error = "Cancelled.";
                            return false;
                        }

                        if (entry.FullName.EndsWith("/"))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? root);
                        entry.ExtractToFile(target, true);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
