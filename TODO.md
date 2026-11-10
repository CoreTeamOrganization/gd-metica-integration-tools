# TODO

## To build

Nothing queued.

## Test in Unity

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

### MainAdNetwork (GD SDK and standalone)

- [ ] GD SDK 5.x, fresh: Patch the existing SDK files compiles; first session logs
      "main AdNetwork (Unassigned)" and AppMetrica gets GameData { MainAdNetwork: Unassigned }.
- [ ] Firebase AdsSettings with "UseMetica": true → next session Metica, GameData "Metica";
      false → "MAX". AdsSettings without UseMetica → stays Unassigned.
- [ ] A 5.x game patched by the earlier tool (UseMetica bool), and a GD SDK 6.2.1+ game: the step
      shows the bool lines missing; Apply moves them; every user starts Unassigned.
- [ ] GD SDK 6.0–6.2.0: Remote Metica switch writes the MainAdNetwork read and a GameData event.
- [ ] Standalone: rewrite the runtime (Overwrite on); OnEvent gets GameData once per session;
      MeticaRemoteConfig.Apply(true) → next session Metica.
- [ ] Logs (GD SDK): after a fetch, "Remote Switch saved: Metica as pref" (or "Remote AdsSettings
      has no UseMetica, MainAdNetwork stays Unassigned"); next launch "Using Metica as main
      AdNetwork (Metica)". GD 6.x shows the value in brackets too. A project patched before the
      logs: the patch step flags them missing and adds them.

### GD SDK 5.0.0-beta5 banner (IsTopPriority never set)

- [ ] Copy of the beta5 game, already integrated: Wrapper files shows "MeticaBanner waits for
      IsTopPriority…"; running it rewrites only the load handler to `AdsManager.ShowBanner()`;
      compiles; Compare shows MeticaBanner as the tool's.
- [ ] Fresh beta5 copy: Wrapper files writes the NoPriority banner straight away.
- [ ] On device (beta5, Metica on): the banner shows on the first screen, AdMob's is hidden, the
      PADDownloader banner canvas turns on; it stays shown across auto-refreshes and hides on
      `AdsManager.HideBanner()`; with ads removed it never shows.
- [ ] Stock 5.0.0 / 5.0.1: still the IsTopPriority banner, nothing flagged.

### Metica ad units (GD SDK 6, per platform)

- [ ] iOS build target, only iOS IDs in Applovin: Copy fills Metica's iOS key and IDs, leaves
      Android as it was, and the step passes ("Checked for iOS").
- [ ] Android target: the same for Android; switch target to iOS → the step asks for the iOS copy.

### Integration review (optional last step, both runs)

- [ ] Claude Code CLI installed: "Build bundle and run review" writes
      `MeticaIntegrationReview/<time>/` (review.md, facts.json, sdk-diffs/, sdk-original/,
      git-diff.patch, git-log.txt), installs `.claude/skills/metica-integration-review`, shows
      "Claude is reviewing…", and turns into the findings when done (result.json read).
- [ ] A recompile while it runs: the step still shows it running and picks up result.json after.
- [ ] Blockers: each an open row (detail, fix, Open file); ticking Acknowledged passes the step
      once all are ticked; warnings never block. Stop the review kills it.
- [ ] No CLI: the step says so; Build bundle only copies the prompt; pasting it into Claude Code
      in the project folder writes result.json and the step picks it up.
- [ ] Compare with commit: a commit hash there is used as the git base; a bad one is reported.
- [ ] Beta5 copy with the old banner: the review reports the IsTopPriority blocker; unknown build
      warning shown on the step.
- [ ] OneLineDraw (5.4.0), a 6.x game and a standalone game: reviews finish with sensible
      findings (no false blockers on the tool's own patches).
- [ ] macOS: the CLI is found from a Hub-started Unity (short PATH).

### Game additions to carry over (GD SDK)

- [ ] OneLineDraw (GD 5.4.0): the step lists the AdsManager hooks, the Adjust
      `AdImpressionAppLovin` lines (adapted, `MeticaMaxAdInfo.From(meticaAd)`), the logs (tagged,
      unticked) and the edited rewarded-failed retry (by hand). Carry → it compiles, the lines sit
      where the hint said, `MeticaMaxAdInfo.cs` is written, and a second Carry changes nothing.
- [ ] An unmodified GD SDK: the step doesn't show.
- [ ] A game with a displayed handler (`OnAdDisplayedEvent`): the Metica unit gets an
      `On…ShowSuccessEvent` handler and its subscription.
- [ ] Untick everything but logs → passes; a code line left unticked → asks to tick or skip.
- [ ] Compare → Replace with original on an AppLovin unit with uncarried lines: the dialog warns.
- [ ] On device with Metica on: the hooks and the Adjust ad-revenue event fire.

### Commit the tool install (Home)

- [ ] Fresh install from the git URL: Home shows "Commit the tool install" with both files and
      "Add Metica Integration Tools 0.2.0 (<hash>)"; Commit → only those two files committed; the
      section goes away.
- [ ] Update (lock entry deleted, re-resolved): "Update Metica Integration Tools to …".
- [ ] Another package also uncommitted in manifest.json: it's listed, Commit asks first.
- [ ] Not a git repo / only someone else's package change: nothing shown.

### Performance events (Analytics)

- [ ] Add GD Performance Tracker → it installs, GD_PERFORMANCE_TRACKER shows on Android and iOS
      (from the genre creator; the row's button if not).
- [ ] Prefab row: Scene dropdown lists the enabled Build Settings scenes; Add puts GDPerfTracker in
      the boot scene without closing the open one; the row turns done.
- [ ] GD SDK 5.x: Add to RemoteConfigManager → compiles, ConfigurePerformanceTracking runs after the
      fetch (check the Console with the three Firebase keys set).
- [ ] loadTime / perfStats: pick a script + function → the calls land at its end and compile (a game
      asmdef gets the genre runtime reference); a value-returning function without a final return
      says to copy instead. Copy the code puts the snippet on the clipboard.
- [ ] On an Android device: loadTime and perfStats show in the Metica logs.

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

None open.
