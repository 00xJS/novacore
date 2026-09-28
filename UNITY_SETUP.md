# Unity / Xcode Cheatsheet — Galaxy Royale

Reference for Editor settings, build config, and platform gotchas. The Unity
project lives in [`GalaxyRoyale/`](GalaxyRoyale/) — open that folder in Unity
Hub, not the repo root. Initial install steps live in [README.md](README.md);
this file is the "what do I set/click once the project is open" reference.

## Toolchain

- **Unity Hub** — https://unity.com/download
- **Unity `6000.5.2f1`** with modules:
  - iOS Build Support
  - Android Build Support (+ Android SDK & NDK, OpenJDK)
  - Mac Build Support (Mono) — optional, handy for local QA
- **Xcode** — already installed
- **Android Studio** — optional, for USB deployment testing

## Editor settings (set once per machine)

- **Edit → Project Settings → Editor → Enter Play Mode Options** = enabled
  (faster domain reloads — big quality-of-life win).
- **Edit → Project Settings → Player → Resolution and Presentation** →
  Default Orientation = **Portrait**.
- **Game view** → aspect ratio → add a custom Fixed Resolution **`402×874`**
  (iPhone 16 Pro logical points) and select it. UI Toolkit + safe-area insets
  scale the layout to whatever device runs the app.

## Player Settings — iOS

- **Company Name:** `Galaxy Royale`
- **Product Name:** `Galaxy Royale`
- **Bundle Identifier:** `com.example.galaxyroyale` — a **placeholder**. Set
  your own reverse-domain id before building to a device; it must be unique to
  your Apple developer account.
- **Signing Team:** `YOUR_TEAM_ID` — placeholder. Replace with your Apple
  Developer team id (Xcode → Signing & Capabilities shows it), or just let
  Xcode pick your team after opening the generated project.
- **Target minimum iOS Version:** `13`

## Player Settings — Android

- **Package Name:** `com.example.galaxyroyale` (placeholder — same as iOS)
- **Target API level:** latest stable Play-Console requirement
- **Scripting Backend:** IL2CPP + ARM64 (required for Play Store)

## Building

**iOS (device)**
1. Menu `GalaxyRoyale → Build iOS (Xcode project)` (or Build Settings → iOS →
   Build). Produces `GalaxyRoyale/Builds/iOS/Unity-iPhone.xcodeproj`.
2. Open in Xcode → Archive → TestFlight from there — or use the script below.

**TestFlight** (`scripts/testflight.sh`)

One command runs the Unity release build, the Xcode archive and the App Store
export. Every run gets a fresh, always-rising build number: the UTC time as
`yymmddHHMM`. Set `GR_BUILD_NUMBER` to override it.

```bash
GR_BUNDLE_ID=com.you.galaxyroyale GR_TEAM_ID=ABCDE12345 scripts/testflight.sh
```

Without flags it stops at a signed `.ipa` under
`GalaxyRoyale/Builds/TestFlight/<build>/export/` and uploads nothing. Add
`--upload` to send the build to App Store Connect.

Before the first upload:

1. In App Store Connect, go to **Apps → + → New App** and use the same bundle
   id. An upload has nowhere to land until that app record exists.
2. Signing is automatic. Either sign in to your developer team in **Xcode →
   Settings → Accounts**, or pass an App Store Connect API key with
   `GR_ASC_KEY_PATH`, `GR_ASC_KEY_ID` and `GR_ASC_ISSUER_ID`.

After an upload, App Store Connect processes the build, which usually takes
5–30 minutes. The build then shows under **TestFlight**. Add yourself as an
internal tester to install it through the TestFlight app. Internal testing
needs no review.

Every build does two things to the generated Xcode project
(`Editor/IosPostProcess.cs`):

- It sets `ITSAppUsesNonExemptEncryption = NO`, so uploads skip the
  export-compliance question.
- It adds the iCloud key-value storage capability that the save backup needs.
  Set `GR_ICLOUD=0` to build without that capability.

Signing without editing tracked files: `BuildScript` reads your identity from
the environment and restores the committed placeholders after the build, so
`ProjectSettings.asset` never picks up a real bundle id or team:

```bash
GR_BUNDLE_ID=com.you.galaxyroyale GR_TEAM_ID=ABCDE12345 \
  Unity -batchmode -projectPath GalaxyRoyale -buildTarget iOS \
  -executeMethod BuildScript.BuildIos
```

**iOS Simulator** (Apple silicon, no signing needed)
1. Menu `GalaxyRoyale → Build iOS Simulator (Xcode project)` or
   `-executeMethod BuildScript.BuildIosSimulator`. Produces
   `GalaxyRoyale/Builds/iOS-Sim/Unity-iPhone.xcodeproj`.
2. `xcodebuild -project Builds/iOS-Sim/Unity-iPhone.xcodeproj -scheme Unity-iPhone
   -configuration ReleaseForRunning -sdk iphonesimulator build`, then install the
   `.app` with `xcrun simctl install booted <path>`.

**Android**
1. `File → Build Settings → Android → Switch Platform`.
2. `Build` → APK for local testing, AAB for Play Console.

## Single-player notes (post-pivot)

- **No server, no accounts** — the game is fully offline. Nothing needs
  configuring server-side, ever.
- Saves live at `Application.persistentDataPath/galaxy-royale-save.json`
  (+ `.bak`). Delete both to hard-reset outside the in-game RESET flow.
- **iCloud backup** (`Local/CloudSave.cs` + `Plugins/iOS/GRCloudSave.mm`):
  the save is gzipped (~40 KB) into the app's iCloud key-value store every
  5 minutes and whenever the app goes to the background. A fresh install or a
  new phone offers **RESTORE FROM ICLOUD** on the title screen. A device whose
  save is older than the backup (you played on another device) asks which one
  to keep. The Editor has no iCloud, so there the backup is simply off.
- **Seasons & events** run on galaxy time (`SeasonSystem`, `EventSystem`).
  A new galaxy gets 48 quiet hours before the first event. The events repeat
  on a weekly cycle and a season lasts 14 days.
- The simulated galaxy's pacing knobs are consts on `BotSystem`
  (`Assets/Scripts/Sim/Bots/Bots.cs`) — think cadence, aggression multiplier,
  inbound raid cooldown, pre-sim head start.

## Ship & research art (drop-in)

Every hull and tech shows a picture — a painted schematic / emblem until real
art exists. To replace one, drop a PNG at `Assets/Resources/Ships/<HullId>.png`
(e.g. `Leviathan.png`) or `Assets/Resources/Research/<TechId>.png` (e.g.
`IonThrusters.png`); no code changes. Raw renders with a baked transparency
checkerboard or a white background can go in `Assets/Textures/Ships/` /
`Assets/Textures/Research/` (named after the id) and be cleaned up with
`GalaxyRoyale → Process Ship & Research Art`.

## Troubleshooting

- **"Type or namespace X could not be found"** — usually a missing package.
  Check Package Manager for Input System + TextMeshPro.
- **Multiple Unity versions in Hub** — right-click the project in Hub →
  "Change Editor Version" and make sure it's the `6000.x` build.
- **iOS build fails on missing signing team** — set the team in Player
  Settings *before* building, not after.
- **Meta files missing after moving scripts into `Assets/`** — expected on
  the first Unity refresh; the editor regenerates them and they should be
  committed alongside the `.cs` files.
- **Markers/planet rendering oddities after the pivot** — building markers
  render on layer 1 (TransparentFX) via an overlay camera created at runtime
  by `BuildingMarkers`; if markers vanish entirely, check that nothing else
  claimed that layer.
