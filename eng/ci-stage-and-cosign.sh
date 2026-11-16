#!/usr/bin/env bash
# CI-only: stage each signed per-RID binary under its FINAL release name, write a GNU two-column SHA-256
# checksum (decision checksum-gnu-format), self-verify it (sha256sum -c / shasum -c — "proof in the build"),
# and cosign-sign it keyless (decision signing-mechanisms). Reused by all three per-OS jobs so the checksum and
# cosign bundle are computed over the EXACT bytes that ship (decisions release-asset-names / verify-sigs-before-publish).
#
# Naming the asset here (denice-v<version>-<rid>[.exe], version from DENICE_ASSET_VERSION) — not in the release job — is what keeps each .sha256 and
# .cosign.bundle valid against the released file: the artifact round-trip preserves bytes, and the release job's
# chmod +x changes only the mode, never the bytes/hash.
#
# Usage: eng/ci-stage-and-cosign.sh <dist-dir> <ext> <rid> [<rid> ...]
#   ext = "" on macOS/Linux, ".exe" on Windows.
# Requires cosign (from sigstore/cosign-installer) and sha256sum or shasum on PATH. Runs on macOS, Linux, and
# Windows (Git bash).
set -euo pipefail

: "${DENICE_ASSET_VERSION:?DENICE_ASSET_VERSION (the full build version) must be set}"
DIST="$1"; EXT="$2"; shift 2
mkdir -p "$DIST"

for rid in "$@"; do
  cp "src/Denice.Runtime/bin/Release/net10.0/$rid/publish/denice$EXT" "$DIST/denice-v${DENICE_ASSET_VERSION}-$rid$EXT"
done

cd "$DIST"
if command -v sha256sum >/dev/null 2>&1; then SUM=(sha256sum); else SUM=(shasum -a 256); fi

for rid in "$@"; do
  name="denice-v${DENICE_ASSET_VERSION}-$rid"
  bin="$name$EXT"
  "${SUM[@]}" "$bin" > "$name.sha256"          # GNU two-column: "<hex>  <name>"
  "${SUM[@]}" -c "$name.sha256"                # proof in the build
  cosign sign-blob --yes --bundle "$name.cosign.bundle" "$bin"
  echo "staged + checksummed + cosigned $bin"
done
