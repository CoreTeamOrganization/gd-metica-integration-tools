# TODO

## Test in Unity before `v1-migration` goes to `main`

Everything below compiles and passes the scratch test harness, but has not been run in a
real project yet.

Suggested runs: one GD SDK 5.0.x game, and one custom-SDK game that had Metica v1. Those two
cover all of the list below.

### Window and steps

- [ ] "Reviewed — next step" moves to the next step at once, and keeps working after a step
      that makes Unity recompile (fix for the stuck "refresh queued" flag).
- [ ] Re-check and Skip still respond after a recompile.
- [ ] A step waiting on a compile shows the "waiting" box, then turns into done (or into
      the real error).
- [ ] Per-step commit: the Commit button commits only that step's files (and their .meta),
      with the prefilled summary and description; nothing is pushed.

### Compare

- [ ] Optional "Compare with original GD SDK" step: opens the window, can be skipped in one
      click, and the flow carries on.
- [ ] Compare window: clicking a file shows its diff; filters (Metica / Tool / All) work;
      Open project file / Open original work; release picker shows when the version is
      unreadable.

### Metica v1

- [ ] Metica v1 code step lists the v1 lines, and passes once they are gone.
- [ ] File rows show name + count, folder faded below; clicking a row opens its lines.
- [ ] Hover Comment / Remove on a file and on a single line; Remove on a file asks first;
      "by hand" lines have no buttons; Unity recompiles after, and any leftover shows as a
      compile error.
- [ ] Metica SDK step removes old Metica from both Package Manager (`com.metica.unity`) and
      Assets, then adds 2.45.2, and the project compiles.
- [ ] MeticaSdk scene object warning shows, and clears once the object is deleted.

### GD SDK 5.0 – 5.2 (device)

- [ ] Interstitial, rewarded, banner and MREC show through Metica.
- [ ] Revenue events fire (Firebase, Adjust, AppMetrica).
- [ ] 5.0 consent patch: `MeticaConsentSettings` is in the consent list and consent reaches Metica.
- [ ] 5.0.0 – 5.0.1 banner (hand-ported, the riskiest): shows, hides, reloads, and respects
      other banners' priority.

### Standalone (device)

- [ ] Banner and MREC start at the positions set in MeticaAdsConfig.
- [ ] `MeticaAdsManager.LoadInterstitial()` / `LoadRewarded()` load an ad, and do nothing
      when one is already loaded.
- [ ] MREC with MRec Offset (e.g. Bottom, (0, -140)) lands in the same place as the game's
      MAX MREC, on Android and iOS; `RepositionMRec(position, offset)` moves it.

### Standalone revenue on the native thread

- [ ] Interstitial / rewarded revenue reaches Firebase, Adjust and AppMetrica while the ad is
      still open (check logcat timestamps vs ad close), with no "can only be called from the
      main thread" errors.
- [ ] Banner / MREC revenue still reported.

## Known issues

- [ ] GD SDK v6 + Callback mode: the "Remove the unused async init path" step removes
      `IAsyncAdNetworkService` and the `AdNetworkController` async constructor while the
      SDK's own `AdNetworkMetica` still uses them, so the build breaks. `StillImplementing()`
      in `AsyncCleanupStep` checks Admob and AppLovin only, not Metica.
