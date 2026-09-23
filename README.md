# GameDistrict Metica Integration Tools

Editor tooling for Metica, as a Unity package. Two independent wizards, picked one at a
time from **GameDistrict → Metica**:

- **Ads Integration…** — gets Metica ads into a project, standalone or wired into the GD
  Monetization SDK. See [Editor/Ads/README.md](Editor/Ads/README.md).
- **Genre Creator…** — a setup wizard (Android toolchain, scripting define, optional
  Performance Tracker package), whose last step opens the actual genre-authoring window —
  moved in from `gd-analytics-genre-creator`, unchanged. See
  [Editor/Genre/README.md](Editor/Genre/README.md).

Both flows share one Gradle/Kotlin/AGP layer (`Editor/Shared/Gradle/`), so there is a
single place that owns `baseProjectTemplate.gradle`, not two.

## Installing

**Active development** (this project) — a local package reference, so edits here are
picked up immediately without cutting a release:

```json
"com.gamedistrict.metica-integration-tools": "file:../../gd-metica-integration-tools"
```

**Shipping game** — pin a released tag once one exists:

```json
"com.gamedistrict.metica-integration-tools": "https://github.com/CoreTeamOrganization/gd-metica-integration-tools.git#v1.0.0"
```

## Why a package, not an Assets folder

The tool used to live at `Assets/MeticaIntegrationTool/` in the GD Monetization SDK repo,
copied by hand into every consuming project — including re-copying by hand on every
change. One versioned package fixes that: every project points at the same source.

## Structure

```
Editor/
  GameDistrict.MeticaIntegrationTools.Editor.asmdef   references GameDistrict.MeticaAnalytics.Runtime
  Shared/
    MeticaStepWizardWindow   the generic wizard shell (verify, sign off, review gate, log) —
                             both windows below are this plus their own step list and header
    MeticaStep, MeticaPaths, SourcePatcher, TemplateWriter, the log/progress stores
    Gradle/                GradleVersionStep, KotlinTemplateStep, GradleTemplateEditor,
                            GradleJdkStep — used by both flows, so one place owns
                            baseProjectTemplate.gradle and gradleTemplate.properties, not two
  Ads/                     MeticaIntegrationWindow + its steps/templates
  Genre/
    GenreWizardWindow      the setup wizard: shared Gradle steps, MeticaSymbolInstaller
                           (still an [InitializeOnLoad] installer, not a step — see below),
                           PerformanceTrackerStep, GenreDefinitionStep
    GenreCreator/          the actual genre-authoring window and codegen engine, unchanged —
                           opened by GenreDefinitionStep, no menu item of its own
Runtime/
  GameDistrict.MeticaAnalytics.Runtime.asmdef   Genre Creator's base classes (AnalyticsEventData,
                                                 GDMeticaAnalytics) — name, namespace and script
                                                 GUIDs kept exactly as in gd-analytics-genre-creator,
                                                 since existing games already generated code and
                                                 saved references against them
```

## A deliberate asymmetry: two namespaces, not one

Everything under `Editor/` shares one namespace, `GameDistrict.MeticaIntegrationTools` —
that part *was* renamed freely, since nothing outside this package references an
editor-only script by name or GUID.

`Runtime/` is different on purpose: it kept its original name, namespace
(`GameDistrict.MeticaAnalytics`) and script GUIDs unchanged. Every game that already used
Genre Creator has generated `.cs` files that inherit from these classes by name, and
scenes/prefabs/ScriptableObjects that reference them by GUID. Renaming either would break
every one of those on update. `GenreCodeGenerator` also still emits `GameDistrict.MeticaAnalytics`
into newly generated genre files — a hardcoded string constant, unaffected by anything the
*editor* side is called.

## Known gap, not yet addressed

`MeticaSymbolInstaller` still runs on every Editor load (`[InitializeOnLoad]`) and silently
sets `METICA_ANALYTICS` / `GD_PERFORMANCE_TRACKER` scripting defines once it detects the
matching SDK. Unlike the old `PerformanceTrackerPrompt` (now `PerformanceTrackerStep`, fixed
in the move to Genre wizard steps), this one never shows a dialog and only ever adds a
define — harmless for an Ads-only project, just not yet made explicit the way everything
else in this package is. Left alone for now since it does not actually bother anyone; worth
converting to a step later for consistency.
