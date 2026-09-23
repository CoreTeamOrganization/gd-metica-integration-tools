# GameDistrict Metica Genre Creator

Unity package for generating type-safe Metica analytics genre files from the Unity Editor — no boilerplate, no manual event wiring.

## Requirements

- Unity 2021.3+
- [Metica Unity SDK](https://github.com/meticalabs/metica-unity-sdk) installed in your project
- `com.unity.nuget.newtonsoft-json` 3.2.1 (pulled in automatically)
- [GD Performance Tracker](https://github.com/CoreTeamOrganization/GDPerformanceTracker.git) — required only if you log the `perfStats` / `loadTime` events (see [Performance Events](#performance-events))
- **Android builds only:** JDK 17+ and Gradle 8.6+ (Unity 2022.3 bundles Gradle 7.5.1 which is too old — see [Android Build Setup](#android-build-setup) below)

## Installation

**In a shipping game, pin a released tag** — see [CHANGELOG.md](CHANGELOG.md) before upgrading,
especially across a major version:

```json
"com.gamedistrict.metica-genre-creator": "https://github.com/CoreTeamOrganization/gd-analytics-genre-creator.git#v2.0.0"
```

For active co-development, a local reference works too, but never ships pinned to a version:

```json
"com.gamedistrict.metica-genre-creator": "file:../path/to/com.gamedistrict.metica-genre-creator"
```

Or place the folder inside your project's `Packages/` directory directly.

### Performance Events

`LogPerfStatsEvent` and `LogLoadTimeEvent` log payloads produced by [GD Performance Tracker](https://github.com/CoreTeamOrganization/GDPerformanceTracker.git). UPM does not resolve git URLs listed in a package's `dependencies`, so it ships as a separate package alongside this one.

The first time this package loads in a project without it, a popup offers to add it via `Client.Add` — click **Add Package**, or **Not Now** to skip (asked once per Editor session). To add it manually instead — **Package Manager → `+` → Add package from git URL…**: 

```
https://github.com/CoreTeamOrganization/GDPerformanceTracker.git
```

or add to `Packages/manifest.json`:

```json
"com.gamedistrict.performance-tracker": "https://github.com/CoreTeamOrganization/GDPerformanceTracker.git#v1.3.0"
```

Once installed, `GDMeticaAnalytics` wires the tracker's API for you — it fetches each payload
internally and skips logging when the tracker returns null (tracking off / nothing recorded):

```csharp
// Once, after remote config is fetched — overrides the GDPerfTracker prefab's Inspector defaults:
analytics.ConfigurePerformanceTracking(perfEnabled, sampleIntervalSeconds, startupEnabled);

// Once, the moment the game is genuinely playable:
analytics.MarkGameInteractive();

// At your chosen logging moment — customPayload adds game-context fields (taskId, day, ...):
analytics.LogPerfStatsEvent(customPayload);

// When logging cold start:
analytics.LogLoadTimeEvent(customPayload);
```

> If your game already calls `GDPerformance.ConsumePerfPayload()` / `GetStartupPayload()` itself
> and passes the result straight into `LogPerfStatsEvent` / `LogLoadTimeEvent`, don't upgrade to
> this version without updating that call site — these methods now fetch internally, so the
> caller's own fetch would consume the window first and the internal fetch would come back null.

> Both the `METICA_ANALYTICS` and `GD_PERFORMANCE_TRACKER` scripting define symbols are added
> automatically to Android and iOS as soon as each SDK is detected in the project.

---

## Android Build Setup

The Metica SDK requires a newer Android toolchain than Unity 2022.3 ships with. Complete all three steps before making an Android build.

### Prerequisites

- **JDK 17 or later** — download [AWS Corretto 17](https://aws.amazon.com/corretto/) or [Adoptium 17](https://adoptium.net/)
- **Gradle 8.6 or later** — download from [gradle.org/releases](https://gradle.org/releases/) and extract to a known location (e.g. `C:\gradle-8.6` on Windows or `~/gradle-8.6` on macOS)

### Step 1 — Point Unity to external Gradle

Unity 2022.3 bundles Gradle 7.5.1, which is incompatible. You must override it.

1. Open **Edit → Preferences → External Tools** (Windows) or **Unity → Preferences → External Tools** (macOS)
2. Uncheck **Gradle Installed with Unity (recommended)**
3. Set the path to your Gradle 8.6 installation directory

### Step 2 — Update `baseProjectTemplate.gradle`

1. In **Project Settings → Player → Android → Publishing Settings → Build**, enable **Custom Base Gradle Template**
2. Open the generated `Assets/Plugins/Android/baseProjectTemplate.gradle` and replace its contents with:

```groovy
buildscript {
    dependencies {
        classpath 'com.android.tools.build:gradle:8.4.0'
        classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:1.9.22'
    }
}
plugins {
    id 'com.android.application' version '8.4.0' apply false
    id 'com.android.library' version '8.4.0' apply false
    **BUILD_SCRIPT_DEPS**
}

task clean(type: Delete) {
    delete rootProject.buildDir
}
```

### Step 3 — Point Gradle to JDK 17

1. In **Project Settings → Player → Android → Publishing Settings → Build**, enable **Custom Gradle Properties Template**
2. Open the generated `Assets/Plugins/Android/gradleTemplate.properties` and add:

```properties
org.gradle.java.home=<PATH_TO_JDK_17>
```

Replace `<PATH_TO_JDK_17>` with the absolute path to your JDK 17 installation (e.g. `C:/Program Files/Amazon Corretto/jdk17.0.x_x` on Windows or `/Library/Java/JavaVirtualMachines/amazon-corretto-17.jdk/Contents/Home` on macOS). Use forward slashes even on Windows.

---

## Concepts

Read this section first if you have never integrated Metica before. These terms appear throughout the API and the Inspector.

### userId (Metica User ID)

The unique identifier Metica uses to track a player across sessions. It is sent with every event.

- Default: `SystemInfo.deviceUniqueIdentifier` — the device ID. Works out of the box with no setup.
- Override: if your game has an account system, pass your account or player ID via `Initialize(overrideUserId: myAccountId)`. This ties all events to the player rather than the device, which matters when a player reinstalls or switches devices.

### adId (Adjust Ad ID)

A per-user advertising identifier that the Adjust SDK resolves **asynchronously** after its own initialisation. It lets Metica link in-game analytics events back to the ad campaign that drove the install.

- This is *not* the same as your app token — it is different for every user.
- Because Adjust delivers this value via a callback, you set it after the fact with `UpdateAdjustInfo()` (see the Adjust Integration section below).

### appToken (Adjust App Token)

Your app's key on the Adjust dashboard — the same value for every user of your game. It is passed alongside `adId` so Metica knows which Adjust app the attribution data belongs to.

### Base fields — what is in every event payload

Every event your genre class sends (level started, level finished, etc.) automatically includes the following fields in its payload, merged in before the event is dispatched to Metica:

| Field | Value |
|---|---|
| `adid` | Adjust ad ID (empty string if not set) |
| `appToken` | Adjust app token (empty string if not set) |
| `abTest` | A/B test name (omitted if not set) |
| `abGroup` | A/B test variant (omitted if not set) |
| `abTestStartDate` | A/B test start date (omitted if not set) |

You never call `CreateBaseEvent()` directly — it is called internally by each generated event method. Understanding that these fields exist helps when you are inspecting Metica event logs or debugging missing attribution data.

### Custom fields — genre events vs. core Metica events

There are two different patterns for attaching extra data to events, depending on the event type:

**Genre events** (your generated methods) accept a typed data class that inherits `AnalyticsEventData`. That class has a `CustomFields` dictionary for ad-hoc additions without modifying the generated class:

```csharp
var data = new PuzzleLevelStarted(level: 3, levelType: "hard");
data.CustomFields = new Dictionary<string, object>
{
    { "difficulty_modifier", 2.5 },
    { "booster_active", true }
};
_analytics.LevelStarted(data);
```

**Core Metica events** (`LogPurchaseEvent`, `LogImpressionEvent`, `LogSessionStartEvent`, etc.) pre-date this abstraction and take a raw `Dictionary<string, object>? customPayload` parameter directly. They do not use `AnalyticsEventData`:

```csharp
_analytics.LogPurchaseEvent(
    productId:     "gems_pack_100",
    currency:      "USD",
    amount:        0.99,
    status:        "success",
    errorCode:     null,
    referenceId:   "txn_abc123",
    customPayload: new Dictionary<string, object> { { "source_screen", "shop" } }
);
```

---

## Quick Start

### 1. Create a Genre

Open **GameDistrict → Metica Analytics → Create New Genre…**

- **Manual mode**: type the genre name, add events and fields, click **Create Genre Files**.
- **Excel mode**: browse to an `.xlsx` schema file, pick a sheet, click **Import & Preview**, then **Create Genre Files**.

After Unity recompiles, the generated asset lands at:
```
Assets/MeticaGenres/Resources/{Genre}Analytics.asset
```

### 2. Configure the Analytics Asset

Select the generated `{Genre}Analytics` asset in the Project window.

| Inspector Field | Description |
|---|---|
| **App Id** | Your Metica application ID |
| **Api Key** | Your Metica API key |
| **User Id** | Leave empty to use `SystemInfo.deviceUniqueIdentifier` (recommended). Set this only if you want to hardcode a user ID in the Inspector, which is rarely needed. |
| **Ad Id** | Leave empty. Set at runtime via `UpdateAdjustInfo()` once Adjust provides it. |
| **App Token** | Your Adjust app token. Can be set here or passed to `UpdateAdjustInfo()`. |

### 3. Initialize at Startup

Call `Initialize()` once before logging any events. The Metica SDK is a singleton — initialising it more than once is safe but a no-op.

```csharp
[SerializeField] private PuzzleAnalytics _analytics;

void Awake()
{
    // Uses App Id, Api Key, and User Id from the Inspector
    _analytics.Initialize();

    // Or supply a userId at runtime (e.g. your account system's ID):
    _analytics.Initialize(overrideUserId: myAccountId);
}
```

### 4. Log Genre Events

```csharp
_analytics.LevelStarted(new PuzzleLevelStarted(level: 1, levelType: "normal"));

_analytics.LevelFinished(new PuzzleLevelFinished(
    level:       1,
    levelType:   "normal",
    levelStatus: "win",
    playTime:    45.2f
));
```

### 5. Log Core Metica Events

```csharp
_analytics.LogSessionStartEvent(null);

_analytics.LogPurchaseEvent(
    productId:   "gems_pack_100",
    currency:    "USD",
    amount:      0.99,
    status:      "success",
    errorCode:   null,
    referenceId: "txn_abc123",
    customPayload: null
);

_analytics.LogImpressionEvent(
    value:     0.05,
    type:      "rewarded",
    mediator:  "applovin",
    source:    "ironSource",
    placement: "LevelEnd",
    customPayload: null
);
```

---

## Accessing the Analytics Instance

### From a MonoBehaviour (Inspector reference)

The simplest approach. Wire the asset in the Inspector once per scene entry point (e.g. your GameManager):

```csharp
[SerializeField] private PuzzleAnalytics _analytics;

void Start()
{
    _analytics.Initialize();
    _analytics.LevelStarted(new PuzzleLevelStarted(1, "normal"));
}
```

### From anywhere — static access

Every generated class includes a static `Instance` property that loads the asset from the `Resources` folder and caches it. Use this from utility classes, static methods, or any code that cannot hold an Inspector reference:

```csharp
// No MonoBehaviour or Inspector reference needed
PuzzleAnalytics.Instance.LevelStarted(new PuzzleLevelStarted(1, "normal"));
```

The asset must exist at `Assets/MeticaGenres/Resources/PuzzleAnalytics.asset` (the Genre Creator places it there automatically). The first call to `Instance` loads it; subsequent calls use the cached reference.

> Initialize the analytics before calling `Instance` for the first time. The recommended place is still a scene entry-point MonoBehaviour that holds a serialised reference and calls `Initialize()` in `Awake()`. Static access is for callers further down the call stack.

---

## Adjust Integration

Adjust delivers the ad ID asynchronously via a callback. Register the callback after Adjust initialises and pass both values to `UpdateAdjustInfo()`:

```csharp
Adjust.GetAdid(adid =>
{
    _analytics.UpdateAdjustInfo(adid, adjustAppToken);
});
```

This updates the `adId` and `appToken` stored on the asset. All subsequent events will include the resolved values.

---

## A/B Test Fields

Set these on the analytics instance before logging events. They are merged into every event payload automatically.

```csharp
_analytics.AbTest          = "test_shop_v2";
_analytics.AbGroup         = "variant_b";
_analytics.AbTestStartDate = "2025-01-15";
```

---

## Custom Fields on Genre Events

Add arbitrary key-value pairs to any genre event without modifying the generated data class:

```csharp
var data = new PuzzleLevelStarted(level: 3, levelType: "hard");
data.CustomFields = new Dictionary<string, object>
{
    { "difficulty_modifier", 2.5 },
    { "booster_active", true }
};
_analytics.LevelStarted(data);
```

---

## File Structure (generated output)

For a genre named `Puzzle`:

```
Assets/MeticaGenres/
  Puzzle/
    IPuzzleAnalytics.cs     ← interface for the genre events
    PuzzleData.cs           ← event data classes (PuzzleLevelStarted, etc.)
    PuzzleAnalytics.cs      ← ScriptableObject implementation + static Instance
  Resources/
    PuzzleAnalytics.asset   ← configure this in the Inspector; accessed via Instance at runtime
```

---

## Excel Schema Format

Columns: `parameter`, `type` (string/int/float/bool), and one column per event name.

| parameter | type   | LevelStarted | LevelFinished |
|-----------|--------|-------------|---------------|
| Level     | int    | ✓           | ✓             |
| LevelType | string | ✓           | ✓             |
| Status    | string |             | ✓             |
| PlayTime  | float  |             | ✓             |

The genre name is inferred from the sheet name if the **Genre Name** field is left empty.