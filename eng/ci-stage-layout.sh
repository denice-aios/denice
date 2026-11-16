#!/usr/bin/env bash
# CI-only: turn one platform's signed install layout into its release file.
#
#   1. versions/<version>/SHA256SUMS lists the SHA-256 of every file in the version folder (GNU two-column format,
#      decision checksum-gnu-format), and the loader at the layout root must equal the loader the version carries;
#   2. cosign signs SHA256SUMS keyless (decision signing-mechanisms) into versions/<version>/SHA256SUMS.cosign.bundle,
#      so the signature and the checksums travel inside the release file, as the old one-binary zips carried theirs
#      (decision release-zip-layout);
#   3. the layout is zipped as dist/denice-v<version>-<rid>.zip (the asset name keeps decision release-asset-names),
#      with a dist/denice-v<version>-<rid>.zip.sha256 beside it.
# Denice Runtime checks the same SHA256SUMS and bundle before it moves a downloaded version into place.
#
# Usage: eng/ci-stage-layout.sh <layout-dir> <dist-dir> <rid>
# Requires: DENICE_ASSET_VERSION, cosign, zip, sha256sum or shasum. Runs on macOS, Linux and Windows (Git Bash).
set -euo pipefail

: "${DENICE_ASSET_VERSION:?DENICE_ASSET_VERSION (the full build version) must be set}"
LAYOUT="$(cd "$1" && pwd)"; DIST="$2"; RID="$3"
mkdir -p "$DIST"; DIST="$(cd "$DIST" && pwd)"
VERSION_DIR="$LAYOUT/versions/$DENICE_ASSET_VERSION"
case "$RID" in win-*) EXE=".exe" ;; *) EXE="" ;; esac
if command -v sha256sum >/dev/null 2>&1; then SUM=(sha256sum); else SUM=(shasum -a 256); fi

[ -d "$VERSION_DIR" ] || { echo "ci-stage-layout: no version folder $VERSION_DIR" >&2; exit 1; }
cmp -s "$LAYOUT/denice$EXE" "$VERSION_DIR/denice$EXE" \
  || { echo "ci-stage-layout: the root loader differs from the loader the version carries" >&2; exit 1; }

( cd "$VERSION_DIR"
  rm -f SHA256SUMS SHA256SUMS.cosign.bundle .started .bad
  find . -type f ! -name SHA256SUMS ! -name SHA256SUMS.cosign.bundle | sed 's#^\./##' | LC_ALL=C sort \
    | while IFS= read -r file; do "${SUM[@]}" "$file"; done > SHA256SUMS
  "${SUM[@]}" -c SHA256SUMS > /dev/null      # proof in the build
  cosign sign-blob --yes --bundle SHA256SUMS.cosign.bundle SHA256SUMS )

name="denice-v${DENICE_ASSET_VERSION}-$RID"
rm -f "$DIST/$name.zip"
( cd "$LAYOUT" && zip -q -r -X -y "$DIST/$name.zip" "denice$EXE" versions )
( cd "$DIST" && "${SUM[@]}" "$name.zip" > "$name.zip.sha256" && "${SUM[@]}" -c "$name.zip.sha256" )
echo "ci-stage-layout: staged $DIST/$name.zip ($(wc -l < "$VERSION_DIR/SHA256SUMS" | tr -d ' ') files checksummed and cosigned)"
