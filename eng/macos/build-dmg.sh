#!/usr/bin/env bash
# Builds the drag-to-Applications disk image for one Denice.app: a compressed read-only DMG whose window holds
# Denice.app and a link to /Applications. With --sign it also code-signs the DMG itself (Apple notarizes a signed DMG;
# eng/macos/notarize.sh does that step). Without --sign the DMG is unsigned, which is how it runs on a developer Mac.
#
# Usage: eng/macos/build-dmg.sh <Denice.app> <out.dmg> [--sign <identity> [--keychain <keychain>] [--timestamp]]
#   <identity>   a codesign identity (SHA-1 or name); --timestamp asks Apple's timestamp server, which only a real
#                Developer ID may use (CI passes it when DENICE_MACOS_SIGNING=developer-id).
# Requires: macOS (hdiutil, ditto, codesign). Writes only <out.dmg> and a temporary folder it removes.
set -euo pipefail

usage() { echo "usage: $0 <Denice.app> <out.dmg> [--sign <identity> [--keychain <keychain>] [--timestamp]]" >&2; exit 2; }
[ $# -ge 2 ] || usage
APP="$1"; OUT="$2"; shift 2
IDENTITY=""; KEYCHAIN=""; TIMESTAMP=""
while [ $# -gt 0 ]; do
  case "$1" in
    --sign) [ $# -ge 2 ] || usage; IDENTITY="$2"; shift 2 ;;
    --keychain) [ $# -ge 2 ] || usage; KEYCHAIN="$2"; shift 2 ;;
    --timestamp) TIMESTAMP="--timestamp"; shift ;;
    *) usage ;;
  esac
done
if [ -z "$IDENTITY" ] && { [ -n "$KEYCHAIN" ] || [ -n "$TIMESTAMP" ]; }; then
  echo "build-dmg: --keychain and --timestamp need --sign" >&2; exit 2
fi
[ -d "$APP" ] && [ -f "$APP/Contents/Info.plist" ] || { echo "build-dmg: $APP is not an app bundle" >&2; exit 1; }
[ "$(basename "$APP")" = "Denice.app" ] || { echo "build-dmg: expected Denice.app, got $(basename "$APP")" >&2; exit 1; }

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/denice-dmg.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
# ditto keeps the app's code signature, extended attributes and any stapled notarization ticket intact.
ditto "$APP" "$STAGE/Denice.app"
ln -s /Applications "$STAGE/Applications"

mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"
hdiutil create -volname Denice -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$OUT" >/dev/null

if [ -n "$IDENTITY" ]; then
  # shellcheck disable=SC2086  # TIMESTAMP is empty or one flag
  codesign --force --sign "$IDENTITY" ${KEYCHAIN:+--keychain "$KEYCHAIN"} $TIMESTAMP "$OUT"
  codesign --verify --strict "$OUT"
fi
hdiutil verify -quiet "$OUT"
echo "build-dmg: wrote $OUT ($([ -n "$IDENTITY" ] && echo signed || echo unsigned))"
