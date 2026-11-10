using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Filled by JsonUtility, never assigned in code.
#pragma warning disable 0649

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// result.json, as the metica-integration-review skill writes it into the bundle folder.
    /// Plain fields so JsonUtility reads it — no JSON package needed.
    /// </summary>
    [Serializable]
    internal sealed class ReviewResult
    {
        public string summary;
        public string scenario;
        public ReviewFinding[] findings = Array.Empty<ReviewFinding>();

        public IEnumerable<ReviewFinding> Blockers => findings.Where(f => f.IsBlocker);
        public IEnumerable<ReviewFinding> Warnings => findings.Where(f => f.Severity == "warning");

        /// <summary>The bundle's result, or null when there is none yet; <paramref name="error"/> when it can't be read.</summary>
        public static ReviewResult Load(string bundle, out string error)
        {
            error = null;
            var path = Path.Combine(ReviewBundle.Absolute(bundle), ReviewBundle.ResultFile);
            if (!File.Exists(path)) return null;

            try
            {
                var result = JsonUtility.FromJson<ReviewResult>(File.ReadAllText(path));
                if (result == null)
                {
                    error = $"{ReviewBundle.ResultFile} is empty.";
                    return null;
                }

                result.findings = result.findings ?? Array.Empty<ReviewFinding>();
                for (var i = 0; i < result.findings.Length; i++)
                    if (string.IsNullOrEmpty(result.findings[i].id)) result.findings[i].id = "finding-" + (i + 1);
                return result;
            }
            catch (Exception e)
            {
                error = $"{ReviewBundle.ResultFile} can't be read: {e.Message}";
                return null;
            }
        }

        // ── Acknowledged blockers ──────────────────────────────────────────────

        private const string AcknowledgedFile = "acknowledged.txt";

        /// <summary>Blocker ids someone has looked at and accepted, kept in the bundle folder.</summary>
        public static HashSet<string> Acknowledged(string bundle)
        {
            var path = Path.Combine(ReviewBundle.Absolute(bundle), AcknowledgedFile);
            try
            {
                return File.Exists(path)
                    ? new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0))
                    : new HashSet<string>();
            }
            catch (IOException)
            {
                return new HashSet<string>();
            }
        }

        public static void SetAcknowledged(string bundle, string id, bool value)
        {
            var ids = Acknowledged(bundle);
            if (value) ids.Add(id);
            else ids.Remove(id);
            File.WriteAllLines(Path.Combine(ReviewBundle.Absolute(bundle), AcknowledgedFile), ids.OrderBy(i => i));
        }
    }

    [Serializable]
    internal sealed class ReviewFinding
    {
        public string id;

        /// <summary>"blocker", "warning" or "info".</summary>
        public string severity;

        public string title;
        public string file;
        public int line;
        public string detail;
        public string fix;

        public string Severity => (severity ?? "info").Trim().ToLowerInvariant();
        public bool IsBlocker => Severity == "blocker";

        public string Where => string.IsNullOrEmpty(file) ? null : line > 0 ? $"{file}:{line}" : file;
    }
}
