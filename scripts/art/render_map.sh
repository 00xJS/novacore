#!/bin/zsh
# Regenerate the galaxy map's art: map_art.py writes the SVGs (the planet atlas,
# the type badges, the Core's sun and its zone) and render_svgs.sh renders them
# to transparent PNGs in the game's Resources/Map (where MapView loads them).
# Needs python3 and Google Chrome.
#
# usage: scripts/art/render_map.sh [name ...]    (no names = all of them)
set -e
HERE="${0:A:h}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
python3 "$HERE/map_art.py" "$TMP/svg" "$@"
"$HERE/render_svgs.sh" "$TMP/svg" "$HERE/../../GalaxyRoyale/Assets/Resources/Map" 256
