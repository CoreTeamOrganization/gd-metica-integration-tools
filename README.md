# GameDistrict Metica Integration Tools

Editor tooling for Metica, as a Unity package. One window, **GameDistrict → Metica →
Metica Integration…**, whose Home screen picks one of two flows:

- **Ads Integration** — gets Metica ads into a project, standalone or wired into the GD
  Monetization SDK. See [Editor/Ads/README.md](Editor/Ads/README.md).
- **Analytics Integration** — for GD SDK below 6.2.0 or a custom SDK: installs the Metica
  SDK, analytics abstractions and the genre creator, makes the genre files from the game name
  and Excel schema, ties Adjust to Metica, and the required Android build setup. See
  [Editor/Analytics/README.md](Editor/Analytics/README.md).

A flow runs one step per screen: a stepper shows every step's state, a step unlocks once the
one before it verifies and is signed off, and done or skipped steps can be revisited. The
window is built with UI Toolkit to the design in `Editor/UI/` (theme: `MeticaTheme.uss`).

The Gradle/Kotlin/AGP logic lives in `Editor/Shared/Gradle/`, the one place that owns
`baseProjectTemplate.gradle`.

**Remove Integration Tools…** (same menu, the window's ⋮ menu, and its tab menu) removes the
package.

The Genre Creator that used to be bundled here (with its `Runtime/` classes) was removed after
`v0.1.0`; Analytics Integration now installs the standalone genre creator package instead.

## Installing

In Unity: **Window → Package Manager → + → Add package from git URL…**, and paste:

```
https://github.com/CoreTeamOrganization/gd-metica-integration-tools.git
```

Or add the line to `Packages/manifest.json`:

```json
"com.gamedistrict.metica-integration-tools": "https://github.com/CoreTeamOrganization/gd-metica-integration-tools.git",
```

That is the `main` branch — always the latest tool. Unity needs git on the PATH and access to
the CoreTeamOrganization GitHub org.

**Updating.** Unity pins the commit it installed in `Packages/packages-lock.json`. To get the
latest, delete the `com.gamedistrict.metica-integration-tools` entry there and return to Unity.

**Version settings.** The Metica SDK and AppLovin MAX versions the tool installs come from the
package's `Editor/Ads/MeticaTargetVersion.asset`. A game can override them with its own
`Assets/MeticaIntegrationToolsSettings/MeticaTargetVersion.asset` (**Change target version…** in
the Metica SDK step); delete that file to follow the tool's versions again.

**Tool development** — a local package reference, so edits are picked up without pushing:

```json
"com.gamedistrict.metica-integration-tools": "file:C:/path/to/gd-metica-integration-tools",
```

`#v0.1.0` is the old tool (with the bundled Genre Creator); `#v1-migration` was the development
branch and now matches `main`.

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
    StepCommit               commits one step's changed files to the game's git repo, and pushes on request
    LineDiff                 line diff (Myers) and hunks, for the compare window
    ToolRemover              removes the package (menu item + ⋮ menu)
    PackageRequests          waits on Package Manager requests without blocking the editor
    Gradle/                GradleVersionStep, KotlinTemplateStep, GradleTemplateEditor,
                            GradleJdkStep, AndroidBuildRows — the Troubleshooting step and the
                            Analytics build setup; one place owns baseProjectTemplate.gradle and
                            gradleTemplate.properties
  Ads/                     AdsFlow (its two step lists) + steps, templates, stock GD SDK copies
  Analytics/               AnalyticsFlow + steps, the abstractions and genre creator packages
```

