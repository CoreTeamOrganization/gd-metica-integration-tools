using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace GameDistrict.MeticaIntegrationTools
{
    // Adds METICA_ANALYTICS / GD_PERFORMANCE_TRACKER to Android and iOS when each SDK is installed.
    [InitializeOnLoad]
    internal static class MeticaSymbolInstaller
    {
        private const string MeticaSymbol     = "METICA_ANALYTICS";
        private const string PerfTrackerSymbol = "GD_PERFORMANCE_TRACKER";

        static MeticaSymbolInstaller()
        {
            // The Metica SDK alone is not enough: its own analytics code is gated on this same
            // define and needs the analytics abstractions assembly, so an ads-only project (Metica
            // SDK, no abstractions) would stop compiling the moment this define appeared.
            if (IsAssemblyLoaded("Metica.SDK") && IsAssemblyLoaded("MeticaAnalyticsAbstractions"))
            {
                AddSymbol(NamedBuildTarget.Android, MeticaSymbol);
                AddSymbol(NamedBuildTarget.iOS, MeticaSymbol);
            }
            if (IsAssemblyLoaded("GDPerformanceTracker.Runtime"))
            {
                AddSymbol(NamedBuildTarget.Android, PerfTrackerSymbol);
                AddSymbol(NamedBuildTarget.iOS, PerfTrackerSymbol);
            }
        }

        private static bool IsAssemblyLoaded(string assemblyName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == assemblyName)
                    return true;
            return false;
        }

        private static void AddSymbol(NamedBuildTarget target, string symbol)
        {
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] symbols);
            if (symbols.Contains(symbol)) return;
            var list = new List<string>(symbols) { symbol };
            PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
        }
    }
}