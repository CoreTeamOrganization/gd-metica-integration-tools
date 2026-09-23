using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    public class GenreCreatorWindow : EditorWindow
    {
        // ── UI State ──────────────────────────────────────────────────────────
        private string               _genreName  = "";
        private List<GenreEventDef>  _events     = new();
        private Vector2              _scrollPos;
        private string               _statusMsg  = "";
        private MessageType          _statusType = MessageType.None;

        // ── Import State ──────────────────────────────────────────────────────
        private enum CreatorMode { Manual, ImportExcel }
        private CreatorMode  _mode          = CreatorMode.Manual;
        private string       _excelPath     = "";
        private List<string> _sheetNames    = new();
        private int          _selectedSheet = 0;

        // ── Menu ──────────────────────────────────────────────────────────────
        [MenuItem("GameDistrict/Metica/Genre Creator...", false, 20)]
        public static void ShowWindow()
        {
            var w = GetWindow<GenreCreatorWindow>("Genre Creator");
            w.minSize = new Vector2(460, 540);
            w.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void OnEnable()
        {
            if (_events.Count == 0)
                ResetDefaultEvents();
        }

        private void ResetDefaultEvents()
        {
            _events = new List<GenreEventDef>
            {
                new("LevelStarted", new List<GenreFieldDef>
                {
                    new("Level",     GenreFieldType.Int),
                    new("LevelType", GenreFieldType.String),
                }),
                new("LevelFinished", new List<GenreFieldDef>
                {
                    new("Level",       GenreFieldType.Int),
                    new("LevelType",   GenreFieldType.String),
                    new("LevelStatus", GenreFieldType.String),
                    new("PlayTime",    GenreFieldType.Float),
                }),
            };
        }

        // ── GUI ───────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            GUILayout.Label("New Analytics Genre", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            DrawModeTabs();
            EditorGUILayout.Space(6);

            if (_mode == CreatorMode.ImportExcel)
            {
                DrawImportSection();
                EditorGUILayout.Space(8);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Fill in the genre name and define its events, then click Create Genre Files.\n" +
                    "After Unity recompiles, the assets are created automatically at Assets/MeticaGenres/Resources.",
                    MessageType.None);
                EditorGUILayout.Space(6);
            }

            DrawGenreNameField();
            EditorGUILayout.Space(8);
            DrawEventsSection();
            EditorGUILayout.Space(8);
            DrawStatus();
            DrawCreateButton();
        }

        private void DrawModeTabs()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_mode == CreatorMode.Manual,      "Manual",            EditorStyles.miniButtonLeft))
                _mode = CreatorMode.Manual;
            if (GUILayout.Toggle(_mode == CreatorMode.ImportExcel, "Import from Excel", EditorStyles.miniButtonRight))
                _mode = CreatorMode.ImportExcel;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawImportSection()
        {
            EditorGUILayout.HelpBox(
                "Select an Excel event schema (.xlsx) and choose the sheet to import.\n" +
                "Review the preview below, then click Create Genre Files.",
                MessageType.None);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Excel File", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _excelPath = EditorGUILayout.TextField(_excelPath);
            if (GUILayout.Button("Browse...", GUILayout.Width(72)))
            {
                string p = EditorUtility.OpenFilePanel("Select Excel Event Schema", "", "xlsx");
                if (!string.IsNullOrEmpty(p))
                {
                    _excelPath     = p;
                    _selectedSheet = 0;
                    LoadSheetNames();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_sheetNames.Count > 0)
            {
                _selectedSheet = EditorGUILayout.Popup("Sheet", _selectedSheet, _sheetNames.ToArray());
                EditorGUILayout.Space(4);
                if (GUILayout.Button("Import & Preview", GUILayout.Height(28)))
                    ImportFromExcel();
            }
        }

        private void DrawGenreNameField()
        {
            EditorGUILayout.LabelField("Genre Name", EditorStyles.boldLabel);
            _genreName = EditorGUILayout.TextField("Name", _genreName).Trim();

            string error = IdentifierValidator.GetError(_genreName);
            if (error != null)
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void DrawEventsSection()
        {
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            int removeAt = -1;
            for (int i = 0; i < _events.Count; i++)
            {
                if (!DrawEvent(i))
                    removeAt = i;
            }
            if (removeAt >= 0)
                _events.RemoveAt(removeAt);

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("+ Add Event"))
                _events.Add(new GenreEventDef($"NewEvent{_events.Count + 1}", new List<GenreFieldDef>()));
        }

        private bool DrawEvent(int i)
        {
            var  evt  = _events[i];
            bool keep = true;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Event", GUILayout.Width(42));
            evt.Name = EditorGUILayout.TextField(evt.Name);
            if (GUILayout.Button("✕", GUILayout.Width(22)))
                keep = false;
            EditorGUILayout.EndHorizontal();

            if (!keep)
            {
                EditorGUILayout.EndVertical();
                return false;
            }

            string evtError = IdentifierValidator.GetIdentifierError(evt.Name, "Event name");
            if (evtError == null && _events.Count(e => e.Name == evt.Name) > 1)
                evtError = $"Duplicate event name '{evt.Name}'.";
            if (evtError != null)
                EditorGUILayout.HelpBox(evtError, MessageType.Error);

            EditorGUI.indentLevel++;

            int removeField = -1;
            for (int j = 0; j < evt.Fields.Count; j++)
            {
                EditorGUILayout.BeginHorizontal();
                var field = evt.Fields[j];
                string displayed = field.PayloadKey ?? field.Name;
                string edited    = EditorGUILayout.TextField(displayed, GUILayout.MinWidth(120));
                if (edited != displayed)
                {
                    field.PayloadKey = edited;
                    field.Name       = GenreCodeGenerator.ToPascal(edited);
                }
                field.Type = (GenreFieldType)EditorGUILayout.EnumPopup(field.Type, GUILayout.Width(70));
                if (GUILayout.Button("−", GUILayout.Width(20)))
                    removeField = j;
                EditorGUILayout.EndHorizontal();

                string fieldError = IdentifierValidator.GetIdentifierError(field.Name, "Field name");
                if (fieldError == null && evt.Fields.Count(f => f.Name == field.Name) > 1)
                    fieldError = $"Duplicate field '{field.Name}'.";
                if (fieldError != null)
                    EditorGUILayout.HelpBox(fieldError, MessageType.Error);
            }
            if (removeField >= 0)
                evt.Fields.RemoveAt(removeField);

            EditorGUI.indentLevel--;

            if (GUILayout.Button("+ Add Field", GUILayout.Height(18)))
                evt.Fields.Add(new GenreFieldDef($"Field{evt.Fields.Count + 1}", GenreFieldType.String));

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
            return true;
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                EditorGUILayout.HelpBox(_statusMsg, _statusType);
                EditorGUILayout.Space(4);
            }
        }

        private void DrawCreateButton()
        {
            GUI.enabled = !HasAnyValidationError();
            if (GUILayout.Button("Create Genre Files", GUILayout.Height(34)))
                CreateGenreFiles();
            GUI.enabled = true;
        }

        private bool HasAnyValidationError()
        {
            if (IdentifierValidator.GetError(_genreName) != null) return true;

            var seenEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var evt in _events)
            {
                if (IdentifierValidator.GetIdentifierError(evt.Name, "Event name") != null) return true;
                if (!seenEvents.Add(evt.Name)) return true;

                var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in evt.Fields)
                {
                    if (IdentifierValidator.GetIdentifierError(f.Name, "Field name") != null) return true;
                    if (!seenFields.Add(f.Name)) return true;
                }
            }
            return false;
        }

        // ── Import Orchestration ──────────────────────────────────────────────
        private void LoadSheetNames()
        {
            try
            {
                _sheetNames = GenreExcelParser.GetSheetNames(_excelPath);
                if (_sheetNames.Count == 0)
                    SetStatus("No sheets found in the selected file.", MessageType.Warning);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not read file: {ex.Message}", MessageType.Error);
            }
        }

        private void ImportFromExcel()
        {
            if (string.IsNullOrEmpty(_excelPath) || _sheetNames.Count == 0) return;

            try
            {
                var (genreName, events) = GenreExcelParser.Parse(_excelPath, _selectedSheet);
                if (events.Count == 0)
                {
                    SetStatus("No events found. Check that the sheet has a 'parameter' column header.", MessageType.Warning);
                    return;
                }

                _events = events;
                if (!string.IsNullOrEmpty(genreName) && string.IsNullOrEmpty(_genreName))
                    _genreName = genreName;

                int total = events.Sum(e => e.Fields.Count);
                SetStatus(
                    $"Imported {events.Count} events ({total} fields) from '{_sheetNames[_selectedSheet]}'.\n" +
                    "Review below and set the genre name before generating.",
                    MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Import error: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
        }

        // ── Create ────────────────────────────────────────────────────────────
        private void CreateGenreFiles()
        {
            string error = IdentifierValidator.GetError(_genreName);
            if (error != null) { SetStatus(error, MessageType.Error); return; }

            try
            {
                GenreAssetCreator.CreateFiles(_genreName, _events);
                SetStatus(
                    $"Files created for '{_genreName}'.\nUnity is recompiling — assets will be created automatically.",
                    MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
        }

        private void SetStatus(string msg, MessageType type)
        {
            _statusMsg  = msg;
            _statusType = type;
            Repaint();
        }
    }
}