# GD SDK 5.0.0 – 5.0.1

Everything in `gd-5.0.2-5.2.md` applies (Pre530 wrappers), plus a **different banner model**,
which is where this release goes wrong.

## The banner model

- `BannerAdUnit` has `public bool IsTopPriority`, `protected int bannerPriority`, a per-unit
  `bannerStatus`, `isLoaded`, and `GetPriority()`.
- Stock `AdsManager.ShowBanner()` collects the banners whose `HasBanner()` is true, orders them
  by `GetPriority()` and sets `bannerAd.IsTopPriority = (i == 0)` on each, then shows the top
  one. `AdsManager.BannerStatus` is private. `HideAllBannersExcept(BannerAdUnit)` hides the rest.
- Priorities: AdMob collapsible 0 (or 3), AppLovin 1, AdMob 2. The Metica banner takes 1, in
  AppLovin's place.
- `HasBanner()` sets the unit's `bannerStatus = true` (the banner is wanted), creates/loads it
  if needed and returns `isLoaded`.

## The tool's banner (two variants)

`review.md` says which template `MeticaBanner.cs` came from:

- `Pre530/MeticaBanner.Before502.cs.txt` — for a stock `AdsManager` that sets `IsTopPriority`.
  On load: `if (!bannerStatus) HideBanner(); else if (IsTopPriority) { ShowBanner();
  AdsManager.HideAllBannersExcept(this); }`.
- `Pre530/MeticaBanner.Before502.NoPriority.cs.txt` — when nothing in `AdsManager` assigns
  `IsTopPriority` (outside comments). On load, if wanted: `AdsManager.ShowBanner()`, letting the
  game's own `ShowBanner` pick, show and hide the others.

## What to check, hard

1. **Who sets `IsTopPriority`?** Grep the whole project for `IsTopPriority =` outside comments.
   If nothing sets it and `MeticaBanner` still waits for it → **blocker**: the banner loads and
   never shows unless the game happens to call `AdsManager.ShowBanner()` again after the load
   (5.0.0-beta5 is exactly this: its priority loop is commented out). Fix: re-run Wrapper files
   (it now switches to the NoPriority variant and flags this), or replace the block with
   `AdsManager.ShowBanner()`.
2. If something does set it — is it set **before** the Metica banner's load callback can run,
   and for the Metica banner too (not only for units of some type)?
3. With the NoPriority variant: does the game's `AdsManager.ShowBanner()` put Metica first when
   Metica is the main network (network order `{metica, admob}`) and hide the AdMob banner? Does
   calling it on every Metica refresh (auto-refresh fires the load callback again) do anything
   unwanted in this game (analytics events, canvas toggles fired each time)? Usually harmless —
   say so as info, or a warning if it sends an event per call.
4. Load failure: the Metica banner calls `AdsManager.ShowBanner()` to fall through to the next
   network. With a custom `ShowBanner`, confirm Metica's `HasBanner()` returns false then (it
   returns `isLoaded`) so it doesn't loop on itself.
5. Compare with the game's own `ApplovinBanner` in this project — it is the behaviour that works
   today with this `AdsManager`. The Metica banner should do the same on load / fail / hide.
