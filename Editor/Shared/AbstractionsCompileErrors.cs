using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Notices when the project fails to compile for want of com.metica.analytics.abstractions.
    ///
    /// <para>Metica's own code uses the package only behind METICA_ANALYTICS, which its asmdef
    /// sets when the package is installed. But a game can have the symbol in Player settings
    /// without the package (left by the genre creator or an older integration), or game / SDK
    /// code that uses Metica.Analytics.Abstractions directly — then nothing compiles. Two
    /// signals, either is enough: compile errors that name the abstractions (caught as each
    /// assembly finishes; a failed compile keeps this domain, so the hook is still listening),
    /// and METICA_ANALYTICS in the Player settings of Android, iOS or Standalone.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class AbstractionsCompileErrors
    {
        private const string Key = "GameDistrict.MeticaIntegrationTools.AbstractionsErrors";

        static AbstractionsCompileErrors()
        {
            CompilationPipeline.compilationStarted += _ => SessionState.EraseString(Key);
            CompilationPipeline.assemblyCompilationFinished += Collect;
        }

        /// <summary>The errors from the last compile that name the abstractions, at most five.</summary>
        public static IReadOnlyList<string> Errors =>
            SessionState.GetString(Key, string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The package isn't there, and the project shows it needs it.</summary>
        public static bool PackageNeeded =>
            !AnalyticsAbstractions.Present && (Errors.Count > 0 || DefinedWithoutPackage);

        /// <summary>METICA_ANALYTICS is in the Player settings of a platform games ship to.</summary>
        public static bool DefinedWithoutPackage =>
            new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone }
                .Any(target =>
                {
                    PlayerSettings.GetScriptingDefineSymbols(target, out var symbols);
                    return Array.IndexOf(symbols, MeticaAnalyticsDefine.Symbol) >= 0;
                });

        private static void Collect(string assembly, CompilerMessage[] messages)
        {
            var found = messages
                .Where(m => m.type == CompilerMessageType.Error && NamesAbstractions(m.message))
                .Select(m => m.message.Replace('\n', ' '))
                .ToList();
            if (found.Count == 0) return;

            var all = Errors.Concat(found).Distinct().Take(5);
            SessionState.SetString(Key, string.Join("\n", all));
        }

        /// <summary>
        /// "The type or namespace name 'Abstractions' does not exist in the namespace
        /// 'Metica.Analytics'", "...'IMeticaAnalytics' could not be found", and the like.
        /// </summary>
        internal static bool NamesAbstractions(string message) =>
            message.IndexOf("Abstraction", StringComparison.OrdinalIgnoreCase) >= 0
                ? message.IndexOf("Metica", StringComparison.OrdinalIgnoreCase) >= 0
                : message.IndexOf("IMeticaAnalytics", StringComparison.Ordinal) >= 0;
    }
}
