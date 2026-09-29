# App Store kit

Drafts and a checklist for putting Galaxy Royale on the App Store. Nothing here is uploaded anywhere; the upload itself is `scripts/testflight.sh --upload`, run by you.

| File | What it is |
|---|---|
| `listing.md` | Name, subtitle, promotional text, description, keywords, category, age-rating answers — within Apple's limits |
| `privacy.md` | App Privacy answers ("no data collected") and why |
| `review-notes.md` | Notes for App Review |
| `../scripts/screenshots.sh` | Screenshot tour: opens each screen in the Simulator and saves a 6.9" PNG |

## Checklist (in App Store Connect unless noted)

1. **Create the app record** with the same bundle id you build with (`GR_BUNDLE_ID`), platform iOS, **iPhone only** (the build targets iPhone; the layout is portrait-phone).
2. **Game Center** (optional; the in-game switch is off by default):
   - Enable Game Center on the app.
   - Create the leaderboard **`galaxyroyale.might`**: "Might", integer, high to low.
   - Create an achievement for each id in `GalaxyRoyale/Assets/Scripts/Game/GameCenter.cs` (`galaxyroyale.<achievement-id>`, 100 points total or your own split). Achievements that don't exist in App Store Connect are skipped quietly.
3. **Screenshots:** run `scripts/screenshots.sh` on the 6.9" Simulator (iPhone Pro Max) with a save that has something to show. Upload the PNGs (1320 × 2868).
4. **Listing:** paste from `listing.md`. Fill in the support URL (a GitHub page works).
5. **Privacy:** answer from `privacy.md` and add a privacy policy URL.
6. **Age rating:** answer from `listing.md` (expected 9+).
7. **Pricing:** your call (free or paid; there are no in-app purchases).
8. **Build:** `GR_BUNDLE_ID=… GR_TEAM_ID=… scripts/testflight.sh --upload`, then pick the build in the version and submit for review with `review-notes.md`.
