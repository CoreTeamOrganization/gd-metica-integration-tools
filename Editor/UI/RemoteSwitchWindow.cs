using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static GameDistrict.MeticaIntegrationTools.MeticaIntegrationWindow;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The remote switch on its own, without walking the Ads integration: Firebase's
    /// "MainAdNetwork" (Unassigned / MAX / Metica) saved as the device's main ad network.
    ///
    /// <para><b>GD SDK games:</b> the same two dropdowns and patch as the "Patch the existing SDK
    /// files" step (<see cref="RemoteSwitchPatch"/>), so it writes, converts and keeps the switch
    /// exactly as the step would — and nothing else. It needs what the step adds around the switch
    /// (the MainAdNetwork preference, AdsManager reading it). On GD SDK 6.x, which has Metica but
    /// not those, Prepare adds just them; anywhere else it says so and changes nothing.</para>
    ///
    /// <para><b>Custom SDK games:</b> the game's own fetch code calls MeticaRemoteConfig, and the
    /// tool never edits it. This brings MeticaRemoteConfig.cs up to the tool's version (after a
    /// confirm, with a backup) and gives the line to paste into the fetch handler.</para>
    /// </summary>
    public sealed class RemoteSwitchWindow : EditorWindow
    {
        private const string LogTitle = "Remote switch";

        private static string UiRoot => MeticaPaths.ToolRoot + "/Editor/UI";

        // GD SDK: the dropdowns as picked; null while they show what the code has.
        private RemoteSwitchForm? _form;
        private RemoteSwitchSource? _source;

        // Custom SDK: what the snippet is for.
        [SerializeField] private int _customSource = -1;
        [SerializeField] private bool _customKeepBoth;

        /// <summary>What the last Apply did, shown under the button.</summary>
        private List<string> _lastLog = new List<string>();

        private VisualElement _root;
        private ScrollView _scroll;
        private VisualElement _content;

        [MenuItem("GameDistrict/Metica/Remote Switch...", false, 12)]
        public static void Open()
        {
            var window = GetWindow<RemoteSwitchWindow>();
            window.titleContent = new GUIContent("Remote Switch");
            window.minSize = new Vector2(520, 420);
            window.Show();
            window.Rebuild();
        }

        private void OnFocus() => Rebuild();

        /// <summary>The Home card's check line: where this project stands, and whether the window can act on it.</summary>
        internal static (string text, bool ok) HomeCheck()
        {
            if (!MeticaPaths.HasGDSdk)
                return MeticaPaths.FileExists(RemoteConfigPath)
                    ? ("Custom SDK — MeticaRemoteConfig and the lines for your fetch handler", true)
                    : ("Custom SDK — needs the Metica ads runtime first (Ads integration)", false);

            var sdk = "GD Monetization SDK " + (GdSdkVersion.Reported() is string v ? v + " " : "");
            if (PatchCoreFilesStep.RemoteSwitchBlocker() == null)
                return (sdk + "— remote switch: " + RemoteSwitchPatch.Describe().TrimEnd('.'), true);

            var prep = PatchCoreFilesStep.RemoteSwitchPrep();
            return prep != null && prep.Count > 0
                ? (sdk + "— Prepare first, then the switch", true)
                : (sdk + "— needs the Ads integration first", false);
        }

        public void CreateGUI()
        {
            _root = rootVisualElement;
            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiRoot + "/MeticaTheme.uss");
            if (theme != null) _root.styleSheets.Add(theme);
            _root.AddToClassList("mi-root");

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("mi-scroll");
            _content = El("mi-content");
            _scroll.Add(_content);
            _root.Add(_scroll);

            Rebuild();
        }

        private void Rebuild()
        {
            if (_content == null) return;
            _content.Clear();

            var head = El();
            head.Add(Text("Remote switch", "mi-title"));
            head.Add(Text("Firebase picks each user's ad network: MainAdNetwork = Unassigned, MAX or Metica. " +
                          "Only the remote switch and what it needs — nothing else in the project changes.", "mi-summary"));
            _content.Add(head);

            try
            {
                MeticaPaths.ForgetCache();
                if (MeticaPaths.HasGDSdk) BuildGdSdk();
                else BuildCustomSdk();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _content.Add(Box("mi-problem", MiIcon.Kind.Error, "Couldn't read the project — see the Console.", "mi-problem__text"));
            }
        }

        // ── GD SDK ─────────────────────────────────────────────────────────────

        private void BuildGdSdk()
        {
            var blocker = PatchCoreFilesStep.RemoteSwitchBlocker();
            var prep = blocker == null ? null : PatchCoreFilesStep.RemoteSwitchPrep();
            if (prep != null && prep.Count > 0)
            {
                BuildPrepare(prep);
                return;
            }

            if (blocker != null)
            {
                _content.Add(Box("mi-warning", MiIcon.Kind.Error, blocker));
                _content.Add(OpenIntegrationButton());
                return;
            }

            _content.Add(Line("Now: " + RemoteSwitchPatch.Describe()));

            var controls = El("mi-controls");
            foreach (var choice in RemoteSwitchPatch.Choices(_form, _source, form => _form = form, source => _source = source))
                controls.Add(Choice(choice));
            _content.Add(controls);

            var todo = RemoteSwitchPatch.Todo(_form, _source, "Apply");
            var missing = RemoteSwitchPatch.Missing(_form, _source);

            if (todo != null)
                _content.Add(Box("mi-warning", MiIcon.Kind.Error, todo));
            else if (missing.Count == 0)
                _content.Add(Box("mi-success", MiIcon.Kind.Check, "Remote switch in place."));

            if (missing.Count > 0 && RemoteSwitchPatch.Picked(_form, _source))
            {
                var list = El();
                foreach (var what in missing) list.Add(Text("• " + what, "mi-caption"));
                _content.Add(list);
            }

            var buttons = El("mi-row");
            var apply = IconTextButton(MiIcon.Kind.Check, "Apply", ApplyGdSdk, "mi-btn--primary");
            apply.SetEnabled(missing.Count > 0 && RemoteSwitchPatch.Picked(_form, _source));
            buttons.Add(apply);
            buttons.Add(OpenIntegrationButton());
            _content.Add(buttons);

            ShowLastLog();

            _content.Add(Text("Check on a device: the logs show \"Remote Switch saved: Metica as pref\" after the fetch, " +
                              "then \"Using Metica as main AdNetwork (Metica)\" at ads init.", "mi-caption"));
        }

        /// <summary>
        /// GD SDK 6.x without the Ads integration's patches: the switch would save MainAdNetwork
        /// with nothing to read it. Prepare adds just that — the preference, its enum, and
        /// AdsManager reading it — then the dropdowns show.
        /// </summary>
        private void BuildPrepare(List<string> prep)
        {
            var legacy = RemoteSwitchStep.LegacyFlag() != null;
            _content.Add(Line($"GD Monetization SDK {GdSdkVersion.Reported()} picks the network " +
                              (legacy ? "with a build-time flag" : "from the old UseMetica bool") +
                              ". The switch saves MainAdNetwork, so first this project needs:"));

            var list = El();
            foreach (var what in prep) list.Add(Text("• " + what, "mi-caption"));
            _content.Add(list);

            _content.Add(Text("Prepare makes only these edits, the same as the Ads integration's — in MonetizationPreferences.cs " +
                              "and AdsManager.cs" + (legacy ? ", and SDKConfiguration.cs" : "") +
                              "; originals are backed up to MeticaIntegrationBackups/. Every user starts Unassigned (AppLovin) " +
                              "until a fetch carrying the switch lands, and it applies from the next launch.", "mi-caption"));

            var buttons = El("mi-row");
            buttons.Add(IconTextButton(MiIcon.Kind.Check, "Prepare", PrepareGdSdk, "mi-btn--primary"));
            buttons.Add(OpenIntegrationButton());
            _content.Add(buttons);

            ShowLastLog();
        }

        private void PrepareGdSdk()
        {
            NextTick(() =>
            {
                var log = new List<string>();
                try
                {
                    log = PatchCoreFilesStep.PrepareRemoteSwitch();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    log.Add("Failed — see the Console: " + e.Message);
                }

                MeticaIntegrationLog.Record(LogTitle + " — prepare", log);
                AssetDatabase.Refresh();
                Finish(log);
            });
        }

        private void ApplyGdSdk()
        {
            var form = _form;
            var source = _source;
            NextTick(() =>
            {
                var log = new List<string>();
                try
                {
                    RemoteSwitchPatch.Apply(log, form, source);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    log.Add("Failed — see the Console: " + e.Message);
                }

                MeticaIntegrationLog.Record(LogTitle, log);
                AssetDatabase.Refresh();
                Finish(log);
            });
        }

        // ── Custom SDK ─────────────────────────────────────────────────────────

        private static string RemoteConfigPath => MeticaPaths.StandaloneRoot + "/MeticaRemoteConfig.cs";
        private const string RemoteConfigTemplate = "Standalone/MeticaRemoteConfig.cs.txt";

        private void BuildCustomSdk()
        {
            _content.Add(Line("No GD SDK here: your own fetch code calls MeticaRemoteConfig. The tool keeps " +
                              "MeticaRemoteConfig.cs up to date and gives you the line to paste."));

            if (!MeticaPaths.FileExists(RemoteConfigPath))
            {
                _content.Add(Box("mi-warning", MiIcon.Kind.Error,
                    $"No {RemoteConfigPath} — write the Metica ads runtime first (Ads integration)."));
                _content.Add(OpenIntegrationButton());
                return;
            }

            // 1. MeticaRemoteConfig.cs, as this tool writes it.
            var template = TemplateText();
            if (template == null)
                _content.Add(Box("mi-warning", MiIcon.Kind.Error, "This tool's MeticaRemoteConfig template is missing."));
            else if (FileText.Same(File.ReadAllText(MeticaPaths.ToAbsolute(RemoteConfigPath)), template))
                _content.Add(Box("mi-success", MiIcon.Kind.Check, "MeticaRemoteConfig.cs is up to date."));
            else
            {
                _content.Add(Box("mi-warning", MiIcon.Kind.Error,
                    "MeticaRemoteConfig.cs isn't this tool's version — older, or edited by hand."));
                var update = El("mi-row");
                update.Add(IconTextButton(MiIcon.Kind.Refresh, "Update MeticaRemoteConfig.cs", UpdateRemoteConfig, "mi-btn--primary"));
                _content.Add(update);
            }

            ShowLastLog();

            // 2. The line for the game's fetch handler.
            var controls = El("mi-controls");
            controls.Add(Choice(new StepChoice("Remote value",
                new[] { "Pick one…", RemoteSwitchPatch.ParameterLabel, RemoteSwitchPatch.JsonLabel },
                _customSource + 1,
                index => _customSource = index - 1,
                RemoteSwitchPatch.ParameterSummary + "\n" + RemoteSwitchPatch.JsonSummary)));

            if (_customSource == (int)RemoteSwitchSource.Parameter)
                controls.Add(Choice(new StepChoice("Remote switch",
                    new[] { RemoteSwitchPatch.KeepBothLabel, RemoteSwitchPatch.NewLabel },
                    _customKeepBoth ? 0 : 1,
                    index => _customKeepBoth = index == 0,
                    RemoteSwitchPatch.KeepBothSummary + "\n" + RemoteSwitchPatch.NewSummary)));
            _content.Add(controls);

            if (_customSource < 0) return;

            var snippet = Snippet((RemoteSwitchSource)_customSource, _customKeepBoth);
            _content.Add(Line("Paste into your Remote Config fetch handler, after the fetch is activated " +
                              "(Firebase shown — needs using Firebase.RemoteConfig;)." +
                              (_customSource == (int)RemoteSwitchSource.Json
                                  ? " ApplyAdsSettings also reads the old UseMetica key when MainAdNetwork is missing. " +
                                    "Use your own parameter name if it isn't AdsSettings."
                                  : "")));

            var code = Text(snippet, "mi-code-box");
            code.selection.isSelectable = true;
            MeticaIntegrationWindow.Mono(code);
            _content.Add(code);

            var copy = El("mi-row");
            copy.Add(IconTextButton(MiIcon.Kind.Copy, "Copy", () =>
            {
                EditorGUIUtility.systemCopyBuffer = snippet;
                ShowNotification(new GUIContent("Copied"));
            }, "mi-btn--secondary"));
            _content.Add(copy);

            _content.Add(Text("Check on a device: \"Remote switch stored: MainAdNetwork = Metica\" after the fetch, " +
                              "then \"MainAdNetwork = Metica from saved pref → Metica this session\" at ads init.", "mi-caption"));
        }

        /// <summary>The call for the game's fetch handler. Only a value that came from the fetch counts, not an in-app default.</summary>
        internal static string Snippet(RemoteSwitchSource source, bool keepBoth)
        {
            if (source == RemoteSwitchSource.Json)
                return "// Metica remote switch: AdsSettings' \"MainAdNetwork\" (Unassigned / MAX / Metica).\n" +
                       "var adsSettings = FirebaseRemoteConfig.DefaultInstance.GetValue(\"AdsSettings\");\n" +
                       "MeticaIntegration.MeticaRemoteConfig.ApplyAdsSettings(\n" +
                       "    adsSettings.Source == ValueSource.RemoteValue ? adsSettings.StringValue : null);";

            if (!keepBoth)
                return "// Metica remote switch: the \"MainAdNetwork\" parameter (Unassigned / MAX / Metica).\n" +
                       "var mainAdNetwork = FirebaseRemoteConfig.DefaultInstance.GetValue(\"MainAdNetwork\");\n" +
                       "MeticaIntegration.MeticaRemoteConfig.Apply(\n" +
                       "    mainAdNetwork.Source == ValueSource.RemoteValue ? mainAdNetwork.StringValue : null);";

            return "// Metica remote switch: the \"MainAdNetwork\" parameter (Unassigned / MAX / Metica),\n" +
                   "// and the old \"UseMetica\" (true / false) when MainAdNetwork isn't there.\n" +
                   "var remoteConfig = FirebaseRemoteConfig.DefaultInstance;\n" +
                   "var mainAdNetwork = remoteConfig.GetValue(\"MainAdNetwork\");\n" +
                   "var useMetica = remoteConfig.GetValue(\"UseMetica\");\n" +
                   "if (mainAdNetwork.Source == ValueSource.RemoteValue)\n" +
                   "    MeticaIntegration.MeticaRemoteConfig.Apply(mainAdNetwork.StringValue);\n" +
                   "else if (useMetica.Source == ValueSource.RemoteValue)\n" +
                   "    MeticaIntegration.MeticaRemoteConfig.Apply(useMetica.BooleanValue);";
        }

        private static string TemplateText()
        {
            if (MeticaPaths.TemplatesRoot == null) return null;
            var path = MeticaPaths.ToAbsolute(MeticaPaths.TemplatesRoot + "/" + RemoteConfigTemplate);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private void UpdateRemoteConfig()
        {
            NextTick(() =>
            {
                if (!EditorUtility.DisplayDialog("Update MeticaRemoteConfig.cs",
                        $"Replace {RemoteConfigPath} with this tool's version?\n\nChanges made to it by hand are lost; " +
                        "the current file is backed up to MeticaIntegrationBackups/ first.",
                        "Replace", "Cancel"))
                    return;

                var log = new List<string>();
                try
                {
                    var template = TemplateText();
                    if (template == null)
                    {
                        log.Add("This tool's MeticaRemoteConfig template is missing.");
                    }
                    else
                    {
                        SourcePatcher.WriteWithBackup(RemoteConfigPath, template);
                        log.Add($"Updated {RemoteConfigPath} to this tool's version (backup in MeticaIntegrationBackups/)");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    log.Add("Failed — see the Console: " + e.Message);
                }

                MeticaIntegrationLog.Record(LogTitle, log);
                AssetDatabase.Refresh();
                Finish(log);
            });
        }

        // ── Shared ─────────────────────────────────────────────────────────────

        private void Finish(List<string> log)
        {
            if (this == null) return;
            _lastLog = log;
            _form = null;
            _source = null;
            RefreshOpenWindows();
            Rebuild();
        }

        private void ShowLastLog()
        {
            if (_lastLog.Count == 0) return;
            var log = El();
            foreach (var entry in _lastLog) log.Add(Text(entry, "mi-log__entry"));
            _content.Add(log);
        }

        private static Button OpenIntegrationButton() =>
            IconTextButton(MiIcon.Kind.Ad, "Open the Ads integration", MeticaIntegrationWindow.Open, "mi-btn--secondary");

        private static Label Line(string text) => Text(text, "mi-summary");

        private VisualElement Choice(StepChoice choice)
        {
            var block = El();
            var row = El("mi-choice");
            row.Add(Text(choice.Label, "mi-choice__label"));

            var select = new Button().With("mi-select");
            select.Add(Text(choice.Options[choice.Selected]));
            select.Add(new MiIcon(MiIcon.Kind.ChevronDown, 2f, "mi-icon--12"));
            select.clicked += () =>
            {
                var menu = new GenericMenu();
                for (var i = 0; i < choice.Options.Length; i++)
                {
                    var index = i;
                    menu.AddItem(new GUIContent(choice.Options[i]), i == choice.Selected, () =>
                    {
                        choice.OnChanged(index);
                        _lastLog.Clear();
                        NextTick(Rebuild);
                    });
                }
                menu.DropDown(select.worldBound);
            };
            row.Add(select);
            block.Add(row);
            if (choice.Caption != null) block.Add(Text(choice.Caption, "mi-caption"));
            return block;
        }

        /// <summary>Runs <paramref name="action"/> once, on the next editor update — never inside a UI callback.</summary>
        private static void NextTick(Action action)
        {
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                EditorApplication.update -= tick;
                action();
            };
            EditorApplication.update += tick;
        }
    }
}
