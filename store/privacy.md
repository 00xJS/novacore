# App Privacy — draft answers

App Store Connect › App Privacy asks what data the app collects. For this build:

**Data collection: "No, we do not collect data from this app."**

Why that's accurate:
- The game is single-player and offline. There's no account, no server, no analytics SDK, no ads, and no tracking.
- The save lives on the device. The backup goes to the player's **own** iCloud (key-value storage via `NSUbiquitousKeyValueStore`). Apple stores it for the player, and the developer never receives it.
- Unity's hardware-statistics reporting is off (`submitAnalytics: 0` in Player settings).
- Local notifications are scheduled on the device; nothing is sent anywhere.
- The home-screen widget and the raid Live Activity read a small snapshot the game writes to an App Group on the device (timers, resources, might). It never leaves the phone.
- **Game Center** (optional, off until the player turns it on in Settings) is Apple's service. Scores and achievements go to Apple under the player's Game Center account, and the developer doesn't collect them.
- **Report a problem** only opens the iOS share sheet with a text report the player can read first. Nothing is sent unless the player chooses where to share it.

Revisit these answers if any of the following change:
- The AI features (planned) send gameplay facts, but no personal data, to a proxy you run. If they ship, answer for that proxy: "Other data (gameplay content), not linked to the user, not used for tracking".
- Crash reporting, analytics or ads.

**Privacy policy URL:** App Store Connect requires one even for no collection. A short page stating the above is enough (it can live in this repo or on GitHub Pages).
