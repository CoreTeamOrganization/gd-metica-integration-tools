# Unknown GD SDK build

The project's `MonetizationInitializeOnLoad.cs` declares a version no original release declares:
a beta (e.g. `5.0.0-beta5`, later withdrawn), a fork, or a team's own branch. The tool's patches
and templates were written and tested for released versions only. They were picked by reading
the code's shape (see "GD SDK code shape" in `review.md`), so they usually fit — but "usually" is
why this review exists.

## How to review it

1. **Find the closest release by shape, not by number.** Use the shape facts in `review.md`:
   - `bannerHasReposition` true → 5.3+ banner model (`gd-5.3-5.5.md`).
   - `bannerLoadIsPublic` true, no reposition → 5.0.2 – 5.2 (`gd-5.0.2-5.2.md`).
   - neither → 5.0.0 – 5.0.1 banner model (`gd-5.0.0-5.0.1.md`).
   - an async `AdNetworkMetica` shipped by the SDK → 6.x (`gd-6.0-6.2.0.md` / `gd-6.2.1-plus.md`).
   Read that file; then treat every claim in it as a hypothesis to confirm in this code.
2. **There is no original to diff against.** `sdk-diffs/` is empty or against a release picked
   by hand. Use `git-diff.patch` (since before the integration) for what the integration changed,
   and read the SDK's own code for what the build already did differently.
3. **Read the SDK's core files whole**, not just the diffs: `AdsManager`, `AdNetworkController`,
   the base classes in `Ads/Abstraction/` (`BannerAdUnit`, `MRecAdUnit`, `IntersitialAdUnit`,
   `RewardedAdUnit`, `AdUnit`), `RemoteConfigManager`, `MonetizationPreferences`,
   `ConsentManager`. Look for commented-out blocks, early returns and changed loops — betas
   often have work in progress commented out.
4. **Apply SKILL.md step 2 with no shortcuts**: for every base-class member and `AdsManager`
   member the Metica units read, find the writer in this build. The known case: 5.0.0-beta5's
   `AdsManager.ShowBanner` had the priority loop that set `IsTopPriority` commented out, so a
   banner waiting for it never showed.
5. **The game's AppLovin units in this build are the ground truth** for how a unit must behave
   with this `AdsManager`. Diff the Metica unit against them behaviour by behaviour.
6. Report the build as unknown in the summary, and add an **info** finding naming the release
   whose shape it matched and anything that did not match it.
