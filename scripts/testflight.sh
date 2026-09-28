#!/bin/zsh
# Galaxy Royale → TestFlight: Unity release build, Xcode archive, App Store
# export — and, only with --upload, the upload to App Store Connect.
#
#   GR_BUNDLE_ID=com.you.galaxyroyale GR_TEAM_ID=ABCDE12345 scripts/testflight.sh            # build + sign an .ipa
#   GR_BUNDLE_ID=com.you.galaxyroyale GR_TEAM_ID=ABCDE12345 scripts/testflight.sh --upload   # ...and send it
#
# Before the first upload:
#   1. App Store Connect → Apps → + New App, with the SAME bundle id (the
#      upload has nowhere to land without the app record).
#   2. Xcode → Settings → Accounts: be signed in to the developer team (or
#      pass an App Store Connect API key, below). Signing is automatic; the
#      first archive may create the team's Apple Distribution certificate.
#
# Optional environment:
#   GR_BUILD_NUMBER   build number (default: UTC yymmddHHMM — always rising)
#   GR_VERSION        marketing version (default: BuildScript's)
#   GR_ASC_KEY_PATH / GR_ASC_KEY_ID / GR_ASC_ISSUER_ID
#                     App Store Connect API key (.p8) instead of the Xcode account
#   GR_UNITY          Unity editor binary (default: the Hub's 6000.5.2f1)
#   GR_ICLOUD=0       build without the iCloud save-backup capability
set -euo pipefail

UPLOAD=0
for arg in "$@"; do
  case "$arg" in
    --upload) UPLOAD=1 ;;
    -h|--help) sed -n '2,23p' "$0"; exit 0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

: "${GR_BUNDLE_ID:?set GR_BUNDLE_ID to your app's bundle id}"
: "${GR_TEAM_ID:?set GR_TEAM_ID to your Apple developer team id}"
export GR_BUNDLE_ID GR_TEAM_ID
export GR_BUILD_NUMBER="${GR_BUILD_NUMBER:-$(date -u +%y%m%d%H%M)}"

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO/GalaxyRoyale"
UNITY="${GR_UNITY:-/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/MacOS/Unity}"
OUT="$PROJECT/Builds/TestFlight/$GR_BUILD_NUMBER"
mkdir -p "$OUT"

AUTH=()
if [[ -n "${GR_ASC_KEY_PATH:-}" ]]; then
  : "${GR_ASC_KEY_ID:?GR_ASC_KEY_ID is required with GR_ASC_KEY_PATH}"
  : "${GR_ASC_ISSUER_ID:?GR_ASC_ISSUER_ID is required with GR_ASC_KEY_PATH}"
  AUTH=(-authenticationKeyPath "$GR_ASC_KEY_PATH" -authenticationKeyID "$GR_ASC_KEY_ID"
        -authenticationKeyIssuerID "$GR_ASC_ISSUER_ID")
fi

echo "==> Unity release build (build $GR_BUILD_NUMBER)"
# A batch build rewrites files under ProjectSettings/ (identity, services
# toggles): snapshot the folder and put it back however the script ends.
SETTINGS="$PROJECT/ProjectSettings"
rm -rf "$OUT/ProjectSettings.orig"
cp -R "$SETTINGS" "$OUT/ProjectSettings.orig"
trap 'cp -R "$OUT/ProjectSettings.orig/." "$SETTINGS/"' EXIT
# Unity can exit non-zero at shutdown after a good build — the log decides.
"$UNITY" -batchmode -projectPath "$PROJECT" -buildTarget iOS \
  -executeMethod BuildScript.BuildIos -logFile "$OUT/unity.log" || true
if ! grep -q '\[Build\] iOS: Succeeded' "$OUT/unity.log"; then
  echo "Unity build failed — see $OUT/unity.log" >&2
  grep -E 'error CS[0-9]+|\[Build\]' "$OUT/unity.log" | head -20 >&2 || true
  exit 1
fi

echo "==> Xcode archive"
xcodebuild -project "$PROJECT/Builds/iOS/Unity-iPhone.xcodeproj" -scheme Unity-iPhone \
  -configuration Release -destination 'generic/platform=iOS' \
  -archivePath "$OUT/GalaxyRoyale.xcarchive" -allowProvisioningUpdates "${AUTH[@]}" \
  DEVELOPMENT_TEAM="$GR_TEAM_ID" CODE_SIGN_STYLE=Automatic \
  archive > "$OUT/archive.log" 2>&1 || { tail -30 "$OUT/archive.log" >&2; exit 1; }

DESTINATION=export
(( UPLOAD )) && DESTINATION=upload
cat > "$OUT/ExportOptions.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>method</key><string>app-store-connect</string>
  <key>destination</key><string>$DESTINATION</string>
  <key>teamID</key><string>$GR_TEAM_ID</string>
  <key>signingStyle</key><string>automatic</string>
  <key>uploadSymbols</key><true/>
  <key>manageAppVersionAndBuildNumber</key><false/>
</dict>
</plist>
PLIST

echo "==> Export ($DESTINATION)"
xcodebuild -exportArchive -archivePath "$OUT/GalaxyRoyale.xcarchive" \
  -exportOptionsPlist "$OUT/ExportOptions.plist" -exportPath "$OUT/export" \
  -allowProvisioningUpdates "${AUTH[@]}" > "$OUT/export.log" 2>&1 || { tail -30 "$OUT/export.log" >&2; exit 1; }

if (( UPLOAD )); then
  echo "Uploaded build $GR_BUILD_NUMBER. App Store Connect processes it (usually 5-30 min);"
  echo "then it appears under TestFlight — add yourself as an internal tester to install it."
else
  echo "Signed App Store build: $(ls "$OUT"/export/*.ipa)"
  echo "Nothing was uploaded. Re-run with --upload to send a build to TestFlight."
fi
