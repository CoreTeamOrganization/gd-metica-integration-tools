using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The confirmation shown before the tool deletes anything from a project: every folder
    /// and file that goes, one per line, and what is deliberately kept.
    /// </summary>
    internal static class DeleteConfirm
    {
        /// <summary>True to go ahead. Nothing to delete shows nothing and returns false.</summary>
        public static bool Ask(string title, IEnumerable<string> paths, string kept = null)
        {
            var list = paths.ToList();
            if (list.Count == 0) return false;

            var message = "These will be deleted:\n\n" + string.Join("\n", list.Select(path => "  • " + path));
            if (kept != null) message += "\n\nKept: " + kept;

            return EditorUtility.DisplayDialog(title, message, "Delete", "Cancel");
        }

        /// <summary>
        /// A folder's direct contents as project paths, folders ending in "/", for listing what
        /// deleting the folder removes. <paramref name="except"/> names are left out.
        /// </summary>
        public static List<string> Contents(string projectFolder, params string[] except)
        {
            var absolute = MeticaPaths.ToAbsolute(projectFolder);
            if (!Directory.Exists(absolute)) return new List<string>();

            return Directory.GetFileSystemEntries(absolute)
                .Where(entry => !entry.EndsWith(".meta"))
                .Select(entry => (name: Path.GetFileName(entry), folder: Directory.Exists(entry)))
                .Where(entry => !except.Contains(entry.name))
                .OrderBy(entry => entry.folder ? 0 : 1).ThenBy(entry => entry.name)
                .Select(entry => $"{projectFolder}/{entry.name}{(entry.folder ? "/" : "")}")
                .ToList();
        }
    }
}
