# Analytics Integration

Metica analytics for games on **GD SDK below 6.2.0** (5.0.0 – 6.0.1) or a **custom SDK**.
GD SDK 6.2.0+ has analytics built in, so the flow does not run there (yet).

## Steps

| # | Step | What it does |
|---|------|--------------|
| 1 | Add Metica v2.45.6 | The Ads flow's Metica SDK install — same target version, old Metica removed first, then **Android libraries resolved** (passes only once they are). |
| 2 | Add Metica Analytics Abstractions | Downloads `com.metica.analytics.abstractions-1.0.0.tgz` into `Packages/` and adds it to `manifest.json` as a `file:` dependency — or **Pick .tgz instead…**. Before the genre creator, which turns `METICA_ANALYTICS` on. |
| 3 | Add Genre Creator v2.0.0 | Adds `https://github.com/CoreTeamOrganization/gd-analytics-genre-creator.git#v2.0.0`. An embedded copy in `Packages/` counts. |
| 4 | Create genre files | Takes the **game name** and the **Excel schema** (a **Sheet** dropdown only when the file has more than one), plus **App Id**, **Api Key** and **App Token** (all optional). **Create genre files** stays disabled until the name is valid and an Excel file is chosen. The genre creator writes `Assets/MeticaGenres/<Game>/` and, after the compile, `Assets/MeticaGenres/Resources/<Game>Analytics.asset`; the step then writes the filled fields to it and turns **Enable Metica Logs** on. Once the asset exists the same three fields edit it directly. |
| 5 | Connect Adjust to Metica | Around `Adjust.InitSdk(config);`: before it, `config.ExternalDeviceId` and the `muid` global callback parameter = `SystemInfo.deviceUniqueIdentifier`; after it, the genre asset is loaded from Resources and `Adjust.GetAdid` → `UpdateAdjustInfo(adid, config.AppToken)`. GD SDK: `AdjustAnalyticsNetwork.cs`. Custom SDK: the file calling `Adjust.InitSdk`, picked from a list when there are several; with none, **Copy the code** to add it by hand. A file inside an `.asmdef` also gets a reference to `GameDistrict.MeticaAnalytics.Runtime`. Already wired by hand counts as done. |
| 6 | Start Metica analytics | Writes `Assets/MeticaGenres/MeticaAnalyticsStartup.cs`: before the first scene loads, `<Game>Analytics.Initialize()`, `LogInstallEvent` once per install (PlayerPrefs `Metica_InstallLogged`), `LogSessionStartEvent` every launch. No game code is touched. If the game already calls `Initialize` / `LogInstallEvent` / `LogSessionStartEvent` itself, nothing is written. The first events may go out before the Adjust ID arrives. |
| 7 | Set up the Android build | Required: Gradle 8.6, JDK 17, Kotlin 1.9.22 and AGP 8.4.0 — [Metica docs, steps 1–3](https://docs.metica.com/api/unity-sdk/unity-sdk-2#step-2-update-baseprojecttemplate.gradle). Same rows as the Ads flow's Troubleshooting step. |


Each step is its own review and commit (**Commit**, or **Commit and push**).

After the flow, the game still logs its genre events where they happen — the developer's job.

Backups of edited files go to `MeticaIntegrationBackups/` (project root), which ignores itself in
git, so they never show up as changes.

## Notes

- **Order of steps 1–3 matters.** The genre creator turns `METICA_ANALYTICS` on as soon as the
  Metica SDK exists; with it on, the Metica SDK only compiles with the abstractions present.
- **How the tool makes the genre files.** The genre creator has no public API besides its
  window, so the tool calls the same internal methods its window does, by name
  (`GenreExcelParser.GetSheetNames` / `Parse`, `GenreAssetCreator.CreateFiles`). The files
  are written by the genre creator's code. Checked against 1.0.0 and 2.0.0; any other
  version that renames them is reported as unsupported.
- **Game name** becomes the class names: letters and digits, starting with a letter.

## Layout

```
AnalyticsFlow.cs          the step list, and which projects it covers
AnalyticsPackages.cs      manifest edits, the abstractions .tgz, the genre creator bridge
Steps/                    one file per step
```
