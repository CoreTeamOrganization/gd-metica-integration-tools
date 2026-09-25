using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The one Metica Integration window: Home picks a flow (Ads Integration or Genre
    /// Creator), then the flow runs one step per screen. Built with UI Toolkit against
    /// Editor/UI/MeticaTheme.uss; the flows themselves (<see cref="MeticaFlow"/>) hold no UI.
    ///
    /// <para>The whole screen is rebuilt on every refresh — on focus, after a recompile, and
    /// after every action — rather than kept in sync piece by piece. Steps describe their own
    /// controls (<see cref="StepControl"/>); this window draws them in the theme.</para>
    /// </summary>
    public sealed class MeticaIntegrationWindow : EditorWindow, IHasCustomMenu
    {
        private enum Page { Home, Ads, Genre }

        [SerializeField] private Page _page = Page.Home;

        /// <summary>A done or skipped step being revisited; null shows the current step.</summary>
        [SerializeField] private string _viewedStepId;

        [SerializeField] private bool _logOpen;
        [SerializeField] private List<string> _whyOpen = new List<string>();

        private MeticaFlow _ads;
        private MeticaFlow _genre;

        private bool _busy;
        private bool _refreshQueued;
        private string _renderedKey;

        private VisualElement _root;
        private VisualElement _header;
        private VisualElement _stepper;
        private ScrollView _scroll;
        private VisualElement _content;
        private VisualElement _footer;
        private VisualElement _menu;
        private Button _moreButton;

        private static string UiRoot => MeticaPaths.ToolRoot + "/Editor/UI";

        /// <summary>
        /// The review panel's "git diff" command and its Copy button. Off for now — the panel
        /// shows its hint and the sign-off only. Kept, not removed: set true to bring it back.
        /// </summary>
        private static readonly bool ShowDiffCommand = false;

        private MeticaFlow Flow => _page == Page.Ads ? _ads : _page == Page.Genre ? _genre : null;

        [MenuItem("GameDistrict/Metica/Metica Integration...", false, 10)]
        public static void Open()
        {
            var window = GetWindow<MeticaIntegrationWindow>();
            window.Show();
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void OnEnable()
        {
            var iconSize = EditorGUIUtility.pixelsPerPoint > 1f ? 32 : 16;
            titleContent = new GUIContent("Metica Integration",
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{UiRoot}/Icons/tab-icon-{iconSize}.png"));
            minSize = new Vector2(520, 480);

            _ads = AdsFlow.Create();
            _genre = GenreFlow.Create();

            AssemblyReloadEvents.afterAssemblyReload += QueueRefresh;
        }

        private void OnDisable() => AssemblyReloadEvents.afterAssemblyReload -= QueueRefresh;

        private void OnFocus() => QueueRefresh();

        public void AddItemsToMenu(GenericMenu menu) =>
            menu.AddItem(new GUIContent("Remove Metica Integration Tools…"), false, ToolRemover.Remove);

        public void CreateGUI()
        {
            _root = rootVisualElement;
            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiRoot + "/MeticaTheme.uss");
            if (theme != null) _root.styleSheets.Add(theme);
            _root.AddToClassList("mi-root");

            _header = El("mi-header");
            _stepper = El("mi-stepper");
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("mi-scroll");
            _content = El("mi-content");
            _scroll.Add(_content);
            _footer = El("mi-footer");

            _root.Add(_header);
            _root.Add(_stepper);
            _root.Add(_scroll);
            _root.Add(_footer);

            // The ⋮ menu closes on any click outside it.
            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_menu == null) return;
                var target = evt.target as VisualElement;
                if (target != null && (_menu.Contains(target) || _moreButton.Contains(target))) return;
                CloseMenu();
            }, TrickleDown.TrickleDown);

            RefreshNow();
        }

        /// <summary>Re-verifies on the next editor tick — never inside a UI callback.</summary>
        private void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;

            EditorApplication.delayCall += () =>
            {
                _refreshQueued = false;
                if (this != null) RefreshNow();
            };
        }

        private void RefreshNow()
        {
            if (_root == null) return;

            MeticaPaths.ForgetCache();
            _ads.Refresh();
            _genre.Refresh();
            Rebuild();
        }

        /// <summary>Runs a step's action on the next tick, then re-verifies everything.</summary>
        private void RunAction(MeticaFlow flow, int index)
        {
            if (_busy) return;
            _busy = true;
            Rebuild();

            EditorApplication.delayCall += () =>
            {
                flow.Apply(index);
                _busy = false;
                _viewedStepId = null;
                if (this != null) RefreshNow();
            };
        }

        // ── Screens ────────────────────────────────────────────────────────────

        private void Rebuild()
        {
            if (_root == null) return;

            CloseMenu();
            var flow = Flow;
            var index = flow == null ? -1 : ViewedIndex(flow);

            // Keep the scroll position while the same screen is rebuilt (Re-check, toggling Why?).
            var key = $"{_page}/{index}";
            var offset = _scroll.scrollOffset;

            _header.Clear();
            _stepper.Clear();
            _content.Clear();
            _footer.Clear();

            BuildHeader(flow);

            if (flow == null)
            {
                _stepper.style.display = DisplayStyle.None;
                BuildHome();
            }
            else
            {
                _stepper.style.display = DisplayStyle.Flex;
                BuildStepper(flow, index);
                if (index >= flow.Steps.Length) BuildFinished(flow);
                else BuildStep(flow, index);
            }

            BuildFooter(flow);

            if (key == _renderedKey) _scroll.schedule.Execute(() => _scroll.scrollOffset = offset);
            else _scroll.scrollOffset = Vector2.zero;
            _renderedKey = key;
        }

        private int ViewedIndex(MeticaFlow flow)
        {
            if (_viewedStepId != null)
            {
                var index = flow.IndexOf(_viewedStepId);
                if (index >= 0 && (flow.StateOf(index) == StepState.Done || flow.StateOf(index) == StepState.Skipped))
                    return index;
                _viewedStepId = null;
            }

            return flow.CurrentIndex;
        }

        private void ShowPage(Page page)
        {
            _page = page;
            _viewedStepId = null;
            Rebuild();
        }

        private void GoHome() => ShowPage(Page.Home);

        // ── Header ─────────────────────────────────────────────────────────────

        private void BuildHeader(MeticaFlow flow)
        {
            var left = El("mi-header__left");

            var home = IconButton(MiIcon.Kind.Home, GoHome, "Home");
            home.SetEnabled(flow != null);
            left.Add(home);
            left.Add(El("mi-divider-v"));

            var crumb = El("mi-crumb");
            if (flow == null)
            {
                crumb.Add(Text("Home", "mi-crumb__current"));
            }
            else
            {
                crumb.Add(new Button(GoHome) { text = "<u>Home</u>" }.With("mi-link", "mi-crumb__link"));
                crumb.Add(Text("›", "mi-crumb__sep"));
                crumb.Add(Text(flow.Name, "mi-crumb__current"));
            }
            left.Add(crumb);

            var right = El("mi-header__right");
            if (flow == _ads && MeticaPaths.HasGDSdk)
            {
                var chip = El("mi-chip");
                chip.Add(new MiIcon(MiIcon.Kind.Chip, 1.75f, "mi-icon--12"));
                chip.Add(Text("GD Monetization SDK"));
                right.Add(chip);
            }

            _moreButton = IconButton(MiIcon.Kind.More, ToggleMenu, "More options");
            right.Add(_moreButton);

            _header.Add(left);
            _header.Add(right);
        }

        private void ToggleMenu()
        {
            if (_menu != null) { CloseMenu(); return; }

            _menu = El("mi-menu");
            if (Flow != null)
            {
                _menu.Add(MenuRow(MiIcon.Kind.Home, "Back to home", GoHome));
                _menu.Add(El("mi-menu__divider"));
            }

            _menu.Add(MenuRow(MiIcon.Kind.Trash, "Remove Metica Integration Tools…",
                () => EditorApplication.delayCall += ToolRemover.Remove));

            _root.Add(_menu);
            _moreButton.AddToClassList("mi-icon-btn--active");
        }

        private void CloseMenu()
        {
            _menu?.RemoveFromHierarchy();
            _menu = null;
            _moreButton?.RemoveFromClassList("mi-icon-btn--active");
        }

        private Button MenuRow(MiIcon.Kind icon, string label, Action action)
        {
            var item = new Button(() =>
            {
                CloseMenu();
                action();
            }).With("mi-menu__item");
            item.Add(new MiIcon(icon, 1.75f, "mi-icon--16"));
            item.Add(Text(label));
            return item;
        }

        // ── Home ───────────────────────────────────────────────────────────────

        private void BuildHome()
        {
            var intro = El();
            intro.Add(Text("What do you want to set up?", "mi-home-title"));
            intro.Add(Text("Two separate jobs. Pick one — you can come back to the other.", "mi-home-sub"));
            _content.Add(intro);

            var adsCount = _ads.Steps.Length;
            var gdSdk = MeticaPaths.HasGDSdk ? GdSdkVersion.Reported() : null;
            var gdSdkSupported = GdSdkVersion.IsSupported(gdSdk);
            _content.Add(FlowCard(_ads, MiIcon.Kind.Ad,
                "Add Metica ads to the game. Works standalone, or wires into the GD Monetization SDK if the project has it.",
                !MeticaPaths.HasGDSdk ? $"No GD Monetization SDK — standalone, {adsCount} steps"
                : !gdSdkSupported ? $"GD Monetization SDK {gdSdk} isn't supported — needs {GdSdkVersion.Minimum}+"
                : $"GD Monetization SDK {(gdSdk == null ? "" : gdSdk + " ")}detected — {adsCount} steps",
                gdSdkSupported, () => ShowPage(Page.Ads)));

            var meticaInstalled = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Metica.SDK");
            _content.Add(FlowCard(_genre, MiIcon.Kind.Chart,
                "Set up the Android toolchain and create genre analytics files.",
                meticaInstalled
                    ? "Needs the Metica SDK — installed"
                    : "Needs the Metica SDK — install it in Ads Integration first",
                meticaInstalled, () => ShowPage(Page.Genre)));
        }

        private Button FlowCard(MeticaFlow flow, MiIcon.Kind icon, string description, string check, bool checkOk,
            Action open)
        {
            var card = new Button(open).With("mi-card");
            card.SetEnabled(!_busy);

            var tile = El("mi-card__tile");
            tile.Add(new MiIcon(icon, 1.75f, "mi-icon--24"));
            card.Add(tile);

            var body = El("mi-card__body");
            var head = El("mi-card__head");
            head.Add(Text(flow.Name, "mi-card__title"));
            var cta = El("mi-card__cta");
            cta.Add(Text(!flow.Started ? "Start" : flow.Finished ? "Review" : "Continue"));
            cta.Add(new MiIcon(MiIcon.Kind.ChevronRight, 2f));
            head.Add(cta);
            body.Add(head);

            body.Add(Text(description, "mi-card__desc"));

            var checkRow = El("mi-card__check");
            if (!checkOk) checkRow.AddToClassList("mi-card__check--missing");
            checkRow.Add(new MiIcon(checkOk ? MiIcon.Kind.Check : MiIcon.Kind.Error, 2f));
            checkRow.Add(Text(check));
            body.Add(checkRow);

            var total = flow.Steps.Length;
            if (flow.Started)
            {
                var bar = El("mi-card__progress");
                for (var i = 0; i < total; i++)
                {
                    var segment = El("mi-card__segment");
                    var state = flow.StateOf(i);
                    if (state == StepState.Done) segment.AddToClassList("mi-card__segment--done");
                    if (state == StepState.Skipped) segment.AddToClassList("mi-card__segment--skipped");
                    bar.Add(segment);
                }
                body.Add(bar);
            }

            var skipped = flow.SkippedCount;
            body.Add(Text(!flow.Started ? "Not started"
                : flow.Finished ? skipped == 0 ? $"All {total} done" : $"All done — {flow.DoneCount} done, {skipped} skipped"
                : $"{flow.DoneCount} of {total} done", "mi-card__status"));

            card.Add(body);
            return card;
        }

        // ── Stepper ────────────────────────────────────────────────────────────

        private void BuildStepper(MeticaFlow flow, int viewed)
        {
            var total = flow.Steps.Length;
            var skipped = flow.SkippedCount;

            _stepper.Add(Text(viewed >= total
                ? skipped == 0 ? $"{total} of {total} — all done" : $"{total} of {total} — {flow.DoneCount} done, {skipped} skipped"
                : $"Step {viewed + 1} of {total} — {flow.Steps[viewed].Title}", "mi-stepper__label"));

            var row = El("mi-stepper__row");
            for (var i = 0; i < total; i++)
            {
                row.Add(StepDot(flow, i, viewed));
                if (i < total - 1)
                {
                    var line = El("mi-step-line");
                    if (flow.StateOf(i) == StepState.Done) line.AddToClassList("mi-step-line--done");
                    row.Add(line);
                }
            }
            _stepper.Add(row);
        }

        private Button StepDot(MeticaFlow flow, int index, int viewed)
        {
            var state = flow.StateOf(index);
            var stepId = flow.Steps[index].Id;
            var revisitable = state == StepState.Done || state == StepState.Skipped;

            var dot = new Button(() =>
            {
                _viewedStepId = revisitable ? stepId : null;
                Rebuild();
            }).With("mi-step");
            dot.tooltip = $"{index + 1}. {flow.Steps[index].Title} — {state.ToString().ToLowerInvariant()}";

            switch (state)
            {
                case StepState.Done:
                    dot.AddToClassList("mi-step--done");
                    dot.Add(new MiIcon(MiIcon.Kind.Check, 2.5f, "mi-icon--12"));
                    break;
                case StepState.Current:
                    dot.AddToClassList("mi-step--current");
                    dot.text = (index + 1).ToString();
                    break;
                case StepState.Review:
                    dot.AddToClassList("mi-step--review");
                    dot.Add(new MiIcon(MiIcon.Kind.Check, 2.5f, "mi-icon--12"));
                    break;
                case StepState.Skipped:
                    dot.AddToClassList("mi-step--skipped");
                    dot.Add(new MiIcon(MiIcon.Kind.Skip, 2f, "mi-icon--12"));
                    break;
                default:
                    dot.AddToClassList("mi-step--locked");
                    dot.Add(new MiIcon(MiIcon.Kind.Lock, 2f, "mi-icon--12"));
                    dot.SetEnabled(false);
                    break;
            }

            if (index == viewed && revisitable) dot.AddToClassList("mi-step--viewing");
            return dot;
        }

        // ── One step ───────────────────────────────────────────────────────────

        private void BuildStep(MeticaFlow flow, int index)
        {
            var step = flow.Steps[index];
            var result = flow.Result(index);
            var state = flow.StateOf(index);
            var reviewing = state == StepState.Review && !flow.IsReviewed(index);
            var extra = Math.Max(0, result.Problems.Count - 1);

            // Title + summary.
            var head = El();
            var titleRow = El("mi-title-row");
            titleRow.Add(Text(step.Title, "mi-title"));
            if (step.Optional) titleRow.Add(Text("Optional", "mi-pill"));
            head.Add(titleRow);
            head.Add(Text(step.Summary, "mi-summary"));
            _content.Add(head);

            // Status: at most one problem line, or the success line of a finished step.
            var status = El("mi-notes");
            if (state == StepState.Done)
            {
                status.Add(Box("mi-success", MiIcon.Kind.Check, "Done — verified and signed off."));
            }
            else if (result.Problems.Count > 0)
            {
                var problem = Box("mi-problem", MiIcon.Kind.Error, result.Problems[0], "mi-problem__text");
                if (extra > 0)
                    problem.Add(new Button(() => OpenWhy(step.Id)) { text = $"<u>+{extra} more</u>" }
                        .With("mi-link", "mi-problem__more"));
                status.Add(problem);
            }

            foreach (var note in result.Notes) status.Add(Text("• " + note, "mi-note"));
            if (status.childCount > 0) _content.Add(status);

            // The step's own controls: extra buttons, switches, dropdowns.
            var controls = step.Controls(result).ToList();
            if (controls.Count > 0) _content.Add(ControlsBlock(controls));

            // Actions: one yellow button per screen — the next thing to do.
            var actions = El("mi-row");
            if (step.ActionLabel != null)
            {
                var primary = !reviewing && state != StepState.Done && state != StepState.Skipped;
                var action = new Button(() => RunAction(flow, index)) { text = step.ActionLabel }
                    .With("mi-btn", primary ? "mi-btn--primary" : "mi-btn--secondary");
                action.SetEnabled(!_busy);
                actions.Add(action);
            }

            actions.Add(IconTextButton(MiIcon.Kind.Refresh, "Re-check", QueueRefresh, "mi-btn--secondary"));

            if (step.Optional)
            {
                actions.Add(El("mi-row__spacer"));
                actions.Add(state == StepState.Skipped
                    ? IconTextButton(MiIcon.Kind.Undo, "Un-skip this step", () =>
                    {
                        flow.Unskip(index);
                        _viewedStepId = null;
                        QueueRefresh();
                    }, "mi-btn--ghost")
                    : IconTextButton(MiIcon.Kind.Skip, "Skip this step", () =>
                    {
                        flow.Skip(index);
                        _viewedStepId = null;
                        QueueRefresh();
                    }, "mi-btn--ghost"));
            }
            _content.Add(actions);

            if (reviewing) _content.Add(ReviewPanel(flow, index));

            if (step.Why != null || extra > 0) _content.Add(WhySection(step, result, extra));
        }

        private VisualElement ReviewPanel(MeticaFlow flow, int index)
        {
            var step = flow.Steps[index];
            var panel = El("mi-review");

            var head = El("mi-review__head");
            head.Add(new MiIcon(MiIcon.Kind.Check, 2.5f, "mi-icon--16"));
            head.Add(Text("Verified — review before continuing", "mi-review__title"));
            panel.Add(head);

            if (step.ReviewHint != null) panel.Add(Text(step.ReviewHint, "mi-review__hint"));

            var paths = ShowDiffCommand
                ? step.TouchedPaths.Where(p => !string.IsNullOrEmpty(p)).ToArray()
                : Array.Empty<string>();
            var command = "git diff -- " + string.Join(" ", paths.Select(p => $"\"{p}\""));

            if (paths.Length > 0)
            {
                panel.Add(Text("Diff command", "mi-review__label"));
                var code = Text(command, "mi-code-box");
                code.selection.isSelectable = true;
                Mono(code);
                panel.Add(code);
            }
            else if (ShowDiffCommand)
            {
                panel.Add(Text("This step changed no files — nothing to diff.", "mi-review__hint"));
            }

            var buttons = El("mi-review__buttons");
            if (paths.Length > 0)
                buttons.Add(IconTextButton(MiIcon.Kind.Copy, "Copy the diff command", () =>
                {
                    EditorGUIUtility.systemCopyBuffer = command;
                    ShowNotification(new GUIContent("Diff command copied"));
                }, "mi-btn--secondary"));
            else
                buttons.Add(El());

            buttons.Add(IconTextButton(MiIcon.Kind.Check, "Reviewed — next step", () =>
            {
                flow.MarkReviewed(index);
                _viewedStepId = null;
                QueueRefresh();
            }, "mi-btn--primary"));
            panel.Add(buttons);

            return panel;
        }

        private VisualElement WhySection(MeticaStep step, VerifyResult result, int extra)
        {
            var open = _whyOpen.Contains(step.Id);
            var why = El("mi-why");

            var toggle = new Button(() =>
            {
                if (!_whyOpen.Remove(step.Id)) _whyOpen.Add(step.Id);
                Rebuild();
            }).With("mi-why__toggle");
            toggle.Add(new MiIcon(open ? MiIcon.Kind.ChevronDown : MiIcon.Kind.ChevronRight, 2f));
            toggle.Add(Text("Why?"));
            why.Add(toggle);

            if (!open) return why;

            var body = El("mi-why__body");
            if (step.Why != null) body.Add(Text(step.Why, "mi-why__text"));
            for (var i = 1; i <= extra; i++)
            {
                var row = El("mi-why__problem");
                row.Add(new MiIcon(MiIcon.Kind.Error, 2f, "mi-icon--12"));
                var label = Text(result.Problems[i]);
                Mono(label);
                row.Add(label);
                body.Add(row);
            }
            why.Add(body);
            return why;
        }

        private void OpenWhy(string stepId)
        {
            if (!_whyOpen.Contains(stepId)) _whyOpen.Add(stepId);
            Rebuild();
        }

        /// <summary>
        /// A step's controls in the theme. Consecutive buttons share a row; a switch or a
        /// dropdown gets its own.
        /// </summary>
        private VisualElement ControlsBlock(IEnumerable<StepControl> controls)
        {
            var block = El("mi-controls");
            VisualElement buttons = null;

            foreach (var control in controls)
            {
                switch (control)
                {
                    case StepButton button:
                        if (buttons == null) block.Add(buttons = El("mi-row"));
                        var element = button.Icon == StepIcon.None
                            ? new Button(() => RunControl(button.OnClick)) { text = button.Label }.With("mi-btn", "mi-btn--secondary")
                            : IconTextButton(button.Icon == StepIcon.Folder ? MiIcon.Kind.Folder : MiIcon.Kind.File,
                                button.Label, () => RunControl(button.OnClick), "mi-btn--secondary");
                        element.SetEnabled(button.Enabled && !_busy);
                        buttons.Add(element);
                        break;

                    case StepToggle toggle:
                        buttons = null;
                        block.Add(Switch(toggle));
                        break;

                    case StepChoice choice:
                        buttons = null;
                        block.Add(Choice(choice));
                        if (choice.Caption != null) block.Add(Text(choice.Caption, "mi-caption"));
                        break;
                }
            }

            return block;
        }

        private Button Switch(StepToggle toggle)
        {
            var row = new Button(() => RunControl(() => toggle.OnChanged(!toggle.Value))).With("mi-switch");
            if (toggle.Value) row.AddToClassList("mi-switch--on");

            var track = El("mi-switch__track");
            track.Add(El("mi-switch__knob"));
            row.Add(track);
            row.Add(Text(toggle.Label, "mi-switch__label"));
            return row;
        }

        private VisualElement Choice(StepChoice choice)
        {
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
                    menu.AddItem(new GUIContent(choice.Options[i]), i == choice.Selected,
                        () => RunControl(() => choice.OnChanged(index)));
                }
                menu.DropDown(select.worldBound);
            };
            row.Add(select);
            return row;
        }

        /// <summary>
        /// A control's callback, on the next tick like a step action — some open dialogs or
        /// touch assets — then a full re-check, since a control can change what steps verify.
        /// </summary>
        private void RunControl(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                if (this != null) RefreshNow();
            };
        }

        // ── Finished ───────────────────────────────────────────────────────────

        private void BuildFinished(MeticaFlow flow)
        {
            var card = El("mi-done-card");
            var badge = El("mi-done-badge");
            badge.Add(new MiIcon(MiIcon.Kind.Check, 2.5f, "mi-icon--24"));
            card.Add(badge);
            card.Add(Text("All done.", "mi-title"));
            var summary = Text("Build to a device and check the Metica logs.", "mi-summary");
            summary.style.marginBottom = 16;
            card.Add(summary);

            var buttons = El("mi-row");
            buttons.Add(IconTextButton(MiIcon.Kind.Home, "Back to home", GoHome, "mi-btn--primary"));
            buttons.Add(IconTextButton(MiIcon.Kind.Refresh, "Re-check everything", QueueRefresh, "mi-btn--secondary"));
            card.Add(buttons);
            _content.Add(card);

            var skipped = Enumerable.Range(0, flow.Steps.Length).Where(flow.IsSkipped).ToList();
            if (skipped.Count == 0) return;

            var panel = El("mi-panel");
            var head = El("mi-panel__head");
            head.Add(Text($"SKIPPED STEPS ({skipped.Count})", "mi-section-label"));
            head.Add(Text("Optional — fine to leave", "mi-caption"));
            panel.Add(head);

            foreach (var index in skipped)
            {
                var stepId = flow.Steps[index].Id;
                var row = El("mi-panel__row");
                row.Add(new MiIcon(MiIcon.Kind.Skip, 2f));
                row.Add(Text((index + 1).ToString(), "mi-panel__num"));
                row.Add(Text(flow.Steps[index].Title, "mi-panel__name"));
                row.Add(new Button(() =>
                {
                    _viewedStepId = stepId;
                    Rebuild();
                }) { text = "Open step" }.With("mi-btn", "mi-btn--secondary", "mi-btn--small"));
                panel.Add(row);
            }
            _content.Add(panel);
        }

        // ── Footer ─────────────────────────────────────────────────────────────

        private void BuildFooter(MeticaFlow flow)
        {
            var row = El("mi-footer__row");
            row.Add(Ghost("Re-check everything", QueueRefresh));
            row.Add(El("mi-footer__sep"));

            var backups = Ghost("Reveal backups", () => EditorUtility.RevealInFinder(MeticaPaths.BackupRoot + "/"));
            backups.SetEnabled(Directory.Exists(MeticaPaths.BackupRoot));
            row.Add(backups);
            row.Add(El("mi-footer__sep"));

            row.Add(Ghost("Reset sign-offs", () =>
            {
                if (!EditorUtility.DisplayDialog("Reset sign-offs",
                        "Forget which steps you have reviewed or skipped? Nothing in the project changes — " +
                        "the run just asks you to sign each step off again.", "Reset", "Cancel"))
                    return;

                if (flow != null) flow.ResetSignOffs();
                else { _ads.ResetSignOffs(); _genre.ResetSignOffs(); }
                QueueRefresh();
            }));
            _footer.Add(row);

            var entries = MeticaIntegrationLog.All;
            var toggle = new Button(() =>
            {
                _logOpen = !_logOpen;
                Rebuild();
            }).With("mi-log-toggle");
            toggle.Add(new MiIcon(_logOpen ? MiIcon.Kind.ChevronDown : MiIcon.Kind.ChevronRight, 2f, "mi-icon--12"));
            toggle.Add(Text($"What this tool changed ({entries.Count})"));
            _footer.Add(toggle);

            if (!_logOpen || entries.Count == 0) return;

            var log = new ScrollView(ScrollViewMode.Vertical);
            log.AddToClassList("mi-log");
            for (var i = entries.Count - 1; i >= 0; i--) log.Add(Text(entries[i], "mi-log__entry"));
            log.Add(Ghost("Clear", () =>
            {
                MeticaIntegrationLog.Clear();
                Rebuild();
            }));
            _footer.Add(log);
        }

        // ── Small builders ─────────────────────────────────────────────────────

        private static VisualElement El(params string[] classes)
        {
            var element = new VisualElement();
            foreach (var c in classes) element.AddToClassList(c);
            return element;
        }

        private static Label Text(string text, params string[] classes) => new Label(text).With(classes);

        private static VisualElement Box(string boxClass, MiIcon.Kind icon, string text, string textClass = null)
        {
            var box = El(boxClass);
            box.Add(new MiIcon(icon, 2f, "mi-icon"));
            box.Add(textClass == null ? Text(text) : Text(text, textClass));
            return box;
        }

        private Button IconButton(MiIcon.Kind icon, Action action, string tooltip)
        {
            var button = new Button(action) { tooltip = tooltip }.With("mi-icon-btn");
            button.Add(new MiIcon(icon, 1.75f, "mi-icon--16"));
            return button;
        }

        private static Button IconTextButton(MiIcon.Kind icon, string label, Action action, string style)
        {
            var button = new Button(action).With("mi-btn", style);
            button.Add(new MiIcon(icon, 1.75f));
            button.Add(Text(label));
            return button;
        }

        private static Button Ghost(string label, Action action) =>
            new Button(action) { text = label }.With("mi-btn", "mi-btn--ghost");

        private static Font _monoFont;

        /// <summary>The editor's own monospace font, or a system one; left as is if neither loads.</summary>
        private static void Mono(VisualElement element)
        {
            try
            {
                if (_monoFont == null)
                    _monoFont = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font
                                ?? Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New" }, 12);
                if (_monoFont != null)
                    element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(_monoFont));
            }
            catch
            {
                // Keep the default font.
            }
        }
    }

    internal static class VisualElementExtensions
    {
        /// <summary>
        /// Adds theme classes, and drops Unity's own default look (unity-button / unity-label)
        /// so the theme's margins, colours and alignment are the only ones that apply —
        /// Unity's defaults would otherwise win over the theme's spacing.
        /// </summary>
        public static T With<T>(this T element, params string[] classes) where T : VisualElement
        {
            if (element is Button) element.RemoveFromClassList(Button.ussClassName);
            else if (element is Label) element.RemoveFromClassList(Label.ussClassName);

            foreach (var c in classes) element.AddToClassList(c);
            return element;
        }
    }
}
