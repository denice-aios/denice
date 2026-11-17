#!/usr/bin/env bash
# Checks a PUBLISHED Denice release, from its downloaded assets, for rust/REQUIREMENTS.md F9 F10 F11 F14:
#   - the release holds, for all six platforms (osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-arm64), the
#     install-layout zip denice-v<version>-<rid>.zip with its .zip.sha256, the two Windows setup programs with their
#     .sha256 and .cosign.bundle, and install.sh (F11);
#   - each zip matches its .zip.sha256; unpacked, it holds exactly the loader (Denice.app on macOS, denice[.exe]) and
#     versions/<version>/ with the versioned entry denice-v<version>-<rid>[.exe], Denice Runtime, SHA256SUMS and its
#     cosign bundle, and the loader the version carries equals the root loader (F11);
#   - SHA256SUMS lists every file of the version folder, no more and no fewer, and each matches (F10);
#   - every cosign bundle (each SHA256SUMS and each setup program) verifies against Denice's CI workflow identity, the
#     same identity pattern and issuer Denice Runtime pins (src/Denice.Updates/ReleaseSignature.cs) (F10);
#   - macOS (needs codesign, so only on a Mac): every Mach-O passes `codesign --verify --strict`; the loader app (both
#     copies) and the entry carry the hardened runtime and are signed by a pinned Denice certificate (F9 F14);
#   - Windows (any OS: eng/check-authenticode.py): the loader (both copies), the entry, the Denice.*.dll assemblies and
#     both setup programs carry an intact Authenticode signature by a pinned Denice certificate (F9 F14).
# The pinned certificates are read from src/Denice.Updates/DeniceSigningCertificates.cs, the list Denice Runtime uses.
# Writes every check and a final "RESULT: PASS" or "RESULT: FAIL" to <work-dir>/check-release.txt; exits 1 on FAIL.
#
# Usage: eng/check-release.sh <tag> <work-dir>      e.g. eng/check-release.sh 0.1.108556414-pre-release /tmp/rel
# Requires: gh (authenticated, or GH_TOKEN), cosign, unzip, python3, openssl; codesign for the macOS checks.
set -uo pipefail

[ "$#" -eq 2 ] || { echo "usage: check-release.sh <tag> <work-dir>" >&2; exit 2; }
TAG="$1"; WORK="$2"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO="denice-aios/denice"
RIDS=(osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-arm64)
SIGNATURE_SOURCE="$REPO_ROOT/src/Denice.Updates/ReleaseSignature.cs"
IDENTITY="$(sed -n 's/^ *@"\(\^https:.*\$\)";$/\1/p' "$SIGNATURE_SOURCE")"
ISSUER="$(sed -n 's/.*GitHubActionsIssuer = "\(.*\)";$/\1/p' "$SIGNATURE_SOURCE")"
PINNED="$(grep -oE '"([0-9A-F]{2}:){31}[0-9A-F]{2}"' "$REPO_ROOT/src/Denice.Updates/DeniceSigningCertificates.cs" | tr -d '"' | paste -sd, -)"
if command -v sha256sum >/dev/null 2>&1; then SUM=(sha256sum); else SUM=(shasum -a 256); fi

rm -rf "$WORK"; mkdir -p "$WORK/assets"; WORK="$(cd "$WORK" && pwd)"
RESULT="$WORK/check-release.txt"
ok=1
pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*"; ok=0; }

pinned_mach_o() { # <path>: hardened runtime + leaf certificate pinned
  local path="$1" details certs fingerprint
  details="$(codesign -d --verbose=2 "$path" 2>&1)"
  case "$details" in *"flags="*"(runtime)"*) ;; *) fail "no hardened runtime: ${path#$WORK/}"; return ;; esac
  certs="$(mktemp -d)"
  ( cd "$certs" && codesign -d --extract-certificates=cert "$path" >/dev/null 2>&1 )
  fingerprint="$(openssl x509 -inform DER -in "$certs/cert0" -noout -fingerprint -sha256 2>/dev/null | cut -d= -f2)"
  rm -rf "$certs"
  case ",$PINNED," in
    *",$fingerprint,"*) pass "codesign hardened runtime, signer pinned $fingerprint: ${path#$WORK/}" ;;
    *) fail "codesign signer not pinned ('$fingerprint'): ${path#$WORK/}" ;;
  esac
}

