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
- `RemoteConfiguration.UseMetica` (part of the `AdsSettings` JSON) and in `RemoteConfigManager`
  a `PersistRemoteToggles` handler on the fetch-complete event: if the fetched `AdsSettings`
  contains `"UseMetica"`, store `Metica` or `MAX` and log `Remote Switch saved: … as pref`;
  otherwise log `… MainAdNetwork stays …` and store nothing.
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
