#!/bin/zsh
# Regenerate the app icon and the launch logo: icon_art.py writes the SVGs,
# render_svgs.sh renders them, png_opaque.py flattens the icon onto the game's
# background (App Store Connect rejects an icon with an alpha channel) and sips
# makes the 180 px copy. The launch logo (960 px, for @3x) keeps its
# transparency: the launch screen puts it on the same dark background, and
# IosPostProcess moves it into the app's asset catalog. Needs python3 and Chrome.
#
# usage: scripts/art/render_icon.sh
set -e
HERE="${0:A:h}"
ASSETS="$HERE/../../GalaxyRoyale/Assets"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
python3 "$HERE/icon_art.py" "$TMP/svg"
"$HERE/render_svgs.sh" "$TMP/svg" "$TMP/png"
python3 "$HERE/png_opaque.py" "$TMP/png/icon.png" "$ASSETS/icon1024.png" 05030E
sips -z 180 180 "$ASSETS/icon1024.png" --out "$ASSETS/icon180.png" >/dev/null
mkdir -p "$ASSETS/../iOSLaunch"
sips -z 960 960 "$TMP/png/launch.png" --out "$ASSETS/../iOSLaunch/LaunchLogo.png" >/dev/null
echo "icon1024.png, icon180.png and iOSLaunch/LaunchLogo.png updated"
