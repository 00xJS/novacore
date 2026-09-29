#!/bin/zsh
# Regenerate the ship art: ship_art.py writes an SVG per hull and render_svgs.sh
# renders each to a transparent 512 x 512 PNG in the game's Resources/Ships
# (where ShipArt.Photo loads it). Needs python3 and Google Chrome.
#
# usage: scripts/art/render_ships.sh [hull ...]    (no hulls = all of them)
set -e
HERE="${0:A:h}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
python3 "$HERE/ship_art.py" "$TMP/svg" "$@"
rm -f "$TMP/svg/sheet.html"
"$HERE/render_svgs.sh" "$TMP/svg" "$HERE/../../GalaxyRoyale/Assets/Resources/Ships" 512
