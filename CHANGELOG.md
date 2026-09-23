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
    sense when the tool was a plain Assets folder.
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
