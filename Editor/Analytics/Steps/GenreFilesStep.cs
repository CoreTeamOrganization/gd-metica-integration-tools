using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The game's genre files, from its name and its Excel event schema. The tool only takes the
    /// two inputs and hands them to the genre creator, which writes the scripts and — once Unity
    /// has compiled them — the ScriptableObject. The step then checks both are there.
    /// </summary>
    public sealed class GenreFilesStep : MeticaStep
    {
        public const string StepTitle = "Create genre files";

        public override string Title => StepTitle;

        public override string Summary => "Enter the game name and choose the Excel event schema — the genre creator makes the files.";

        public override string Why =>
            "The genre creator reads the Excel sheet's events and writes three scripts to " +
            $"{MeticaPaths.GenresRoot}/<Game>/ — the analytics class, its interface and the event data — then, " +
            $"after Unity recompiles, <Game>Analytics.asset in {MeticaPaths.GenresResources}. The game name becomes " +
            "the class names, so it is one word: letters and digits, starting with a letter. App Id, Api Key and the " +
            "Adjust App Token are optional here; whatever is filled is written to the asset once it exists, and " +
            "Enable Metica Logs is turned on. They can be changed here afterwards too.";

        /// <summary>
        /// Only the leftover-asset case. "Create genre files" is a control instead, so it can stay
        /// disabled until the name and the Excel file are valid, as they are typed and picked.
        /// </summary>
        public override string ActionLabel
        {
            get
            {
                var game = GameName;
                if (ApiProblem != null || NameError(game) != null) return null;
                return ScriptsExist(game) && AnalyticsType(game) != null && !AssetExists(game)
                    ? $"Create {game}Analytics.asset"
                    : null;
            }
        }

        public override IEnumerable<string> TouchedPaths => new[] { MeticaPaths.GenresRoot };

        public override string ReviewHint => $"{MeticaPaths.GenresRoot}/<Game>/ scripts and the asset in Resources.";

        // ── Inputs, kept per project ───────────────────────────────────────────

        private static string Pref(string name) =>
            $"GameDistrict.MeticaIntegrationTools.Analytics.{name}.{Application.dataPath.GetHashCode():X8}";

        /// <summary>
        /// The game name typed here — or, when nothing is typed and the project already has one
        /// genre, that genre, so a project set up by hand is recognised.
        /// </summary>
        internal static string GameName
        {
            get
            {
                var typed = EditorPrefs.GetString(Pref("GameName"), "").Trim();
                if (typed.Length > 0) return typed;

                var existing = ExistingGenres();
                return existing.Count == 1 ? existing[0] : "";
            }
            set => EditorPrefs.SetString(Pref("GameName"), value ?? "");
        }

        private static string ExcelPath
        {
            get => EditorPrefs.GetString(Pref("ExcelPath"), "");
            set => EditorPrefs.SetString(Pref("ExcelPath"), value ?? "");
        }

        private static int Sheet
        {
            get => EditorPrefs.GetInt(Pref("Sheet"), 0);
            set => EditorPrefs.SetInt(Pref("Sheet"), value);
        }

        /// <summary>The asset's optional fields, as typed before the asset exists.</summary>
        private static readonly (string field, string label, bool secret)[] AssetFields =
        {
            ("appId", "App Id", false),
            ("apiKey", "Api Key", true),
            ("appToken", "App Token", false)
        };

        private static string Typed(string field) => EditorPrefs.GetString(Pref("Asset." + field), "");

        private static void SetTyped(string field, string value) => EditorPrefs.SetString(Pref("Asset." + field), value ?? "");

        /// <summary>Set by Create; cleared once the typed fields are written to the new asset.</summary>
        private static bool FieldsPending
        {
            get => EditorPrefs.GetBool(Pref("Asset.Pending"), false);
            set => EditorPrefs.SetBool(Pref("Asset.Pending"), value);
        }

        // ── Verify ─────────────────────────────────────────────────────────────

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var apiProblem = ApiProblem;
            if (apiProblem != null) return result.Problem(apiProblem + " See the previous step.").Seal();

            var game = GameName;
            if (NameError(game) != null || !ScriptsExist(game) && !File.Exists(ExcelPath))
            {
                result.Problem("Enter the game name and choose the Excel event schema.");
                return result.Seal();
            }

            if (!ScriptsExist(game))
            {
                if (!File.Exists(ExcelPath)) result.Problem("Choose the Excel event schema (.xlsx).");
                else result.Problem($"Genre files for {game} not created yet.");
                return result.Seal();
            }

            if (AnalyticsType(game) == null)
            {
                if (ScriptCompile.Pending(MeticaPaths.GenresRoot + "/" + game)) result.Wait();
                else result.Problem($"{game}Analytics hasn't compiled — check the Console.");
                return result.Seal();
            }

            if (!AssetExists(game))
            {
                result.Problem($"{game}Analytics.asset not created yet.");
                return result.Seal();
            }

            result.Note($"{AssetPath(game)}");

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath(game));
            if (asset != null)
            {
                // The genre creator makes the asset after the compile; the fields typed with the
                // name go in the first time the step sees it.
                // Enable Metica Logs is turned on once per asset — after that it is the developer's.
                if (FieldsPending || !EditorPrefs.GetBool(Pref("Asset.LogsOn." + game), false)) WriteTypedFields(asset);

                var serialized = new SerializedObject(asset);
                var empty = new[] { ("appId", "App Id"), ("apiKey", "Api Key") }
                    .Where(field => string.IsNullOrEmpty(serialized.FindProperty(field.Item1)?.stringValue))
                    .Select(field => field.Item2).ToList();
                if (empty.Count > 0)
                    result.Warning($"{string.Join(" and ", empty)} not set on {game}Analytics.asset — optional, fill below.");

                if (serialized.FindProperty("enableMeticaLogs")?.boolValue == true) result.Note("Metica logs on");
            }

            return result.Seal();
        }

        // ── Act ────────────────────────────────────────────────────────────────

        public override void Apply()
        {
            var game = GameName;
            var nameError = NameError(game);
            if (nameError != null)
            {
                MeticaIntegrationLog.Record(Title, nameError);
                return;
            }

            // Scripts there and compiled, but the genre creator's after-compile step missed the
            // asset (Unity was busy, or the editor restarted): make it the way it does.
            if (ScriptsExist(game) && AnalyticsType(game) != null && !AssetExists(game))
            {
                CreateAsset(game);
                WriteTypedFields(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath(game)));
                return;
            }

            if (!File.Exists(ExcelPath))
            {
                MeticaIntegrationLog.Record(Title, "Choose the Excel event schema first.");
                return;
            }

            try
            {
                var sheets = GenreCreatorPackage.SheetNames(ExcelPath);
                if (sheets.Count == 0)
                {
                    MeticaIntegrationLog.Record(Title, "No sheets found in the Excel file.");
                    return;
                }

                var sheet = Mathf.Clamp(Sheet, 0, sheets.Count - 1);
                var (events, count) = GenreCreatorPackage.Parse(ExcelPath, sheet);
                if (count == 0)
                {
                    MeticaIntegrationLog.Record(Title,
                        $"No events found in sheet '{sheets[sheet]}' — it needs a 'parameter' column header.");
                    return;
                }

                FieldsPending = true;
                GenreCreatorPackage.CreateFiles(game, events);
                MeticaIntegrationLog.Record(Title,
                    $"Created {game} genre files from '{sheets[sheet]}' ({count} events) — Unity is compiling them");
            }
            catch (Exception e)
            {
                MeticaIntegrationLog.Record(Title, $"Couldn't create the genre files: {e.Message}");
            }
        }

        private static void CreateAsset(string game)
        {
            var type = AnalyticsType(game);
            if (!AssetDatabase.IsValidFolder(MeticaPaths.GenresResources))
                AssetDatabase.CreateFolder(MeticaPaths.GenresRoot, "Resources");

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(type), AssetPath(game));
            AssetDatabase.SaveAssets();
            MeticaIntegrationLog.Record(StepTitle, $"Created {AssetPath(game)}");
        }

        /// <summary>
        /// Writes the typed App Id, Api Key and App Token (the filled ones) to the asset, and turns
        /// Enable Metica Logs on.
        /// </summary>
        private static void WriteTypedFields(ScriptableObject asset)
        {
            FieldsPending = false;
            if (asset == null) return;

            var serialized = new SerializedObject(asset);
            var written = new List<string>();
            foreach (var (field, label, _) in AssetFields)
            {
                var value = Typed(field).Trim();
                var property = serialized.FindProperty(field);
                if (value.Length == 0 || property == null) continue;
                property.stringValue = value;
                written.Add(label);
            }

            var logs = serialized.FindProperty("enableMeticaLogs");
            if (logs != null) logs.boolValue = true;
            EditorPrefs.SetBool(Pref("Asset.LogsOn." + asset.name.Substring(0, Math.Max(0, asset.name.Length - "Analytics".Length))), true);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            MeticaIntegrationLog.Record(StepTitle, written.Count == 0
                ? $"Turned Metica logs on in {asset.name}"
                : $"Set {string.Join(", ", written)} and turned Metica logs on in {asset.name}");
        }

        /// <summary>Edits one field of the existing asset straight away.</summary>
        private static void WriteField(string game, string field, string value)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath(game));
            if (asset == null) return;

            var serialized = new SerializedObject(asset);
            var property = serialized.FindProperty(field);
            if (property == null) return;

            property.stringValue = value?.Trim() ?? "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static string AssetValue(string game, string field)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath(game));
            return asset == null ? "" : new SerializedObject(asset).FindProperty(field)?.stringValue ?? "";
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (ApiProblem != null) yield break;

            var game = GameName;
            if (ScriptsExist(game) && AssetExists(game))
            {
                // Created: the same fields, now editing the asset itself.
                foreach (var (field, label, secret) in AssetFields)
                    yield return new StepText(label, AssetValue(game, field), value => WriteField(game, field, value), secret);

                yield return new StepButton($"Select {game}Analytics.asset",
                    () => SelectAndPing(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath(game))), icon: StepIcon.File);
                yield break;
            }

            yield return new StepText("Game name", EditorPrefs.GetString(Pref("GameName"), game),
                value => GameName = value, validate: NameError);

            var excel = ExcelPath;
            yield return new StepButton(File.Exists(excel) ? $"Excel: {Path.GetFileName(excel)}" : "Choose Excel file…",
                ChooseExcel, icon: StepIcon.File);

            if (File.Exists(excel))
            {
                List<string> sheets;
                try { sheets = GenreCreatorPackage.SheetNames(excel); }
                catch { sheets = new List<string>(); }

                // Most schemas are one sheet; only ask when there is a choice.
                if (sheets.Count > 1)
                    yield return new StepChoice("Sheet", sheets.ToArray(), Mathf.Clamp(Sheet, 0, sheets.Count - 1),
                        index => Sheet = index);
            }

            // Optional — written to the asset once it exists.
            foreach (var (field, label, secret) in AssetFields)
                yield return new StepText($"{label} (optional)", Typed(field), value => SetTyped(field, value), secret);

            // Enabled only while the typed name is valid and an Excel file is chosen.
            yield return new StepButton("Create genre files", Apply,
                enabledWhen: () => NameError(GameName) == null && File.Exists(ExcelPath), primary: true);
        }

        private static void ChooseExcel()
        {
            var start = File.Exists(ExcelPath) ? Path.GetDirectoryName(ExcelPath) : "";
            var picked = EditorUtility.OpenFilePanel("Excel event schema", start, "xlsx");
            if (string.IsNullOrEmpty(picked)) return;

            ExcelPath = picked;
            Sheet = 0;
        }

        // ── Reading the project ────────────────────────────────────────────────

        private static string ApiProblem => GenreCreatorPackage.ApiProblem;

        /// <summary>
        /// One word for class and file names: letters and digits, starting with a letter — the
        /// genre creator's own rule.
        /// </summary>
        internal static string NameError(string game)
        {
            if (string.IsNullOrEmpty(game)) return "Enter the game name.";
            if (!char.IsLetter(game[0])) return "The game name must start with a letter.";
            if (!Regex.IsMatch(game, "^[A-Za-z][A-Za-z0-9]*$"))
                return "The game name can only have letters and digits — no spaces or symbols.";
            return null;
        }

        internal static string AssetPath(string game) => $"{MeticaPaths.GenresResources}/{game}Analytics.asset";

        private static bool ScriptsExist(string game) =>
            new[] { $"{game}Analytics.cs", $"I{game}Analytics.cs", $"{game}Data.cs" }
                .All(file => MeticaPaths.FileExists($"{MeticaPaths.GenresRoot}/{game}/{file}"));

        private static bool AssetExists(string game) => MeticaPaths.FileExists(AssetPath(game));

        /// <summary>The compiled {Game}Analytics class (generated into GameDistrict.MeticaAnalytics).</summary>
        private static Type AnalyticsType(string game) =>
            TemplateWriter.FindType($"GameDistrict.MeticaAnalytics.{game}Analytics");

        /// <summary>Genres already in the project, by their assets in Resources.</summary>
        private static List<string> ExistingGenres()
        {
            var folder = MeticaPaths.ToAbsolute(MeticaPaths.GenresResources);
            if (!Directory.Exists(folder)) return new List<string>();

            return Directory.GetFiles(folder, "*Analytics.asset")
                .Select(file => Path.GetFileNameWithoutExtension(file))
                .Select(name => name.Substring(0, name.Length - "Analytics".Length))
                .Where(name => name.Length > 0)
                .ToList();
        }
    }
}
