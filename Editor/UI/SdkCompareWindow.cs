using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using static GameDistrict.MeticaIntegrationTools.MeticaIntegrationWindow;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Shows how the project's GD SDK code differs from the original release it declares:
    /// changed, added and missing files, each with its diff. The only changes made from here
    /// are "Replace with original" and "Delete" (added files), one file at a time, after a
    /// confirm and with a backup.
    /// See <see cref="SdkCompare"/> for what is compared.
    /// </summary>
    public sealed class SdkCompareWindow : EditorWindow
    {
        private enum Filter { Metica, Tool, All }

        [SerializeField] private Filter _filter = Filter.Metica;
        [SerializeField] private string _selected;

        // Ticked files, for one action on several — always a subset of what the filter shows.
        [SerializeField] private List<string> _checked = new List<string>();
        private readonly Dictionary<string, Button> _checks = new Dictionary<string, Button>();
        private List<SdkFileChange> _shown = new List<SdkFileChange>();
        private VisualElement _bar;

        private SdkCompareResult _result;
        private VisualElement _root;
        private VisualElement _top;
        private ScrollView _list;
        private ScrollView _diff;

        private static string UiRoot => MeticaPaths.ToolRoot + "/Editor/UI";

        [MenuItem("GameDistrict/Metica/Compare with original GD SDK...", false, 11)]
        public static void Open()
        {
            var window = GetWindow<SdkCompareWindow>();
            window.titleContent = new GUIContent("Compare GD SDK");
            window.minSize = new Vector2(720, 420);
            window.Show();

            // A window already open shows its last scan; opening it again re-scans.
            if (window._root != null) window.Rescan();
        }

        public void CreateGUI()
        {
            _root = rootVisualElement;
            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiRoot + "/MeticaTheme.uss");
            if (theme != null) _root.styleSheets.Add(theme);
            _root.AddToClassList("mi-root");
            _root.AddToClassList("mi-cmp");

            _top = El("mi-cmp__top");
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("mi-cmp__list");
            _diff = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _diff.AddToClassList("mi-cmp__diff");

            var split = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("mi-cmp__split");
            split.Add(_list);
            split.Add(_diff);

            _root.Add(_top);
            _root.Add(split);

            if (_result == null) Rescan();
            else Rebuild();
        }

        private void Rescan()
        {
            try
            {
                MeticaPaths.ForgetCache();
                _result = SdkCompare.Run();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _result = new SdkCompareResult { Problem = "The comparison failed — see the Console." };
            }

            Rebuild();
        }

        // ── Screen ─────────────────────────────────────────────────────────────

        private void Rebuild()
        {
            if (_root == null || _result == null) return;

            _top.Clear();
            _list.Clear();
            _diff.Clear();

            BuildTop();

            var shown = _shown = Shown().ToList();
            _checked.RemoveAll(path => shown.All(c => c.Path != path));
            _checks.Clear();

            if (shown.Count == 0)
            {
                _list.Add(Text(_result.Problem != null ? string.Empty
                    : _filter == Filter.All ? "No differences — every file matches the original."
                    : "Nothing in this filter. Try All.", "mi-cmp__empty"));
                return;
            }

            if (_selected == null || shown.All(c => c.Path != _selected)) _selected = shown[0].Path;

            _bar = El("mi-cmp__bar");
            _list.Add(_bar);
            foreach (var change in shown) _list.Add(FileRow(change));
            UpdateSelection();
            BuildDiff(shown.First(c => c.Path == _selected));
        }

        // ── Selection ──────────────────────────────────────────────────────────

        /// <summary>
        /// Ticks and the bar above the list, redrawn in place so the list keeps its scroll.
        /// An action shows only when every ticked file allows it: added files can only be
        /// deleted, changed and missing ones only replaced, so a mix allows neither.
        /// </summary>
        private void UpdateSelection()
        {
            foreach (var pair in _checks) SetTick(pair.Value, _checked.Contains(pair.Key));

            _bar.Clear();
            var all = _checked.Count == _shown.Count;
            var tickAll = new Button(() =>
            {
                _checked.Clear();
                if (!all) _checked.AddRange(_shown.Select(c => c.Path));
                UpdateSelection();
            }).With("mi-cmp__check");
            tickAll.tooltip = all ? "Untick all" : "Tick all";
            SetTick(tickAll, all, some: _checked.Count > 0);
            _bar.Add(tickAll);
            _bar.Add(Text(_checked.Count == 0 ? "Tick files to act on several" : $"{_checked.Count} ticked", "mi-cmp__bar-label"));

            if (_checked.Count == 0) return;

            var picked = _shown.Where(c => _checked.Contains(c.Path)).ToList();
            var anyAdded = picked.Any(c => c.Kind == ChangeKind.Added);
            var anyOther = picked.Any(c => c.Kind != ChangeKind.Added);

            var actions = El("mi-row", "mi-cmp__bar-actions");
            var replace = IconTextButton(MiIcon.Kind.Undo, "Replace", () => ReplaceWithOriginal(picked), "mi-btn--secondary");
            replace.AddToClassList("mi-btn--small");
            replace.SetEnabled(anyOther && !anyAdded);
            actions.Add(replace);

            var delete = IconTextButton(MiIcon.Kind.Trash, "Delete", () => DeleteAdded(picked), "mi-btn--secondary");
            delete.AddToClassList("mi-btn--small");
            delete.SetEnabled(anyAdded && !anyOther);
            actions.Add(delete);
            _bar.Add(actions);

            if (anyAdded && anyOther)
                _bar.Add(Text("Added files can only be deleted, the others only replaced — tick one kind.", "mi-cmp__bar-hint"));
        }

        private void ToggleTick(string path)
        {
            if (!_checked.Remove(path)) _checked.Add(path);
            UpdateSelection();
        }

        private static void SetTick(Button box, bool on, bool some = false)
        {
            box.Clear();
            box.EnableInClassList("mi-cmp__check--on", on || some);
            if (on) box.Add(new MiIcon(MiIcon.Kind.Check, 2f, "mi-icon--12"));
            else if (some) box.Add(El("mi-cmp__check-dash"));
        }

        private IEnumerable<SdkFileChange> Shown() =>
            _result.Changes.Where(c => _filter == Filter.All
                                       || (_filter == Filter.Metica && c.Metica)
                                       || (_filter == Filter.Tool && c.ByTool));

        private void BuildTop()
        {
            var head = El("mi-cmp__head");
            var title = El("mi-cmp__title");
            title.Add(Text(VersionLine(), "mi-title"));
            if (_result.Problem == null) title.Add(Text(CountLine(), "mi-summary"));
            head.Add(title);
            head.Add(IconTextButton(MiIcon.Kind.Refresh, "Re-scan", Rescan, "mi-btn--secondary"));
            _top.Add(head);

            if (_result.Problem != null) _top.Add(Box("mi-problem", MiIcon.Kind.Error, _result.Problem));

            // A release can only be picked when the declared Version names none.
            if (_result.Version == null || _result.ChosenByHand) _top.Add(ReleasePicker());

            if (_result.Problem != null) return;

            var filters = El("mi-row", "mi-cmp__filters");
            filters.Add(FilterButton(Filter.Metica, "Metica", _result.Changes.Count(c => c.Metica)));
            filters.Add(FilterButton(Filter.Tool, "Tool", _result.Changes.Count(c => c.ByTool)));
            filters.Add(FilterButton(Filter.All, "All", _result.Changes.Count));
            _top.Add(filters);
            _top.Add(Text(FilterHint(), "mi-caption"));
        }

        private string VersionLine()
        {
            if (_result.Version == null) return "Compare with original GD SDK";

            var line = $"GD SDK {_result.Version}";
            if (_result.BaseVersion != null) line += $" (based on non-Metica {_result.BaseVersion})";
            if (_result.ChosenByHand) line += " — picked by hand";
            return line;
        }

        private string CountLine()
        {
            var differ = _result.Changes.Count;
            var line = $"{_result.Compared} files compared · {_result.Identical} identical · {differ} differ";
            if (!_result.ServicesCompared) line += " · Monetization/Scripts/Services not in this project";
            return line;
        }

        private string FilterHint()
        {
            switch (_filter)
            {
                case Filter.Metica: return "Files whose changed lines mention Metica — this tool's, or an earlier integration's.";
                case Filter.Tool: return "Files that are exactly what this tool's patches or templates produce.";
                default: return "Every changed, added and missing file. Assets, prefabs and scenes are not compared.";
            }
        }

        private VisualElement ReleasePicker()
        {
            var row = El("mi-choice");
            row.Add(Text(_result.Declared == null
                ? "Compare with"
                : $"Declares \"{_result.Declared}\" — compare with", "mi-choice__label"));

            var select = new Button().With("mi-select");
            select.Add(Text(_result.ChosenByHand ? _result.Version : "Pick a release"));
            select.Add(new MiIcon(MiIcon.Kind.ChevronDown, 2f, "mi-icon--12"));
            select.clicked += () =>
            {
                var menu = new GenericMenu();
                var stock = StockFiles.Packaged;
                foreach (var version in stock?.Versions ?? Enumerable.Empty<string>())
                {
                    var picked = version;
                    menu.AddItem(new GUIContent(version), picked == ModifiedFileCheck.ChosenVersion, () =>
                    {
                        ModifiedFileCheck.ChosenVersion = picked;
                        Rescan();
                    });
                }
                menu.DropDown(select.worldBound);
            };
            row.Add(select);
            return row;
        }

        private Button FilterButton(Filter filter, string label, int count)
        {
            var button = new Button(() =>
            {
                _filter = filter;
                _selected = null;
                Rebuild();
            }) { text = $"{label} ({count})" }.With("mi-btn", "mi-btn--small", "mi-cmp__filter");
            if (filter == _filter) button.AddToClassList("mi-cmp__filter--on");
            return button;
        }

        private VisualElement FileRow(SdkFileChange change)
        {
            var row = new Button(() =>
            {
                _selected = change.Path;
                Rebuild();
            }).With("mi-cmp__file");
            if (change.Path == _selected) row.AddToClassList("mi-cmp__file--selected");

            var top = El("mi-cmp__file-top");
            top.Add(Text(KindLabel(change.Kind), "mi-cmp__kind", "mi-cmp__kind--" + change.Kind.ToString().ToLowerInvariant()));
            top.Add(Text(change.Name, "mi-cmp__name"));
            row.Add(top);

            var folder = Path.GetDirectoryName(change.Path)?.Replace('\\', '/');
            row.Add(Text(string.IsNullOrEmpty(folder) ? "/" : folder, "mi-cmp__folder"));

            var meta = El("mi-cmp__file-meta");
            if (change.Kind != ChangeKind.Missing) meta.Add(Text($"+{change.AddedLines}", "mi-cmp__plus"));
            if (change.Kind != ChangeKind.Added) meta.Add(Text($"−{change.RemovedLines}", "mi-cmp__minus"));
            if (change.Metica) meta.Add(Text("Metica", "mi-cmp__tag"));
            if (change.ByTool) meta.Add(Text("Tool", "mi-cmp__tag", "mi-cmp__tag--tool"));
            row.Add(meta);

            // The tick sits beside the row, not in it, so ticking never changes the diff shown.
            var line = El("mi-cmp__file-line");
            var tick = new Button(() => ToggleTick(change.Path)).With("mi-cmp__check");
            _checks[change.Path] = tick;
            line.Add(tick);
            line.Add(row);
            return line;
        }

        private static string KindLabel(ChangeKind kind) =>
            kind == ChangeKind.Added ? "Added" : kind == ChangeKind.Missing ? "Missing" : "Changed";

        // ── Diff ───────────────────────────────────────────────────────────────

        private void BuildDiff(SdkFileChange change)
        {
            var head = El("mi-cmp__diff-head");
            var path = Text(change.ProjectPath, "mi-cmp__diff-path");
            path.selection.isSelectable = true;
            head.Add(path);

            var buttons = El("mi-row");
            var openProject = IconTextButton(MiIcon.Kind.File, "Open project file",
                () => OpenProjectFile(change), "mi-btn--secondary");
            openProject.SetEnabled(change.Kind != ChangeKind.Missing);
            buttons.Add(openProject);

            var openOriginal = IconTextButton(MiIcon.Kind.File, "Open original",
                () => OpenOriginal(change), "mi-btn--secondary");
            openOriginal.SetEnabled(change.Kind != ChangeKind.Added);
            buttons.Add(openOriginal);

            // An added file has no original to go back to — it can only go.
            buttons.Add(change.Kind == ChangeKind.Added
                ? IconTextButton(MiIcon.Kind.Trash, "Delete", () => DeleteAdded(new[] { change }), "mi-btn--secondary")
                : IconTextButton(MiIcon.Kind.Undo, "Replace with original",
                    () => ReplaceWithOriginal(new[] { change }), "mi-btn--secondary"));
            head.Add(buttons);
            _diff.Add(head);

            if (change.Diff == null) return;

            foreach (var hunk in LineDiff.Hunks(change.Diff))
            {
                var block = El("mi-cmp__hunk");
                block.Add(Text($"Original line {hunk.OldStart} · project line {hunk.NewStart}", "mi-cmp__hunk-head"));
                foreach (var line in hunk.Lines) block.Add(DiffLine(line));
                _diff.Add(block);
            }
        }

        private static VisualElement DiffLine(LineDiff.Line line)
        {
            var row = El("mi-cmp__line");
            if (line.Op == LineDiff.Op.Added) row.AddToClassList("mi-cmp__line--added");
            if (line.Op == LineDiff.Op.Removed) row.AddToClassList("mi-cmp__line--removed");

            row.Add(Number(line.OldNumber));
            row.Add(Number(line.NewNumber));

            var sign = line.Op == LineDiff.Op.Added ? "+" : line.Op == LineDiff.Op.Removed ? "−" : " ";
            var text = Text(sign + " " + line.Text.Replace("\t", "    "), "mi-cmp__code");
            text.enableRichText = false;
            MeticaIntegrationWindow.Mono(text);
            row.Add(text);
            return row;
        }

        private static Label Number(int number)
        {
            var label = Text(number > 0 ? number.ToString() : string.Empty, "mi-cmp__num");
            MeticaIntegrationWindow.Mono(label);
            return label;
        }

        private static void OpenProjectFile(SdkFileChange change)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(change.ProjectPath);
            if (asset != null) AssetDatabase.OpenAsset(asset);
            else InternalEditorUtility.OpenFileAtLineExternal(MeticaPaths.ToAbsolute(change.ProjectPath), 1);
        }

        /// <summary>
        /// Writes the original to the project's Temp folder — outside Assets, so it never
        /// compiles, and cleared when Unity closes — and opens it in the code editor.
        /// </summary>
        private void OpenOriginal(SdkFileChange change)
        {
            var text = StockFiles.Packaged?.Get(_result.Version, change.Path);
            if (text == null) return;

            var relative = change.Path.StartsWith(StockFiles.ServicesPrefix, StringComparison.Ordinal)
                ? "Monetization/" + change.Path.Substring(StockFiles.ServicesPrefix.Length)
                : "GDMonetization/" + change.Path;
            var target = MeticaPaths.ToAbsolute($"Temp/MeticaStock/{_result.Version}/{relative}");

            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            File.WriteAllText(target, text);
            InternalEditorUtility.OpenFileAtLineExternal(target, 1);
        }

        // ── Replace / delete ───────────────────────────────────────────────────

        /// <summary>
        /// Puts the original release's files back — over changed files, or in place of missing
        /// ones. Current files are copied to MeticaIntegrationBackups/Replaced/&lt;time&gt;/ first:
        /// always this state, unlike the patch backups, which keep only the pre-tool one.
        /// </summary>
        private void ReplaceWithOriginal(IReadOnlyList<SdkFileChange> changes)
        {
            var originals = changes
                .Select(change => (change, text: StockFiles.Packaged?.Get(_result.Version, change.Path)))
                .Where(item => item.text != null && item.change.Kind != ChangeKind.Added)
                .ToList();
            if (originals.Count == 0) return;

            var missing = originals.Count(item => !File.Exists(MeticaPaths.ToAbsolute(item.change.ProjectPath)));
            var message = $"Replace with the originals from GD SDK {_result.Version}:\n\n" +
                          PathList(originals.Select(item => item.change.ProjectPath));
            if (missing < originals.Count) message += "\n\nCurrent files are copied to MeticaIntegrationBackups/Replaced first.";
            if (missing > 0)
                message += "\n\nMissing files are added back. Unity gives them a new .meta — a scene or prefab " +
                           "that used the old one needs re-linking.";
            if (originals.Any(item => item.change.Metica))
                message += "\n\nTheir Metica changes go too — run the tool's step again to put them back.";

            if (!EditorUtility.DisplayDialog("Replace with original", message, "Replace", "Cancel")) return;

            var backupRoot = $"{MeticaPaths.BackupRoot}/Replaced/{DateTime.Now:yyyyMMdd-HHmmss}";
            var failed = new List<string>();
            foreach (var (change, original) in originals)
            {
                var absolute = MeticaPaths.ToAbsolute(change.ProjectPath);
                var text = original;
                try
                {
                    if (File.Exists(absolute))
                    {
                        // Keep the file's own line endings, so git shows only the real change.
                        if (File.ReadAllText(absolute).Contains("\r\n")) text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

                        var backup = $"{backupRoot}/{change.ProjectPath}";
                        Directory.CreateDirectory(Path.GetDirectoryName(backup) ?? backupRoot);
                        File.Copy(absolute, backup, true);
                        SourcePatcher.IgnoreBackupsInGit();
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? ".");
                    File.WriteAllText(absolute, text);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    failed.Add($"{change.ProjectPath}: {e.Message}");
                }
            }

            Debug.Log($"[Metica Integration] Replaced {originals.Count - failed.Count} file(s) with the GD SDK " +
                      $"{_result.Version} originals (previous copies: {backupRoot})");
            Finish("Replace with original", "Could not replace", failed);
        }

        /// <summary>
        /// Deletes files the original release doesn't have, with their .meta. Both are copied to
        /// MeticaIntegrationBackups/Deleted/&lt;time&gt;/ first; a file whose copy fails stays.
        /// </summary>
        private void DeleteAdded(IReadOnlyList<SdkFileChange> changes)
        {
            var added = changes
                .Where(change => change.Kind == ChangeKind.Added && File.Exists(MeticaPaths.ToAbsolute(change.ProjectPath)))
                .ToList();
            if (added.Count == 0) return;

            var message = "These will be deleted:\n\n" + PathList(added.Select(change => change.ProjectPath)) +
                          "\n\nCopies go to MeticaIntegrationBackups/Deleted first. Code that uses them stops compiling.";
            if (added.Any(change => change.Metica)) message += "\n\nSome mention Metica — they may be part of the integration.";

            if (!EditorUtility.DisplayDialog("Delete added files", message, "Delete", "Cancel")) return;

            var backupRoot = $"{MeticaPaths.BackupRoot}/Deleted/{DateTime.Now:yyyyMMdd-HHmmss}";
            var failed = new List<string>();
            var backedUp = new List<string>();
            foreach (var change in added)
            {
                var absolute = MeticaPaths.ToAbsolute(change.ProjectPath);
                var backup = $"{backupRoot}/{change.ProjectPath}";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup) ?? backupRoot);
                    File.Copy(absolute, backup, true);
                    if (File.Exists(absolute + ".meta")) File.Copy(absolute + ".meta", backup + ".meta", true);
                    SourcePatcher.IgnoreBackupsInGit();
                    backedUp.Add(change.ProjectPath);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    failed.Add($"{change.ProjectPath}: not backed up, so not deleted ({e.Message})");
                }
            }

            var notDeleted = new List<string>();
            if (backedUp.Count > 0) AssetDatabase.DeleteAssets(backedUp.ToArray(), notDeleted);
            failed.AddRange(notDeleted.Select(path => $"{path}: Unity could not delete it"));

            Debug.Log($"[Metica Integration] Deleted {backedUp.Count - notDeleted.Count} added file(s) (copies: {backupRoot})");
            Finish("Delete added files", "Could not delete", failed);
        }

        /// <summary>After a replace or delete: re-import, report what didn't work, re-scan.</summary>
        private void Finish(string title, string failedLead, List<string> failed)
        {
            AssetDatabase.Refresh();
            if (failed.Count > 0)
                EditorUtility.DisplayDialog(title, $"{failedLead}:\n\n{PathList(failed)}\n\nSee the Console.", "OK");

            _checked.Clear();
            Rescan();
        }

        /// <summary>One per line, cut short so a long selection still fits in a dialog.</summary>
        private static string PathList(IEnumerable<string> paths)
        {
            const int max = 15;
            var list = paths.ToList();
            var lines = list.Take(max).Select(path => "  • " + path);
            if (list.Count > max) lines = lines.Append($"  … and {list.Count - max} more");
            return string.Join("\n", lines);
        }
    }
}
