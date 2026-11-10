# Shared checklist

Go through every line for every integration. Each one is a question about **this project's
code**; answer it from the code, with file:line.

## Startup and the switch

- [ ] Exactly one path initializes Metica (one `AdNetworkMetica` in use, one initializer, one
      `MeticaSdk.Initialize…` call reachable). No Metica v1 init left.
- [ ] When the switch says Metica, AppLovin MAX is **not** initialized for the same formats; when
      it says MAX or Unassigned, Metica is not initialized at all.
- [ ] The switch is read once, at ads start, from a stored value; the remote value is stored
      only when the fetched config actually has the key (users outside the test stay
      Unassigned → MAX).
- [ ] The stored value is written by the remote-config code that really runs in this game
      (the fetch callback the game uses, not one it replaced or never subscribes to).
- [ ] `GameData` { `MainAdNetwork` } (or the game's equivalent) is sent once per session.

## Every format (interstitial, rewarded, banner, MREC)

- [ ] The game's show call reaches the Metica unit when Metica is on (no AppLovin-only branch
      in `AdsManager` or game code that bypasses the network list — `is ApplovinX`,
      `AdPlatforms.APPLOVIN`, `MaxSdk.` calls in game code, `adUnits.Applovin` reads).
- [ ] The ad unit ID the Metica unit uses is the build target's and is set (AdUnitsSettings
      Metica section / MeticaAdsConfig). Same value as AppLovin's.
- [ ] Load → loaded callback → ready check (`IsReady` / `HasX`) agree; a failed load retries
      the way the AppLovin unit does.
- [ ] Every value the unit waits for has a writer on the Metica path (see SKILL.md, step 2).
- [ ] Callbacks hop to the main thread before touching Unity (`ThreadDispatcher.Enqueue` or the
      game's own) — except revenue for interstitial/rewarded, which is reported at once by design.
- [ ] Every callback the AppLovin unit handles has a Metica counterpart doing the same game
      work, including lines the game added (analytics, Adjust, AppMetrica, UI canvases, timers,
      `AdsManager.ExtendAppOpenTime`, pause/resume, audio).

### Interstitial / rewarded

- [ ] The game's close callback runs exactly once after a Metica ad — after close **and** after
      a failed show — so the game resumes.
- [ ] Rewarded: the reward is granted once, only on the reward event, and the game's reward
      callback gets the same argument shape it gets from AppLovin.
- [ ] The next ad loads after a show (or the unit reloads on close like the AppLovin one).

### Banner / MREC

- [ ] A banner requested before it loaded shows when it loads (the beta5 bug), and a banner hidden
      by the game stays hidden when a refresh loads.
- [ ] Whatever decides which network's banner is visible (`AdsManager.ShowBanner`, priorities,
      `HideAllBannersExcept`, a game-made ordering) includes the Metica banner and hides the
      AppLovin one.
- [ ] Ads removed / no-ads purchase: the Metica banner and MREC respect the same check the
      AppLovin ones do.
- [ ] Position and size match what the game sets for AppLovin; reposition calls reach Metica
      where the SDK supports it.
- [ ] Auto-refresh is started once; hide does not destroy the only instance if the unit can't
      recreate it.

## Revenue, consent, analytics

- [ ] Metica revenue reaches the same analytics the AppLovin revenue does (Firebase, Adjust,
      AppMetrica — whatever the AppLovin path calls), with `AdPlatforms.METICA` (GD SDK) and
      Adjust's ad source mapped (`applovin_max_sdk`).
- [ ] Consent: Metica gets the user's consent before init (GD SDK: `MeticaConsentSettings` in the
      consent services, or `MeticaAdsHooks.HasUserConsent` standalone).
- [ ] No double-counting: revenue isn't reported both from the Metica unit and from a game hook.

## Settings and build

- [ ] AdUnitsSettings Metica section: App Key + the four IDs set for the build target's
      platform; no App-Open entry. MeticaSettings: App ID + API Key for that platform.
- [ ] No `METICA_ANALYTICS` define for an ads-only integration (it breaks the Metica SDK
      compile without the abstractions assembly).
- [ ] Every step's own check in `review.md` passes; any that fails or is skipped is explained.

## What the integration removed or changed

- [ ] Every line removed or commented out (SDK and game) — was anything relying on it?
- [ ] Every "By tool: no" SDK diff — does it change behaviour Metica depends on?
- [ ] Code inside `#if` that only one platform compiles — is the build target's branch right?
