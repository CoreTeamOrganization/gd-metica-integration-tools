# Metica Integration Tool

Editor wizard that adds **Metica for ads** (Smart Floors through MAX mediation) to a game
running GD Monetization SDK **v5.x or older**. Every patch anchor was verified across
`v5.3.4`–`v5.5.0`.

Open it from **GameDistrict → Monetization → Metica Integration Tool**.

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

**If the tool will not do it**, `Documentation/Metica-Standalone/` has the same thirteen
files as ordinary `.cs` (outside `Assets/`, so Unity ignores them until you copy them in)
next to a runbook for doing the whole standalone integration by hand.

## The steps

| # | Step | What it does |
|---|---|---|
| 1 | Metica SDK | Lists the **ten most recent** releases from `meticalabs/metica-unity-package`, downloads and imports the chosen one. A project already on one of those ten is left alone; anything older is **removed first**, since importing over it leaves the old files behind. Also drops `Metica.SDK.asmdef`'s reference to the analytics abstractions assembly that an ads-only install does not have |
| 2 | Resolve libraries | Runs the Android resolver so `com.metica:metica-sdk` reaches Gradle, and enables the xcframework for iOS |
| 3 | Wrapper files | Writes `AdNetworkMetica` (plain `IAdNetworkService`), `MeticaInitializer` (callback-based init), the four ad units, `MeticaConfiguration`, `MeticaConsentSettings` |
| 4 | Patch the existing SDK files | Nine ads-layer edits: `AdPlatforms.METICA`, `Tag.Metica`, the settings resource path, `AdRevenueInfo.RevenuePayload`, `AdUnitsConfiguration.Metica`, the `UseMetica` preference and remote flag, the `AdsManager` network switch. `AdNetworkController`, `AdNetworkAdmob` and `AdNetworkAppLovin` are untouched |
| 5 | Remote Metica switch | Points `AdsManager` at `MonetizationPreferences.UseMetica` instead of the build-time flag on `SDKConfiguration`, and drops the dead field. Only v6.0.0–v6.2.0 need it; a no-op everywhere else |
| 6 | Metica settings asset | Creates `MeticaSettings.asset`, empty. The API Key / App ID are per-game — the developer fills them |
| 7 | Metica ad units | Copies the Applovin App Key and ad unit IDs into the Metica section — Metica runs through MAX, so they are the same values |
| 8 | Remove the unused async init path | Only bites on a project that already had the async Metica integration: strips `IAsyncAdNetworkService` and the `AdNetworkController` overload built for it. Leaves them if `AdNetworkAdmob` / `AdNetworkAppLovin` still implement the interface |
| 9 | Finish up | Teaches the Remove SDK menu about Metica, sets the local `UseMetica` default, lists what is left |
| 10 | Dependencies and Moloco version | Checks the floors Metica publishes: AppLovin MAX Unity plugin **8.2.0+**, Moloco SDK and adapters **4.3.0+** |
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

## Getting the Metica SDK

Step 1 reads `https://api.github.com/repos/meticalabs/metica-unity-package/releases?per_page=10`
and offers those ten. Asset names are not assumed — whatever a release attaches, the first
file ending in `.unitypackage` is taken, and a release with no such asset is still listed so
its page can be opened and the download done by hand.

The installed version comes from `Assets/MeticaSdk/package.json`. If it is not one of the
ten, step 1 refuses to import over it and offers to delete `Assets/MeticaSdk` first: Unity
merges a package import rather than replacing, so files dropped between versions would
survive and still compile.

If the release list cannot be fetched, the installed version is treated as fine. A network
hiccup should not turn into a demand to delete the SDK.

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

That has one consequence worth knowing: `Metica.SDK.asmdef` references the
`MeticaAnalyticsAbstractions` assembly, and Unity fails the whole assembly when a
referenced assembly is missing. Step 1 detects that case and rewrites the asmdef without
the reference (original backed up first). If your Metica package ships its own abstractions
assembly, the reference resolves and step 1 leaves it alone.

**Metica analytics** needs the `AnalyticsNetworkSO`, Genre and Bootstrap architectures
introduced in `v6.1`/`v6.2`, which a v5 project does not have — a full upgrade, not a port.
Afterwards, `Assets/GDMonetization/METICA_INTEGRATION.md` in `v6.2.x` is the guide.

## Where it lives

`Assets/MeticaIntegrationTool/` — deliberately outside `Assets/GDMonetization/`, so it
exports and imports as a standalone package without dragging the SDK along, and so removing
the SDK does not remove the tool.

It carries its own assembly definition, `GDMonetization.MeticaIntegration.Editor.asmdef`,
marked **Editor-only** with **no references**. Two things follow from that:

- **It compiles wherever you put it.** Without an asmdef, editor code has to sit under a
  folder literally named `Editor`, or `UnityEditor` will not resolve — `Assembly-CSharp`
  does not reference it. The asmdef removes that constraint. If you ever delete the asmdef,
  move the folder under an `Editor/` one.
- **It never reaches a build.** Editor-only, and `autoReferenced` is off, so no game
  assembly can accidentally take a dependency on it.

The tool has no compile-time dependency on the SDK at all — only `System.*`, `UnityEditor`
and `UnityEngine`. Everything it knows about GDMonetization it reads from disk or through
`SerializedObject`, which is why it can be imported into a project long before the SDK is
in the state it expects. It finds the SDK by locating
`Runtime/Scripts/Ads/Core/AdsManager.cs`, and finds its own `Templates/` by locating
`MeticaIntegrationWindow.cs`, so neither folder is hardcoded.

## Layout

```
GDMonetization.MeticaIntegration.Editor.asmdef   Editor-only, no references
MeticaIntegrationWindow.cs   the wizard: ordering, gating, drawing
MeticaStep.cs                step contract + VerifyResult
MeticaPaths.cs               path discovery (the GD root is found, not hardcoded)
SourcePatcher.cs             anchored, idempotent, backed-up C# edits
TemplateWriter.cs            copies Templates/*.cs.txt into place
MeticaIntegrationLog.cs      running record of what changed
Steps/                       one file per step
Templates/                   wrapper sources, .cs.txt so they never compile from here
```

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
