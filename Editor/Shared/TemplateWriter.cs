using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Copies the wrapper sources shipped with this tool (as .cs.txt so they never compile
    /// from the Templates folder) into their place in the SDK.
    /// </summary>
    public static class TemplateWriter
    {
        /// <summary>
        /// Writes <paramref name="templateName"/> to <paramref name="targetProjectPath"/>.
        /// Existing files are left alone unless <paramref name="overwrite"/> is set, so a
        /// project that hand-modified a wrapper does not silently lose those changes.
        /// </summary>
        public static bool Write(string templateName, string targetProjectPath, bool overwrite, out string message)
        {
            message = null;

            var templatesRoot = MeticaPaths.TemplatesRoot;
            if (templatesRoot == null)
            {
                message = "Could not locate this tool's Templates folder.";
                return false;
            }

            var source = MeticaPaths.ToAbsolute(templatesRoot + "/" + templateName);
            if (!File.Exists(source))
            {
                message = $"Template missing: {templateName}";
                return false;
            }

            var target = MeticaPaths.ToAbsolute(targetProjectPath);
            if (File.Exists(target) && !overwrite)
            {
                message = $"Kept existing {targetProjectPath}";
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
                File.WriteAllText(target, File.ReadAllText(source));
                message = $"Wrote {targetProjectPath}";
                return true;
            }
            catch (Exception e)
            {
                message = $"Failed to write {targetProjectPath}: {e.Message}";
                return false;
            }
        }

        /// <summary>Writes a set of template → target pairs and reports every outcome.</summary>
        public static List<string> WriteAll(IEnumerable<(string template, string target)> files, bool overwrite,
            out bool allOk)
        {
            var messages = new List<string>();
            allOk = true;

            foreach (var (template, target) in files)
            {
                if (!Write(template, target, overwrite, out var message)) allOk = false;
                if (!string.IsNullOrEmpty(message)) messages.Add(message);
            }

            AssetDatabase.Refresh();
            return messages;
        }

        /// <summary>True when a type with this full name is loaded, i.e. its assembly compiled.</summary>
        public static bool TypeIsLoaded(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            {
                try { return a.GetType(fullName) != null; }
                catch { return false; }
            });
        }

        /// <summary>Finds a loaded type by full name, or null.</summary>
        public static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a =>
                {
                    try { return a.GetType(fullName); }
                    catch { return null; }
                })
                .FirstOrDefault(t => t != null);
        }
    }
}
