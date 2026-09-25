# GameDistrict Metica Integration Tools

Editor tooling for Metica, as a Unity package. One window, **GameDistrict → Metica →
Metica Integration…**, whose Home screen picks one of two independent flows:

- **Ads Integration** — gets Metica ads into a project, standalone or wired into the GD
  Monetization SDK. See [Editor/Ads/README.md](Editor/Ads/README.md).
- **Genre Creator** — a setup flow (Android toolchain, optional Performance Tracker
  package), whose last step opens the actual genre-authoring window — moved in from
  `gd-analytics-genre-creator`, unchanged. See [Editor/Genre/README.md](Editor/Genre/README.md).

A flow runs one step per screen: a stepper shows every step's state, a step unlocks once the
one before it verifies and is signed off, and done or skipped steps can be revisited. The
window is built with UI Toolkit to the design in `Editor/UI/` (theme: `MeticaTheme.uss`).

Both flows share one Gradle/Kotlin/AGP layer (`Editor/Shared/Gradle/`), so there is a
single place that owns `baseProjectTemplate.gradle`, not two.

**Remove Integration Tools…** (same menu, the window's ⋮ menu, and its tab menu) removes the
package. It refuses while the project has generated genres, since those inherit from `Runtime/`.

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
  UI/
    MeticaIntegrationWindow  the one window: Home, stepper, one step per screen, review panel,
                             Why?, finished screen, footer, ⋮ menu (UI Toolkit)
    MeticaTheme.uss          the design's tokens and component classes
    MiIcon                   the design's line icons, drawn from their SVG path data
  Shared/
    MeticaFlow               one run's engine, no UI: verify, sign off, skip, current step
    MeticaStep, MeticaPaths, SourcePatcher, TemplateWriter, the log/progress stores
    ToolRemover              removes the package (menu item + ⋮ menu)
    PackageRequests          waits on Package Manager requests without blocking the editor
    Gradle/                GradleVersionStep, KotlinTemplateStep, GradleTemplateEditor,
                            GradleJdkStep — used by both flows, so one place owns
                            baseProjectTemplate.gradle and gradleTemplate.properties, not two
  Ads/                     AdsFlow (its two step lists) + steps, templates, stock GD SDK copies
  Genre/
    GenreFlow              the setup flow: shared Gradle steps, PerformanceTrackerStep,
                           GenreDefinitionStep
    MeticaSymbolInstaller  [InitializeOnLoad] define installer, not a step — see below
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

## MeticaSymbolInstaller

Runs on every Editor load (`[InitializeOnLoad]`) and adds scripting defines for Android and
iOS:

- `METICA_ANALYTICS` — only when **both** `Metica.SDK` and `MeticaAnalyticsAbstractions`
  are loaded. The Metica SDK's own analytics code is gated on this define and needs the
  abstractions assembly, so adding it to an Ads-only project (Metica SDK, no abstractions)
  would stop the Metica SDK compiling. An earlier version checked `Metica.SDK` alone and did
  exactly that.
- `GD_PERFORMANCE_TRACKER` — when `GDPerformanceTracker.Runtime` is loaded.

It never removes a define. Still not a wizard step; worth converting later for consistency.
