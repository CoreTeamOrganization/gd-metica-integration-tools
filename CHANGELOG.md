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
