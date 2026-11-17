#!/usr/bin/env bash
# CI-only (decision verify-sigs-before-publish): verify staged release files and record a machine-checkable result.
# For each dist/denice-v<version>-<rid>.zip:
#   - the zip matches its .zip.sha256;
#   - unpacked, versions/<version>/SHA256SUMS matches every file, and its cosign bundle verifies against this workflow's
#     keyless identity (the check Denice Runtime repeats before using a downloaded version);
#   - the root loader equals the carried loader;
#   - native signatures: codesign --verify --strict on every Mach-O (mac), signtool verify /pa on Denice's own .exe and
#     Denice.*.dll files (Windows), none on Linux (cosign only).
# Writes the command output plus a final "RESULT: PASS" or "RESULT: FAIL" to <result-file>; exits non-zero on failure.
#
# Usage: eng/ci-verify-layout.sh <result-file> <codesign|signtool|none> <identity> <dist-dir> <rid> [<rid> ...]
set -uo pipefail

if [ "$#" -lt 5 ]; then
  echo "usage: ci-verify-layout.sh <result-file> <native> <identity> <dist-dir> <rid> [<rid> ...]" >&2
  exit 2
fi
RESULT="$1"; NATIVE="$2"; IDENTITY="$3"; DIST="$4"; shift 4
ISSUER="https://token.actions.githubusercontent.com"
: "${DENICE_ASSET_VERSION:?DENICE_ASSET_VERSION must be set}"
mkdir -p "$(dirname "$RESULT")"
if command -v sha256sum >/dev/null 2>&1; then SUM=(sha256sum); else SUM=(shasum -a 256); fi
find_signtool() { find "/c/Program Files (x86)/Windows Kits/10/bin" -name signtool.exe 2>/dev/null | grep -i '/x64/' | sort -r | head -1; }

ok=1
{
  echo "verify results for ${GITHUB_SHA:-<local>} at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "identity: $IDENTITY"
  SIGNTOOL=""
  if [ "$NATIVE" = signtool ]; then SIGNTOOL="$(find_signtool)"; [ -n "$SIGNTOOL" ] || { echo "signtool.exe not found"; ok=0; }; fi
  for rid in "$@"; do
    name="denice-v${DENICE_ASSET_VERSION}-$rid"
    case "$rid" in win-*) EXE=".exe" ;; *) EXE="" ;; esac
    echo "== $name.zip =="
    ( cd "$DIST" && "${SUM[@]}" -c "$name.zip.sha256" ) 2>&1 || { echo "zip checksum FAIL: $rid"; ok=0; }
    work="$(mktemp -d)"
    # Git Bash on the Windows runner may lack Info-ZIP; 7-Zip unpacks the same zip there.
    if command -v unzip > /dev/null 2>&1; then unpack=(unzip -q "$DIST/$name.zip" -d "$work"); else unpack=(7z x -bso0 -bsp0 "-o$work" "$DIST/$name.zip"); fi
    "${unpack[@]}" || { echo "unzip FAIL: $rid"; ok=0; continue; }
    vdir="$work/versions/$DENICE_ASSET_VERSION"
    ( cd "$vdir" && "${SUM[@]}" -c --quiet SHA256SUMS ) 2>&1 && echo "SHA256SUMS PASS: $rid" || { echo "SHA256SUMS FAIL: $rid"; ok=0; }
    if cosign verify-blob --certificate-identity "$IDENTITY" --certificate-oidc-issuer "$ISSUER" \
         --bundle "$vdir/SHA256SUMS.cosign.bundle" "$vdir/SHA256SUMS" 2>&1; then echo "cosign PASS: $rid"; else echo "cosign FAIL: $rid"; ok=0; fi
    case "$rid" in osx-*) LOADER="Denice.app" ;; *) LOADER="denice$EXE" ;; esac
    diff -rq "$work/$LOADER" "$vdir/$LOADER" > /dev/null && echo "loader copy PASS: $rid" || { echo "loader copy FAIL: $rid"; ok=0; }
    case "$NATIVE" in
      codesign)
        while IFS= read -r -d '' f; do
          if file "$f" | grep -q 'Mach-O'; then
            codesign --verify --strict "$f" 2>&1 || { echo "codesign FAIL: ${f#$work/}"; ok=0; }
          fi
        done < <(find "$work" -type f -print0)
        for app in "$work/Denice.app" "$vdir/Denice.app"; do
          codesign --verify --deep --strict "$app" 2>&1 || { echo "codesign FAIL: ${app#$work/}"; ok=0; }
        done
        echo "codesign checked: $rid"
        ;;
      signtool)
        while IFS= read -r -d '' f; do
          MSYS_NO_PATHCONV=1 "$SIGNTOOL" verify /pa /q "$f" 2>&1 || { echo "signtool FAIL: ${f#$work/}"; ok=0; }
        done < <(find "$work" -type f \( -name 'denice*.exe' -o -name 'Denice.*.dll' \) -print0)
        echo "signtool checked: $rid"
        ;;
      none) ;;
      *) echo "unknown native mode '$NATIVE'"; ok=0 ;;
    esac
    rm -rf "$work"
  done
  if [ "$ok" = 1 ]; then echo "RESULT: PASS"; else echo "RESULT: FAIL"; fi
} > "$RESULT" 2>&1

cat "$RESULT"
grep -q "^RESULT: PASS$" "$RESULT"
