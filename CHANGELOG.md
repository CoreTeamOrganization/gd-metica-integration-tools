# Changelog

## Unreleased

- Repo created. Ads Integration wizard moved in from `Monetization-SDK-Unity`'s
  `Assets/MeticaIntegrationTool/`, repackaged as `com.gamedistrict.metica-integration-tools`.
  - `MeticaPaths.ToolRoot` now resolves via `PackageManager.PackageInfo.FindForAssembly`
    instead of scanning `Assets/` — required once the tool no longer lives there.
  - Target SDK version pinning now reads a per-project override
    (`Assets/MeticaIntegrationToolsSettings/MeticaTargetVersion.asset`) if the project has
    created one, falling back to the packaged default — a shared, often-read-only package
    install is the wrong place to hand-edit a per-project setting.
  - `RemoveToolStep` now removes the package via `Client.Remove(...)` (Package Manager)
    instead of `AssetDatabase.DeleteAsset` on its own folder — the old approach only made
    sense when the tool was a plain Assets folder. *(Since replaced by `ToolRemover`, below.)*
  - `GradleVersionStep`, `KotlinTemplateStep`, `GradleTemplateEditor` moved to
    `Editor/Shared/Gradle/` — shared with the Genre Creator flow once it lands, so only one
    place owns `baseProjectTemplate.gradle`.
- Genre Creator moved in from `gd-analytics-genre-creator`, verbatim — still its original
  ad-hoc window, not yet rebuilt as wizard steps.
  - `Runtime/` (`AnalyticsEventData`, `GDMeticaAnalytics`, and their asmdef) kept their
    exact original name, namespace (`GameDistrict.MeticaAnalytics`) and script GUIDs —
    existing games already have generated code and saved references against them.
  - Editor-only genre code (`GenreCreator/`, `MeticaSymbolInstaller`, `PerformanceTrackerPrompt`)
    renamed into the shared `GameDistrict.MeticaIntegrationTools` namespace — safe, since
    nothing outside the package references an editor script by name.
  - Added `GameDistrict.MeticaAnalytics.Runtime` to the Editor asmdef's references
    (`GenreExcelParser` reflects on `typeof(GDMeticaAnalytics)`), and
    `com.unity.nuget.newtonsoft-json` to `package.json`.
  - Menu moved from `GameDistrict/Metica Analytics/Create New Genre...` to
    `GameDistrict/Metica/Genre Creator...`, alongside Ads Integration.
  - **Known gap, not yet fixed:** `PerformanceTrackerPrompt`'s `[InitializeOnLoad]` popup
    now fires for Ads-only projects too, not just Genre Creator ones. Fold into a Genre
    wizard step when the window is rebuilt (next phase), instead of a package-wide prompt.

