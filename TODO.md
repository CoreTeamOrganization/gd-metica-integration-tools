# TODO

## To build

### MainAdNetwork instead of the UseMetica bool (fair A/B split)

Why: with a bool pref, a user whose Firebase fetch never landed (or landed after the
AppMetrica event) counts as `false`, so the MAX group grows. A three-value pref leaves those
users out of the comparison.

- [ ] Pref `MonetizationPreferences.MainAdNetwork` (string, key `"GDPrefs_MainAdNetwork"`,
      default `"Unassigned"`), values from `enum MainAdNetwork { Unassigned, MAX, Metica }`,
      replacing the `UseMetica` bool pref. Firebase stays `UseMetica: true / false`.
- [ ] `PersistRemoteToggles`: on a successful fetch, `true` → `Metica`, `false` → `MAX`. Only
      when the fetched AdsSettings JSON has `"UseMetica"` — users outside the experiment stay
      `Unassigned` (matters when exposure is below 100%).
- [ ] AdsManager reads the pref once at ads init: that one value picks the network
      (`Metica` → Metica; `MAX` or `Unassigned` → AppLovin) and is what's reported, so the
      label always matches the network that served the session:

      | When the fetch lands  | Pref at ads init                     | Reported this session | Network served this session |
      |-----------------------|--------------------------------------|-----------------------|-----------------------------|
      | Not fetched yet       | `Unassigned`                         | `Unassigned`          | MAX (fallback)              |
      | Fetched after ads init| `Unassigned` (saved for next session)| `Unassigned`          | MAX (fallback)              |
      | Fetched before ads init| `MAX` / `Metica`                    | `MAX` / `Metica`      | the same                    |

- [ ] AppMetrica: `AnalyticsManager.SendEvent("GameData", { "MainAdNetwork": value })`,
      replacing `{ "UseMetica": bool }`.
- [ ] Tool: PatchCoreFilesStep / RemoteSwitchStep write this (and migrate projects already
      patched with the bool); the standalone runtime (`MeticaRemoteConfig`) gets the same
      three values, reported through `MeticaAdsHooks.OnEvent`.
- [ ] Analysis note for the README: compare per session, exclude `Unassigned`, filter by
      install date from the test start.

### Game additions to carry over (AppLovin units → Metica units, GD SDK)

Why: games add their own lines to the AppLovin units (OneLineDraw, GD SDK 5.4.0: Adjust raw-data
`AdjustTrackingManager.AdImpressionAppLovin(arg2, …)`, `AdsManager.OnFullScreenAdShown/Hide` and
`OnRewardedAdShown/Hide` invokes, logs). The Metica units don't have them, so with Metica on they
stop firing.

Fact checked: the `AdsManager.On…AdShown/Hide` hooks are **not** GD SDK's — no release 5.0.0–6.2.5
has them. OneLineDraw declared them in its own AdsManager.cs (and calls them from its AdMob units
too). So they are carried over like any other game line, not added to the templates.

- [ ] New optional step "Game additions to carry over" in AdsFlow, after WrapperFilesStep and
      before PatchCoreFilesStep.
- [ ] Detect: each AppLovin unit vs its stock copy for the declared version (StockFiles /
      SdkCompare), statement by statement per method (CSharpSource). Ignore usings, whitespace,
      comments, Metica v1 code. Edited (not just added) statements → "add by hand".
