# GD SDK 6.0.0 – 6.2.0

These releases ship Metica themselves: the SDK's own `AdNetworkMetica` (async,
`IAsyncAdNetworkService`, `MeticaSdk.InitializeAsync`), its own Metica units, the Metica section
in `AdUnitsConfiguration` (per-platform: `AndroidAppKey` / `iOSAppKey`, unit `Android` / `iOS`),
and `MeticaSettings`. The switch, though, is **build-time**: a `UseMetica` flag on
`SDKConfiguration`.

## What the tool does here

- **Remote Metica switch** step: points `AdsManager` at `MonetizationPreferences.MainAdNetwork`
  (read once at ads init, Metica only for `Metica`), adds the `GameData { MainAdNetwork }` event
  and the `(mainAdNetwork)` in the "as main AdNetwork" log, and drops the dead `SDKConfiguration`
  field. The patch step adds `MainAdNetwork`, `RemoteConfiguration.UseMetica` and
  `PersistRemoteToggles` as in 5.x.
- The tool's `MeticaInitializer` must **not** exist next to the SDK's async `AdNetworkMetica` —
  it would shadow `Analytics.MeticaInitializer` (6.2.x) or is unused (6.0.x initializes inline).
  The wrapper step skips it and fails if one is there.
- Wrapper files are not written over the SDK's own units.

## What to check

- Nothing still reads the build-time flag (`SDKConfiguration.UseMetica`,
  `m_SdkConfiguration.UseMetica`) — every reader moved to the stored `MainAdNetwork`.
- Only one `AdNetworkMetica` / `MeticaInitializer` type is reachable from `AdsManager` (no
  tool-written copy in `Monetization.Runtime.Ads` shadowing the SDK's).
- The async init path the SDK uses completes and the units are created after it (the SDK's own
  design — check the game didn't change `AdNetworkController` or the async overload).
- AdUnitsSettings: the Metica section's **build target** platform fields set.
- The SDK's Metica units against the game's AppLovin units: game additions to the AppLovin
  units that the SDK's Metica units lack.
