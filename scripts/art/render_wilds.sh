#!/bin/zsh
# Regenerate the Wilds' art: wilds_art.py writes the SVGs (finds, the survey
# beacon, the drone) and render_svgs.sh renders them to transparent PNGs in the
# game's Resources/Wilds (where WildsView loads them). Needs python3 and Chrome.
#
# usage: scripts/art/render_wilds.sh [name ...]    (no names = all of them)
set -e
HERE="${0:A:h}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
python3 "$HERE/wilds_art.py" "$TMP/svg" "$@"
"$HERE/render_svgs.sh" "$TMP/svg" "$HERE/../../GalaxyRoyale/Assets/Resources/Wilds" 256