- [ ] Map method to method: `Show*` → Metica `Show*` (after its show call); hidden → hidden
      (inside its ThreadDispatcher.Enqueue when the original was); reward → reward; revenue →
      revenue; loaded / failed → loaded / failed; displayed → `OnAdShowSuccess` (listener and
      handler added, the units don't listen yet); no match → "add by hand".
- [ ] UI: a checkbox per line (file, method, one-line preview), unchecked by default; log lines
      tagged "log"; Apply inserts only the checked lines.
- [ ] Needs adapting (`arg1` / `arg2`, `MaxSdkBase.AdInfo`, `MaxSdk.*`): badge plus a suggested
      Metica version — for the Adjust line `AdImpressionAppLovin(MeticaAdInfo(meticaAd), …)`, a
      helper building `new MaxSdkBase.AdInfo(dict)` from the MeticaAd (keys `adUnitId`,
      `networkName`, `placement`, `revenue` — the public MAX constructor). Inserted with a
      `// TODO` comment when checked, never silently.
- [ ] Idempotent: a marker comment on each inserted line, skip lines already there, back up the
      Metica units to MeticaIntegrationBackups/, log through MeticaIntegrationLog. AppLovin units
      untouched.
- [ ] Verify: passes when every checked line is present, or nothing is checked (sign off); note
      with found / carried / skipped / by hand.
- [ ] SdkCompareWindow "Replace with original" on an AppLovin unit: warn "This file has N
      game-added lines that will be lost. Carry them to Metica first?"
- [ ] Editor/Ads/README.md and CHANGELOG.md.
- [ ] Open questions: hooks through this step instead of the templates (OK?); list log-only lines
      or hide them; scan the AdMob units too (they keep working, AdMob isn't replaced — leaning no).

### Commit the tool's own install from the tool

Why: adding the tool through the Package Manager (git URL) changes the project, and nothing in
the tool commits that — so it sits uncommitted next to the first step's changes.

- [ ] On Home (or before step 1), when git shows changes to the tool's install, offer "Commit
      the tool install" (and "Commit and push"), like the per-step commit section.
- [ ] Files: `Packages/manifest.json` (the `com.gamedistrict.metica-integration-tools` entry) and
      `Packages/packages-lock.json` (its pinned commit). Commit only the tool's lines? — git
      commits whole files, so check first that nothing else in them is uncommitted; if there is,
      list it and let the user decide.
- [ ] Prefilled message, e.g. "Add Metica Integration Tools <version> (<commit>)"; same again
      for a tool update (the lock file's hash changes).
- [ ] Nothing to show when both files are clean.

### Analytics: perfStats and loadTime (GD Performance Tracker)

Source: https://github.com/CoreTeamOrganization/GDPerformanceTracker (README + wizard, read
v1.3.2). Package `com.gamedistrict.performance-tracker`; one API, `GDPerformance`:
`Configure(perfOn, interval, startupOn)`, `ConsumePerfPayload()`, `MarkGameInteractive()`,
`GetStartupPayload()`. Payloads come back as dictionaries; the game adds the base fields and
sends `perfStats` / `loadTime` through `MeticaSdk.Analytics.LogCustomEvent`.

- [ ] Install: add `https://github.com/CoreTeamOrganization/GDPerformanceTracker.git` to the
      manifest (pin a tag?), like the genre creator step.
- [ ] Prefab: put `GDPerfTracker.prefab` in the first scene enabled in Build Settings, the way
      its wizard does (skip if one exists, save the scene). Pick the scene from a list when the
      first one isn't the boot scene.
- [ ] Remote config: `GDPerformance.Configure(perf_tracking_enabled, perf_sample_interval,
      startup_tracking_enabled)` once remote config is fetched. GD SDK: hook next to the tool's
      PersistRemoteToggles in RemoteConfigManager (check `remoteConfig.GetRemoteValue<T>(key)`
      reads separate keys on 5.x — the wizard's snippet uses 6.2's `MonetizationServices.Remote`);
      custom SDK: pick the fetch callback or copy the code. List the three Firebase keys to add
      (the tool can't create them in Firebase).
- [ ] Senders: generate `LogPerfStats()` / `LogLoadTime()` helpers in the genre folder — payload,
      base fields (`adid`, `appToken`, `abTest`, `abGroup`, `abTestStartDate` from the genre asset /
      Adjust), null-strip, `LogCustomEvent`. On GD 5.x / custom there is no
      `AdjustAnalyticsNetwork.AdId`: take adid from the genre asset's stored Adjust info.
- [ ] loadTime: `MarkGameInteractive()` + `LogLoadTime()` where the game becomes playable — the
      game decides where. Script / function picker (like the init patch), or copy the code.
- [ ] perfStats: `LogPerfStats()` at the logging moment (level end / session end) — same picker.
- [ ] Verify: package in, prefab in the boot scene, Configure wired, both calls found in game
      code (or signed off).
- [ ] Note: startup timing is Android-only (iOS falls back to engine time); Editor readings
      aren't comparable.
- [ ] Also check the guide PDF (Documentation/GDPerformanceTracker-Guide.pdf) for any step the
      README doesn't list.

## Test in Unity before `v1-migration` goes to `main`

Everything below compiles and passes the scratch test harness, but has not been run in a
real project yet.

Suggested runs: one GD SDK 5.0.x game, and one custom-SDK game that had Metica v1. Those two
cover all of the list below.

### Window and steps

- [ ] "Reviewed — next step" moves to the next step at once, and keeps working after a step
      that makes Unity recompile (fix for the stuck "refresh queued" flag).
- [ ] Re-check and Skip still respond after a recompile.
- [ ] A step waiting on a compile shows the "waiting" box, then turns into done (or into
      the real error).
- [ ] Per-step commit: the Commit button commits only that step's files (and their .meta),
      with the prefilled summary and description; nothing is pushed.
- [ ] Commit and push pushes the current branch (sets upstream the first time); Push shows on
      a committed step while there are unpushed commits, and disappears once pushed.

### Analytics abstractions in the Metica SDK step (GD SDK v5)

- [ ] METICA_ANALYTICS in Player settings, no abstractions package: the step shows the problem
      and "Add analytics abstractions"; it lands in Packages/ + manifest and Metica compiles.
- [ ] Compile errors naming Metica.Analytics.Abstractions show as notes; they clear after it's added.
- [ ] Pick the .tgz… and Open download folder work; GD SDK 6.2.2+ (own copy) never asks.

### Wrapper files: members the game added to the SDK

- [ ] A game with `public abstract void RefershBanner();` in BannerAdUnit: the step shows the
      problem; running it adds the stub to MeticaBanner, it compiles, and the warning names the
      game's own RefershBanner to copy from. Unmodified GD SDK: nothing reported.

### iOS build target

- [ ] iOS project: Metica SDK step passes without Android (note instead), the xcframework gets
      iOS ticked, no Enable iOS switch; MAX step and Android build setup pass with a note.
- [ ] Switch to Android: those steps ask for the resolve / build rows again.

### Compare

- [ ] Optional "Compare with original GD SDK" step: opens the window, can be skipped in one
      click, and the flow carries on.
- [ ] Compare window: clicking a file shows its diff; filters (Metica / Tool / All) work;
      Open project file / Open original work; release picker shows when the version is
      unreadable.
- [ ] Replace with original: asks first; a changed file goes back to the original (line
      endings kept) with a copy in MeticaIntegrationBackups/Replaced/<time>/; a missing file is
      added back; the list re-scans and the file is gone from it.
- [ ] Delete (added files only, in place of Replace): asks first; file and .meta copied to
      MeticaIntegrationBackups/Deleted/<time>/, then deleted; the list re-scans.
- [ ] Ticks: tick several files (and tick-all in the bar); ticking keeps the list's scroll and
      the diff shown; Replace only when all ticked are changed/missing, Delete only when all
      are added, both off for a mix (with the hint); one confirm for all; filter change drops
      ticks that are no longer shown.

### Metica v1

- [ ] Metica v1 code step lists the v1 lines, and passes once they are gone.
- [ ] File rows show name + count, folder faded below; clicking a row opens its lines.
- [ ] Hover Comment / Remove on a file and on a single line; Remove on a file asks first;
      "by hand" lines have no buttons; Unity recompiles after, and any leftover shows as a
      compile error.
- [ ] Metica SDK step removes old Metica from both Package Manager (`com.metica.unity`) and
      Assets, then adds 2.45.6, and the project compiles.
- [ ] MeticaSdk scene object warning shows, and clears once the object is deleted.

### GD SDK 5.0 – 5.2 (device)

- [ ] Interstitial, rewarded, banner and MREC show through Metica.
- [ ] Revenue events fire (Firebase, Adjust, AppMetrica).
- [ ] 5.0 consent patch: `MeticaConsentSettings` is in the consent list and consent reaches Metica.
- [ ] 5.0.0 – 5.0.1 banner (hand-ported, the riskiest): shows, hides, reloads, and respects
      other banners' priority.

### Standalone (device)

- [ ] Banner and MREC start at the positions set in MeticaAdsConfig.
- [ ] Metica banner auto-refreshes (no `startAutoRefresh … BANNER not found` in logcat), GD SDK
      and standalone.
- [ ] `MeticaAdsManager.LoadInterstitial()` / `LoadRewarded()` load an ad, and do nothing
      when one is already loaded.
- [ ] MREC with MRec Offset (e.g. Bottom, (0, -140)) lands in the same place as the game's
      MAX MREC, on Android and iOS; `RepositionMRec(position, offset)` moves it.

### Standalone game-code patches (a custom-SDK game with MAX)

- [ ] Init patch: pick script + function; UseMetica added; MAX → `_Max`, AdMob → `_AdMob`;
      compiles; with the switch on, Metica starts and MAX doesn't; off, MAX + AdMob as before.
- [ ] Interstitial / rewarded / banner / MREC patches: calls switch on UseMetica; the game's
      close and reward handlers run after a Metica ad (reward granted once, game resumes).
- [ ] Rename: ShowMax… → Show… with every caller; skipped and reported when wired in a scene.
- [ ] `isMaxInitialized`-style warnings appear where they should.
- [ ] Revenue step: subscription before Initialize, empty handler, warning while empty.
- [ ] Runtime: `ShowRewarded` onClosed after close and after a failed show (not compiled
      against the Metica SDK yet).

### Standalone revenue on the native thread

- [ ] Interstitial / rewarded revenue reaches Firebase, Adjust and AppMetrica while the ad is
      still open (check logcat timestamps vs ad close), with no "can only be called from the
      main thread" errors.
- [ ] Banner / MREC revenue still reported.

### Analytics Integration (custom SDK game, and a GD SDK 5.x game)

- [ ] Steps 1–3: Metica SDK (Android libraries resolved), abstractions (.tgz download →
      Packages/ + manifest), genre creator #v2.0.0; the project compiles after each; each
      commits on its own.
- [ ] "Pick .tgz instead…" works with a local copy.
- [ ] Step 4: Create stays disabled until the name is valid and Excel chosen; name error under
      the field while typing; files + `<Game>Analytics.asset` after the compile; App Id / Api
      Key / App Token written to the asset; Enable Metica Logs on.
- [ ] Step 4: game name prefilled from Player Settings (e.g. "Anti Stress Game" → AntiStressGame);
      Adjust App Token prefilled (GD: AdjustConfiguration; custom: AdjustConfig in code / Adjust
      prefab); Metica App ID / API Key prefilled from MeticaSettings (build target's platform) or
      MeticaAdsConfig; typing (even clearing) wins; values land in the asset.
- [ ] Secret fields show a Show / Hide text button (genre step and Metica settings step).
- [ ] Step 6, custom SDK: startup script written (or skipped when the game does it); on device,
      install once, sessionStart every launch.
- [ ] Step 6, GD SDK: no script — `<Game>Analytics.Initialize()` at the top of
      AnalyticsManager.Initialize(); an old MeticaAnalyticsStartup.cs is deleted; compiles.
- [ ] Step 5, GD SDK: install (first launch) and sessionStart in the GetAdid callback after
      UpdateAdjustInfo; a file wired by the earlier version gets just those lines; on device,
      install once, sessionStart every launch, both after the Adjust ID.
- [ ] Add Genre Creator's commit includes ProjectSettings.asset (METICA_ANALYTICS); no
      MeticaIntegrationBackups file left uncommitted.
- [ ] Step 5: GD SDK — AdjustAnalyticsNetwork.cs patched and compiles; custom SDK — the
      Adjust.InitSdk file found / picked; .asmdef reference added when inside one.
- [ ] On device: Adjust callbacks carry `muid`; the genre asset gets the Adjust ID.
- [ ] Step 7 passes only with Gradle 8.6, JDK 17, Kotlin and AGP 8.4.0 set.

## Known issues

- [ ] GD SDK v6 + Callback mode: the "Remove the unused async init path" step removes
      `IAsyncAdNetworkService` and the `AdNetworkController` async constructor while the
      SDK's own `AdNetworkMetica` still uses them, so the build breaks. `StillImplementing()`
      in `AsyncCleanupStep` checks Admob and AppLovin only, not Metica.
