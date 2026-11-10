---
name: metica-integration-review
description: Review a Metica ads integration in a Unity game (GD Monetization SDK or standalone) from a bundle written by Metica Integration Tools under MeticaIntegrationReview/<time>/. Traces every ad format end to end, checks that everything the Metica code waits for is actually set by this project's code, compares with the game's AppLovin MAX path, and writes result.json. Use when asked to review a Metica integration or a MeticaIntegrationReview bundle.
---

# Metica integration review

You review one game's Metica ads integration and report what would stop Metica from
working the way the game's AppLovin MAX path works today. **You report; you do not edit the
game.** The only file you write is `result.json` in the bundle folder.

Integrations differ per GD SDK release, per fork or beta of it, and per game. The tool that
did the integration can only check what it knows to look for. Your job is everything else:
read what this project's code actually does.

Why this exists: a game on GD SDK 5.0.0-beta5 (a release later withdrawn) passed every step
of the tool. Its Metica banner waited for `IsTopPriority`, which the stock 5.0.0 `AdsManager`
sets in `ShowBanner` — but beta5 had commented that loop out, so nothing ever set it and the
Metica banner never showed. Every file compiled and every check passed. Only reading who
writes the value the Metica code reads would have caught it. That is the method below.

## Inputs

The user gives a bundle folder, e.g. `MeticaIntegrationReview/20261110-153000`. Paths in it
are relative to the Unity project folder (your working directory).

1. Read `<bundle>/review.md` first — the index: scenario, GD SDK version, code shape, which
   template each wrapper file came from, every step's own check, the SDK files that differ
   from the original release, and the git summary.
2. `<bundle>/facts.json` has the same facts as data.
3. `<bundle>/sdk-diffs/*.diff` (GD SDK, known release): each changed SDK file against the
   original; `sdk-original/` has the originals. "By tool: no" rows are changes someone else
   made — the game, an older integration, or a fork. They matter most.
4. `<bundle>/git-diff.patch`: the working tree against the commit before the integration —
   every change, addition and removal in C#, asmdefs and the ads settings assets, whoever
   made it. New untracked files are listed in `review.md`; read them directly.
5. The project itself: read any file you need. `git log`, `git show`, `git diff`, `git blame`
   are allowed. Do not build, run, or modify anything.

If the bundle folder is missing or has no `review.md`, say so and stop.

## Scenario

`review.md` names the scenario. Read its reference file, plus the shared checklist. They ship
with this skill, in the `references/` folder next to this SKILL.md
(`.claude/skills/metica-integration-review/references/`), not in the bundle:

| Scenario | File |
|---|---|
| GD SDK 5.0.0 – 5.0.1 | `references/gd-5.0.0-5.0.1.md` |
| GD SDK 5.0.2 – 5.2.0 | `references/gd-5.0.2-5.2.md` |
| GD SDK 5.3.0 – 5.5.0 | `references/gd-5.3-5.5.md` |
| GD SDK 6.0.0 – 6.2.0 | `references/gd-6.0-6.2.0.md` |
| GD SDK 6.2.1 and newer | `references/gd-6.2.1-plus.md` |
| No GD SDK (standalone) | `references/standalone.md` |
| Unknown build (beta, fork, removed release) | `references/unknown.md`, then the file of the release whose code shape it matches |
| Always | `references/checklist.md` |

The scenario file says what that release's integration looks like. Treat it as a map, not
the truth: **this project's code is the truth.** Where they disagree, the code wins, and the
disagreement is often the finding.

## Method

Do all of it, for every format the game uses: interstitial, rewarded, banner, MREC (and
App-Open only to confirm Metica does not take it over — Metica serves the other four).

### 1. Trace each format end to end

From the game's call (e.g. `AdsManager.ShowRewarded(...)`, or the game's own wrapper), through
the network switch, into the Metica unit, out to the Metica SDK call, and back through each
callback (loaded, failed, shown, failed to show, hidden/closed, reward, revenue, clicked) to
what the game expects to happen (reward granted once, game resumed, next ad loaded, revenue
reported, banner visible). Write down where the path breaks or differs.

### 2. Every value the Metica code waits for must have a writer

