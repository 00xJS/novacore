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

**iOS**
1. Menu `GalaxyRoyale → Build iOS (Xcode project)` (or Build Settings → iOS →
   Build). Produces `GalaxyRoyale/Builds/iOS/Unity-iPhone.xcodeproj`.
2. Open in Xcode → Archive → TestFlight from there.

**Android**
1. `File → Build Settings → Android → Switch Platform`.
2. `Build` → APK for local testing, AAB for Play Console.

## Single-player notes (post-pivot)

- **No server, no accounts** — the game is fully offline. Nothing needs
  configuring server-side, ever.
- Saves live at `Application.persistentDataPath/galaxy-royale-save.json`
  (+ `.bak`). Delete both to hard-reset outside the in-game RESET flow.
- The simulated galaxy's pacing knobs are consts on `BotSystem`
  (`Assets/Scripts/Sim/Bots/Bots.cs`) — think cadence, aggression multiplier,
  inbound raid cooldown, pre-sim head start.

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
