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

**With the GD SDK** — twelve steps, below. Metica is wired into the ads layer that is
already there.

**Without it** — seven steps. There is nothing to patch, no remote flag and no async path,
so those four steps do not exist. Instead the tool writes a self-contained Metica ads
runtime to `Assets/MeticaAds/`:

| # | Step | What it does |
|---|---|---|
| 1 | Metica SDK | as below |
| 2 | Resolve libraries | as below |
| 3 | Metica ads runtime | Writes fourteen files to `Assets/MeticaAds/` and waits for them to compile |
| 4 | Metica ads config | Creates `Resources/MeticaAdsConfig.asset` and waits while you fill in the keys |
| 5 | Dependencies and Moloco version | as below |
| 6 | Gradle 8.6 or later *(optional)* | as below |
| 7 | Kotlin in the base Gradle template *(optional)* | as below |

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

`MeticaRemoteConfig` is the remote on/off switch, off unless **Use Remote Switch** is ticked
in the config. Feed it from whatever remote config the game already has:

```csharp
MeticaRemoteConfig.Apply(myRemoteConfig.GetBool("use_metica"));
```

The value applies to the **next** session — Metica initializes long before a remote fetch
returns, so the flag is persisted and read at boot, the same trade the GD SDK makes with
`MonetizationPreferences.UseMetica`.

**If the tool will not do it**, `Documentation/Metica-Standalone/` in the
`Monetization-SDK-Unity` repo has the same files as ordinary `.cs` (outside `Assets/`, so
Unity ignores them until you copy them in) next to a runbook for doing the whole standalone
integration by hand.

## The steps

| # | Step | What it does |
|---|---|---|
| 1 | Metica SDK | Downloads and imports the **pinned target version** from `meticalabs/metica-unity-package`. A project already on it is left alone; any other version is **removed first**, since importing over it leaves the old files behind |
| 2 | Resolve libraries | Runs the Android resolver (Force Resolve) so `com.metica:metica-sdk` reaches Gradle. With **Enable iOS** ticked, also enables the xcframework for iOS |
| 3 | Wrapper files | Writes `AdNetworkMetica` (plain `IAdNetworkService`), `MeticaInitializer` (callback-based init), the four ad units, `MeticaConfiguration`, `MeticaConsentSettings` |
| 4 | Patch the existing SDK files | Nine ads-layer edits: `AdPlatforms.METICA`, `Tag.Metica`, the settings resource path, `AdRevenueInfo.RevenuePayload`, `AdUnitsConfiguration.Metica`, the `UseMetica` preference and remote flag, the `AdsManager` network switch. `AdNetworkController`, `AdNetworkAdmob` and `AdNetworkAppLovin` are untouched |
| 5 | Remote Metica switch | Points `AdsManager` at `MonetizationPreferences.UseMetica` instead of the build-time flag on `SDKConfiguration`, and drops the dead field. Only v6.0.0–v6.2.0 need it; a no-op everywhere else |
| 6 | Metica settings asset | Creates `MeticaSettings.asset`, empty. The API Key / App ID are per-game — the developer fills them |
| 7 | Metica ad units | Copies the Applovin App Key and ad unit IDs into the Metica section — Metica runs through MAX, so they are the same values |
| 8 | Remove the unused async init path | Only bites on a project that already had the async Metica integration: strips `IAsyncAdNetworkService` and the `AdNetworkController` overload built for it. Leaves them if `AdNetworkAdmob` / `AdNetworkAppLovin` still implement the interface |
| 9 | Finish up | Teaches the Remove SDK menu about Metica. The Metica on/off switch stays on remote config — there is no local override |
| 10 | Dependencies and Moloco version | Checks the floors Metica publishes: AppLovin MAX Unity plugin **8.1.0+**, Moloco SDK and adapters **4.3.1+**. Per-platform **Fix Moloco** buttons rewrite the declared version in `Dependencies.xml`; MAX is upgraded in AppLovin's Integration Manager |
| 11 | Gradle 8.6 or later *(optional)* | Metica needs Gradle 8.6+; Unity 2022.3 bundles 7.5.1. Reads the editor's Android Gradle preference, works out the version in use, and can point it at a folder you choose. An editor preference, not a project setting — per machine, never committed |
| 12 | Kotlin in the base Gradle template *(optional)* | Enables Custom Base Gradle Template if it is off (by copying Unity's own default, which carries the right AGP version), then writes the `buildscript` block above `plugins` with the Kotlin plugin classpath and an AGP classpath matching `com.android.application` |

Steps 11 and 12 are the two halves of Metica's own Gradle setup, and both are **optional**.
They sit last on purpose: neither can really be judged until there is an Android build to
judge, so the run reaches them once the integration is in place and a build has been tried,
rather than stopping on them beforehand.

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
| `OnAdRevenuePaidEvent` main-thread dispatch | 5.3.0 | skipped — the event does not exist |
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
