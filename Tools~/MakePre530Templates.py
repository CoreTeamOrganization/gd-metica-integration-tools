"""Generates Editor/Ads/Templates/Pre530/ (GD SDK 5.0.0 - 5.2.0) from the current templates by
exact, asserted edits. Re-run after changing any of the 5.3.0+ templates:

    python Tools~/MakePre530Templates.py

Everything not named here stays byte-for-byte the tested 5.3.0+ code. Each edit must match
exactly once, or the script stops.
"""
import os

T = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Editor", "Ads", "Templates")
OUT = os.path.join(T, "Pre530")
os.makedirs(OUT, exist_ok=True)


def read(name):
    with open(os.path.join(T, name), encoding="utf-8", newline="") as f:
        return f.read()


def write(name, text):
    with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("wrote", name)


def edit(text, old, new, label):
    nl = "\r\n" if "\r\n" in text else "\n"
    old, new = old.replace("\n", nl), new.replace("\n", nl)
    count = text.count(old)
    assert count == 1, f"{label}: expected 1 match, found {count}"
    return text.replace(old, new)


def header(text, what):
    nl = "\r\n" if "\r\n" in text else "\n"
    note = ("// GD SDK 5.0.0 - 5.2.0 version, generated from the 5.3.0+ template by" + nl +
            "// Tools~/MakePre530Templates.py: " + what + nl)
    return note + text


# --- Interstitial: no close callback before 5.3.0 -------------------------------------
s = read("MeticaInterstitial.cs.txt")
s = edit(s, "public override void ShowInterstitial(string placementName, Action onClosed)\n        {\n            this.onAdClosed = onClosed;\n",
         "public override void ShowInterstitial(string placementName)\n        {\n", "interstitial show")
s = edit(s, "                LoadInterstitial();\n                onAdClosed?.Invoke();\n",
         "                LoadInterstitial();\n", "interstitial dismissed")
# 5.0 - 5.2: Add(Action, float, bool ignoreTimeScale) - the SDK's own units pass true.
s = edit(s, "DelayedActionManager.Add(LoadInterstitial, (float)retryDelay);",
         "DelayedActionManager.Add(LoadInterstitial, (float)retryDelay, true);", "interstitial retry")
write("MeticaInterstitial.cs.txt", header(s, "ShowInterstitial(placement) - the base has no close callback; "
                                             "DelayedActionManager.Add takes ignoreTimeScale."))

# --- Rewarded: only DelayedActionManager.Add differs ------------------------------------
r = read("MeticaRewarded.cs.txt")
r = edit(r, "DelayedActionManager.Add(LoadRewarded, retryDelay);",
         "DelayedActionManager.Add(LoadRewarded, retryDelay, true);", "rewarded retry")
write("MeticaRewarded.cs.txt", header(r, "DelayedActionManager.Add takes ignoreTimeScale."))

# --- Banner, 5.0.2 - 5.2.0: no RepositionBanner -----------------------------------------
banner = read("MeticaBanner.cs.txt")
reposition = """        public override void RepositionBanner(BannerPosition newPosition)
        {
            bannerInfo.BannerPosition = newPosition;
            if (!IsAdUnitEmpty)
            {
                Message.Log(Tag.Metica, $"Banner is repositioned to {newPosition}");
                MeticaSdk.Ads.UpdateBannerPosition(adUnitId, ConvertToMeticaBannerPos());
            }
        }

"""
b = edit(banner, reposition, "", "banner reposition")
b = edit(b, '}, 3f, "Retry Metica banner");', "}, 3f, true);", "banner retry")
write("MeticaBanner.cs.txt", header(b, "no RepositionBanner - the base has none; DelayedActionManager.Add "
                                       "takes ignoreTimeScale (5.0.2 - 5.2.0)."))

# Banner for 5.0.0 - 5.0.1 is NOT generated: that SDK's banners work another way (a per-unit
# bannerStatus, priorities, AdsManager.HideAllBannersExcept; AdsManager.BannerStatus is
# private), so Pre530/MeticaBanner.Before502.cs.txt is a hand port of 5.0.0's ApplovinBanner.

