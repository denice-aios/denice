#!/usr/bin/env bash
# CI-only: notarize one signed Denice.app or DMG with Apple and staple the ticket to it.
#   .app — zipped with `ditto -c -k --keepParent` (the form notarytool takes), submitted, then the APP is stapled;
#   .dmg — submitted as it is, then the DMG is stapled.
# It waits for Apple's verdict, prints Apple's log when the submission is not Accepted, and ends with Gatekeeper's own
# assessment (spctl), so a pass means macOS will open the file without warnings.
#
# Credentials: an App Store Connect API key (the `notarytool --key --key-id --issuer` form), from the secrets
#   APPLE_NOTARY_KEY     the .p8 private key file's text (-----BEGIN PRIVATE KEY----- ... -----END PRIVATE KEY-----)
#   APPLE_NOTARY_KEY_ID  the key's Key ID
#   APPLE_NOTARY_ISSUER  the Issuer ID shown above the team keys in App Store Connect
# Only a Developer ID signature can be notarized, so ci.yml runs this only when DENICE_MACOS_SIGNING=developer-id
# (docs/signing-switchover.md).
#
# Usage: eng/macos/notarize.sh <Denice.app | file.dmg>
set -euo pipefail

[ $# -eq 1 ] || { echo "usage: $0 <Denice.app | file.dmg>" >&2; exit 2; }
TARGET="${1%/}"
for name in APPLE_NOTARY_KEY APPLE_NOTARY_KEY_ID APPLE_NOTARY_ISSUER; do
  if [ -z "${!name:-}" ]; then
    echo "::error::$name is not set; DENICE_MACOS_SIGNING=developer-id needs the Apple notarization secrets (docs/signing-switchover.md)." >&2
    exit 1
  fi
done

WORK="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/notarize.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
( umask 077 && printf '%s\n' "$APPLE_NOTARY_KEY" > "$WORK/key.p8" )

case "$TARGET" in
  *.app) SUBMIT="$WORK/$(basename "$TARGET" .app).zip"; ditto -c -k --keepParent "$TARGET" "$SUBMIT"; KIND=execute ;;
  *.dmg) SUBMIT="$TARGET"; KIND=open ;;
  *) echo "notarize: $TARGET is neither an .app nor a .dmg" >&2; exit 2 ;;
esac

AUTH=(--key "$WORK/key.p8" --key-id "$APPLE_NOTARY_KEY_ID" --issuer "$APPLE_NOTARY_ISSUER")
echo "notarize: submitting $(basename "$TARGET") and waiting for Apple"
xcrun notarytool submit "$SUBMIT" "${AUTH[@]}" --wait --timeout 30m --output-format json > "$WORK/result.json" || true
STATUS="$(plutil -extract status raw -o - "$WORK/result.json" 2>/dev/null || echo unknown)"
ID="$(plutil -extract id raw -o - "$WORK/result.json" 2>/dev/null || echo '')"
echo "notarize: $(basename "$TARGET") submission ${ID:-<none>} is $STATUS"
if [ "$STATUS" != "Accepted" ]; then
  cat "$WORK/result.json" >&2 || true
  [ -n "$ID" ] && xcrun notarytool log "$ID" "${AUTH[@]}" >&2 || true
  echo "::error::Apple did not accept $(basename "$TARGET") for notarization (status $STATUS)." >&2
  exit 1
fi

xcrun stapler staple "$TARGET"
xcrun stapler validate "$TARGET"
if [ "$KIND" = open ]; then
  spctl --assess --type open --context context:primary-signature -vv "$TARGET"
else
  spctl --assess --type execute -vv "$TARGET"
fi
echo "notarize: $(basename "$TARGET") notarized, stapled and accepted by Gatekeeper"
