# result.json

Write it to the bundle folder. UTF-8, valid JSON, these fields only:

```json
{
  "summary": "GD SDK 5.0.0-beta5 (unknown build). 1 blocker, 2 warnings. The Metica banner never shows.",
  "scenario": "unknown",
  "findings": [
    {
      "id": "banner-istoppriority-no-writer",
      "severity": "blocker",
      "title": "Metica banner waits for IsTopPriority, which nothing sets",
      "file": "Assets/GDMonetization/Runtime/Scripts/Ads/Metica/MeticaBanner.cs",
      "line": 136,
      "detail": "OnBannerLoadedEvent shows only if IsTopPriority. AdsManager.ShowBanner (line 120) has the loop that set it commented out and shows the first network whose HasBanner() is true, so a banner that loads after ShowBanner was called never shows.",
      "fix": "Re-run the Wrapper files step (it now calls AdsManager.ShowBanner() on load when nothing sets IsTopPriority), or replace the IsTopPriority block with AdsManager.ShowBanner()."
    }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `summary` | string | One or two sentences, shown at the top of the step. |
| `scenario` | string | The scenario from `review.md`. |
| `findings` | array | Blockers first, then warnings, then info. Empty array when there is nothing to say. |
| `id` | string | Stable kebab-case, unique in the file — the tool keeps "acknowledged" ticks by id, so the same issue keeps the same id across reviews. |
| `severity` | string | `blocker`, `warning` or `info`. |
| `title` | string | One line, under ~90 characters. |
| `file` | string | Project-relative path (`Assets/...`), or empty. |
| `line` | number | 1-based line in that file, or 0. |
| `detail` | string | The evidence: what the code does, quoted briefly, and why it matters. |
| `fix` | string | What to change. Empty for info. |

Never put an ad unit ID, App Key, API Key or App ID in the file.