For each condition, field, property, flag or event a Metica unit (or the code that picks the
network) **reads** — `IsTopPriority`, `bannerStatus`, `AdsManager.BannerStatus`,
`AdsManager.MRecStatus`, `isLoaded`, `IsInitialized`, `MainAdNetwork`, consent values, ad unit
IDs, App Key, events it subscribes to — find the code in **this project** that **writes** or
raises it, outside comments and outside `#if` branches that are off for the build target.

- Nothing writes it → **blocker** (the code waits forever, like the beta5 banner).
- Written only on a path that does not run for Metica (e.g. only inside the AppLovin unit, or
  only when `useMetica` is false) → **blocker** unless the Metica path has its own writer.
- Written, but at the wrong time (after the only read, e.g. set after a load callback that
  already gave up) → **blocker** or **warning** by impact.

Use Grep across `Assets/` (the whole project, not only the SDK — games reach in from their
own code). Comments and commented-out code do not count as writers.

### 3. Compare with the game's AppLovin path

The game's AppLovin MAX units are the reference: they are what ships today and works. For
each format, put the Metica unit next to the AppLovin unit of the **same project** (not of the
stock SDK) and compare, callback by callback:

- What the AppLovin unit does that the Metica unit does not (game additions: analytics events,
  Adjust or AppMetrica calls, reward bookkeeping, UI canvas toggles, retry logic, extra
  `AdsManager` calls). Missing behaviour the game relies on → **blocker** (reward, resume,
  ad show/hide) or **warning** (analytics, logging).
- Where the AppLovin unit relies on its own `AdsManager` behaviour (the order it shows
  banners, how it hides others), check the Metica unit fits the same `AdsManager`.
- Anything the game added to `AdsManager` or the base classes that only knows about AppLovin
  (`is ApplovinBanner`, `AdPlatforms.APPLOVIN` checks, `Applovin` config reads) and so skips
  Metica.

### 4. The network switch and remote config

Who decides Metica vs MAX, when it is read, where it is stored, and that the stored value is
the one read at the next start. Metica must never run alongside MAX for the same format, and
a user outside the test (no remote value) must stay on MAX. See the scenario file for the
expected shape.

### 5. Everything the integration changed or removed

Go through `git-diff.patch` and every "By tool: no" SDK diff. For each removed or changed line:
was something relying on it? For each added line: does it do what its place in the flow
needs? Pay special attention to:

- Lines commented out (`//`) in the SDK or the game — the beta5 bug was a commented-out loop.
- Edits inside `#if` blocks, and code only one platform compiles.
- Metica v1 leftovers (`Metica.ADS`, `MeticaAds.*`, `NotifyAd*`, `InitializeWithResultAsync`).
- A second, older integration still wired in (two `AdNetworkMetica`s, two initializers).

### 6. Settings

`AdUnitsSettings.asset` Metica section: App Key and the four ad unit IDs for the **build
target's** platform equal the AppLovin ones (GD SDK 6 keeps Android and iOS separately; only
the build target's matter for this build). `MeticaSettings.asset` has an App ID and API Key for
that platform. Do not copy any key or ID into your output — say "set" / "empty" / "differs".

## Severity

- **blocker** — Metica would fail to serve, show, reward, resume, or would run alongside MAX,
  or the switch cannot turn it on/off, or revenue never reaches the game's analytics. Ship
  only after a fix or a conscious decision.
- **warning** — works, but differs from the AppLovin path in a way someone should know
  (a missing analytics event, a log, a missing retry, a platform not set up).
- **info** — confirmed fine and worth stating (e.g. "IsTopPriority has no writer, but the
  banner uses AdsManager.ShowBanner() on load — fine").

Every blocker and warning must point at a file and line, quote the code briefly, and say what
to change. No finding without evidence from the code. If you could not check something, say
so as a warning rather than guessing.

## Output

Write `<bundle>/result.json` (replacing any there) exactly in the format of
`references/result-format.md` (the
Unity tool reads it with a strict parser: those field names, no others needed). Then reply
with a short summary: the scenario, the number of blockers and warnings, and the blockers in
one line each.
