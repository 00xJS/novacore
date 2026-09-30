# App Store kit

Drafts and a checklist for putting Galaxy Royale on the App Store. Nothing here is uploaded anywhere; the upload itself is `scripts/testflight.sh --upload`, run by you.

| File | What it is |
|---|---|
| `listing.md` | Name, subtitle, promotional text, description, keywords, category, age-rating answers — within Apple's limits |
| `privacy.md` | App Privacy answers ("no data collected") and why |
| `review-notes.md` | Notes for App Review |
| `screenshots/` | Six captioned 6.9" App Store screenshots (1320 × 2868 JPEG, retaken 2026-09-30): the colony, the Galactic Core, the Frontier, the whole galaxy, the Wilds and a battle report |
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
3. **Screenshots:** upload `screenshots/*.jpg` (6.9", 1320 × 2868, in order).
   - **Privacy manifests** ship in the build (`GalaxyRoyale/iOSPrivacy/` for the app, `GalaxyRoyale/iOSWidget/` for the widget): no tracking, no data collected, and the UserDefaults reasons. Keep them in step with `privacy.md` if the app ever starts collecting anything. To make fresh ones, run `scripts/screenshots.sh` on the 6.9" Simulator (iPhone Pro Max) with a save that has something to show, then `scripts/store_frames.py`.
4. **Web pages:** GitHub Pages serves `docs/` from `main` at https://00xjs.github.io/novacore/ (enabled 2026-09-30). Pages on a private repo need a paid GitHub plan, so if you make the repo private, move `docs/` to another static host and update `Links` in `GalaxyRoyale/Assets/Scripts/Game/UI/Panels/CreditsPanel.cs`.
5. **Listing:** paste from `listing.md`. Support URL: `…/support.html`; marketing URL (optional): the `docs/` home page.
6. **Privacy:** answer from `privacy.md`; privacy policy URL: `…/privacy.html`.
7. **Age rating:** answer from `listing.md` (expected 9+).
8. **Pricing and in-app purchases:** the app is free, with Dark Matter packs as consumable in-app purchases.
   - Business › Agreements: accept the **Paid Apps** agreement and fill in banking and tax. Until it's active, the store returns no products and the game's DARK MATTER tab says so.
   - App › Monetization › In-App Purchases: create six **Consumable** products with exactly these ids (`GalaxyRoyale/Assets/Scripts/Data/DarkMatterPacks.cs`):

     | Product id | Reference name | Suggested price |
     |---|---|---|
     | `galaxyroyale.darkmatter.120` | Pinch of Dark Matter (120) | $0.99 |
     | `galaxyroyale.darkmatter.650` | Pouch of Dark Matter (650) | $4.99 |
     | `galaxyroyale.darkmatter.1400` | Crate of Dark Matter (1,400) | $9.99 |
     | `galaxyroyale.darkmatter.3000` | Vault of Dark Matter (3,000) | $19.99 |
     | `galaxyroyale.darkmatter.8000` | Hoard of Dark Matter (8,000) | $49.99 |
     | `galaxyroyale.darkmatter.17500` | Trove of Dark Matter (17,500) | $99.99 |

     Each needs a display name and description (for example "120 Dark Matter" / "Premium currency for speed-ups, shields and skins"), a price, and a review screenshot (a capture of the DARK MATTER tab works). The game shows the App Store's own localized price, so change prices freely there.
   - Add the products to the version before you submit (the first in-app purchases are reviewed with the app).
   - Test with a **Sandbox** tester (Users and Access › Sandbox) on a TestFlight or development build: buy each pack, then kill the app mid-purchase once to check the purchase is delivered at the next launch.
9. **Build:** `GR_BUNDLE_ID=… GR_TEAM_ID=… scripts/testflight.sh --upload`, then pick the build in the version and submit for review with `review-notes.md`.
   - The first signed build registers what the app's capabilities need on your developer account, through automatic signing:
     - the widget extension's App ID (`<bundle id>.widget`) and the App Group `group.<bundle id>` it shares with the app;
     - Game Center and iCloud key-value storage on the app's App ID.
   - To leave any of them out, build with `GR_WIDGET=0`, `GR_GAMECENTER=0` or `GR_ICLOUD=0`.
