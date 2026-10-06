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
    /// changed, added and missing files, each with its diff. Read-only — nothing in the
    /// project is changed from here. See <see cref="SdkCompare"/> for what is compared.
    /// </summary>
    public sealed class SdkCompareWindow : EditorWindow
    {
        private enum Filter { Metica, Tool, All }

        [SerializeField] private Filter _filter = Filter.Metica;
        [SerializeField] private string _selected;

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

            var shown = Shown().ToList();
            if (shown.Count == 0)
            {
                _list.Add(Text(_result.Problem != null ? string.Empty
                    : _filter == Filter.All ? "No differences — every file matches the original."
                    : "Nothing in this filter. Try All.", "mi-cmp__empty"));
                return;
            }

            if (_selected == null || shown.All(c => c.Path != _selected)) _selected = shown[0].Path;

            foreach (var change in shown) _list.Add(FileRow(change));
            BuildDiff(shown.First(c => c.Path == _selected));
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
            return row;
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
    }
}
