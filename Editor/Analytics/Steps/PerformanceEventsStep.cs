using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The perfStats (FPS / memory) and loadTime (cold start) events, from GD Performance
    /// Tracker (com.gamedistrict.performance-tracker). Five rows, each set on its own: the
    /// package, its prefab in the boot scene, its switches from remote config, and the two
    /// places in the game's code that send the events.
    ///
    /// <para>The sending itself is the genre asset's: the genre creator (2.0.0+) gives
    /// GDMeticaAnalytics ConfigurePerformanceTracking, MarkGameInteractive, LogPerfStatsEvent and
    /// LogLoadTimeEvent, which fetch the tracker's payload and add the base fields (adid,
    /// appToken, the AB test) themselves. So every line the tool writes is a call on
    /// &lt;Game&gt;Analytics, loaded from Resources — no GDPerformance call, no new assembly
    /// reference beyond the genre's.</para>
    /// </summary>
    public sealed class PerformanceEventsStep : MeticaStep
    {
        public const string PackageName = "com.gamedistrict.performance-tracker";
        public const string GitUrl = "https://github.com/CoreTeamOrganization/GDPerformanceTracker.git";
        public const string RuntimeAssembly = "GDPerformanceTracker.Runtime";
        public const string Define = "GD_PERFORMANCE_TRACKER";

        public static readonly string[] RemoteKeys = { "perf_tracking_enabled", "perf_sample_interval", "startup_tracking_enabled" };

        private const string RemoteMarker = "Metica analytics: GD Performance Tracker's switches from remote config";
        private const string LoadTimeMarker = "Metica analytics: the game is playable — GD Performance Tracker's loadTime";
        private const string PerfStatsMarker = "Metica analytics: GD Performance Tracker's perfStats — FPS and memory since the last one";

        public override string Title => "Performance events (perfStats, loadTime)";

        public override bool Optional => true;

        public override string Summary => "Send FPS / memory (perfStats) and cold-start time (loadTime) through the genre asset.";

        public override string Why =>
            $"GD Performance Tracker records into RAM and hands the genre asset a payload to send. Five rows: add the package " +
            $"({GitUrl}) — the genre creator then adds {Define} for Android and iOS; put its GDPerfTracker prefab in the boot " +
            "scene (the first one in Build Settings — FPS needs it, startup timing doesn't); switch it from remote config " +
            $"({string.Join(", ", RemoteKeys)} — add the three keys in Firebase; GD SDK: patched into RemoteConfigManager after " +
            "each fetch, custom SDK: copy the call to your fetch callback); call MarkGameInteractive + LogLoadTimeEvent where " +
            "the game is first playable; and LogPerfStatsEvent at a logging moment (level end, session end). The tool can put " +
            "those last two at the end of a function you pick, or copy them. Base fields (adid, appToken, AB test) are added " +
            "by the genre asset; pass the game's own context (taskId, day …) as a dictionary if it has any. Startup timing is " +
            "Android-only (iOS and the Editor measure from engine start), so compare device numbers only. Skip the step if " +
            "the game doesn't send these events.";

        public override IEnumerable<string> TouchedPaths
        {
            get
            {
                var paths = new List<string> { MeticaPaths.PackagesManifest, "Packages/packages-lock.json", MeticaAnalyticsDefine.ProjectSettings };
                if (MeticaPaths.HasGDSdk) paths.Add(MeticaPaths.RemoteConfigManager);
                if (BootScene != null) paths.Add(BootScene);
                return paths.Concat(GameCodePrefs.List(Id + ".Touched")).Distinct();
            }
        }

        public override string ReviewHint =>
            "The tracker in manifest.json, its prefab in the boot scene, a few lines after RemoteConfigManager's fetch (GD SDK), " +
            "and the calls in the functions you picked — each marked \"Metica analytics: …\".";

        // ── State ──────────────────────────────────────────────────────────────

        private static string Game => GenreFilesStep.GameName;

        private static bool GenreReady() =>
            GenreFilesStep.NameError(Game) == null && MeticaPaths.FileExists(GenreFilesStep.AssetPath(Game));

        private static bool PackagePresent =>
            ManifestDependencies.Value(PackageName) != null || MeticaPaths.DirectoryExists("Packages/" + PackageName);

        private static bool PackageLoaded => AnalyticsAbstractions.AssemblyLoaded(RuntimeAssembly);

        private static bool DefineSet(UnityEditor.Build.NamedBuildTarget target)
        {
            PlayerSettings.GetScriptingDefineSymbols(target, out var symbols);
            return Array.IndexOf(symbols, Define) >= 0;
        }

        private static bool DefinesSet =>
            DefineSet(UnityEditor.Build.NamedBuildTarget.Android) && DefineSet(UnityEditor.Build.NamedBuildTarget.iOS);

        /// <summary>The genre creator's GDMeticaAnalytics has the perf calls (2.0.0+).</summary>
        private static bool GenreHasPerfCalls =>
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == GenreCreatorPackage.RuntimeAssembly)
                ?.GetType("GameDistrict.MeticaAnalytics.GDMeticaAnalytics")
                ?.GetMethod("LogPerfStatsEvent") != null;

        // ── Boot scene ──

        private static string[] BuildScenes =>
            EditorBuildSettings.scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToArray();

        /// <summary>The scene the prefab goes in: the one picked, else the first enabled in Build Settings.</summary>
        private static string BootScene
        {
            get
            {
                var scenes = BuildScenes;
                var chosen = GameCodePrefs.Get("PerfScene");
                return scenes.Contains(chosen) ? chosen : scenes.FirstOrDefault();
            }
        }

        private static GameObject Prefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("GDPerfTracker t:Prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent("GDPerfTracker") != null) return prefab;
            }
            return null;
        }

        /// <summary>The scene has the prefab, or the tracker component itself (an unpacked copy).</summary>
        private static bool SceneHasTracker(string scenePath)
        {
            if (scenePath == null) return false;

            // An open scene may not be saved yet: look at its objects.
            var loaded = EditorSceneManager.GetSceneByPath(scenePath);
            var tracker = TrackerType();
            if (tracker != null && loaded.IsValid() && loaded.isLoaded
                && loaded.GetRootGameObjects().Any(root => root.GetComponentInChildren(tracker, true) != null))
                return true;

            var prefab = Prefab();
            var guids = new List<string>();
            if (prefab != null) guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
            guids.AddRange(AssetDatabase.FindAssets("GDPerfTracker t:MonoScript"));

            try
            {
                // Scenes run to megabytes, and this is asked on every repaint: read once per save.
                var absolute = MeticaPaths.ToAbsolute(scenePath);
                var stamp = File.GetLastWriteTimeUtc(absolute);
                if (!SceneTexts.TryGetValue(absolute, out var cached) || cached.written != stamp)
                    SceneTexts[absolute] = cached = (stamp, File.ReadAllText(absolute));
                return guids.Any(guid => cached.text.Contains("guid: " + guid));
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static readonly Dictionary<string, (DateTime written, string text)> SceneTexts =
            new Dictionary<string, (DateTime, string)>();

        /// <summary>The tracker component's type, or null while the package isn't loaded.</summary>
        private static Type TrackerType() =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == RuntimeAssembly)?.GetType("GDPerfTracker");

        // ── Remote config ──

        private static bool RemotePatched =>
            MeticaPaths.HasGDSdk && MeticaPaths.FileExists(MeticaPaths.RemoteConfigManager)
                                 && SourcePatcher.ReadAll(MeticaPaths.RemoteConfigManager).Contains(RemoteMarker);

        private static string GameCodeWith(params string[] calls) =>
            calls.SelectMany(call => GameScripts.Containing(call)).Select(s => s.Path).FirstOrDefault();

        private static string RemoteInGameCode => GameCodeWith("ConfigurePerformanceTracking(", "GDPerformance.Configure(");
        private static string LoadTimeInGameCode => GameCodeWith("LogLoadTimeEvent(", "GDPerformance.GetStartupPayload(");
        private static string InteractiveInGameCode => GameCodeWith("MarkGameInteractive(");
        private static string PerfStatsInGameCode => GameCodeWith("LogPerfStatsEvent(", "GDPerformance.ConsumePerfPayload(");

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            if (!GenreReady()) return result.Problem("Create the genre files first.").Seal();

            if (!PackagePresent) return result.Problem("GD Performance Tracker isn't added yet.").Seal();
            if (!PackageLoaded) return result.Wait("Package Manager is adding GD Performance Tracker…").Seal();
            if (!GenreHasPerfCalls)
                return result.Problem($"The genre creator {GenreCreatorPackage.InstalledVersion ?? ""} has no perf calls — " +
                                      $"update it to {GenreCreatorPackage.Version}.").Seal();
            if (!DefinesSet) result.Problem($"{Define} isn't set for Android and iOS.");

            if (BootScene == null) result.Problem("No scene enabled in Build Settings — the prefab needs the boot scene.");
            else if (!SceneHasTracker(BootScene)) result.Problem($"The GDPerfTracker prefab isn't in {Path.GetFileNameWithoutExtension(BootScene)}.");

            if (!RemotePatched && RemoteInGameCode == null) result.Problem("Not switched from remote config yet.");
            if (LoadTimeInGameCode == null || InteractiveInGameCode == null) result.Problem("loadTime isn't sent yet.");
            if (PerfStatsInGameCode == null) result.Problem("perfStats isn't sent yet.");

            result.Note($"Firebase keys to add: {string.Join(", ", RemoteKeys)} — the tracker stays off without them.");
            result.Note("Startup timing is Android-only — iOS and Editor readings aren't comparable.");
            return result.Seal();
        }

        // ── Controls ───────────────────────────────────────────────────────────

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (!GenreReady()) yield break;

            // 1. The package and its define.
            var packageDone = PackageLoaded && DefinesSet && GenreHasPerfCalls;
            var packageActions = new List<StepControl>();
            if (!PackagePresent) packageActions.Add(new StepButton("Add GD Performance Tracker", AddPackage, primary: true));
            else if (PackageLoaded && !DefinesSet) packageActions.Add(new StepButton($"Add {Define}", AddDefines));
            yield return new StepItem("GD Performance Tracker", packageDone,
                !PackagePresent ? "Not added" : !PackageLoaded ? "Adding…" : !GenreHasPerfCalls ? "Genre creator too old"
                : DefinesSet ? $"Added · {Define}" : $"{Define} missing",
                packageActions, risk: "Nothing is recorded, and the genre asset's perf calls do nothing.");

            if (!PackageLoaded) yield break;

            // 2. The prefab in the boot scene.
            var scenes = BuildScenes;
            var scene = BootScene;
            var sceneDone = SceneHasTracker(scene);
            var sceneActions = new List<StepControl>();
            if (scenes.Length > 0)
                sceneActions.Add(new StepChoice("Scene", scenes, Math.Max(0, Array.IndexOf(scenes, scene)),
                    index => GameCodePrefs.Set("PerfScene", scenes[index]),
                    "The first scene that loads — the boot or splash scene."));
            if (scene != null && !sceneDone)
                sceneActions.Add(new StepButton($"Add to {Path.GetFileNameWithoutExtension(scene)}", () => AddPrefab(scene), primary: true));
            yield return new StepItem("Prefab in the boot scene", sceneDone,
                scene == null ? "No scene in Build Settings" : sceneDone ? Path.GetFileNameWithoutExtension(scene) : "Not added",
                sceneActions, risk: "No FPS or memory is recorded (startup timing still works).");

            // 3. Remote config.
            var remoteAt = RemotePatched ? Path.GetFileName(MeticaPaths.RemoteConfigManager) : RemoteInGameCode;
            var remoteActions = new List<StepControl>();
            if (remoteAt == null)
            {
                if (MeticaPaths.HasGDSdk) remoteActions.Add(new StepButton("Add to RemoteConfigManager", PatchRemote, primary: true));
                remoteActions.Add(new StepButton("Copy the code", () => Copy(RemoteCode(""), "Configure call")));
            }
            yield return new StepItem("Switched from remote config", remoteAt != null, remoteAt ?? "Not yet",
                remoteActions, risk: $"The prefab's defaults apply — all off. Add {string.Join(", ", RemoteKeys)} in Firebase too.");

            // 4. loadTime and 5. perfStats.
            yield return CallRow("loadTime — where the game is first playable", "PerfLoad",
                LoadTimeInGameCode != null && InteractiveInGameCode != null ? LoadTimeInGameCode : null,
                LoadTimeLines, "No loadTime event — cold-start time isn't measured.");
            yield return CallRow("perfStats — at a logging moment (level end, session end)", "PerfStats",
                PerfStatsInGameCode, PerfStatsLines, "No perfStats event — FPS and memory aren't sent.");
        }

        /// <summary>A row for a call the game makes: pick a script and function to add it to, or copy it.</summary>
        private StepItem CallRow(string title, string pref, string foundIn, Func<string, string[]> lines, string risk)
        {
            var actions = new List<StepControl>();
            if (foundIn == null)
            {
                var scripts = new[] { "(pick a script)" }.Concat(GameScripts.Containing("").Select(s => s.Path)).ToArray();
                var script = GameCodePrefs.Get(pref + "Script");
                var scriptIndex = Math.Max(0, Array.IndexOf(scripts, script));
                actions.Add(new StepChoice("Script", scripts, scriptIndex, index =>
                {
                    GameCodePrefs.Set(pref + "Script", index == 0 ? "" : scripts[index]);
                    GameCodePrefs.Set(pref + "Method", "");
                }));

                if (scriptIndex > 0)
                {
                    var read = GameScripts.Read(scripts[scriptIndex]);
                    var methods = read == null ? new string[0] : CSharpSource.Methods(read.Masked).Select(m => m.Label).ToArray();
                    var method = GameCodePrefs.Get(pref + "Method");
                    if (methods.Length > 0)
                    {
                        var methodIndex = Math.Max(0, Array.IndexOf(methods, method));
                        actions.Add(new StepChoice("Function", methods, methodIndex,
                            index => GameCodePrefs.Set(pref + "Method", methods[index]),
                            "The calls go at the end of it, ahead of a final return."));
                        actions.Add(new StepButton($"Add to {methods[methodIndex].Split('(')[0]}()",
                            () => AddCall(scripts[scriptIndex], methods[methodIndex], lines), primary: true));
                    }
                }

                actions.Add(new StepButton("Copy the code", () => Copy(string.Join("\n", lines("")), title.Split(' ')[0])));
            }

            return new StepItem(title, foundIn != null, foundIn ?? "Not yet", actions, risk: risk);
        }

        // ── Code ───────────────────────────────────────────────────────────────

        private static string Load(string variable) =>
            $"var {variable} = UnityEngine.Resources.Load<GameDistrict.MeticaAnalytics.GDMeticaAnalytics>(\"{Game}Analytics\");";

        /// <summary>To copy: GD SDK reads through RemoteConfigManager, anything else straight from Firebase.</summary>
        private static string RemoteCode(string i) => string.Join("\n", MeticaPaths.HasGDSdk
            ? RemoteLines(i, "Monetization.Runtime.RemoteConfig.RemoteConfigManager.GetRemoteValue")
            : new[]
            {
                $"{i}// {RemoteMarker} — once Firebase Remote Config has fetched and activated:",
                $"{i}var remote = Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance;",
                $"{i}{Load("meticaPerformance")}",
                $"{i}if (meticaPerformance != null)",
                $"{i}    meticaPerformance.ConfigurePerformanceTracking(remote.GetValue(\"{RemoteKeys[0]}\").BooleanValue,",
                $"{i}        (float)remote.GetValue(\"{RemoteKeys[1]}\").DoubleValue, remote.GetValue(\"{RemoteKeys[2]}\").BooleanValue);"
            });

        /// <param name="get">How the remote values are read: RemoteConfigManager's own GetRemoteValue inside it.</param>
        private static string[] RemoteLines(string i, string get) => new[]
        {
            $"{i}// {RemoteMarker}: {string.Join(", ", RemoteKeys)}.",
            $"{i}{Load("meticaPerformance")}",
            $"{i}if (meticaPerformance != null)",
            $"{i}    meticaPerformance.ConfigurePerformanceTracking({get}<bool>(\"{RemoteKeys[0]}\").Value,",
            $"{i}        {get}<float>(\"{RemoteKeys[1]}\").Value, {get}<bool>(\"{RemoteKeys[2]}\").Value);"
        };

        private static string[] LoadTimeLines(string i) => new[]
        {
            $"{i}// {LoadTimeMarker}. Pass the game's own context (taskId, day …) as a dictionary if it has any.",
            $"{i}{Load("meticaLoadTime")}",
            $"{i}if (meticaLoadTime != null)",
            $"{i}{{",
            $"{i}    meticaLoadTime.MarkGameInteractive();",
            $"{i}    meticaLoadTime.LogLoadTimeEvent();",
            $"{i}}}"
        };

        private static string[] PerfStatsLines(string i) => new[]
        {
            $"{i}// {PerfStatsMarker}. Pass the game's own context (taskId, day …) as a dictionary if it has any.",
            $"{i}{Load("meticaPerfStats")}",
            $"{i}if (meticaPerfStats != null) meticaPerfStats.LogPerfStatsEvent();"
        };

        // ── Act ────────────────────────────────────────────────────────────────

        private void AddPackage()
        {
            MeticaIntegrationLog.Record(Title, $"Adding GD Performance Tracker from {GitUrl}");
            PackageRequests.Track(Client.Add(GitUrl), "Adding GD Performance Tracker", request =>
                MeticaIntegrationLog.Record(Title, request.Status == StatusCode.Success
                    ? $"Added GD Performance Tracker {(request as UnityEditor.PackageManager.Requests.AddRequest)?.Result?.version}"
                    : $"Couldn't add GD Performance Tracker: {request.Error?.message} — Unity needs git on the PATH " +
                      "and access to the CoreTeamOrganization GitHub org."));
        }

        /// <summary>Normally the genre creator's job, on the editor load after the tracker arrives.</summary>
        private void AddDefines()
        {
            foreach (var target in new[] { UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.NamedBuildTarget.iOS })
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out var symbols);
                if (Array.IndexOf(symbols, Define) >= 0) continue;
                PlayerSettings.SetScriptingDefineSymbols(target, symbols.Concat(new[] { Define }).ToArray());
            }
            MeticaIntegrationLog.Record(Title, $"Added {Define} for Android and iOS");
        }

        private void AddPrefab(string scenePath)
        {
            var prefab = Prefab();
            if (prefab == null)
            {
                MeticaIntegrationLog.Record(Title, "GDPerfTracker prefab not found — re-import GD Performance Tracker.");
                return;
            }

            var scene = EditorSceneManager.GetSceneByPath(scenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            if (wasLoaded && scene.isDirty && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { scene }))
            {
                MeticaIntegrationLog.Record(Title, "Cancelled — the scene has unsaved changes.");
                return;
            }

            // Opened beside what is open, so the developer's scene stays.
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            if (!SceneHasTracker(scenePath))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Add GDPerfTracker");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                MeticaIntegrationLog.Record(Title, $"Added the GDPerfTracker prefab to {scenePath}");
            }

            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        private void PatchRemote()
        {
            var path = MeticaPaths.RemoteConfigManager;
            if (!SourcePatcher.Exists(path)) { MeticaIntegrationLog.Record(Title, $"{path} not found."); return; }

            var text = SourcePatcher.ReadAll(path);
            if (text.Contains(RemoteMarker)) return;

            var masked = CSharpSource.Mask(text);
            var method = CSharpSource.Methods(masked).FirstOrDefault(m => m.Name == "CreateAndUpdateConfig");
            if (method == null)
            {
                MeticaIntegrationLog.Record(Title, $"No CreateAndUpdateConfig in {path} — copy the code to where remote config is fetched.");
                return;
            }

            var patched = InsertAtEnd(text, masked, method, i => RemoteLines(i, "GetRemoteValue"));
            if (patched == null) { MeticaIntegrationLog.Record(Title, $"Couldn't place the lines in {path} — copy the code."); return; }

            SourcePatcher.WriteWithBackup(path, patched);
            var asmdef = AdjustWiringStep.AsmdefFor(path);
            if (asmdef != null && !AdjustWiringStep.References(asmdef)) AdjustWiringStep.AddReference(asmdef);
            AssetDatabase.ImportAsset(path);
            MeticaIntegrationLog.Record(Title, $"{path}: ConfigurePerformanceTracking after every fetch");
        }

        private void AddCall(string path, string methodLabel, Func<string, string[]> lines)
        {
            var script = GameScripts.Read(path);
            var method = script == null ? null : CSharpSource.Methods(script.Masked).FirstOrDefault(m => m.Label == methodLabel);
            if (method == null) { MeticaIntegrationLog.Record(Title, $"{methodLabel} not found in {path}."); return; }

            var patched = InsertAtEnd(script.Text, script.Masked, method, lines);
            if (patched == null)
            {
                MeticaIntegrationLog.Record(Title, $"{methodLabel} returns a value with no final return to go ahead of — copy the code instead.");
                return;
            }

            SourcePatcher.WriteWithBackup(path, patched);
            var asmdef = GameScripts.AsmdefFor(path);
            if (asmdef != null) AdjustWiringStep.AddReference(asmdef);
            GameCodePrefs.AddToList(Id + ".Touched", asmdef == null ? new[] { path } : new[] { path, asmdef });
            AssetDatabase.ImportAsset(path);
            MeticaIntegrationLog.Record(Title, $"{path}: added to {methodLabel}");
        }

        /// <summary>
        /// The lines at the end of <paramref name="method"/>'s body, ahead of a final return; null
        /// for a function that returns a value without one there.
        /// </summary>
        private static string InsertAtEnd(string text, string masked, CSharpSource.Method method, Func<string, string[]> lines)
        {
            var statements = CSharpSource.Statements(masked, method.Open, method.Close);
            var last = statements.LastOrDefault();
            // A coroutine or async function can end without a return; anything else returning a value can't.
            var returns = method.ReturnType != "void" && !method.IsAsync
                          && !System.Text.RegularExpressions.Regex.IsMatch(method.ReturnType, @"\bIEnumerator\b");
            var beforeReturn = last != null && last.Keyword == "return";
            if (returns && !beforeReturn) return null;

            var at = beforeReturn ? last.Start : method.Close;
            var lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            var onOwnLine = text.Substring(lineStart, at - lineStart).Trim().Length == 0;
            var indent = last != null ? CSharpSource.IndentAt(text, last.Start) : CSharpSource.IndentAt(text, method.Open) + "    ";
            var nl = CSharpSource.NewlineOf(text);
            var block = string.Join(nl, lines(indent)) + nl;

            return onOwnLine
                ? text.Insert(lineStart, (beforeReturn || last == null ? "" : nl) + block)
                : text.Insert(at, nl + block + CSharpSource.IndentAt(text, method.Open));
        }

        private static void Copy(string code, string what)
        {
            EditorGUIUtility.systemCopyBuffer = code;
            MeticaIntegrationLog.Record("Performance events", $"Copied the {what} code to the clipboard");
        }
    }
}
