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
- `RemoteConfigManager.PersistRemoteToggles` calls `PersistMainAdNetwork`, which stores what the
  fetched `AdsSettings`' `"MainAdNetwork"` string says (`"Unassigned"` / `"MAX"` / `"Metica"`, any
  casing; anything else refused with an error log). The SDK ships the old `"UseMetica"` code
  itself, so the patch step asks here even on a brand-new game: **Keep both** (`"UseMetica"` still
  read when `MainAdNetwork` isn't there, through `EarlierUseMetica`) or **Implement new**
  (`MainAdNetwork` only), and **Parameter** or **JSON** for where the values are. Until both are
  picked the SDK's own line stays. See `gd-5.3-5.5.md` for the states, the details and the logs.
- Every user starts `Unassigned` after the update (the bool is not carried over — by design).
- The **stock** SDK (not patched by the tool) reads only `"UseMetica"`: a Firebase set up with
  `"MainAdNetwork"` does nothing there.

Everything else (units, network, settings) is the SDK's own; the tool's `MeticaInitializer` is
skipped (6.2.x has `Analytics.MeticaInitializer`).

## What to check

- No reader of the old `MonetizationPreferences.UseMetica` bool is left anywhere (AdsManager,
  game code, analytics) — a leftover reader keeps using a value nothing writes any more.
- `PersistRemoteToggles` is subscribed to the fetch event this game raises, and the SDK's own
  writer of the bool (if still present) doesn't fight it.
- The key the game's Firebase uses is one this build reads: `"MainAdNetwork"` needs
  `PersistMainAdNetwork` in `RemoteConfigManager`; `"UseMetica"` needs the SDK's own line still
  there, or `EarlierUseMetica` (Keep both). With Implement new, `"UseMetica"` is ignored.
- 6.2.2+ ships its own copy of the analytics abstractions; `METICA_ANALYTICS` there is the SDK's
  business — don't flag it unless the abstractions are missing.
- AdUnitsSettings Metica section: build target platform set.
- The SDK's Metica units against the game's AppLovin units (game additions).
