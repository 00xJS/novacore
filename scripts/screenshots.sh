#!/bin/zsh
# App Store screenshot tour: opens each screen in the Simulator through the
# GR_OPEN launch hook (Game/DebugLaunch.cs) and saves a PNG at the device's
# native size. A 6.9" iPhone (the Pro Max Simulators) gives 1320 × 2868, the
# size App Store Connect asks for first.
#
#   scripts/screenshots.sh                    # the booted Simulator → store/screenshots/
#   scripts/screenshots.sh <udid> <out-dir>
#
# Needs the app installed in that Simulator (BuildScript.BuildIosSimulator +
# xcodebuild, as for any Simulator run) and a save worth showing. The game
# runs full-screen (no status bar); the status bar is still set to 9:41 with
# full battery and signal, in case a screen shows it, and restored afterwards.
# GR_SETTINGS (e.g. "textSize=0;colorBlind=0") pins display settings for the run.
#
# Optional environment:
#   GR_BUNDLE_ID    the app's bundle id in the Simulator (default com.example.galaxyroyale)
#   GR_SHOT_WAIT    seconds to wait for each launch (default 70 — the Simulator
#                   decompresses textures on the CPU, so boots are slow)
#   GR_SHOTS        screens, space-separated (default below; "base" = no GR_OPEN)
set -euo pipefail

UDID="${1:-booted}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${2:-$REPO/store/screenshots}"
BUNDLE="${GR_BUNDLE_ID:-com.example.galaxyroyale}"
WAIT="${GR_SHOT_WAIT:-70}"
SCREENS=(${=GR_SHOTS:-base map core boss commander clan market rankings})

mkdir -p "$OUT"
xcrun simctl status_bar "$UDID" override --time 9:41 --dataNetwork wifi --wifiBars 3 \
  --cellularMode active --cellularBars 4 --batteryState charged --batteryLevel 100 || true
trap 'xcrun simctl status_bar "$UDID" clear || true' EXIT

n=0
for screen in "${SCREENS[@]}"; do
  n=$((n + 1))
  xcrun simctl terminate "$UDID" "$BUNDLE" 2>/dev/null || true
  export SIMCTL_CHILD_GR_SETTINGS="${GR_SETTINGS:-}"
  if [[ "$screen" == base ]]; then
    xcrun simctl launch "$UDID" "$BUNDLE" >/dev/null
  else
    SIMCTL_CHILD_GR_OPEN="$screen" xcrun simctl launch "$UDID" "$BUNDLE" >/dev/null
  fi
  sleep "$WAIT"
  file="$OUT/$(printf '%02d' $n)-$screen.png"
  xcrun simctl io "$UDID" screenshot "$file" >/dev/null 2>&1
  echo "saved $file"
done
