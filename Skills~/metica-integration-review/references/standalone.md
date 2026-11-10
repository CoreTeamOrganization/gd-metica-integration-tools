# Standalone (no GD SDK)

The game has its own ads code calling AppLovin MAX directly. The tool wrote a self-contained
Metica runtime to `Assets/MeticaAds/` and (optionally) patched the game's own ads code.

## The runtime

- `MeticaAdsManager` — the one class the game calls: `Initialize()`, `ShowInterstitial(placement,
  onClosed)`, `ShowRewarded(placement, onComplete, onClosed)`, `ShowBanner()` / `HideBanner()`,
  `ShowMRec()` / `HideMRec()`, `HasInterstitial(bool)` / `HasRewarded(bool)`, `LoadInterstitial()` /
  `LoadRewarded()`, `RepositionBanner` / `RepositionMRec`.
- `MeticaAdsHooks` — the game's plug points, **meant to be edited**: `OnAdRevenue`,
  `HasUserConsent`, `OnEvent`. Revenue for interstitial/rewarded arrives on Metica's native
  thread (by design, so it is sent even if the app dies before close); banner/MREC on the main
  thread.
- `MeticaRemoteConfig` — the switch. Config asset `Resources/MeticaAdsConfig.asset`: **Use
  Remote Switch** on and **Default Use Metica** off by default, so Metica is off until the game
  calls `MeticaRemoteConfig.Apply(true)`; the value applies from the **next** session.
  `ShouldInitialize(config)` is what the patched init reads. Stored as `MainAdNetwork`
  (`Unassigned` until `Apply`), reported once per session through `MeticaAdsHooks.OnEvent` as
  `GameData { MainAdNetwork }`.
- Stand-ins for the GD SDK pieces the units lean on (`AdUnitBase`, logger, `AdRevenueInfo`,
  `ThreadDispatcher` via `MeticaAdsRunner`).

## The game-code patches (optional steps)

- **Init**: in the function that starts MAX, `public static bool UseMetica` set first from
  `MeticaRemoteConfig.ShouldInitialize(MeticaAdsConfig.Load())`; MAX statements moved to
  `<Function>_Max()`, AdMob to `<Function>_AdMob()`; then
  `if (UseMetica) { MeticaAdsManager.Initialize(); <F>_AdMob(); } else { <F>_Max(); <F>_AdMob(); }`.
- **Formats**: ready checks become `(UseMetica ? MeticaAdsManager.HasX(false) : MaxSdk.IsXReady(id))`;
  each run of MAX show calls becomes `if (UseMetica) { Metica } else { MAX }`. Metica's close /
  reward callbacks call the game's own MAX `OnAdHiddenEvent` / `OnAdReceivedRewardEvent` handlers.
  Create / destroy / position / refresh calls stay MAX-only. Load, display and click callbacks are
  **not** mapped.
- **Revenue**: `MeticaAdsHooks.OnAdRevenue += OnMeticaAdRevenue;` before `Initialize()`, and an
  empty `OnMeticaAdRevenue` with a TODO.

## What to check, hard

1. **`MeticaRemoteConfig.Apply` is called somewhere that runs** — from the game's remote config,
   only when the key exists. If nothing calls it and Use Remote Switch is on → **blocker**:
   Metica never runs.
2. `UseMetica` is set before any patched show/ready check can run, and nothing else writes it.
3. With `UseMetica` true, **no** MAX init and no MAX show for the four formats; with it false, no
   Metica. Search game code for `MaxSdk.` calls the patches did not cover (other scripts,
   scenes' UnityEvents, prefabs) — they would run MAX while Metica is on, or skip Metica.
4. Things the patches left for a hand edit (reported as warnings in the step checks in
   `review.md`): code in `#if`, non-void or coroutine functions, shared locals, renamed
   functions wired in scenes.
5. The game's hidden / reward handlers called from Metica's callbacks do the right thing when
   called that way: they don't read MAX's `AdInfo` / ad unit ID arguments in a way that breaks,
   and the reward is granted once.
6. `isMaxInitialized`-style flags guarding a patched call — with Metica on they stay false and
   block the Metica call.
7. Load / display / click work the game does in MAX callbacks (analytics, preloading the next
   ad, pausing audio) that now has no Metica counterpart.
8. `OnMeticaAdRevenue` is not empty — or the game reports revenue some other way; otherwise
   Metica revenue reaches no analytics (**blocker** if the MAX path reports revenue).
9. `MeticaAdsHooks.OnEvent` is wired before ads start, if the game wants `GameData`.
10. Consent: `HasUserConsent` reflects the game's consent flow.
11. `MeticaAdsConfig` has the App ID / API Key and the MAX SDK key; the ad unit IDs match MAX's
    for the build target.
