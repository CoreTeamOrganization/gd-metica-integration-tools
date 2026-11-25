using System;
using System.Collections.Generic;
using System.Linq;
using O = GameDistrict.MeticaIntegrationTools.SourcePatcher.Outcome;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>Which keys a project's remote switch reads.</summary>
    internal enum RemoteSwitchForm
    {
        /// <summary>No PersistRemoteToggles at all: Metica's remote switch was never in this project.</summary>
        None,

        /// <summary>
        /// The old code: it reads only the "UseMetica" bool. GD SDK 6.2.1+'s own bool preference
        /// (this tool's first patch too), this tool's lines from before "MainAdNetwork", or a
        /// game's own line setting that bool preference from a parameter.
        /// </summary>
        Old,

        /// <summary>"MainAdNetwork" only.</summary>
        New,

        /// <summary>"MainAdNetwork", and "UseMetica" when it isn't there.</summary>
        Both
    }

    /// <summary>Where in Firebase Remote Config the remote switch's value is.</summary>
    internal enum RemoteSwitchSource
    {
        /// <summary>Firebase parameters of their own: "MainAdNetwork" (string), "UseMetica" (bool).</summary>
        Parameter,

        /// <summary>Fields of the "AdsSettings" JSON parameter, as the GD SDK's other remote settings.</summary>
        Json
    }

    /// <summary>
    /// The remote switch in the GD SDK's RemoteConfigManager: which ad network remote config asks
    /// for, saved to MonetizationPreferences.MainAdNetwork after every fetch.
    ///
    /// <para>The value is "MainAdNetwork": one of the MainAdNetwork enum's names, matched
    /// ignoring case and outer spaces, saved by the enum's own spelling — so nothing but
    /// "Unassigned", "MAX" or "Metica" ever reaches the preference or the GameData event.
    /// "Unassigned" is saved like the others: it takes the device back out of the test. A value
    /// that matches none is refused, with an error log. Without the value nothing is saved:
    /// users outside the test keep what they have.</para>
    ///
    /// <para><b>Where it is</b> (<see cref="RemoteSwitchSource"/>), always asked: a field of the
    /// AdsSettings JSON — read into a fresh object on every fetch, since JsonUtility cannot read
    /// an enum by name and m_Configuration keeps the last fetch's value when a later one lacks
    /// the key — or a Firebase parameter of its own, which counts only when it came from the
    /// fetch (RemoteValue.Source), not from an in-app default.</para>
    ///
    /// <para><b>Which keys</b> (<see cref="RemoteSwitchForm"/>). A project where the switch was
    /// never integrated reads "MainAdNetwork" only, with no question. A project that has the old
    /// code — it read the "UseMetica" bool — is asked, never decided for: <i>Keep both</i> adds one
    /// line and a small method, EarlierUseMetica, so "UseMetica" still counts when
    /// "MainAdNetwork" isn't there (a game already live on it keeps working); <i>Implement
    /// new</i> reads "MainAdNetwork" only.</para>
    ///
    /// <para>Once in place, either can be converted: Keep both ↔ Implement new, and Parameter ↔
    /// JSON. Everything goes through <see cref="SourcePatcher"/>, so it runs on disk or in memory
    /// alike.</para>
    /// </summary>
    internal static class RemoteSwitchPatch
    {
        // ── The options, as the step shows them ────────────────────────────────

        public const string KeepBothLabel = "Keep both";
        public const string NewLabel = "Implement new";

        public const string KeepBothSummary =
            "Keep both: reads MainAdNetwork, and uses the old UseMetica when it's missing. For a game already live on UseMetica.";

        public const string NewSummary =
            "Implement new: reads only MainAdNetwork. Switch Firebase to MainAdNetwork, and keep UseMetica there while older builds are live.";

        public const string ParameterLabel = "Parameter";
        public const string JsonLabel = "JSON (AdsSettings)";

        public const string ParameterSummary =
            "Parameter: MainAdNetwork is a Firebase parameter of its own (string); with Keep both, UseMetica too (bool).";

        public const string JsonSummary =
            "JSON: MainAdNetwork is a field in the AdsSettings JSON, e.g. {\"MainAdNetwork\":\"Metica\"}; with Keep both, UseMetica too.";

        public static string Label(RemoteSwitchForm form) => form == RemoteSwitchForm.Both ? KeepBothLabel : NewLabel;

        public static string Label(RemoteSwitchSource source) => source == RemoteSwitchSource.Parameter ? ParameterLabel : JsonLabel;

        // ── The two dropdowns: the patch step's and the Remote switch window's ──

        /// <summary>
        /// "Remote switch" — Keep both or Implement new — once there is UseMetica code (the old
        /// code, or the tool's switch); a project where the switch was never integrated reads
        /// MainAdNetwork only, with no question. "Remote value" — Parameter or JSON — always.
        /// Before the switch is written they start on "Pick one…"; once it is in place they show
        /// what it is, and picking the other converts it. Null picks mean "as the code has it".
        /// </summary>
        internal static IEnumerable<StepChoice> Choices(RemoteSwitchForm? pickedForm, RemoteSwitchSource? pickedSource,
            Action<RemoteSwitchForm?> pickForm, Action<RemoteSwitchSource?> pickSource)
        {
            const string pickOne = "Pick one…";
            var form = Form();
            var source = Source();
            const string formCaption = KeepBothSummary + "\n" + NewSummary;
            const string sourceCaption = ParameterSummary + "\n" + JsonSummary;

            if (form == RemoteSwitchForm.Old)
                yield return new StepChoice("Remote switch",
                    new[] { pickOne, KeepBothLabel, NewLabel },
                    pickedForm == RemoteSwitchForm.Both ? 1 : pickedForm == RemoteSwitchForm.New ? 2 : 0,
                    index => pickForm(index == 1 ? RemoteSwitchForm.Both : index == 2 ? RemoteSwitchForm.New : (RemoteSwitchForm?)null),
                    formCaption);
            else if (form != RemoteSwitchForm.None)
                yield return new StepChoice("Remote switch",
                    new[] { KeepBothLabel, NewLabel },
                    (pickedForm ?? form) == RemoteSwitchForm.Both ? 0 : 1,
                    index => pickForm(index == 0 ? RemoteSwitchForm.Both : RemoteSwitchForm.New),
                    formCaption);

            if (source == null)
                yield return new StepChoice("Remote value",
                    new[] { pickOne, ParameterLabel, JsonLabel },
                    pickedSource == RemoteSwitchSource.Parameter ? 1 : pickedSource == RemoteSwitchSource.Json ? 2 : 0,
                    index => pickSource(index == 1 ? RemoteSwitchSource.Parameter : index == 2 ? RemoteSwitchSource.Json : (RemoteSwitchSource?)null),
                    sourceCaption);
            else
                yield return new StepChoice("Remote value",
                    new[] { ParameterLabel, JsonLabel },
                    (pickedSource ?? source) == RemoteSwitchSource.Parameter ? 0 : 1,
                    index => pickSource(index == 0 ? RemoteSwitchSource.Parameter : RemoteSwitchSource.Json),
                    sourceCaption);
        }

        /// <summary>
        /// The one line about the remote switch the developer has to act on: what to pick, or the
        /// conversion the dropdowns ask for, ending with <paramref name="action"/>. Null when there is nothing.
        /// </summary>
        internal static string Todo(RemoteSwitchForm? pickedForm, RemoteSwitchSource? pickedSource, string action)
        {
            const string where = "where the remote value is (" + ParameterLabel + " or " + JsonLabel + ")";
            const string which = KeepBothLabel + " or " + NewLabel;
            var form = Form();
            var source = Source();

            switch (form)
            {
                case RemoteSwitchForm.None:
                    return pickedSource == null ? $"Pick {where} in Firebase below, then {action}." : null;

                case RemoteSwitchForm.Old:
                    var picks = new[] { pickedForm == null ? which : null, pickedSource == null ? where : null }
                        .Where(pick => pick != null).ToList();
                    return picks.Count == 0 ? null
                        : $"Old remote switch code found — it reads only UseMetica. Pick {string.Join(", and ", picks)} below, then {action}.";

                default:
                    var changes = new List<string>();
                    if (pickedForm != null && pickedForm != form)
                        changes.Add($"{Label(form)} → {Label(pickedForm.Value)}");
                    if (pickedSource != null && source != null && pickedSource != source)
                        changes.Add($"{Label(source.Value)} → {Label(pickedSource.Value)}");
                    return changes.Count == 0 ? null
                        : $"Convert the remote switch: {string.Join(", ", changes)}. {action} to convert, or set the dropdown back.";
            }
        }

        /// <summary>True when what has to be asked is picked, so Apply would write the switch.</summary>
        internal static bool Picked(RemoteSwitchForm? pickedForm, RemoteSwitchSource? pickedSource)
        {
            switch (Form())
            {
                case RemoteSwitchForm.None: return pickedSource != null;
                case RemoteSwitchForm.Old: return pickedForm != null && pickedSource != null;
                default: return true;
            }
        }

        /// <summary>What the project's switch is now, in a few words.</summary>
        internal static string Describe()
        {
            var form = Form();
            var source = Source();
            switch (form)
            {
                case RemoteSwitchForm.None: return "Not in place yet.";
                case RemoteSwitchForm.Old: return "Old code — it reads only UseMetica.";
                default:
                    return (form == RemoteSwitchForm.Both ? "MainAdNetwork, and the old UseMetica when it's missing" : "MainAdNetwork only") +
                           (source != null ? $" — from the {Label(source.Value)}." : ".");
            }
        }

        // ── What is in the file ────────────────────────────────────────────────

        private const string Hook = "PersistRemoteToggles;";
        private const string Method = "static void PersistRemoteToggles";

        /// <summary>Everything new goes in before this method of RemoteConfigManager, in order.</summary>
        private const string Anchor = "public static void AddOrUpdateValue";

        /// <summary>RemoteConfiguration's bool, which the JSON EarlierUseMetica reads.</summary>
        private const string RemoteFlag = "public bool UseMetica";

        /// <summary>
        /// JSON only: RemoteConfiguration's MainAdNetwork, so DefaultRemoteSettings (the in-app
        /// AdsSettings) and Firebase → Remote → ClipboardJson carry the key. Left empty: the reader
        /// takes an empty value as "no MainAdNetwork", so the in-app default never changes a device.
        /// </summary>
        private const string JsonField = "public string MainAdNetwork;";

        /// <summary>The field's tooltip, which also marks it as the tool's.</summary>
        private const string JsonFieldTooltip = "[Tooltip(\"MainAdNetwork: set it in Firebase's AdsSettings";

        private const string JsonFieldLines =
            "        " + JsonFieldTooltip + " (Unassigned, MAX or Metica), not here. Leave it empty: a value here is saved on every fetch while Firebase has no AdsSettings.\")]\n" +
            "        " + JsonField;

        /// <summary>PersistRemoteToggles' one line, which hands over to PersistMainAdNetwork.</summary>
        internal static string CallStatement(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "PersistMainAdNetwork();"
                : "PersistMainAdNetwork(GetRemoteValue<string>(REMOTE_KEY).Value);";

        private static string CallComment(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "// Metica Integration Tools: save the main ad network the fetched MainAdNetwork parameter asks for (see PersistMainAdNetwork)."
                : "// Metica Integration Tools: save the main ad network the fetched AdsSettings asks for (see PersistMainAdNetwork).";

        internal static string ReaderDeclaration(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "static void PersistMainAdNetwork()"
                : "static void PersistMainAdNetwork(string adsSettings)";

        /// <summary>The line of PersistMainAdNetwork that reads the value; Keep both's line goes after it.</summary>
        private static string ReadLine(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "var remote = parameter.Source == FetchSource.RemoteValue ? parameter.Value : null;"
                : "JsonUtility.FromJson<MainAdNetworkRemote>(adsSettings)?.MainAdNetwork;";

        private static string OldKeyCall(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "remote = EarlierUseMetica();"
                : "remote = EarlierUseMetica(adsSettings);";

        private static string OldKeyDeclaration(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "static string EarlierUseMetica()"
                : "static string EarlierUseMetica(string adsSettings)";

        // PersistRemoteToggles' own lines in the old forms, first line and last: a line setting
        // the UseMetica bool preference (GD SDK 6.2.1+'s own and this tool's first patch, from
        // the JSON; or a game's own, from a parameter), then this tool's lines from when only the
        // UseMetica bool was read from remote config, without and with their logs.
        private const string OldBoolPreference = "MonetizationPreferences.UseMetica.Set(";

        private static readonly string[] OldFirstLine =
        {
            OldBoolPreference,
            "Metica Integration Tools: MainAdNetwork, only when the fetched AdsSettings has UseMetica",
            "var adsSettings = GetRemoteValue<string>(REMOTE_KEY).Value;"
        };

        private static readonly string[] OldLastLine =
        {
            OldBoolPreference,
            "MonetizationPreferences.MainAdNetwork.Set(",
            "Remote Switch saved: "
        };

        // ── What gets written ──────────────────────────────────────────────────

        /// <summary>PersistRemoteToggles' own two lines: a comment and the call. Each line carries <paramref name="indent"/>.</summary>
        internal static string CallLines(string indent, RemoteSwitchSource source) =>
            $"{indent}{CallComment(source)}\n" +
            $"{indent}{CallStatement(source)}";

        /// <summary>
        /// PersistMainAdNetwork (and, for the JSON, the class it reads the JSON into), as members
        /// of RemoteConfigManager: read, check, save. No try around the JSON read — a broken
        /// AdsSettings makes the SDK's own CreateAndUpdateConfig throw first, on the same text,
        /// so this is never reached with one.
        /// </summary>
        internal static string ReaderBlock(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter ? ParameterReader : JsonReader;

        private const string Validate =
            "            var names = System.Enum.GetNames(typeof(MainAdNetwork));\n" +
            "            var mainAdNetwork = System.Array.Find(names, name => string.Equals(name, remote.Trim(), System.StringComparison.OrdinalIgnoreCase));\n" +
            "            if (mainAdNetwork == null)\n" +
            "            {\n" +
            "                Message.LogError(Tag.SDK, \"Remote MainAdNetwork \\\"\" + remote + \"\\\" is not one of \" + string.Join(\", \", names) + \", MainAdNetwork stays \" + stored);\n" +
            "                return;\n" +
            "            }\n" +
            "\n" +
            "            MonetizationPreferences.MainAdNetwork.Set(mainAdNetwork);\n" +
            "            Message.Log(Tag.SDK, \"Remote Switch saved: \" + mainAdNetwork + \" as pref\");\n" +
            "        }\n";

        private const string Stored =
            "            var stored = MonetizationPreferences.MainAdNetwork.Get();\n" +
            "            if (string.IsNullOrEmpty(stored)) stored = MainAdNetwork.Unassigned.ToString();\n" +
            "\n";

        private const string JsonReader =
            "        // Metica Integration Tools: the main ad network, from remote config.\n" +
            "        //\n" +
            "        // AdsSettings carries \"MainAdNetwork\": \"Unassigned\", \"MAX\" or \"Metica\" (any casing). It is\n" +
            "        // saved here and read at the next ads init. \"Unassigned\" takes the device back out of the\n" +
            "        // test. A value that is none of the three is refused. Without the key nothing changes, so\n" +
            "        // users outside the test keep what they have.\n" +
            "        [System.Serializable]\n" +
            "        sealed class MainAdNetworkRemote\n" +
            "        {\n" +
            "            public string MainAdNetwork = null;\n" +
            "        }\n" +
            "\n" +
            "        static void PersistMainAdNetwork(string adsSettings)\n" +
            "        {\n" +
            Stored +
            "            var remote = string.IsNullOrEmpty(adsSettings) ? null : JsonUtility.FromJson<MainAdNetworkRemote>(adsSettings)?.MainAdNetwork;\n" +
            "            if (string.IsNullOrWhiteSpace(remote))\n" +
            "            {\n" +
            "                Message.Log(Tag.SDK, \"Remote AdsSettings has no MainAdNetwork, MainAdNetwork stays \" + stored);\n" +
            "                return;\n" +
            "            }\n" +
            "\n" +
            Validate;

        private const string ParameterReader =
            "        // Metica Integration Tools: the main ad network, from remote config.\n" +
            "        //\n" +
            "        // The Firebase parameter \"MainAdNetwork\" (a string) carries \"Unassigned\", \"MAX\" or \"Metica\"\n" +
            "        // (any casing). It is saved here and read at the next ads init. \"Unassigned\" takes the device\n" +
            "        // back out of the test. A value that is none of the three is refused. Without the parameter\n" +
            "        // (an in-app default doesn't count) nothing changes, so users outside the test keep what they have.\n" +
            "        static void PersistMainAdNetwork()\n" +
            "        {\n" +
            Stored +
            "            var parameter = GetRemoteValue<string>(\"MainAdNetwork\");\n" +
            "            var remote = parameter.Source == FetchSource.RemoteValue ? parameter.Value : null;\n" +
            "            if (string.IsNullOrWhiteSpace(remote))\n" +
            "            {\n" +
            "                Message.Log(Tag.SDK, \"Remote config has no MainAdNetwork parameter, MainAdNetwork stays \" + stored);\n" +
            "                return;\n" +
            "            }\n" +
            "\n" +
            Validate;

        /// <summary>Keep both: the line that goes right after PersistMainAdNetwork reads the value.</summary>
        private static string OldKeyLine(RemoteSwitchSource source) =>
            "            if (string.IsNullOrWhiteSpace(remote)) " + OldKeyCall(source) +
            (source == RemoteSwitchSource.Parameter
                ? " // the old parameter, when MainAdNetwork isn't there"
                : " // the old key, when MainAdNetwork isn't there");

        /// <summary>
        /// Keep both: the old key, read as it always was. JSON: RemoteConfiguration's bool, and
        /// only when the fetched text has the key, since the bool cannot tell false from absent.
        /// Parameter: the "UseMetica" parameter, only when it came from the fetch.
        /// </summary>
        private static string OldKeyBlock(RemoteSwitchSource source) =>
            source == RemoteSwitchSource.Parameter
                ? "        // Metica Integration Tools: the old remote parameter, \"UseMetica\" (true / false), for a game\n" +
                  "        // whose Firebase still has it. Read only when there is no MainAdNetwork parameter. Gives\n" +
                  "        // \"Metica\" or \"MAX\"; null when the fetched config doesn't have this parameter either.\n" +
                  "        static string EarlierUseMetica()\n" +
                  "        {\n" +
                  "            var parameter = GetRemoteValue<bool>(\"UseMetica\");\n" +
                  "            if (parameter.Source != FetchSource.RemoteValue) return null;\n" +
                  "\n" +
                  "            var mainAdNetwork = (parameter.Value ? MainAdNetwork.Metica : MainAdNetwork.MAX).ToString();\n" +
                  "            Message.Log(Tag.SDK, \"Remote config has the old UseMetica parameter, not MainAdNetwork: \" + mainAdNetwork);\n" +
                  "            return mainAdNetwork;\n" +
                  "        }\n"
                : "        // Metica Integration Tools: the old remote key, \"UseMetica\": true / false, for a game whose\n" +
                  "        // Firebase still has it. Read only when AdsSettings has no MainAdNetwork. Gives \"Metica\" or\n" +
                  "        // \"MAX\"; null when the fetched AdsSettings doesn't have this key either.\n" +
                  "        static string EarlierUseMetica(string adsSettings)\n" +
                  "        {\n" +
                  "            if (m_Configuration == null || string.IsNullOrEmpty(adsSettings) || !adsSettings.Contains(\"\\\"UseMetica\\\"\")) return null;\n" +
                  "\n" +
                  "            var mainAdNetwork = (m_Configuration.UseMetica ? MainAdNetwork.Metica : MainAdNetwork.MAX).ToString();\n" +
                  "            Message.Log(Tag.SDK, \"Remote AdsSettings has the old UseMetica key, not MainAdNetwork: \" + mainAdNetwork);\n" +
                  "            return mainAdNetwork;\n" +
                  "        }\n";

        // ── Reading the project ────────────────────────────────────────────────

        private static string Path => MeticaPaths.RemoteConfigManager;

        /// <summary>True when the project has a RemoteConfigManager to put the switch in.</summary>
        public static bool Present => SourcePatcher.Exists(Path);

        /// <summary>Which keys the project's remote switch reads now.</summary>
        public static RemoteSwitchForm Form()
        {
            if (!SourcePatcher.Exists(Path) || !SourcePatcher.Contains(Path, Method)) return RemoteSwitchForm.None;
            if (Source() == null) return RemoteSwitchForm.Old;
            return HasOldKey() ? RemoteSwitchForm.Both : RemoteSwitchForm.New;
        }

        /// <summary>Where the project's remote switch reads its value now; null without the tool's switch.</summary>
        public static RemoteSwitchSource? Source()
        {
            if (!SourcePatcher.Exists(Path)) return null;
            if (SourcePatcher.Contains(Path, CallStatement(RemoteSwitchSource.Json))) return RemoteSwitchSource.Json;
            if (SourcePatcher.Contains(Path, CallStatement(RemoteSwitchSource.Parameter))) return RemoteSwitchSource.Parameter;
            return null;
        }

        private static bool HasOldKey() =>
            new[] { RemoteSwitchSource.Json, RemoteSwitchSource.Parameter }.Any(source =>
                SourcePatcher.Contains(Path, OldKeyCall(source)) || SourcePatcher.Contains(Path, OldKeyDeclaration(source)));

        /// <summary>
        /// What the step's check still finds missing. <paramref name="pickedForm"/> and
        /// <paramref name="pickedSource"/> are the dropdowns as picked in the step; null when not.
        /// </summary>
        public static List<string> Missing(RemoteSwitchForm? pickedForm, RemoteSwitchSource? pickedSource)
        {
            var missing = new List<string>();
            var form = Form();

            if (!SourcePatcher.Contains(Path, Hook)) missing.Add("PersistRemoteToggles subscription");

            switch (form)
            {
                case RemoteSwitchForm.None:
                    missing.Add("PersistRemoteToggles method — saves the remote MainAdNetwork (\"Unassigned\" / \"MAX\" / \"Metica\")");
                    missing.Add("PersistMainAdNetwork method");
                    break;

                case RemoteSwitchForm.Old:
                    missing.Add("MainAdNetwork read by the remote switch — the old code reads only UseMetica" +
                                (pickedForm != null && pickedSource != null ? $" ({Label(pickedForm.Value)}, {Label(pickedSource.Value)} picked)" : ""));
                    break;

                default:
                    var source = Source() ?? RemoteSwitchSource.Json;
                    var wantBoth = (pickedForm ?? form) == RemoteSwitchForm.Both;
                    var converting = pickedSource != null && pickedSource != source;

                    if (converting)
                        missing.Add($"The remote value read from the {Label(pickedSource.Value)} — converting from the {Label(source)}");
                    if (form == RemoteSwitchForm.Both && !wantBoth)
                        missing.Add($"The old UseMetica no longer read — converting to {NewLabel}");
                    if (form == RemoteSwitchForm.New && wantBoth)
                        missing.Add($"The old UseMetica read when MainAdNetwork is missing — converting to {KeepBothLabel}");
                    if (converting) break;

                    if (!SourcePatcher.Contains(Path, ReaderDeclaration(source))) missing.Add("PersistMainAdNetwork method");
                    if (source == RemoteSwitchSource.Json && !SourcePatcher.Contains(MeticaPaths.RemoteConfiguration, JsonField))
                        missing.Add("RemoteConfiguration.MainAdNetwork — so DefaultRemoteSettings and Firebase → Remote → ClipboardJson carry the key");
                    if (form != RemoteSwitchForm.Both || !wantBoth) break;

                    if (!SourcePatcher.Contains(Path, OldKeyCall(source))) missing.Add("PersistMainAdNetwork's call to EarlierUseMetica");
                    if (!SourcePatcher.Contains(Path, OldKeyDeclaration(source))) missing.Add("EarlierUseMetica method");
                    if (source == RemoteSwitchSource.Json && !SourcePatcher.Contains(MeticaPaths.RemoteConfiguration, RemoteFlag))
                        missing.Add("RemoteConfiguration.UseMetica — EarlierUseMetica reads it");
                    break;
            }

            return missing;
        }

        // ── Writing ────────────────────────────────────────────────────────────

        /// <summary>
        /// Brings the remote switch to <paramref name="targetForm"/> reading from
        /// <paramref name="targetSource"/>.
        ///
        /// <para>Null means "not picked". A project without the switch needs the source picked
        /// (the keys are MainAdNetwork only, unless Both is asked for); one with the old code needs
        /// both picked; until then it is left exactly as it is. One already on the tool's switch
        /// keeps what null leaves out — and is converted to what is picked.</para>
        /// </summary>
        public static void Apply(List<string> log, RemoteSwitchForm? targetForm, RemoteSwitchSource? targetSource)
        {
            var form = Form();

            // Nothing goes in, the subscription included, until there is something to call.
            if (form == RemoteSwitchForm.None && targetSource == null)
            {
                log.Add("Remote switch: not written yet — pick where the remote value is in Firebase (Parameter or JSON) " +
                        "in the step, then apply again.");
                return;
            }

            // OnFetchCompleteWithSuccess (bool, string) arrived in v5.3.6. Before that the only
            // event is OnFetchComplete, with no arguments — the same one CreateAndUpdateConfig
            // uses there, which does not know about success either.
            var withSuccess = SourcePatcher.Contains(Path, "OnFetchCompleteWithSuccess");

            Run(log, "PersistRemoteToggles subscription",
                SourcePatcher.InsertAfterLine(Path, Hook,
                    "+= CreateAndUpdateConfig;",
                    withSuccess
                        ? "            OnFetchCompleteWithSuccess += PersistRemoteToggles;"
                        : "            OnFetchComplete += PersistRemoteToggles;"),
                Path,
                "subscribe PersistRemoteToggles alongside CreateAndUpdateConfig");

            switch (form)
            {
                case RemoteSwitchForm.None:
                {
                    var source = targetSource.Value;

                    // Both go in before AddOrUpdateValue, the method first — so PersistMainAdNetwork
                    // sits in the same place whether PersistRemoteToggles is new or was there already.
                    Run(log, "PersistRemoteToggles method",
                        SourcePatcher.InsertBeforeLine(Path, Method, Anchor,
                            (withSuccess
                                ? "        static void PersistRemoteToggles(bool success, string message)\n" +
                                  "        {\n" +
                                  "            if (!success || m_Configuration == null) return;\n"
                                : "        static void PersistRemoteToggles()\n" +
                                  "        {\n" +
                                  "            if (m_Configuration == null) return;\n") +
                            CallLines("            ", source) + "\n" +
                            "        }\n"),
                        Path,
                        "add a PersistRemoteToggles method that calls " + CallStatement(source));
                    AddReader(log, source);
                    if (targetForm == RemoteSwitchForm.Both) AddOldKey(log, source);
                    if (source == RemoteSwitchSource.Json) AddJsonField(log);
                    break;
                }

                case RemoteSwitchForm.Old:
                {
                    if (targetForm == null || targetSource == null)
                    {
                        log.Add($"Remote switch: {Path} has the old code, which reads only UseMetica. Left as it is — pick " +
                                $"{KeepBothLabel} or {NewLabel}, and {ParameterLabel} or {JsonLabel}, in the step, then apply again.");
                        return;
                    }

                    // The call goes in only once the method it calls is in the file — otherwise
                    // the old lines, which work, stay.
                    var source = targetSource.Value;
                    AddReader(log, source);
                    if (!SourcePatcher.Contains(Path, ReaderDeclaration(source)))
                    {
                        log.Add($"PersistRemoteToggles in {Path} still saves the remote switch the old way — add PersistMainAdNetwork first (above).");
                        return;
                    }

                    Run(log, "PersistRemoteToggles saves the remote MainAdNetwork", RewriteOldLines(source), Path,
                        "in PersistRemoteToggles, replace the lines that save UseMetica / MainAdNetwork with " + CallStatement(source));
                    if (!SourcePatcher.Contains(Path, CallStatement(source))) break;
                    if (targetForm == RemoteSwitchForm.Both) AddOldKey(log, source);
                    if (source == RemoteSwitchSource.Json) AddJsonField(log);
                    break;
                }

                default:
                {
                    var current = Source() ?? RemoteSwitchSource.Json;
                    var source = targetSource ?? current;
                    var wantBoth = (targetForm ?? form) == RemoteSwitchForm.Both;

                    if (source != current && !Convert(log, current, source)) return;

                    AddReader(log, source);
                    if (wantBoth) AddOldKey(log, source);
                    else if (HasOldKey() && RemoveOldKey(source))
                        log.Add($"{Path}: the old UseMetica is no longer read ({NewLabel})");

                    if (source == RemoteSwitchSource.Json) AddJsonField(log);
                    else if (RemoveJsonField())
                        log.Add($"{MeticaPaths.RemoteConfiguration}: MainAdNetwork removed — the value is a Firebase parameter now, not in AdsSettings");
                    break;
                }
            }

            // PersistMainAdNetwork logs through Message / Tag.
            if (SourcePatcher.Contains(Path, ReaderDeclaration(RemoteSwitchSource.Json))
                || SourcePatcher.Contains(Path, ReaderDeclaration(RemoteSwitchSource.Parameter)))
                SourcePatcher.EnsureUsing(Path, "using Monetization.Runtime.Logger;");
        }

        private static void AddReader(List<string> log, RemoteSwitchSource source) =>
            Run(log, "PersistMainAdNetwork method",
                SourcePatcher.InsertBeforeLine(Path, ReaderDeclaration(source), Anchor, ReaderBlock(source)),
                Path,
                "add the PersistMainAdNetwork method to RemoteConfigManager — it saves the remote " +
                "\"MainAdNetwork\" to MonetizationPreferences.MainAdNetwork");

        /// <summary>
        /// Keep both: the line in PersistMainAdNetwork and EarlierUseMetica after it — and, for
        /// the JSON, RemoteConfiguration's bool, which it reads.
        /// </summary>
        private static void AddOldKey(List<string> log, RemoteSwitchSource source)
        {
            if (source == RemoteSwitchSource.Json)
                Run(log, "RemoteConfiguration.UseMetica",
                    SourcePatcher.InsertAfterLine(MeticaPaths.RemoteConfiguration, RemoteFlag,
                        "NextInterstitialDelay", "        public bool UseMetica;"),
                    MeticaPaths.RemoteConfiguration, "add: public bool UseMetica;");

            Run(log, "PersistMainAdNetwork reads the old UseMetica",
                SourcePatcher.InsertAfterLine(Path, OldKeyCall(source), ReadLine(source), OldKeyLine(source)),
                Path, "in PersistMainAdNetwork, right after the line that reads the value, add: " + OldKeyLine(source).Trim());

            Run(log, "EarlierUseMetica method",
                SourcePatcher.InsertBeforeLine(Path, OldKeyDeclaration(source), Anchor, OldKeyBlock(source)),
                Path, "add the EarlierUseMetica method to RemoteConfigManager");
        }

        /// <summary>
        /// JSON: RemoteConfiguration's empty MainAdNetwork — after the UseMetica bool when there
        /// is one, so it lands in the same place whichever went in first.
        /// </summary>
        private static void AddJsonField(List<string> log)
        {
            var path = MeticaPaths.RemoteConfiguration;
            Run(log, "RemoteConfiguration.MainAdNetwork",
                SourcePatcher.InsertAfterLine(path, JsonField,
                    SourcePatcher.Contains(path, RemoteFlag) ? RemoteFlag : "NextInterstitialDelay", JsonFieldLines),
                path, "add an empty field: public string MainAdNetwork;");
        }

        /// <summary>The tool's MainAdNetwork field, once the value is a parameter. True when it went.</summary>
        private static bool RemoveJsonField()
        {
            var path = MeticaPaths.RemoteConfiguration;
            if (!SourcePatcher.Contains(path, JsonFieldTooltip)) return false;
            SourcePatcher.RemoveLinesContaining(path, JsonFieldTooltip);
            SourcePatcher.RemoveLinesContaining(path, JsonField);
            return true;
        }

        /// <summary>
        /// The line and the method of Keep both, for <paramref name="source"/>. RemoteConfiguration's
        /// bool stays — GD SDK 6.2.1+ ships it, and game code may read it. True when anything went.
        /// </summary>
        private static bool RemoveOldKey(RemoteSwitchSource source)
        {
            var lines = SourcePatcher.RemoveLinesContaining(Path, OldKeyCall(source));
            var method = SourcePatcher.RemoveBlockContaining(Path, OldKeyDeclaration(source));
            return lines > 0 || method;
        }

        /// <summary>
        /// Moves the tool's switch from reading <paramref name="from"/> to <paramref name="to"/>:
        /// the call, PersistMainAdNetwork and (Keep both) EarlierUseMetica, swapped for their
        /// other version. Only when PersistMainAdNetwork is exactly as the tool wrote it —
        /// otherwise nothing changes, and it is reported for a hand edit.
        /// </summary>
        private static bool Convert(List<string> log, RemoteSwitchSource from, RemoteSwitchSource to)
        {
            var text = SourcePatcher.ReadAll(Path).Replace("\r\n", "\n");
            var reader = string.Join("\n", text.Split('\n').Where(line => !line.Contains(OldKeyCall(from))));
            if (!reader.Contains(ReaderBlock(from)) || !text.Contains(CallStatement(from)))
            {
                log.Add($"Remote switch: PersistMainAdNetwork in {Path} isn't as the tool wrote it (changed by hand?), so it " +
                        $"isn't converted to the {Label(to)}. Convert it by hand, or restore the original RemoteConfigManager.cs " +
                        "(Compare → Replace with original) and apply again.");
                return false;
            }

            RemoveOldKey(from);
            SourcePatcher.ReplaceFirst(Path, ReaderDeclaration(to),
                PatchCoreFilesStep.Lines(Path, ReaderBlock(from)), PatchCoreFilesStep.Lines(Path, ReaderBlock(to)));
            SourcePatcher.ReplaceFirst(Path, CallStatement(to), CallStatement(from), CallStatement(to));
            SourcePatcher.ReplaceFirst(Path, CallComment(to), CallComment(from), CallComment(to));

            log.Add($"{Path}: the remote value is now read from the {Label(to)} (was the {Label(from)})");
            return true;
        }

        /// <summary>
        /// PersistRemoteToggles' own lines in an old form become the one call. Found by their
        /// first and last line inside the method, so lines a game added before or after them
        /// stay where they are.
        /// </summary>
        private static O RewriteOldLines(RemoteSwitchSource source)
        {
            if (!SourcePatcher.Exists(Path)) return O.FileMissing;

            var text = SourcePatcher.ReadAll(Path);
            if (text.Contains(CallStatement(RemoteSwitchSource.Json)) || text.Contains(CallStatement(RemoteSwitchSource.Parameter)))
                return O.AlreadyApplied;

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            var declaration = lines.FindIndex(line => line.Contains(Method));
            if (declaration < 0 || !SourcePatcher.TryFindBlock(lines, declaration, out var open, out var close))
                return O.AnchorNotFound;

            var inside = close - open - 1;
            var first = inside <= 0 ? -1 : lines.FindIndex(open + 1, inside, line => OldFirstLine.Any(line.Contains));
            var last = inside <= 0 ? -1 : lines.FindLastIndex(close - 1, inside, line => OldLastLine.Any(line.Contains));
            if (first < 0 || last < first) return O.AnchorNotFound;

            var indent = lines[first].Substring(0, lines[first].Length - lines[first].TrimStart().Length);
            lines.RemoveRange(first, last - first + 1);
            lines.InsertRange(first, CallLines(indent, source).Split('\n'));

            SourcePatcher.WriteWithBackup(Path, string.Join(newline, lines));
            return O.Applied;
        }

        private static void Run(List<string> log, string what, O outcome, string path, string manualHint)
        {
            log.Add(outcome == O.AnchorNotFound
                ? $"{what}: anchor not found in {path}. Apply by hand — {manualHint}."
                : SourcePatcher.Describe(outcome, path, manualHint));
        }
    }
}
