# GD SDK 5.3.0 – 5.5.0

The reference integration. GD SDK root: usually `Assets/GDMonetization`, scripts under
`Runtime/Scripts`. Metica is wired into the existing ads layer, next to AppLovin.

## What the tool wrote

`Runtime/Scripts/Ads/Metica/`: `AdNetworkMetica` (plain `IAdNetworkService`, callback init),
`MeticaInitializer` (one guarded `InitializeAdsWithCallback`), `MeticaInterstitial`,
`MeticaRewarded`, `MeticaBanner`, `MeticaMRec`; `Configurations/MeticaConfiguration.cs`;
`Consent/Services/MeticaConsentSettings.cs`. Possibly `MeticaMaxAdInfo.cs` when game additions
used MAX's ad info.

## What the tool patched

- `AdPlatforms.METICA`, `Tag.Metica`, `MonetizationConfigurationsPath` (`Configurations/MeticaSettings`).
- `AdRevenueInfo.RevenuePayload` (+ constructor parameter) — Metica's payload rides along.
- `AdUnitsConfiguration.Metica` section.
- `MonetizationPreferences.MainAdNetwork` (`Unassigned` / `MAX` / `Metica`) and the enum.
- The remote switch, in `RemoteConfigManager`: a `PersistRemoteToggles` handler on the
  fetch-complete event calls `PersistMainAdNetwork`. That reads `"MainAdNetwork"` — a **string**,
  one of the `MainAdNetwork` enum's names (`"Unassigned"`, `"MAX"`, `"Metica"`), matched ignoring
  case and outer spaces — and stores the enum's own spelling, logging `Remote Switch saved: … as
  pref`. Where it reads it from was picked in the patch step (**Remote value**):
  - **JSON**: `PersistMainAdNetwork(GetRemoteValue<string>(REMOTE_KEY).Value)` reads it out of the
    fetched `AdsSettings` JSON.
  - **Parameter**: `PersistMainAdNetwork()` reads the Firebase parameter `MainAdNetwork`
    (`GetRemoteValue<string>("MainAdNetwork")`), only when `Source == FetchSource.RemoteValue` —
    an in-app default doesn't count. Logs say "Remote config … parameter" instead of "Remote
    AdsSettings". Check the game's Firebase really has a parameter there, not the JSON field
    (you can't see Firebase: say which one this build expects, as an **info** finding).
  - `"Unassigned"` is stored too: it takes the device back out of the test.
  - A value that is none of the three is refused: nothing stored, error log
    `Remote MainAdNetwork "…" is not one of Unassigned, MAX, Metica, MainAdNetwork stays …`.
  - Without the key: nothing stored, log `Remote AdsSettings has no MainAdNetwork, MainAdNetwork stays …`.
  - JSON: `RemoteConfiguration` has an empty `public string MainAdNetwork;` (tooltip: set it in
    Firebase, not here), so DefaultRemoteSettings / ClipboardJson carry the key. A **value** in the
    DefaultRemoteSettings asset is a **warning**: it is the in-app AdsSettings, saved on every fetch
    while Firebase has none. Parameter: no such field.
  The project is in one of three states — say which in an **info** finding:
  - **New**: only the above. `"UseMetica"` in Firebase does nothing here.
  - **Keep both**: `PersistMainAdNetwork` has one more line,
    `if (string.IsNullOrWhiteSpace(remote)) remote = EarlierUseMetica(adsSettings);` (Parameter:
    `EarlierUseMetica()`), and there is an `EarlierUseMetica` method. JSON: it reads
    `RemoteConfiguration.UseMetica` (only when the fetched text has the key) and logs `Remote
    AdsSettings has the old UseMetica key, not MainAdNetwork: …`; `RemoteConfiguration` must have
    `public bool UseMetica` — without it this doesn't compile. Parameter: it reads the Boolean
    parameter `UseMetica`, only from the fetch. So `UseMetica` still counts when `MainAdNetwork`
    isn't there; with both, `MainAdNetwork` wins.
  - **Old**: `PersistRemoteToggles` has the lines inline and reads only `UseMetica` (or, earlier
    still, `MonetizationPreferences.UseMetica.Set(...)`). Fine for a game whose Firebase uses
    `UseMetica`, but it ignores `"MainAdNetwork"`. You can't see the game's Firebase, so report it
    as a **warning**: this build only understands `"UseMetica": true / false`; the patch step
    offers Keep both or Implement new (and Parameter or JSON) to move it.
- `AdsManager`: builds `metica = new AdNetworkController({MeticaInterstitial, MeticaRewarded,
  MeticaBanner, MeticaMRec}, new AdNetworkMetica(), adUnits.Metica)`, reads `MainAdNetwork` once,
  `useMetica = mainAdNetwork == "Metica"`, `adNetworks = useMetica ? {metica, admob} :
  {applovin, admob}`, logs `Using … as main AdNetwork (…)`, sends `GameData { MainAdNetwork }`.
- `AdjustAnalyticsNetwork.GetAdSource`: `AdPlatforms.METICA => "applovin_max_sdk"`.
- `ConsentManager`: Metica consent via `AddAndUpdateConsentService` (in `AdNetworkMetica`).
- Remove SDK menu lists the Metica folders.

## How formats work here

- `AdsManager` loops over `adNetworks` in order and uses the first network whose unit for the
  format is ready (`TryGetAdType` + the unit's ready check). Metica sits where AppLovin was.
- Banner: `AdsManager.ShowBanner()` sets `AdsManager.BannerStatus = true` and shows the first
  ready banner; `HideBanner` sets it false. `MeticaBanner.OnBannerLoadedEvent`: if
  `AdsManager.BannerStatus` → `AdsManager.ShowBanner()`, else `HideBanner()`. Check the game has
  not changed `ShowBanner` / `HideBanner` so that `BannerStatus` is no longer set (e.g. an early
  `return` placed before the assignment for some case that should still show).
- MREC: the same with `AdsManager.MRecStatus` / `ShowMRec`.
- Interstitial: `ShowInterstitial(placement, onClosed)`; the Metica unit calls `onClosed` on
  hidden and on show failure.
- Rewarded: reward on Metica's reward event, close callback on hidden.

## Typical game changes to look for

- Lines added to `ApplovinInterstitial` / `ApplovinRewarded` / `ApplovinBanner` / `ApplovinMRec`
  (Adjust `AdImpressionAppLovin`, AppMetrica events, custom logs) — the "Game additions" step
  carries ticked ones; unticked code lines are gaps.
- `AdsManager` edits: ads-removed checks, canvas toggles, custom first-interstitial logic,
  analytics around show calls — check they treat Metica like AppLovin.
- Before 5.3.6 the fetch event is `OnFetchComplete` (no arguments); after, `OnFetchCompleteWithSuccess`.
  The `PersistRemoteToggles` subscription must be on the one this game raises.
