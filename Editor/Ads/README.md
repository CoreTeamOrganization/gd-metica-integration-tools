# Metica Integration Tool

Editor wizard that adds **Metica for ads** (Smart Floors through MAX mediation) to a game
running GD Monetization SDK **v5.3.0 or newer**, or to a game with no GD SDK at all
(standalone). See [Supported GD SDK versions](#supported-gd-sdk-versions).

Open it from **GameDistrict → Metica → Metica Integration…**, then **Ads Integration** on
the Home screen.

The wrapper sources are taken from GDSDK `v6.2.4`.

## How it behaves

- **Verification is the source of truth.** Every step is re-checked against the real
  project each time the window is focused, so it is safe to close it, let Unity recompile,
  or come back next week.
- **Steps are gated twice.** A step stays locked until the one before it both *verifies*
  and is *signed off* by you. Once a step verifies it stops and shows a review panel — what
  to look for, and a `git diff` command scoped to exactly the files it touched, copyable to
  the clipboard. Nothing else runs until you press **Reviewed**.

  Sign-offs live in `EditorPrefs`, keyed per project, so they survive a domain reload and
  closing the window. They are a review record, not a substitute for verification: if a step
  later stops verifying, its sign-off is discarded and you review it again. **Reset
  sign-offs** in the header replays the whole run without touching the project.
- **Everything is idempotent.** Each source edit is guarded by a marker, so running a step
  twice changes nothing.
- **Nothing is guessed.** When a patch anchor is not found the step reports exactly what to
  add by hand instead of editing blind.
- **Originals are backed up** to `<project>/MeticaIntegrationBackups/` before the first
  change — outside `Assets`, so Unity never imports them.
- **One problem at a time.** A step shows a one-line summary, its first problem and short
  status notes. Any further problems and the longer explanation sit under a **Why?**
  foldout that starts closed.

## Two runs

The tool looks for the GD Monetization SDK and picks a run from what it finds.

**With the GD SDK** — nine steps, below (ten while AppLovin MAX is below 8.1.0). Metica is
wired into the ads layer that is already there.

**Without it** — four steps (five with the MAX step). There is nothing to patch, no remote flag
and no async path, so those steps do not exist. Instead the tool writes a self-contained Metica
ads runtime to `Assets/MeticaAds/`:

| # | Step | What it does |
|---|---|---|
| – | AppLovin MAX version | as below — only while MAX is below 8.1.0 |
| 1 | Metica SDK | as below, including resolving its Android libraries |
| 2 | Metica ads runtime | Writes fourteen files to `Assets/MeticaAds/` and waits for them to compile |
| 3 | Metica ads config | Creates `Resources/MeticaAdsConfig.asset` with the App ID and API Key typed into the step; the MAX SDK key is copied from AppLovinSettings |
| 4 | Dependencies & troubleshooting | as below |

The ad units, ad network and initializer are the GD SDK's, unchanged in everything that
faces Metica. What they leaned on the SDK for — the ad unit base classes, the logger,
`AdRevenueInfo`, the thread dispatcher — is written alongside as small stand-ins under the
same names, so the code reads the same in both places.

A game calls one class:

```csharp
MeticaAdsManager.Initialize();
MeticaAdsManager.ShowInterstitial("level_end");
MeticaAdsManager.ShowRewarded("double_coins", ok => { if (ok) Grant(); });
MeticaAdsManager.ShowBanner();
```

and edits one, `MeticaAdsHooks`, to plug its own analytics and consent in:

```csharp
MeticaAdsHooks.OnAdRevenue    = info => MyAnalytics.LogAdRevenue(info);
MeticaAdsHooks.HasUserConsent = () => MyConsent.PersonalizedAdsAllowed;
```

Both are optional — leave them unset and ads still serve.

**Metica is off by default.** `MeticaRemoteConfig` is the remote on/off switch: the config
asset is created with **Use Remote Switch** on and **Default Use Metica** off, so Metica
stays off until the game calls `MeticaRemoteConfig.Apply(true)`. Feed it from whatever remote
config the game already has:

```csharp
MeticaRemoteConfig.Apply(myRemoteConfig.GetBool("use_metica"));
```

The value applies from the **next** session — Metica initializes long before a remote fetch
returns, so the flag is persisted and read at boot, the same trade the GD SDK makes with
`MonetizationPreferences.UseMetica`. So a first install runs its first session without
Metica. Unticking **Use Remote Switch** makes Metica always on.

> **Warning:** if the game never calls `MeticaRemoteConfig.Apply`, Metica never runs.

**If the tool will not do it**, `Documentation/Metica-Standalone/` in the
`Monetization-SDK-Unity` repo has the same files as ordinary `.cs` (outside `Assets/`, so
Unity ignores them until you copy them in) next to a runbook for doing the whole standalone
integration by hand.

## The steps

| # | Step | What it does |
|---|---|---|
| – | AppLovin MAX version | **Only while MAX is missing or below 8.1.0**, which Metica needs; mandatory. Installs the MAX version set in `MeticaTargetVersion.asset` (**8.1.0** by default): AppLovin's own Unity plugin package from their GitHub releases. The old MAX is removed first, like the Metica SDK, keeping `Mediation/` (your adapters) and `AppLovinSettings.asset` (SDK key). Integration Manager as a fallback |
| 1 | Metica SDK | Downloads and imports the **pinned target version** from `meticalabs/metica-unity-package`. A project already on it is left alone; any other version is **removed first**, since importing over it leaves the old files behind. Then resolves: External Dependency Manager's Force Resolve pulls every Android library the project's SDKs declare into `mainTemplate.gradle` (turning Custom Main Gradle Template on if needed), and the step only passes once all of them are there. **Enable iOS** also enables the xcframework for iOS |
| 2 | Wrapper files | Writes `AdNetworkMetica` (plain `IAdNetworkService`), `MeticaInitializer` (callback-based init), the four ad units, `MeticaConfiguration`, `MeticaConsentSettings` |
| 3 | Patch the existing SDK files | Nine ads-layer edits: `AdPlatforms.METICA`, `Tag.Metica`, the settings resource path, `AdRevenueInfo.RevenuePayload`, `AdUnitsConfiguration.Metica`, the `UseMetica` preference and remote flag, the `AdsManager` network switch; plus a `METICA` case in `AdjustAnalyticsNetwork.GetAdSource` (`"applovin_max_sdk"`), without which Adjust drops Metica revenue. `AdNetworkController`, `AdNetworkAdmob` and `AdNetworkAppLovin` are untouched |
| 4 | Remote Metica switch | Points `AdsManager` at `MonetizationPreferences.UseMetica` instead of the build-time flag on `SDKConfiguration`, and drops the dead field. Only v6.0.0–v6.2.0 need it; a no-op everywhere else |
| 5 | Metica settings asset | Creates `MeticaSettings.asset` with the App ID and API Key typed into the step — Android, plus iOS when Enable iOS is on. Blank keys are a warning, never a block |
| 6 | Metica ad units | Copies the Applovin App Key and ad unit IDs into the Metica section — Metica runs through MAX, so they are the same values |
| 7 | Remove the unused async init path | Only bites on a project that already had the async Metica integration: strips `IAsyncAdNetworkService` and the `AdNetworkController` overload built for it. Leaves them if `AdNetworkAdmob` / `AdNetworkAppLovin` still implement the interface |
| 8 | Finish up | Teaches the Remove SDK menu about Metica. The Metica on/off switch stays on remote config — there is no local override |
| 9 | Dependencies & troubleshooting | One optional checklist, see below. Never blocks |

### Dependencies & troubleshooting

The Android build fixes Metica can need, one row each. A row is green when set and red when
not; click it for the controls that set it. None of them block the run — many projects build
without them.

| Row | Shows | Fix |
|---|---|---|
| Moloco | only if the project has Moloco | Fix Android / iOS adapter → 4.3.1.0 (rewrites `Dependencies.xml`; Resolve libraries fetches it) |
| Gradle 8.6 | always | **Download Gradle 8.6…**: pick a folder, the tool downloads `gradle-8.6-all.zip`, unzips it and points Unity at it with "Gradle installed with Unity" off. Or choose an existing folder. On Gradle 8+, also **Fix dexing property**: MAX 8.1.0 / 8.2.0 write `android.enableDexingArtifactTransform`, which Gradle 8 removed; it becomes `android.useFullClasspathForDexingTransform` (MAX 8.2.1+ already has it) |
| JDK 17 | always | Choose a JDK folder — `org.gradle.java.home` in `gradleTemplate.properties` |
| Kotlin | always | Add the Kotlin plugin and a matching AGP classpath to `baseProjectTemplate.gradle`, turning on Custom Base Gradle Template if needed |
| AGP 8.4.0 | always | Bump `com.android.application` / `library` to 8.4.0 (and an existing buildscript classpath with them) |

Gradle and JDK are editor preferences, per machine; nothing is committed for them.
An optional step has a **Skip this step** button that unlocks the next one without the step
verifying. The skip is remembered, reversible from the same button, and cleared by **Reset
sign-offs**. A step you have finished or skipped stays open: expand it any time to see
where it stands now and run its action again.

## Supported GD SDK versions

**5.3.0 and newer.** On an older version the Metica SDK step stops with "GD SDK 5.2.0 isn't
supported — needs 5.3.0+." and the Home card says the same; nothing is changed.

How that was checked, on every non-beta v5 release (stock copies in `Stock/`):

| GD SDK | Patches apply (in memory) | Compiles in Unity with the patches + wrapper files |
|---|---|---|
| 5.3.0 – 5.5.0 | ✅ all | ✅ 5.3.0, 5.3.1, 5.3.2, 5.3.3, 5.3.4, 5.3.5, 5.3.6, 5.4.0, 5.5.0 |
| 5.0.0 – 5.2.0 | ✅ all | ❌ the wrapper files — tested on 5.0.0 and 5.2.0 |

- **Patches apply**: every patch finds its anchor, the patch step's own check passes, and a
  second run changes nothing. Run by the real patch code on the stock copies, in memory.
- **Compiles**: each release exported from its tag, the patches and the GD wrapper files
  applied, Metica SDK 2.45.2 added, then compiled in Unity 2022.3.62f2 batchmode.
- **Why 5.3.0**: before it the GD SDK's ad-unit base classes are different — no
  interstitial close callback (`ShowInterstitial(string, Action)`), no banner
  `RepositionBanner` / `IsBannerActive`, no `MRecPosition` — so `MeticaInterstitial`,
  `MeticaBanner` and `MeticaMRec` do not compile. Supporting them would need a second set
  of wrapper files; no game has been seen below 5.3.1.

Version differences the patches handle on their own:

| Patch | Before | Instead |
|---|---|---|
| `PersistRemoteToggles` subscription | 5.3.6 | `OnFetchComplete` (no arguments) instead of `OnFetchCompleteWithSuccess` |
| MeticaSettings resource path | 5.1.0 | anchored after `AppMetrica`, since there is no `InApps` |
| `MonetizationPreferences.UseMetica` | 5.3.0 | anchored after `SessionCount`, one-argument `Preferences` constructor |
| `OnAdRevenuePaidEvent` invoked directly (an older `ThreadDispatcher` wrap is undone) | 5.3.0 | nothing to check — the event does not exist |
| Remove SDK menu (Finish up) | 5.2.0 | skipped — `MonetizationRemover.cs` does not exist |

## Getting the Metica SDK

Step 1 installs exactly one version: the one pinned in `MeticaTargetVersion.asset`. The
package ships a default. **Change target version…** copies it to
`Assets/MeticaIntegrationToolsSettings/` as a per-project override, since a git package
install is read-only.

The target has to be among the ten most recent releases at
`https://api.github.com/repos/meticalabs/metica-unity-package/releases?per_page=10`. Asset
names are not assumed — the first file ending in `.unitypackage` is downloaded and imported
interactively, so its contents are visible before anything is written.

The installed version comes from `Assets/MeticaSdk/package.json`. If it is not the target —
older or newer — step 1 refuses to import over it and offers to delete `Assets/MeticaSdk`
first: Unity merges a package import rather than replacing, so files dropped between
versions would survive and still compile.

## The tool never writes async

Metica's callback API matches `IAdNetworkService.Initialize(string, Action)`, so that is all
the tool ever writes. `IAsyncAdNetworkService` only existed because Metica shipped
`InitializeAsync` alone at first, and nothing needs it *added*:

| Project | Has Metica | Has async | Metica switch | What it needs |
|---|---|---|---|---|
| v5.0.0 – v5.5.0 | no | no | — | the full install (steps 1–4, 6–10) |
| v6.0.0 – v6.2.0 | yes | yes | build-time | the remote switch (step 5) |
| v6.2.1 – v6.2.4 | yes | yes | remote | nothing — every step verifies as already done |

Nothing routes on a version number. Each step reads the project and decides for itself, so a
fork sitting between two releases still lands in the right place.

So the only question about async is the reverse one — whether to convert plumbing a project
already has. It is asked only where async exists, and it changes exactly one step:

| Step 7 | Callback | Async |
|---|---|---|
| Async cleanup | removes `IAsyncAdNetworkService` and the `AdNetworkController` overload | leaves them alone |

Everything else is identical either way.

## Ads only

Nothing this tool writes or edits is an analytics file. No abstractions assembly, no
analytics network, no genre code, and the `METICA_ANALYTICS` define is never added — for
an ads-only integration it has to stay off.

That matters because the Metica SDK's own analytics code is gated on `METICA_ANALYTICS` and
needs the `MeticaAnalyticsAbstractions` assembly, which an ads-only install does not have.
Turning the define on there stops the Metica SDK compiling. Genre Creator's
`MeticaSymbolInstaller` (same package) therefore only adds it when **both** `Metica.SDK` and
`MeticaAnalyticsAbstractions` are present.

**Metica analytics** needs the `AnalyticsNetworkSO`, Genre and Bootstrap architectures
introduced in `v6.1`/`v6.2`, which a v5 project does not have — a full upgrade, not a port.
Afterwards, `Assets/GDMonetization/METICA_INTEGRATION.md` in `v6.2.x` is the guide.

## Where it lives

In the `com.gamedistrict.metica-integration-tools` package, under `Editor/Ads/`, compiled
into the package's **Editor-only** assembly (`GameDistrict.MeticaIntegrationTools.Editor`,
`autoReferenced` off), so it never reaches a build.

