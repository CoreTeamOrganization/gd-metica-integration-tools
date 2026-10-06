using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Whether Unity has yet to compile scripts a step just wrote or edited.
    ///
    /// <para>Steps that check for a compiled type (the wrapper files, MeticaConfiguration, the
    /// standalone runtime) cannot tell "not compiled yet" from "does not compile" by the type
    /// alone. Between the two lies the window re-checking while Unity is still importing or
    /// compiling — on focus, straight after an action — and treating that as a failure threw
    /// away sign-offs. So a missing type is only a failure once the scripts it comes from
    /// have been through a compile.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class ScriptCompile
    {
        /// <summary>When the last compile finished. A domain reload means one just did.</summary>
        private static DateTime _lastCompileUtc;

        static ScriptCompile()
        {
            _lastCompileUtc = DateTime.UtcNow;
            CompilationPipeline.compilationFinished += _ => _lastCompileUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// True while Unity is importing or compiling, or when a script under
        /// <paramref name="paths"/> (project-relative files or folders) changed after the last
        /// compile finished — Unity has not picked it up yet.
        /// </summary>
        public static bool Pending(params string[] paths)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return true;

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                try
                {
                    var absolute = MeticaPaths.ToAbsolute(path);
                    if (File.Exists(absolute))
                    {
                        if (ChangedSinceCompile(absolute)) return true;
                    }
                    else if (Directory.Exists(absolute)
                             && Directory.EnumerateFiles(absolute, "*.cs", SearchOption.AllDirectories)
                                 .Any(ChangedSinceCompile))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Unreadable — no evidence of a pending compile.
                }
            }

            return false;
        }

        /// <summary>The line a waiting step shows.</summary>
        public const string WaitingMessage =
            "Waiting for Unity to compile… If it doesn't start, press Ctrl+R (Cmd+R on macOS).";

        private static bool ChangedSinceCompile(string file) => File.GetLastWriteTimeUtc(file) > _lastCompileUtc;
    }
}