- Genre Creator's window rebuilt as wizard steps, matching Ads Integration's UX.
  - Extracted `MeticaStepWizardWindow` (`Editor/Shared/`) from `MeticaIntegrationWindow` —
    the generic verify/sign-off/review-gate/log engine, unchanged in behavior. Both windows
    are now this shell plus their own step list and header; `MeticaIntegrationWindow` itself
    was refactored onto it (mechanical extraction, not a rewrite — the Ads flow's own logic,
    including the GDSDK/standalone step-list switch, is unchanged).
  - New `GenreWizardWindow`, at `GameDistrict/Metica/Genre Creator...` (moved off
    `GenreCreatorWindow`, which keeps its logic but lost its menu item — it opens only from
    the wizard's last step now): `GradleVersionStep`, `GradleJdkStep` (new), `KotlinTemplateStep`
    — all shared with Ads — then `PerformanceTrackerStep`, then `GenreDefinitionStep`.
  - New `GradleJdkStep` (`Editor/Shared/Gradle/`): points `gradleTemplate.properties`'
    `org.gradle.java.home` at a JDK 17+ install, enabling Custom Gradle Properties Template
    the same way `KotlinTemplateStep` enables Custom Base Gradle Template — by copying
    Unity's own default rather than guessing `android.useAndroidX`/`enableJetifier`. Reads a
    JDK's version from its own `release` file, mirroring how `GradleVersionStep` reads
    Gradle's version from `gradle-launcher-*.jar` rather than invoking a binary.
  - **Fixed the known gap above:** `PerformanceTrackerPrompt`'s package-wide
    `[InitializeOnLoad]` popup is gone, replaced by `PerformanceTrackerStep` (Genre wizard
    only, explicit Verify/Apply, `Client.Add` deferred and progress-barred like
    `RemoveToolStep`'s `Client.Remove`).
  - New `GenreDefinitionStep`: not a one-time setup step like the ones before it (there is no
    "wrong" number of genres) — always verifies, and its button opens `GenreCreatorWindow`
    for as long as the game keeps adding genres.
  - **New known gap:** `MeticaSymbolInstaller` still runs as a package-wide
    `[InitializeOnLoad]` installer rather than a step. *(The claim first made here, that it
    was harmless for an Ads-only project, was wrong — see the fix below.)*

- Fixed: `MeticaSymbolInstaller` added `METICA_ANALYTICS` whenever `Metica.SDK` was loaded,
  so in an Ads-only project it would switch on the Metica SDK's own analytics code without
  the `MeticaAnalyticsAbstractions` assembly that code needs, and the Metica SDK stopped
  compiling. It now requires both assemblies.
- Fixed: `GenreExcelParser` lost its `using GameDistrict.MeticaAnalytics;` when editor code
  moved to the `GameDistrict.MeticaIntegrationTools` namespace, so `typeof(GDMeticaAnalytics)`
  no longer resolved and the whole Editor assembly failed to compile.
- Step messages trimmed, in both windows:
  - A step shows a one-line summary, its **first** problem only (with a "+N more" count),
    and short status notes.
  - New `MeticaStep.Why`: the longer explanation, shown with the remaining problems under a
    **Why?** foldout that starts closed. Every step's explanatory `HelpBox` moved there, so
    `DrawBody` is back to interactive controls only.
  - Steps that checked many things (patch core files, ad units, standalone runtime) now report
    one count line ("3 of 13 patches missing.") with the per-item lines under Why?.
- Tool removal is no longer a step at the end of the Ads flow (`RemoveToolStep` deleted).
  New `ToolRemover`: **GameDistrict → Metica → Remove Integration Tools…**, also in both
  windows' ⋮ tab menu (`MeticaStepWizardWindow` implements `IHasCustomMenu`). Refuses while
  `Assets/MeticaGenres/` has genres, since they inherit from `Runtime/`. Otherwise deletes
  `Assets/MeticaIntegrationToolsSettings/` and calls `Client.Remove`.
- New `PackageRequests.Track`: waits on a Package Manager request by polling from
  `EditorApplication.update`, the documented pattern, instead of a `Thread.Sleep` loop on the
  main thread. Used by `ToolRemover` and `PerformanceTrackerStep`.
- `Editor/Ads/README.md` brought up to date: menu path, pinned target version, MAX 8.1.0+ /
  Moloco 4.3.1+, the package location and tool removal.
- Fixed: added the 68 missing `.meta` files (every Ads/Shared script, the templates, the
  folders, `package.json`, the READMEs). A git-installed package is read-only, so Unity
  ignores any asset without a committed `.meta` — the Ads window would not have existed in a
  project that added this package by git URL. Fresh GUIDs; nothing references these by GUID.

- Stock copies of the GD SDK files the tool edits: `Tools~/ExportStockFiles.ps1` exports every
  non-beta v5.x.y release (15, v5.0.0 to v5.5.0) into `Editor/Ads/Stock/` — 49 unique files
  plus `manifest.json` (version → the Version string it reports → path → stored file).
- Modified-file check (`ModifiedFileCheck`, no UI yet): reads the GD SDK version from
  `MonetizationInitializeOnLoad.cs` (5.0.0 reports `"5.0.0-beta5"`) and classifies each file
  the tool patches as stock / patched / modified / missing, ignoring line endings and trailing
  whitespace. "Patched" is worked out by running the real patch code on the stock copy in
  memory (`SourcePatcher.InMemory`, `MeticaPatchSet`), so hand-written code that merely
  contains the tool's marker strings shows as modified. Wrapper files are compared with the
  tool's own templates; `MeticaAdsHooks.cs` is skipped, since it is meant to be edited.
- Fixed patching on GD SDK 5.0.0–5.2.0. Checked on all 15 releases in memory: every patch now
  applies, and a second run changes nothing.
  - `MonetizationConfigurationsPath`: anchors after `AppMetrica` when there is no `InApps`
    (5.0.x).
  - `MonetizationPreferences.UseMetica`: anchors after `SessionCount` when there is no
    `RestorePurchaseOnce`, and uses the one-argument `Preferences` constructor where that is
    all the SDK has (before 5.3.0; its `Get()` already defaults to false).
  - The `OnAdRevenuePaidEvent` main-thread dispatch is skipped, and no longer required by the
    patch step, where the event does not exist (before 5.3.0).
- Fixed: on GD SDK 5.0.0–5.3.5 the patch step wrote code that did not compile.
  `PersistRemoteToggles` subscribed to `RemoteConfigManager.OnFetchCompleteWithSuccess`, which
  only exists from 5.3.6. Before that it now subscribes to `OnFetchComplete` (no arguments)
  instead — the same event `CreateAndUpdateConfig` uses there. Found by compiling each release
  with the patches applied in Unity; the text-only anchor checks could not see it.
- One window instead of two, rebuilt with UI Toolkit to the new design (phase A):
  **GameDistrict → Metica → Metica Integration…** opens Home, which picks Ads Integration or
  Genre Creator. The old Ads Integration and Genre Creator menu items and windows are gone.
  - Header (Home, breadcrumb, GD Monetization SDK chip, ⋮ menu), stepper with done / current /
    review / skipped / locked dots (done and skipped steps can be revisited), one step per
    screen (problem line with "+N more", notes, action row with one yellow primary, Skip /
    Un-skip, Why?, review panel with the diff command), finished screen with skipped steps,
    footer (Re-check everything, Reveal backups, Reset sign-offs, the change log).
  - Step engine moved out of the window into `MeticaFlow` (no UI); `AdsFlow` and `GenreFlow`
    hold the step lists. Sign-offs are unchanged (same keys), so progress carries over.
  - The "Existing async init" dropdown moved from the window header into the patch step.
  - Steps' own controls are described, not drawn (`MeticaStep.Controls` → `StepButton`,
    `StepToggle`, `StepChoice`) and the window renders them in the theme: secondary
    buttons, a switch, a dropdown. `DrawBody` and IMGUI are gone from the steps.
  - "Change target version…" shows only until the Metica SDK is imported.
  - The review panel's diff command and Copy button are hidden for now
    (`ShowDiffCommand`, kept, not removed).
  - New tab icon: a black bolt on a yellow tile — the wide logo was unreadable at 16 px.
- The Metica key steps now take the keys in the step instead of leaving the asset empty:
  - Metica ads config (standalone): App ID and API Key fields, written into
    `MeticaAdsConfig.asset` on create; the MAX SDK key is copied from AppLovinSettings.
  - Metica settings asset (GD SDK): Android App ID / API Key, plus iOS when Enable iOS is on,
    written into `MeticaSettings.asset` on create.
  - After creating, the fields show the asset's values. The Create action disappears once
    the asset exists, and **Save keys** only shows while a field differs from the asset
    (re-checked as you type).
  - "Open …" buttons renamed **Select MeticaAdsConfig / MeticaSettings / AdUnitsSettings**;
    they select the asset and ping it in the Project window.
  - `MeticaAdsConfig.MeticaSdkLogging` now defaults to on (Metica's own SDK logs).
  - Blank keys show one warning line ("App ID and API Key are empty — fill them in before
    you build.") and never block the step. New `VerifyResult.Warning`, `StepText`,
    `AssetKeyFields`.
- New first step **AppLovin MAX version**, only while MAX is missing or below 8.1.0
  (`MeticaStep.Applies` leaves a step out of the run when it has nothing to do). Mandatory —
  the old "my Metica version supports this MAX" tick is gone.
  - Installs the MAX version in `MeticaTargetVersion.asset` (new `maxVersion`, 8.1.0 by
    default): AppLovin's Unity plugin package from their GitHub releases.
  - Two actions, like the Metica SDK: **Remove MAX x** first — keeping
    `Assets/MaxSdk/Mediation` (adapters) and `AppLovinSettings.asset` (SDK key) — then
    **Download and import MAX 8.1.0**, so each is its own change to review and commit.
- New last step **Dependencies & troubleshooting** replaces "Dependencies and Moloco version",
  "Gradle 8.6 or later" and "Kotlin in the base Gradle template" in both Ads runs. One optional
  checklist that never blocks; each row is green or red and opens to its fix (`StepItem`):
  - Moloco — only if the project has it; fix each platform's adapter to 4.3.1.0.
  - Gradle 8.6 — **Download Gradle 8.6…** picks a folder, downloads `gradle-8.6-all.zip`, unzips
    it and sets it in External Tools with "Gradle installed with Unity" off. On Gradle 8+,
    **Fix dexing property** changes MAX's `android.enableDexingArtifactTransform` (still in
    MAX 8.1.0 and 8.2.0) to `android.useFullClasspathForDexingTransform`.
  - JDK 17, Kotlin (added on its own now, `GradleTemplateEditor.AddKotlin`), AGP 8.4.0
    (`GradleTemplateEditor.BumpAgp`).
  - The Genre Creator flow keeps its separate Gradle / JDK / Kotlin steps until its rework.
- Resolving Android libraries is now part of each SDK step, not a step of its own ("Resolve
  libraries" removed). New `AndroidDependencies`: reads every `*Dependencies.xml` in Assets
  (Metica, MAX, mediation networks, Firebase…) and checks each declared `group:artifact` reached
  `mainTemplate.gradle` — generic, nothing hardcoded per SDK.
  - The **Metica SDK** and **AppLovin MAX** steps don't pass until every declared library is
    resolved; after an import their action becomes **Resolve Android dependencies** (EDM Force
    Resolve), turning Custom Main Gradle Template on first if it is off.
  - Metica SDK step also takes over **Enable iOS** and the AndroidX / Jetifier checks.
  - The MAX step stays in the run after an upgrade (until done), so the resolve happens there.
  - The troubleshooting step's Moloco row gains a Resolve button.
- `OnAdRevenuePaidEvent` stays invoked directly — the patch step no longer wraps it in
  `ThreadDispatcher.Enqueue`, and undoes that wrap where an earlier run added it (live lines only;
  a commented-out copy is left alone).
- Fixed: "Download Gradle 8.6…" unzipped Gradle but did not set it — the path was written through
  `AndroidExternalToolsSettings`, which did not persist it. The Gradle step now reads and writes
  Unity's own preferences (`GradleUseEmbedded`, `GradlePath`) and confirms the write.
- Fixed: the JDK row accepted any folder (it showed green with "version unknown" and wrote that
  folder into `org.gradle.java.home`, which breaks the build). A folder without `bin/java` is now
  refused, a bad `java.home` shows red, and an installed JDK 17+ (JAVA_HOME, External Tools,
  Adoptium / Oracle / Corretto / Microsoft / Zulu folders) is offered as **Use JDK … at …**.
- Moloco row: green or red by the active build target's platform only.
- API Key fields are masked, with an eye button to show them.
- Fixed: after Reviewed, the window could stop refreshing for good. Deferred work used
  `EditorApplication.delayCall`, which Unity drops when any other editor code's delayCall throws,
  and a "refresh queued" flag then stayed set. It now runs on the next `EditorApplication.update`.
- Fixed: a later SDK adding libraries un-did an earlier step's sign-off — each SDK step now only
  checks the libraries declared under its own folder (`Assets/MaxSdk`, `Assets/MeticaSdk`).
- GD SDK 5.3.0 is now the minimum (`GdSdkVersion`). On an older version the Metica SDK step
  stops with "GD SDK x isn't supported — needs 5.3.0+." and the Home card says so. The
  wrapper files do not compile against the 5.0–5.2 ad-unit base classes (checked in Unity);
  5.3.0–5.5.0 all compile with the patches and wrappers. Results in `Editor/Ads/README.md`.
- Fixed: Metica ad revenue never reached Adjust. `AdjustAnalyticsNetwork.GetAdSource` maps only
  APPLOVIN and ADMOB and returns null otherwise, so every Metica revenue event went out as
  `new AdjustAdRevenue(null)` and Adjust dropped it (seen in Draw-One-Puzzle: Adjust revenue
  below actual once Metica was on). The patch step now adds
  `AdPlatforms.METICA => "applovin_max_sdk",` after the APPLOVIN line — Metica mediates through
  MAX. The line is only added to the stock shape (APPLOVIN arm ahead of the `_ =>` default);
  otherwise the step says what to add by hand. The step's check fails while METICA has no
  source; any non-null mapping a game added itself counts as done, a null one does not.
  `AdjustAnalyticsNetwork.cs` added to the stock copies (3 variants across v5.0.0–v5.5.0).
