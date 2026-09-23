using System.IO;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    internal static class IdentifierValidator
    {
        public static string GetIdentifierError(string name, string label = "Name")
        {
            if (string.IsNullOrEmpty(name))
                return $"{label} is required.";
            if (!char.IsLetter(name[0]))
                return $"{label} must start with a letter.";
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c))
                    return $"{label} must contain only letters and digits. Invalid character: '{c}'";
            return null;
        }

        public static string GetError(string name)
        {
            string idError = GetIdentifierError(name, "Genre name");
            if (idError != null) return idError;

            string data = Application.dataPath;
            string root = data.Substring(0, data.Length - "Assets".Length);
            string dir  = Path.Combine(root, GenreAssetCreator.ScriptsBase.Replace('/', Path.DirectorySeparatorChar), name);
            if (Directory.Exists(dir))
                return $"Genre '{name}' already exists.";

            return null;
        }
    }
}