The Ads flow has no compile-time dependency on the GD SDK or the Metica SDK. Everything it
knows about GDMonetization it reads from disk or through `SerializedObject`, which is why
the package can be added long before the SDK is in the state it expects. It finds the SDK
by locating `Runtime/Scripts/Ads/Core/AdsManager.cs`, and finds its own `Templates/` through
`PackageInfo.FindForAssembly`, so neither folder is hardcoded.

## Removing the tool

**GameDistrict → Metica → Remove Integration Tools…**, or the same item in the window's ⋮
menu. It removes the package through Package Manager and deletes
`Assets/MeticaIntegrationToolsSettings/`. Everything the Ads flow wrote stays in the
project and needs nothing from the package at runtime.

It refuses while `Assets/MeticaGenres/` has genres: generated genre code inherits from the
package's `Runtime/` classes, so removing the package would break it.

## Layout

```
AdsFlow.cs                   the two step lists (GD SDK / standalone)
MeticaPatchSet.cs            every GD SDK edit, in step order (runs on disk or in memory)
ModifiedFileCheck.cs         stock / patched / modified / missing, per file
StockFiles.cs                reads Stock/ (the untouched GD SDK copies)
Stock/                       exported by Tools~/ExportStockFiles.ps1, .txt so they never compile
Steps/                       one file per step
Templates/                   wrapper sources, .cs.txt so they never compile from here
```

The window (`Editor/UI/`), the flow engine, `MeticaStep`, `MeticaPaths`, `SourcePatcher`,
`TemplateWriter` and the log live in the shared folders, since Genre Creator uses them too.

`Templates/AdNetworkMetica.cs.txt` and `Templates/MeticaInitializer.cs.txt` are the two
templates that differ from `v6.2.4`. That release predates Metica's callback-based init and
uses `MeticaSdk.InitializeAsync`, which is what forced `IAsyncAdNetworkService` and a second
`AdNetworkController` constructor into the SDK. These call
`MeticaSdk.Initialize(config, mediationInfo, callback)` instead, so `AdNetworkMetica`
implements the plain `IAdNetworkService` that Admob and AppLovin already use and the
initialization plumbing stays as it was.

`MeticaInitializer` keeps both a callback and a Task entry point behind one guard, so
whichever a caller uses, Metica is initialized exactly once per session.

## Running it on a project that already has Metica

Every step simply reports as already done. Nothing is overwritten.

## Background

`Documentation/Metica-Integration-For-GDSDK-v5.md` explains each step, and what to do when
an anchor does not match in a fork.

Metica's own requirements page, the source for the step 8 version floors:
<https://docs.metica.com/api/unity-sdk/unity-sdk-2>
