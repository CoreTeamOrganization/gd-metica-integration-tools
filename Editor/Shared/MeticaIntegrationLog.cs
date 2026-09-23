using System.Collections.Generic;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Running record of everything the wizard changed this session, shown at the bottom of
    /// the window and mirrored to the Console so it survives a domain reload in the log file.
    /// </summary>
    public static class MeticaIntegrationLog
    {
        private static readonly List<string> Entries = new List<string>();

        public static IReadOnlyList<string> All => Entries;

        public static void Record(string step, IEnumerable<string> messages)
        {
            foreach (var message in messages) Record(step, message);
        }

        public static void Record(string step, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var entry = $"[{step}] {message}";
            Entries.Add(entry);
            Debug.Log($"[Metica Integration] {entry}");
        }

        public static void Clear() => Entries.Clear();
    }
}
