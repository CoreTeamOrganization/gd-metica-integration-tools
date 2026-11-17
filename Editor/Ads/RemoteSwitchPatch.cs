using System.Collections.Generic;
using System.Linq;
using O = GameDistrict.MeticaIntegrationTools.SourcePatcher.Outcome;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>What a project's remote switch reads from Firebase's AdsSettings.</summary>
    internal enum RemoteSwitchForm
    {
        /// <summary>No PersistRemoteToggles at all: Metica's remote switch was never in this project.</summary>
        None,

        /// <summary>
        /// The old code: it reads only the "UseMetica" bool. GD SDK 6.2.1+'s own bool preference
        /// (this tool's first patch too), or this tool's lines from before "MainAdNetwork".
        /// </summary>
        Old,

        /// <summary>"MainAdNetwork" only.</summary>
        New,

        /// <summary>"MainAdNetwork", and "UseMetica" when it isn't there.</summary>
        Both
    }

    /// <summary>
    /// The remote switch in the GD SDK's RemoteConfigManager: which ad network remote config asks
    /// for, saved to MonetizationPreferences.MainAdNetwork after every fetch.
    ///
    /// <para>Firebase's AdsSettings carries "MainAdNetwork", a string holding one of the
    /// MainAdNetwork enum's names. PersistRemoteToggles hands the fetched JSON to
    /// PersistMainAdNetwork, which reads the string into a fresh object (JsonUtility cannot read
    /// an enum by name, and m_Configuration keeps the last fetch's value when a later one lacks
    /// the key), matches it against the enum's names ignoring case and outer spaces, and saves
    /// the enum's own spelling — so nothing but "Unassigned", "MAX" or "Metica" ever reaches the
    /// preference or the GameData event. "Unassigned" is saved like the others: it takes the
    /// device back out of the test. A value that matches none is refused, with an error log.
    /// Without the key nothing is saved: users outside the test keep what they have.</para>
    ///
    /// <para><b>Two forms.</b> A project where the switch was never integrated gets the new
    /// form, with no question: nothing live can depend on anything else. A project that has the
    /// old code — it read the "UseMetica" bool — is asked, in the patch step, and nothing is
    /// decided for it: <i>Keep both</i> adds one line and a small method, EarlierUseMetica, so
    /// "UseMetica" still counts when "MainAdNetwork" isn't there (a game already live on it
    /// keeps working); <i>Implement new</i> reads "MainAdNetwork" only. A project on Keep both
    /// still has the old key's code, so it can move to Implement new later.</para>
    ///
    /// <para>Everything goes through <see cref="SourcePatcher"/>, so it runs on disk or in
    /// memory alike.</para>
    /// </summary>
    internal static class RemoteSwitchPatch
    {
        // ── The two options, as the step shows them ────────────────────────────

        public const string KeepBothLabel = "Keep both";
        public const string NewLabel = "Implement new";

        public const string KeepBothSummary =
            "Keep both: reads MainAdNetwork, and uses the old UseMetica when it's missing. For a game already live on UseMetica.";

        public const string NewSummary =
            "Implement new: reads only MainAdNetwork. Switch Firebase to MainAdNetwork, and keep UseMetica there while older builds are live.";

        // ── What is in the file ────────────────────────────────────────────────

        private const string Hook = "PersistRemoteToggles;";
        private const string Method = "static void PersistRemoteToggles";

        /// <summary>PersistRemoteToggles' one line: the fetched AdsSettings goes to PersistMainAdNetwork.</summary>
        internal const string Call = "PersistMainAdNetwork(GetRemoteValue<string>(REMOTE_KEY).Value);";
        internal const string Reader = "static void PersistMainAdNetwork(string adsSettings)";

        /// <summary>The line of PersistMainAdNetwork that reads the string out of the JSON.</summary>
        private const string ReadLine = "JsonUtility.FromJson<MainAdNetworkRemote>(adsSettings)?.MainAdNetwork;";

        // Keep both: one line in PersistMainAdNetwork, and the method it calls.
        private const string OldKeyCall = "remote = EarlierUseMetica(adsSettings);";
        private const string OldKeyMethod = "static string EarlierUseMetica(string adsSettings)";

        /// <summary>RemoteConfiguration's bool, which EarlierUseMetica reads.</summary>
        private const string RemoteFlag = "public bool UseMetica";

        /// <summary>Everything new goes in before this method of RemoteConfigManager, in order.</summary>
        private const string Anchor = "public static void AddOrUpdateValue";

        // PersistRemoteToggles' own lines in the old forms, first line and last: GD SDK 6.2.1+'s
        // bool preference (this tool's first patch too), then this tool's lines from when only
        // the UseMetica bool was read from remote config, without and with their logs.
        private const string OldPersist = "MonetizationPreferences.UseMetica.Set(m_Configuration.UseMetica);";

        private static readonly string[] OldFirstLine =
        {
            OldPersist,
            "Metica Integration Tools: MainAdNetwork, only when the fetched AdsSettings has UseMetica",
            "var adsSettings = GetRemoteValue<string>(REMOTE_KEY).Value;"
        };

        private static readonly string[] OldLastLine =
        {
            OldPersist,
            "MonetizationPreferences.MainAdNetwork.Set(",
            "Remote Switch saved: "
        };

        // ── What gets written ──────────────────────────────────────────────────

        /// <summary>
        /// PersistRemoteToggles' own two lines: a comment, and the fetched AdsSettings handed to
        /// PersistMainAdNetwork. Each line carries <paramref name="indent"/>.
        /// </summary>
        internal static string CallLines(string indent) =>
            $"{indent}// Metica Integration Tools: save the main ad network the fetched AdsSettings asks for (see PersistMainAdNetwork).\n" +
            $"{indent}{Call}";

        /// <summary>
        /// PersistMainAdNetwork and the class it reads the JSON into, as members of
        /// RemoteConfigManager: read, check, save. No try around the read — a broken AdsSettings
        /// makes the SDK's own CreateAndUpdateConfig throw first, on the same text, so this is
        /// never reached with one.
        /// </summary>
        internal const string ReaderBlock =
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
            "        " + Reader + "\n" +
            "        {\n" +
            "            var stored = MonetizationPreferences.MainAdNetwork.Get();\n" +
            "            if (string.IsNullOrEmpty(stored)) stored = MainAdNetwork.Unassigned.ToString();\n" +
            "\n" +
            "            var remote = string.IsNullOrEmpty(adsSettings) ? null : " + ReadLine + "\n" +
            "            if (string.IsNullOrWhiteSpace(remote))\n" +
            "            {\n" +
            "                Message.Log(Tag.SDK, \"Remote AdsSettings has no MainAdNetwork, MainAdNetwork stays \" + stored);\n" +
            "                return;\n" +
            "            }\n" +
            "\n" +
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

        /// <summary>Keep both: the line that goes right after PersistMainAdNetwork reads the JSON.</summary>
        internal const string OldKeyLine =
            "            if (string.IsNullOrWhiteSpace(remote)) " + OldKeyCall + " // the old key, when MainAdNetwork isn't there";

        /// <summary>
        /// Keep both: the old key, read as it always was — RemoteConfiguration's bool, and only
        /// when the fetched text has the key, since the bool cannot tell false from absent.
        /// </summary>
        internal const string OldKeyBlock =
            "        // Metica Integration Tools: the old remote key, \"UseMetica\": true / false, for a game whose\n" +
            "        // Firebase still has it. Read only when AdsSettings has no MainAdNetwork. Gives \"Metica\" or\n" +
            "        // \"MAX\"; null when the fetched AdsSettings doesn't have this key either.\n" +
            "        " + OldKeyMethod + "\n" +
            "        {\n" +
            "            if (m_Configuration == null || string.IsNullOrEmpty(adsSettings) || !adsSettings.Contains(\"\\\"UseMetica\\\"\")) return null;\n" +
            "\n" +
            "            var mainAdNetwork = (m_Configuration.UseMetica ? MainAdNetwork.Metica : MainAdNetwork.MAX).ToString();\n" +
            "            Message.Log(Tag.SDK, \"Remote AdsSettings has the old UseMetica key, not MainAdNetwork: \" + mainAdNetwork);\n" +
            "            return mainAdNetwork;\n" +
            "        }\n";

        // ── Reading the project ────────────────────────────────────────────────

        /// <summary>What the project's RemoteConfigManager has now.</summary>
        public static RemoteSwitchForm Form()
        {
            var path = MeticaPaths.RemoteConfigManager;
            if (!SourcePatcher.Exists(path) || !SourcePatcher.Contains(path, Method)) return RemoteSwitchForm.None;
            if (!SourcePatcher.Contains(path, Call)) return RemoteSwitchForm.Old;

            return SourcePatcher.Contains(path, OldKeyCall) || SourcePatcher.Contains(path, OldKeyMethod)
                ? RemoteSwitchForm.Both
                : RemoteSwitchForm.New;
        }

        /// <summary>
        /// What the step's check still finds missing. <paramref name="picked"/> is the option
        /// picked in the step (New or Both), or null when none has been.
        /// </summary>
        public static List<string> Missing(RemoteSwitchForm? picked)
        {
            var path = MeticaPaths.RemoteConfigManager;
            var missing = new List<string>();
            var form = Form();

            if (!SourcePatcher.Contains(path, Hook)) missing.Add("PersistRemoteToggles subscription");

            switch (form)
            {
                case RemoteSwitchForm.None:
                    missing.Add("PersistRemoteToggles method — saves the remote MainAdNetwork (\"Unassigned\" / \"MAX\" / \"Metica\")");
                    missing.Add("PersistMainAdNetwork method");
                    break;

                case RemoteSwitchForm.Old:
                    missing.Add(picked == RemoteSwitchForm.Both ? $"MainAdNetwork read by the remote switch ({KeepBothLabel} picked)"
                        : picked == RemoteSwitchForm.New ? $"MainAdNetwork read by the remote switch ({NewLabel} picked)"
                        : $"MainAdNetwork read by the remote switch — it reads only the old UseMetica; pick {KeepBothLabel} or {NewLabel}");
                    break;

                default:
                    if (!SourcePatcher.Contains(path, Reader)) missing.Add("PersistMainAdNetwork method");
                    if (form != RemoteSwitchForm.Both) break;

                    if (picked == RemoteSwitchForm.New)
                    {
                        missing.Add($"The old UseMetica key no longer read ({NewLabel} picked)");
                        break;
                    }

                    if (!SourcePatcher.Contains(path, OldKeyCall)) missing.Add("PersistMainAdNetwork's call to EarlierUseMetica");
                    if (!SourcePatcher.Contains(path, OldKeyMethod)) missing.Add("EarlierUseMetica method");
                    if (!SourcePatcher.Contains(MeticaPaths.RemoteConfiguration, RemoteFlag))
                        missing.Add("RemoteConfiguration.UseMetica — EarlierUseMetica reads it");
                    break;
            }

            return missing;
        }

        // ── Writing ────────────────────────────────────────────────────────────

        /// <summary>
        /// Brings the remote switch to <paramref name="target"/> (New or Both).
        ///
        /// <para>Null means "not picked": a project without the switch gets New; one with the
        /// old code is left exactly as it is, and told to pick; one already on New or Both stays
        /// there (and gets back anything of it that went missing).</para>
        /// </summary>
        public static void Apply(List<string> log, RemoteSwitchForm? target)
        {
            var path = MeticaPaths.RemoteConfigManager;
            var form = Form();

            // OnFetchCompleteWithSuccess (bool, string) arrived in v5.3.6. Before that the only
            // event is OnFetchComplete, with no arguments — the same one CreateAndUpdateConfig
            // uses there, which does not know about success either.
            var withSuccess = SourcePatcher.Contains(path, "OnFetchCompleteWithSuccess");

            Run(log, "PersistRemoteToggles subscription",
                SourcePatcher.InsertAfterLine(path, Hook,
                    "+= CreateAndUpdateConfig;",
                    withSuccess
                        ? "            OnFetchCompleteWithSuccess += PersistRemoteToggles;"
                        : "            OnFetchComplete += PersistRemoteToggles;"),
                path,
                "subscribe PersistRemoteToggles alongside CreateAndUpdateConfig");

            switch (form)
            {
                case RemoteSwitchForm.None:
                    // Both go in before AddOrUpdateValue, the method first — so PersistMainAdNetwork
                    // sits in the same place whether PersistRemoteToggles is new or was there already.
                    Run(log, "PersistRemoteToggles method",
                        SourcePatcher.InsertBeforeLine(path, Method, Anchor,
                            (withSuccess
                                ? "        static void PersistRemoteToggles(bool success, string message)\n" +
                                  "        {\n" +
                                  "            if (!success || m_Configuration == null) return;\n"
                                : "        static void PersistRemoteToggles()\n" +
                                  "        {\n" +
                                  "            if (m_Configuration == null) return;\n") +
                            CallLines("            ") + "\n" +
                            "        }\n"),
                        path,
                        "add a PersistRemoteToggles method that calls " + Call);
                    AddReader(log);
                    if (target == RemoteSwitchForm.Both) AddOldKey(log);
                    break;

                case RemoteSwitchForm.Old:
                    if (target != RemoteSwitchForm.New && target != RemoteSwitchForm.Both)
                    {
                        log.Add($"Remote switch: {path} has the old code, which reads only UseMetica. Left as it is — " +
                                $"pick {KeepBothLabel} or {NewLabel} in the step, then apply again.");
                        return;
                    }

                    // The call goes in only once the method it calls is in the file — otherwise
                    // the old lines, which work, stay.
                    AddReader(log);
                    if (!SourcePatcher.Contains(path, Reader))
                    {
                        log.Add($"PersistRemoteToggles in {path} still saves the remote switch the old way — add PersistMainAdNetwork first (above).");
                        return;
                    }

                    Run(log, "PersistRemoteToggles saves the remote MainAdNetwork", RewriteOldLines(path), path,
                        "in PersistRemoteToggles, replace the lines that save UseMetica / MainAdNetwork with " + Call);
                    if (target == RemoteSwitchForm.Both && SourcePatcher.Contains(path, Call)) AddOldKey(log);
                    break;

                case RemoteSwitchForm.New:
                    AddReader(log);
                    if (target == RemoteSwitchForm.Both) AddOldKey(log);
                    break;

                case RemoteSwitchForm.Both:
                    AddReader(log);
                    if (target == RemoteSwitchForm.New) RemoveOldKey(log);
                    else AddOldKey(log);
                    break;
            }

            // PersistMainAdNetwork logs through Message / Tag.
            if (SourcePatcher.Contains(path, Reader))
                SourcePatcher.EnsureUsing(path, "using Monetization.Runtime.Logger;");
        }

        private static void AddReader(List<string> log) =>
            Run(log, "PersistMainAdNetwork method",
                SourcePatcher.InsertBeforeLine(MeticaPaths.RemoteConfigManager, Reader, Anchor, ReaderBlock),
                MeticaPaths.RemoteConfigManager,
                "add the PersistMainAdNetwork method (and its MainAdNetworkRemote class) to RemoteConfigManager — " +
                "it saves AdsSettings' \"MainAdNetwork\" to MonetizationPreferences.MainAdNetwork");

        /// <summary>Keep both: RemoteConfiguration's bool, the line in PersistMainAdNetwork, and EarlierUseMetica after it.</summary>
        private static void AddOldKey(List<string> log)
        {
            var path = MeticaPaths.RemoteConfigManager;

            Run(log, "RemoteConfiguration.UseMetica",
                SourcePatcher.InsertAfterLine(MeticaPaths.RemoteConfiguration, RemoteFlag,
                    "NextInterstitialDelay", "        public bool UseMetica;"),
                MeticaPaths.RemoteConfiguration, "add: public bool UseMetica;");

            Run(log, "PersistMainAdNetwork reads the old UseMetica key",
                SourcePatcher.InsertAfterLine(path, OldKeyCall, ReadLine, OldKeyLine),
                path, "in PersistMainAdNetwork, right after the line that reads the JSON, add: " + OldKeyLine.Trim());

            Run(log, "EarlierUseMetica method",
                SourcePatcher.InsertBeforeLine(path, OldKeyMethod, Anchor, OldKeyBlock),
                path, "add the EarlierUseMetica method to RemoteConfigManager");
        }

        /// <summary>
        /// Implement new, on a project that kept both: the line and the method go.
        /// RemoteConfiguration's bool stays — GD SDK 6.2.1+ ships it, and game code may read it.
        /// </summary>
        private static void RemoveOldKey(List<string> log)
        {
            var path = MeticaPaths.RemoteConfigManager;
            var lines = SourcePatcher.RemoveLinesContaining(path, OldKeyCall);
            var method = SourcePatcher.RemoveBlockContaining(path, OldKeyMethod);

            log.Add(lines > 0 || method
                ? $"{path}: the old UseMetica key is no longer read ({NewLabel})"
                : $"{path}: no old UseMetica key to stop reading");
        }

        /// <summary>
        /// PersistRemoteToggles' own lines in an old form become the one call. Found by their
        /// first and last line inside the method, so lines a game added before or after them
        /// stay where they are.
        /// </summary>
        private static O RewriteOldLines(string path)
        {
            if (!SourcePatcher.Exists(path)) return O.FileMissing;

            var text = SourcePatcher.ReadAll(path);
            if (text.Contains(Call)) return O.AlreadyApplied;

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
            lines.InsertRange(first, CallLines(indent).Split('\n'));

            SourcePatcher.WriteWithBackup(path, string.Join(newline, lines));
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
