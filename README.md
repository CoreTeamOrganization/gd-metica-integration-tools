# GameDistrict Metica Integration Tools

Editor tooling for Metica, as a Unity package. Two independent wizards, picked one at a
time from **GameDistrict → Metica**:

- **Ads Integration…** — gets Metica ads into a project, standalone or wired into the GD
  Monetization SDK. See [Editor/Ads/README.md](Editor/Ads/README.md).
- **Genre Creator…** — generates type-safe Metica analytics genre files. Moved in from
  `gd-analytics-genre-creator`; still the original ad-hoc window for now, not yet rebuilt
  as wizard steps. See [Editor/Genre/README.md](Editor/Genre/README.md).

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
  Shared/            MeticaStep, MeticaPaths, SourcePatcher, TemplateWriter, the log/progress
                      stores, and Gradle/ (GradleVersionStep, KotlinTemplateStep,
                      GradleTemplateEditor) — used by both flows
  Ads/               the Ads Integration wizard and its steps/templates
  Genre/             the Genre Creator wizard: GenreCreator/ (the codegen engine, untouched),
                      MeticaSymbolInstaller, PerformanceTrackerPrompt — still the original
                      window, not yet wizard steps
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

## Known gap from the move (not yet addressed)

`PerformanceTrackerPrompt` runs on every Editor load (`[InitializeOnLoad]`) and offers to
install GD Performance Tracker if it's missing. That was fine as a genre-creator-only
package; now that Ads Integration ships in the same package, a project using *only* the Ads
flow gets this prompt too, for a dependency it has no use for. Planned fix: fold this into
a proper Genre wizard step (see CHANGELOG.md) instead of a package-wide auto-prompt, when
the Genre Creator window gets rebuilt as steps.
