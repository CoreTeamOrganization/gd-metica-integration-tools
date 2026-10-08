# GameDistrict Metica Integration Tools

Editor tooling for Metica, as a Unity package. One window, **GameDistrict → Metica →
Metica Integration…**, runs the **Ads Integration** flow: it gets Metica ads into a project,
standalone or wired into the GD Monetization SDK. See [Editor/Ads/README.md](Editor/Ads/README.md).

A flow runs one step per screen: a stepper shows every step's state, a step unlocks once the
one before it verifies and is signed off, and done or skipped steps can be revisited. The
window is built with UI Toolkit to the design in `Editor/UI/` (theme: `MeticaTheme.uss`).

The Gradle/Kotlin/AGP logic lives in `Editor/Shared/Gradle/`, the one place that owns
`baseProjectTemplate.gradle`.

**Remove Integration Tools…** (same menu, the window's ⋮ menu, and its tab menu) removes the
package.

The Genre Creator (and its `Runtime/` classes, `GDMeticaAnalytics` / `AnalyticsEventData`)
was removed after `v0.1.0`. A game that has generated genres must stay on `#v0.1.0`.

## Installing

**Active development** (this project) — a local package reference, so edits here are
picked up immediately without cutting a release:

```json
"com.gamedistrict.metica-integration-tools": "file:../../gd-metica-integration-tools"
```

**Shipping game** — pin a released tag (latest: `v0.1.0`):

```json
"com.gamedistrict.metica-integration-tools": "https://github.com/CoreTeamOrganization/gd-metica-integration-tools.git#v0.1.0"
```

## Why a package, not an Assets folder

The tool used to live at `Assets/MeticaIntegrationTool/` in the GD Monetization SDK repo,
copied by hand into every consuming project — including re-copying by hand on every
change. One versioned package fixes that: every project points at the same source.

## Structure

```
Editor/
  GameDistrict.MeticaIntegrationTools.Editor.asmdef
  UI/
    MeticaIntegrationWindow  the one window: Home, stepper, one step per screen, review panel,
                             Why?, finished screen, footer, ⋮ menu (UI Toolkit)
    MeticaTheme.uss          the design's tokens and component classes
    MiIcon                   the design's line icons, drawn from their SVG path data
    SdkCompareWindow         Compare with original GD SDK: changed files and their diffs
  Shared/
    MeticaFlow               one run's engine, no UI: verify, sign off, skip, current step
    MeticaStep, MeticaPaths, SourcePatcher, TemplateWriter, the log/progress stores
    StepCommit               commits one step's changed files to the game's git repo
    LineDiff                 line diff (Myers) and hunks, for the compare window
    ToolRemover              removes the package (menu item + ⋮ menu)
    PackageRequests          waits on Package Manager requests without blocking the editor
    Gradle/                GradleVersionStep, KotlinTemplateStep, GradleTemplateEditor,
                            GradleJdkStep — used by the Troubleshooting step; one place owns
                            baseProjectTemplate.gradle and gradleTemplate.properties
  Ads/                     AdsFlow (its two step lists) + steps, templates, stock GD SDK copies
```