# --- MRec: no RepositionMRec, position is a BannerPosition, own state fields ------------
m = read("MeticaMRec.cs.txt")
m = edit(m, """        public override void RepositionMRec(MRecPosition newPosition)
        {
            bannerInfo.MrecPosition = newPosition;
            if (!IsAdUnitEmpty)
            {
                Message.Log(Tag.Metica, $"MRec is repositioned to {newPosition}");
                MeticaSdk.Ads.UpdateMrecPosition(adUnitId, ConvertToMeticaMRecPos());
            }
        }

""", "", "mrec reposition")
m = edit(m, """                case MRecPosition.Top:
                    return MeticaAdViewPosition.TopCenter;
                case MRecPosition.TopLeft:
                    return MeticaAdViewPosition.TopLeft;
                case MRecPosition.TopRight:
                    return MeticaAdViewPosition.TopRight;
                case MRecPosition.Center:
                    return MeticaAdViewPosition.Centered;
                case MRecPosition.CenterLeft:
                    return MeticaAdViewPosition.CenterLeft;
                case MRecPosition.CenterRight:
                    return MeticaAdViewPosition.CenterRight;
                case MRecPosition.Bottom:
                    return MeticaAdViewPosition.BottomCenter;
                case MRecPosition.BottomLeft:
                    return MeticaAdViewPosition.BottomLeft;
                case MRecPosition.BottomRight:
                    return MeticaAdViewPosition.BottomRight;
""", """                // Before 5.3.0 the MREC position is a BannerPosition (no CenterLeft / CenterRight).
                case BannerPosition.Top:
                    return MeticaAdViewPosition.TopCenter;
                case BannerPosition.TopLeft:
                    return MeticaAdViewPosition.TopLeft;
                case BannerPosition.TopRight:
                    return MeticaAdViewPosition.TopRight;
                case BannerPosition.Center:
                    return MeticaAdViewPosition.Centered;
                case BannerPosition.Bottom:
                    return MeticaAdViewPosition.BottomCenter;
                case BannerPosition.BottomLeft:
                    return MeticaAdViewPosition.BottomLeft;
                case BannerPosition.BottomRight:
                    return MeticaAdViewPosition.BottomRight;
""", "mrec positions")
m = edit(m, "    internal sealed class MeticaMRec : MRecAdUnit\n    {\n",
         "    internal sealed class MeticaMRec : MRecAdUnit\n    {\n"
         "        // The 5.0.0 - 5.2.0 base class has neither.\n"
         "        private bool isLoading;\n"
         "        private bool hasMrec;\n", "mrec fields")
m = edit(m, '}, 3f, "Retry Metica MRec");', "}, 3f, true);", "mrec retry")
write("MeticaMRec.cs.txt", header(m, "no RepositionMRec, BannerPosition-typed position, own state fields; "
                                     "DelayedActionManager.Add takes ignoreTimeScale."))

# --- AdNetworkMetica: no AddAndUpdateConsentService before 5.3.0 -----------------------
n = read("AdNetworkMetica.cs.txt")
n = edit(n, "                ConsentManager.AddAndUpdateConsentService(new MeticaConsentSettings());\n",
         "                // Before 5.3.0 there is no AddAndUpdateConsentService: apply the current consent\n"
         "                // now, as it does (same ConsentInfo as its CreateConsentInfo). Later changes come\n"
         "                // through ConsentManager's list, which the patch step adds MeticaConsentSettings to.\n"
         "                new MeticaConsentSettings().ApplySettings(new ConsentInfo(\n"
         "                    MonetizationPreferences.ConsentEuropeanArea.Get(),\n"
         "                    MonetizationPreferences.PersonalizedAds.Get()));\n", "network consent")
write("AdNetworkMetica.cs.txt", header(n, "consent applied after init, not via AddAndUpdateConsentService."))

# --- MeticaConsentSettings: in 5.0's fixed list it can run before Metica is up ----------
c = read("MeticaConsentSettings.cs.txt")
c = edit(c, "        public void ApplySettings(ConsentInfo consentInfo)\n        {\n",
         "        public void ApplySettings(ConsentInfo consentInfo)\n        {\n"
         "            // In 5.0's fixed consent list this runs at startup too - before Metica is up.\n"
         "            // AdNetworkMetica applies the consent itself once it is.\n"
         "            if (!Monetization.Runtime.Ads.MeticaInitializer.IsInitialized) return;\n\n", "consent guard")
write("MeticaConsentSettings.cs.txt", header(c, "skips until Metica is initialized."))
