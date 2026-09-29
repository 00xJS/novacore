# App Store kit

Drafts and a checklist for putting Galaxy Royale on the App Store. Nothing here is uploaded anywhere; the upload itself is `scripts/testflight.sh --upload`, run by you.

| File | What it is |
|---|---|
| `listing.md` | Name, subtitle, promotional text, description, keywords, category, age-rating answers — within Apple's limits |
| `privacy.md` | App Privacy answers ("no data collected") and why |
| `review-notes.md` | Notes for App Review |
| `screenshots/` | Six captioned 6.9" App Store screenshots (1320 × 2868 JPEG): the colony, the Wilds, a battle replay, the Spaceport, the galaxy and a battle report |
| `../docs/` | The privacy policy, support and home pages, ready for GitHub Pages or any web host |
| `../scripts/screenshots.sh` | Screenshot tour: opens each screen in the Simulator and saves a raw 6.9" PNG (`screenshots/raw/`, not committed) |
| `../scripts/store_frames.py` | Sets raw captures in the captioned frames above |

The app icon (`GalaxyRoyale/Assets/icon1024.png`) and the launch screen's logo (`GalaxyRoyale/iOSLaunch/LaunchLogo.png`) are drawn by `scripts/art/render_icon.sh`.

## Checklist (in App Store Connect unless noted)

1. **Create the app record** with the same bundle id you build with (`GR_BUNDLE_ID`), platform iOS, **iPhone only** (the build targets iPhone; the layout is portrait-phone).
2. **Game Center** (optional; the in-game switch is off by default):
   - Enable Game Center on the app.
   - Create the leaderboard **`galaxyroyale.might`**: "Might", integer, high to low.
   - Create an achievement for each id in `GalaxyRoyale/Assets/Scripts/Game/GameCenter.cs` (`galaxyroyale.<achievement-id>`, 100 points total or your own split). Achievements that don't exist in App Store Connect are skipped quietly.
3. **Screenshots:** upload `screenshots/*.jpg` (6.9", 1320 × 2868, in order). To make fresh ones, run `scripts/screenshots.sh` on the 6.9" Simulator (iPhone Pro Max) with a save that has something to show, then `scripts/store_frames.py`.
4. **Web pages** (not App Store Connect): put `docs/` online. On GitHub: Settings › Pages › Deploy from a branch › `main` / `docs`. It serves `https://<user>.github.io/<repo>/`; a private repo needs a paid plan for Pages, so any static host works too. First fill in the two `[support email]` / contact placeholders in `docs/support.html` and `docs/privacy.html`.
5. **Listing:** paste from `listing.md`. Support URL: `…/support.html`; marketing URL (optional): the `docs/` home page.
6. **Privacy:** answer from `privacy.md`; privacy policy URL: `…/privacy.html`.
7. **Age rating:** answer from `listing.md` (expected 9+).
8. **Pricing:** your call (free or paid; there are no in-app purchases).
9. **Build:** `GR_BUNDLE_ID=… GR_TEAM_ID=… scripts/testflight.sh --upload`, then pick the build in the version and submit for review with `review-notes.md`.
   - The first signed build registers what the app's capabilities need on your developer account, through automatic signing:
     - the widget extension's App ID (`<bundle id>.widget`) and the App Group `group.<bundle id>` it shares with the app;
     - Game Center and iCloud key-value storage on the app's App ID.
   - To leave any of them out, build with `GR_WIDGET=0`, `GR_GAMECENTER=0` or `GR_ICLOUD=0`.
