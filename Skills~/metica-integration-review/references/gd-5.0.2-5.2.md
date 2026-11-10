# GD SDK 5.0.2 – 5.2.0

As `gd-5.3-5.5.md`, with the `Templates/Pre530/` wrapper files, because the SDK's ad-unit
classes differ. Read that file first; the differences:

- **No interstitial close callback**: `ShowInterstitial(string, Action)` doesn't exist the same
  way; the Metica interstitial matches this SDK's own AppLovin interstitial. Check how the game
  resumes after an interstitial (often a callback of its own or an `AdsManager` event) and that
  the Metica unit triggers the same thing.
- **No banner / MREC reposition**, and the MREC position is a `BannerPosition` (no `MRecPosition`).
- **No `ConsentManager.AddAndUpdateConsentService`**: the tool adds `new MeticaConsentSettings()`
  to `ConsentManager`'s fixed consent services list. Check it is in the list that is actually
  iterated, and that `MeticaConsentSettings` applies consent after `MeticaInitializer.IsInitialized`
  or before init as the template does.
- **`DelayedActionManager.Add(Action, float, bool ignoreTimeScale)`**: retries in the units pass
  `ignoreTimeScale`.
- Before 5.1.0 there is no `InApps` path in `MonetizationConfigurationsPath`; the Metica path is
  anchored after `AppMetrica`.
- Before 5.3.0 `MonetizationPreferences` has a one-argument `Preferences` constructor; an unset
  string reads as null, taken as `Unassigned`.
- 5.2.0 and older: no `MonetizationRemover.cs`, so no Remove SDK menu entry (fine).

Banner model (5.0.2+): `AdsManager.BannerStatus` + `AdsManager.ShowBanner()` on load, as in
5.3. Confirm `BannerStatus` is a field `AdsManager.ShowBanner` / `HideBanner` really set in
this project.
