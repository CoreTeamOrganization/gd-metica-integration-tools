using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    internal static class GenreAssetCreator
    {
        internal const string PendingGenreKey = "GDMeticaAnalytics_PendingGenre";
        internal const string ScriptsBase     = "Assets/MeticaGenres";
        internal const string ResourcesPath   = "Assets/MeticaGenres/Resources";

        public static void CreateFiles(string genre, List<GenreEventDef> events)
        {
            string root = ProjectRoot();
            string dir  = Path.Combine(root, ScriptsBase.Replace('/', Path.DirectorySeparatorChar), genre);

            Directory.CreateDirectory(dir);

            WriteFile(dir, $"I{genre}Analytics.cs", GenreCodeGenerator.Interface(genre, events));
            WriteFile(dir, $"{genre}Data.cs",        GenreCodeGenerator.Data(genre, events));
            WriteFile(dir, $"{genre}Analytics.cs",   GenreCodeGenerator.Implementation(genre, events));

            EditorPrefs.SetString(PendingGenreKey, genre);
            AssetDatabase.Refresh();
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            string genre = EditorPrefs.GetString(PendingGenreKey, "");
            if (string.IsNullOrEmpty(genre)) return;
            EditorPrefs.DeleteKey(PendingGenreKey);

            EnsureFolder(ResourcesPath);

            var analyticsType = FindType($"{genre}Analytics");
            if (analyticsType == null)
            {
                Debug.LogError($"[MeticaGenreCreator] Could not find type '{genre}Analytics'. " +
                               "Make sure there are no compile errors.");
                return;
            }

            string analyticsPath  = $"{ResourcesPath}/{genre}Analytics.asset";
            var    analyticsAsset = ScriptableObject.CreateInstance(analyticsType);
            AssetDatabase.CreateAsset(analyticsAsset, analyticsPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MeticaGenreCreator] '{genre}' asset created at {analyticsPath}");

            EditorUtility.DisplayDialog(
                "Genre Created!",
                $"'{genre}' scripts and asset are ready.\n\n" +
                $"Asset: {analyticsPath}\n\n" +
                "1. Assign the asset to your game.\n" +
                "2. Fill in App Id, Api Key.\n" +
                "3. Call analytics.Initialize() once at startup.",
                "OK");
        }

        internal static string ProjectRoot()
        {
            string data = Application.dataPath;
            return data.Substring(0, data.Length - "Assets".Length);
        }

        internal static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "";
            string name   = Path.GetFileName(assetPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        internal static Type FindType(string typeName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                     .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                     .FirstOrDefault(t => t.Name == typeName);

        private static void WriteFile(string dir, string filename, string content) =>
            File.WriteAllText(Path.Combine(dir, filename), content, Encoding.UTF8);
    }
}