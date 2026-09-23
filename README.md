# GameDistrict Metica Integration Tools

Editor tooling for Metica, as a Unity package. Two independent wizards, picked one at a
time from **GameDistrict → Metica**:

- **Ads Integration…** — gets Metica ads into a project, standalone or wired into the GD
  Monetization SDK. See [Editor/Ads/README.md](Editor/Ads/README.md).
- **Genre Creator…** — generates type-safe Metica analytics genre files. *(not yet moved
  in — see CHANGELOG.md)*

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
  GameDistrict.MeticaIntegrationTools.Editor.asmdef
  MeticaToolsMenu.cs
  Shared/            MeticaStep, MeticaPaths, SourcePatcher, TemplateWriter, the log/progress
                      stores, and Gradle/ (GradleVersionStep, KotlinTemplateStep,
                      GradleTemplateEditor) — used by both flows
  Ads/               the Ads Integration wizard and its steps/templates
  Genre/             the Genre Creator wizard and its steps (pending)
Runtime/
  GameDistrict.MeticaIntegrationTools.Runtime.asmdef   (pending — Genre Creator's base classes)
```
