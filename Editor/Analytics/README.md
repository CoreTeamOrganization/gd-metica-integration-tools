# Analytics Integration

Metica analytics for games on **GD SDK below 6.2.0** (5.0.0 – 6.0.1) or a **custom SDK**.
GD SDK 6.2.0+ has analytics built in, so the flow does not run there (yet).

## Steps

| # | Step | What it does |
|---|------|--------------|
| 1 | Add Metica v2.45.6 | The Ads flow's Metica SDK install — same target version, old Metica removed first, then **Android libraries resolved** (passes only once they are). |
| 2 | Add Metica Analytics Abstractions | Downloads `com.metica.analytics.abstractions-1.0.0.tgz` into `Packages/` and adds it to `manifest.json` as a `file:` dependency — or **Pick .tgz instead…**. Before the genre creator, which turns `METICA_ANALYTICS` on. |
| 3 | Add Genre Creator v2.1.1 | Adds `https://github.com/CoreTeamOrganization/gd-analytics-genre-creator.git#v2.1.1`. An embedded copy in `Packages/` counts. One older than 2.1.0 (no `UpdateFirebaseInfo`) is a problem: **Update to v2.1.1** (an embedded copy is replaced by hand). |
| 4 | Create genre files | Takes the **game name** and the **Excel schema** (a **Sheet** dropdown only when the file has more than one), plus **Metica App ID**, **Metica API Key** and **Adjust App Token** (all optional). The name starts as Player Settings' product name in one word (spaces and symbols dropped); the three fields start filled from the project when it has them — the Adjust token from GD SDK's AdjustConfiguration, an `AdjustConfig` in code or the Adjust prefab / scene; the Metica keys from the Ads integration's MeticaSettings (the build target's platform) or MeticaAdsConfig. Typing replaces them. **Create genre files** stays disabled until the name is valid and an Excel file is chosen. The genre creator writes `Assets/MeticaGenres/<Game>/` and, after the compile, `Assets/MeticaGenres/Resources/<Game>Analytics.asset`; the step then writes the filled fields to it and turns **Enable Metica Logs** on. Once the asset exists the same three fields edit it directly. **Fill empty fields from the game** appears when one is empty and the project has a value. |
| 5 | Connect Adjust to Metica | **GD SDK:** adds to `AdjustAnalyticsNetwork.cs` only what GD SDK 6.2.x has there, line for line: the `AppToken` / `AdId` statics (after `IsInitialized`), `AppToken = adjustSO.AppToken;`, `ExternalDeviceId` and the `muid` global callback parameter = `SystemInfo.deviceUniqueIdentifier` before `Adjust.InitSdk`, and `Adjust.GetAdid(adid => AdId = adid)` after it. Nothing about the genre — `MeticaGenreAnalytics.cs` (step 6) reads the statics. Only missing lines are added. An upgrade to 6.2.0+ replaces the file with the same lines. **Custom SDK:** around `Adjust.InitSdk(config);` in the file calling it (picked from a list when there are several; with none, **Copy the code**): the muid before, and `Adjust.GetAdid` → `UpdateAdjustInfo(adid, config.AppToken)` on the genre asset after. A file inside an `.asmdef` also gets a reference to `GameDistrict.MeticaAnalytics.Runtime`. Already wired by hand counts as done. |
| 5a | Send the Firebase instance ID | **GD SDK:** adds to `FirebaseAnalyticsNetwork.cs` only what GD SDK 6.2.6 has there: `using AdjustSdk;`, the `AppInstanceId` static, and `SendInstanceIdToAdjust()` (before `Initialize()`) — `GetAnalyticsInstanceIdAsync()` → `AppInstanceId` and Adjust's `firebase_app_instance_id` global callback parameter — called after `DependenciesAvailable = true;` (5.3+) or first in the dependencies-available block (5.0 – 5.2). Nothing about the genre — step 6's script calls `UpdateFirebaseInfo`. An upgrade to 6.2.6+ replaces the file with the same lines (6.2.0 – 6.2.5 don't have them). **Custom SDK:** first thing in the `if (task.Result == DependencyStatus.Available) {` block of the file calling `FirebaseApp.CheckAndFixDependenciesAsync()`: the instance ID to Adjust and `UpdateFirebaseInfo(id)` on the genre asset; otherwise **Copy the code**. Either way every Metica event carries `firebaseAppInstanceId`. No Firebase Analytics in the project: passes with a note. |
| 6 | Start Metica analytics | Writes `Assets/MeticaGenreAnalytics/MeticaGenreAnalytics.cs`, the game's own file (outside the SDK, so an upgrade never touches it). `MeticaGenreAnalytics.Genre` is `<Game>Analytics`, loaded from Resources — game code logs every genre event through it. Before the first scene loads: `Initialize()`. When the game leaves its first scene (the splash): `LogInstallEvent` once per install (PlayerPrefs `Metica_InstallLogged`) and `LogSessionStartEvent` once per launch — an `_initialized` bool keeps a return to the splash from sending them again; no scene setting needed. **GD SDK:** `Genre` also hands the genre `AdjustAnalyticsNetwork.AdId` / `AppToken` and `FirebaseAnalyticsNetwork.AppInstanceId` once they are known (`UpdateAdjustInfo`, `UpdateFirebaseInfo`) — install and sessionStart may go out before the Adjust ID, later events carry it. No genre code in any GD SDK file (`AnalyticsManager.cs` untouched). Needs steps 5 / 5a first. If the game already calls `Initialize` / `LogInstallEvent` / `LogSessionStartEvent` itself, nothing is written. |
| 7 | Performance events (perfStats, loadTime) | **Optional.** GD Performance Tracker's two events, sent through the genre asset (genre creator 2.0.0+'s `ConfigurePerformanceTracking`, `MarkGameInteractive`, `LogPerfStatsEvent`, `LogLoadTimeEvent` — they fetch the payload and add adid, appToken and the AB test). Five rows: **the package** (`https://github.com/CoreTeamOrganization/GDPerformanceTracker.git`; the genre creator then adds `GD_PERFORMANCE_TRACKER` for Android and iOS, or **Add GD_PERFORMANCE_TRACKER**); **the GDPerfTracker prefab in the boot scene** (a **Scene** dropdown of the enabled Build Settings scenes, the first by default; opened beside the open scene, saved, closed); **switched from remote config** (`perf_tracking_enabled`, `perf_sample_interval`, `startup_tracking_enabled` — GD SDK: at the end of `RemoteConfigManager.CreateAndUpdateConfig`, after every fetch; custom SDK: **Copy the code** for the Firebase fetch callback; add the three keys in Firebase either way); **loadTime** (`MarkGameInteractive` + `LogLoadTimeEvent`) and **perfStats** (`LogPerfStatsEvent`) — pick a **Script** and **Function** and the calls go at its end (ahead of a final return), or **Copy the code**. Calls the game already makes count as done. Startup timing is Android-only. |
| 8 | Set up the Android build | Required: Gradle 8.6, JDK 17, Kotlin 1.9.22 and AGP 8.4.0 — [Metica docs, steps 1–3](https://docs.metica.com/api/unity-sdk/unity-sdk-2#step-2-update-baseprojecttemplate.gradle). Same rows as the Ads flow's Troubleshooting step. |


Each step is its own review and commit (**Commit**, or **Commit and push**).

After the flow, the game still logs its genre events where they happen — the developer's job.

Backups of edited files go to `MeticaIntegrationBackups/` (project root), which ignores itself in
git, so they never show up as changes.

## Notes

- **New integrations only.** A project the tool integrated before (genre code in
  `AnalyticsManager.cs`, `AdjustAnalyticsNetwork.cs` or `FirebaseAnalyticsNetwork.cs`, or the old
  `Assets/MeticaGenres/MeticaAnalyticsStartup.cs`) is left as it is: steps 5, 5a and 6 pass with a note.
- **Upgrading a game to GD SDK 6.2.6+.** The SDK-file lines are replaced by the same code. Remake
  the genre with the SDK's Genre Creator (same name, same Excel), then in `MeticaGenreAnalytics.cs`
  delete the `GameDistrict.MeticaAnalytics` using, set `GenrePath` to
  `"Configurations/Game Genres/<Game>Analytics"` and delete the lines marked "below GD SDK 6.2.0
  only". Remove `Assets/MeticaGenres/`, the genre-creator package and the abstractions `.tgz`, and
  the `GameDistrict.MeticaAnalytics` using in other game scripts. Keep `Metica_InstallLogged`.

- **Order of steps 1–3 matters.** The genre creator turns `METICA_ANALYTICS` on as soon as the
  Metica SDK exists; with it on, the Metica SDK only compiles with the abstractions present.
- **How the tool makes the genre files.** The genre creator has no public API besides its
  window, so the tool calls the same internal methods its window does, by name
  (`GenreExcelParser.GetSheetNames` / `Parse`, `GenreAssetCreator.CreateFiles`). The files
  are written by the genre creator's code. Checked against 1.0.0, 2.0.0 and 2.1.1; any other
  version that renames them is reported as unsupported.
- **Game name** becomes the class names: letters and digits, starting with a letter.

## Layout

```
AnalyticsFlow.cs          the step list, and which projects it covers
AnalyticsPackages.cs      manifest edits, the abstractions .tgz, the genre creator bridge
Steps/                    one file per step
```
