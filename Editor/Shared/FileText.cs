using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Text comparison that ignores what git and editors change on their own. Same rules as
    /// Tools~/ExportStockFiles.ps1, so stock copies and project files compare like for like.
    /// </summary>
    internal static class FileText
    {
        /// <summary>No BOM, LF line endings, no trailing whitespace, one newline at the end.</summary>
        public static string Normalize(string text)
        {
            if (text == null) return null;

            var lines = text.TrimStart('﻿')
                .Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.TrimEnd(' ', '\t'));

            return string.Join("\n", lines).TrimEnd('\n') + "\n";
        }

        public static bool Same(string a, string b) => Normalize(a) == Normalize(b);
    }
}
