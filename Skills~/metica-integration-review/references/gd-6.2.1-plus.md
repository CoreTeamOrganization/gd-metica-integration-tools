# GD SDK 6.2.1 and newer

These releases ship Metica **and** a remote switch: a `UseMetica` bool preference
(`MonetizationPreferences.UseMetica`) written from remote config and read in `AdsManager`
(`var useMetica = MonetizationPreferences.UseMetica.Get();`, log `Using … as main AdNetwork`,
`GameData { UseMetica }`).

## What the tool does here

The patch step moves it to the three-state `MainAdNetwork` (`Unassigned` / `MAX` / `Metica`):

- `AdsManager` reads `MonetizationPreferences.MainAdNetwork.Get()` (empty → `Unassigned`),
  `useMetica = mainAdNetwork == "Metica"`, the log gets `(mainAdNetwork)`, `GameData` sends
  `MainAdNetwork`.
- `RemoteConfigManager.PersistRemoteToggles` stores `Metica` / `MAX` only when the fetched
  `AdsSettings` contains `"UseMetica"`, and logs it.
- Every user starts `Unassigned` after the update (the bool is not carried over — by design).

Everything else (units, network, settings) is the SDK's own; the tool's `MeticaInitializer` is
skipped (6.2.x has `Analytics.MeticaInitializer`).

## What to check

- No reader of the old `MonetizationPreferences.UseMetica` bool is left anywhere (AdsManager,
  game code, analytics) — a leftover reader keeps using a value nothing writes any more.
- `PersistRemoteToggles` is subscribed to the fetch event this game raises, and the SDK's own
  writer of the bool (if still present) doesn't fight it.
- 6.2.2+ ships its own copy of the analytics abstractions; `METICA_ANALYTICS` there is the SDK's
  business — don't flag it unless the abstractions are missing.
- AdUnitsSettings Metica section: build target platform set.
- The SDK's Metica units against the game's AppLovin units (game additions).
