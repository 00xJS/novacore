#!/bin/zsh
# Regenerate the Frontier's late buildings (the Repair Dock, the Jump Gate, the
# Clan Embassy, the Deep Space Observatory): building_art.py writes the SVGs,
# render_svgs.sh renders them and png_trim.swift crops each to its visible
# extent in the game's Resources/Buildings. Needs python3, Chrome and swift.
#
# usage: scripts/art/render_buildings.sh [name ...]    (no names = all four)
set -e
HERE="${0:A:h}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
python3 "$HERE/building_art.py" "$TMP/svg" "$@"
"$HERE/render_svgs.sh" "$TMP/svg" "$TMP/png" 512
DEST="$HERE/../../GalaxyRoyale/Assets/Resources/Buildings"
for png in "$TMP/png"/*.png; do
  swift "$HERE/png_trim.swift" "$png" "$DEST/${png:t}"
done
