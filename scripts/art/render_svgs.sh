#!/bin/zsh
# Render every SVG in a folder to a transparent PNG with headless Chrome.
# Sizes come from the folder's sizes.txt ("<name> <px>" lines) when it has one,
# else the default size given.
#
# usage: scripts/art/render_svgs.sh <svg dir> <png dir> [default px]
set -e
SRC="$1"; OUT="$2"; DEFAULT="${3:-256}"
CH="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$OUT"
for svg in "$SRC"/*.svg; do
  name="${svg:t:r}"
  size=$DEFAULT
  if [ -f "$SRC/sizes.txt" ]; then
    s=$(awk -v n="$name" '$1 == n { print $2 }' "$SRC/sizes.txt")
    [ -n "$s" ] && size=$s
  fi
  png="$OUT/$name.png"
  rm -f "$png"
  "$CH" --headless=new --disable-gpu --hide-scrollbars --no-first-run --user-data-dir="$TMP/profile" \
    --window-size=$size,$size --force-device-scale-factor=1 --default-background-color=00000000 \
    --virtual-time-budget=5000 --screenshot="$png" "file://$svg" >/dev/null 2>&1 &
  pid=$!
  # Headless Chrome sometimes lingers after writing the file: give it 40 s at most.
  for i in {1..40}; do
    kill -0 $pid 2>/dev/null || break
    [ -s "$png" ] && sleep 1 && break
    sleep 1
  done
  pkill -f "$TMP/profile" 2>/dev/null || true
  [ -s "$png" ] && echo "rendered $name ($size px)" || echo "FAILED $name"
done
