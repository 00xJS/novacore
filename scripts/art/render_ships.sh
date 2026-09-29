#!/bin/zsh
# Regenerate the ship art: ship_art.py writes an SVG per hull, headless Chrome
# renders each one to a transparent 512 x 512 PNG in the game's Resources/Ships
# (where ShipArt.Photo loads it). Needs python3 and Google Chrome.
#
# usage: scripts/art/render_ships.sh [hull ...]    (no hulls = all of them)
set -e
HERE="${0:A:h}"
OUT="$HERE/../../GalaxyRoyale/Assets/Resources/Ships"
CH="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
python3 "$HERE/ship_art.py" "$TMP/svg" "$@"
for svg in "$TMP"/svg/*.svg; do
  name="${svg:t:r}"
  png="$OUT/$name.png"
  rm -f "$png"
  "$CH" --headless=new --disable-gpu --hide-scrollbars --no-first-run --user-data-dir="$TMP/profile" \
    --window-size=512,512 --force-device-scale-factor=1 --default-background-color=00000000 \
    --virtual-time-budget=5000 --screenshot="$png" "file://$svg" >/dev/null 2>&1 &
  pid=$!
  # Headless Chrome sometimes lingers after writing the file: give it 40 s at most.
  for i in {1..40}; do
    kill -0 $pid 2>/dev/null || break
    [ -s "$png" ] && sleep 1 && break
    sleep 1
  done
  pkill -f "$TMP/profile" 2>/dev/null || true
  [ -s "$png" ] && echo "rendered $name" || echo "FAILED $name"
done