{
  echo "check-release $TAG of $REPO at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "cosign identity: $IDENTITY ($ISSUER)"
  echo "pinned signers: $PINNED"
  [ -n "$IDENTITY" ] && [ -n "$ISSUER" ] && [ -n "$PINNED" ] || fail "could not read the pinned identity or certificates"
  command -v cosign >/dev/null || fail "cosign is not on PATH: the cosign bundles cannot be checked"
  gh release download "$TAG" --repo "$REPO" --dir "$WORK/assets" 2>&1 || fail "download of release $TAG"
  gh release view "$TAG" --repo "$REPO" --json tagName,isPrerelease,targetCommitish,publishedAt \
    --jq '"release: \(.tagName) prerelease=\(.isPrerelease) commit=\(.targetCommitish) published=\(.publishedAt)"' 2>&1
  cd "$WORK/assets" || exit 1
  ls -1 | sed 's/^/asset: /'

  # The asset version is the tag plus the build's +hash: find it from the one osx-arm64 zip.
  zips=( "denice-v$TAG"*"-osx-arm64.zip" )
  [ "${#zips[@]}" -eq 1 ] && [ -f "${zips[0]}" ] || fail "expected exactly one denice-v$TAG*-osx-arm64.zip"
  VERSION="${zips[0]#denice-v}"; VERSION="${VERSION%-osx-arm64.zip}"
  echo "asset version: $VERSION"

  for name in install.sh \
      "denice-setup-v$VERSION-win-x64.exe"{,.sha256,.cosign.bundle} "denice-setup-v$VERSION-win-arm64.exe"{,.sha256,.cosign.bundle} \
      $(for rid in "${RIDS[@]}"; do echo "denice-v$VERSION-$rid.zip" "denice-v$VERSION-$rid.zip.sha256"; done); do
    [ -f "$name" ] && pass "asset present: $name" || fail "asset missing: $name"
  done

  for setup in "denice-setup-v$VERSION-win-x64.exe" "denice-setup-v$VERSION-win-arm64.exe"; do
    "${SUM[@]}" -c "$setup.sha256" >/dev/null 2>&1 && pass "sha256: $setup" || fail "sha256: $setup"
    cosign verify-blob --certificate-identity-regexp "$IDENTITY" --certificate-oidc-issuer "$ISSUER" \
      --bundle "$setup.cosign.bundle" "$setup" >/dev/null 2>&1 && pass "cosign: $setup" || fail "cosign: $setup"
  done
  python3 -I "$REPO_ROOT/eng/check-authenticode.py" "$PINNED" "denice-setup-v$VERSION"-win-{x64,arm64}.exe || ok=0

  for rid in "${RIDS[@]}"; do
    name="denice-v$VERSION-$rid"
    case "$rid" in win-*) EXE=".exe" ;; *) EXE="" ;; esac
    case "$rid" in osx-*) LOADER="Denice.app" ;; *) LOADER="denice$EXE" ;; esac
    echo "== $name.zip =="
    "${SUM[@]}" -c "$name.zip.sha256" >/dev/null 2>&1 && pass "sha256: $name.zip" || fail "sha256: $name.zip"
    x="$WORK/unpacked/$rid"; mkdir -p "$x"
    unzip -q "$name.zip" -d "$x" || { fail "unzip: $name.zip"; continue; }
    v="$x/versions/$VERSION"

    top="$(ls -1A "$x" | paste -sd' ' -)"; versions="$(ls -1A "$x/versions" | paste -sd' ' -)"
    [ "$top" = "$(printf '%s\n' "$LOADER" versions | LC_ALL=C sort | paste -sd' ' -)" ] \
      && pass "layout root holds only $LOADER and versions/: $rid" || fail "layout root holds '$top': $rid"
    [ "$versions" = "$VERSION" ] && pass "one version folder $VERSION: $rid" || fail "version folders '$versions': $rid"
    for f in "denice-v$VERSION-$rid$EXE" Denice.Runtime.dll SHA256SUMS SHA256SUMS.cosign.bundle "$LOADER"; do
      [ -e "$v/$f" ] && pass "in the version: $f ($rid)" || fail "missing in the version: $f ($rid)"
    done
    case "$rid" in osx-*) entry_program="$x/$LOADER/Contents/MacOS/denice" ;; *) entry_program="$x/$LOADER" ;; esac
    [ -x "$entry_program" ] || [ -n "$EXE" ] && pass "loader is executable: $rid" || fail "loader is not executable: $rid"
    diff -rq "$x/$LOADER" "$v/$LOADER" >/dev/null && pass "carried loader equals the root loader: $rid" \
      || fail "carried loader differs from the root loader: $rid"

    # Denice Runtime reads only the "<sha256>  <name>" form (two spaces, VersionInstaller); `sha256sum -c` also takes
    # the binary-mode "<sha256> *<name>", so the form is checked on its own.
    odd="$(grep -cvE '^[0-9a-f]{64}  [^ *]' "$v/SHA256SUMS")"
    [ "$odd" = 0 ] && pass "SHA256SUMS is in the form Denice Runtime reads: $rid" \
      || fail "SHA256SUMS has $odd lines Denice Runtime refuses as malformed (not '<sha256>  <name>'), e.g. '$(grep -vE '^[0-9a-f]{64}  [^ *]' "$v/SHA256SUMS" | head -1)': $rid"
    listed="$(sed -E 's/^[0-9a-f]{64} [ *]//' "$v/SHA256SUMS" | LC_ALL=C sort)"
    present="$(cd "$v" && find . -type f ! -name SHA256SUMS ! -name SHA256SUMS.cosign.bundle | sed 's#^\./##' | LC_ALL=C sort)"
    [ "$listed" = "$present" ] && pass "SHA256SUMS lists exactly the $(wc -l <<< "$present" | tr -d ' ') files: $rid" \
      || fail "SHA256SUMS does not list exactly the version's files: $rid"
    ( cd "$v" && "${SUM[@]}" -c --quiet SHA256SUMS ) 2>&1 && pass "SHA256SUMS matches: $rid" || fail "SHA256SUMS: $rid"
    cosign verify-blob --certificate-identity-regexp "$IDENTITY" --certificate-oidc-issuer "$ISSUER" \
      --bundle "$v/SHA256SUMS.cosign.bundle" "$v/SHA256SUMS" >/dev/null 2>&1 \
      && pass "cosign SHA256SUMS: $rid" || fail "cosign SHA256SUMS: $rid"

    case "$rid" in
      osx-*)
        if ! command -v codesign >/dev/null; then
          echo "NOT CHECKED here (no codesign; run on a Mac): macOS code signatures of $rid"
          continue
        fi
        machos=0
        while IFS= read -r -d '' f; do
          file "$f" | grep -q 'Mach-O' || continue
          machos=$((machos + 1))
          codesign --verify --strict "$f" 2>&1 || fail "codesign --verify --strict: ${f#$WORK/}"
        done < <(find "$x" -type f -print0)
        pass "codesign --verify --strict on all $machos Mach-O files: $rid"
        for app in "$x/Denice.app" "$v/Denice.app"; do
          codesign --verify --deep --strict "$app" 2>&1 && pinned_mach_o "$app" || fail "codesign app: ${app#$WORK/}"
        done
        pinned_mach_o "$v/denice-v$VERSION-$rid"
        ;;
      win-*)
        python3 -I "$REPO_ROOT/eng/check-authenticode.py" "$PINNED" "$x/denice.exe" "$v/denice.exe" \
          "$v/denice-v$VERSION-$rid.exe" "$v"/Denice.*.dll || ok=0
        ;;
      linux-*) echo "no OS code signature on Linux: $rid rests on SHA256SUMS + cosign" ;;
    esac
  done

  if [ "$ok" = 1 ]; then echo "RESULT: PASS"; else echo "RESULT: FAIL"; fi
} > "$RESULT" 2>&1

cat "$RESULT"
grep -q "^RESULT: PASS$" "$RESULT"
