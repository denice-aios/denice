#!/usr/bin/env bash
# Copyright (c) Denice.ai. All rights reserved.
#
# Writes <version-dir>/SHA256SUMS over every file in the version folder: one `<hash>  <name>` line (two spaces, the form
# Denice Runtime and install.sh read; decision checksum-gnu-format) per file, sorted, without SHA256SUMS, its cosign
# bundle and the loader's run markers. The one writer of that file: CI staging (eng/ci-stage-layout.sh) and the install
# tests (eng/install/test-install.sh, eng/install/test-real-install.sh) all use it.
# sha256sum in Git Bash on Windows writes binary-mode `<hash> *<name>`, which is rewritten (release
# 0.1.108556414-pre-release shipped it).
#
# Usage: eng/write-sha256sums.sh <version-dir>
set -euo pipefail

[ "$#" -eq 1 ] && [ -d "$1" ] || { echo "usage: write-sha256sums.sh <version-dir>" >&2; exit 2; }
if command -v sha256sum >/dev/null 2>&1; then SUM=(sha256sum); else SUM=(shasum -a 256); fi
cd "$1"
rm -f SHA256SUMS SHA256SUMS.cosign.bundle .started .bad .running
find . -type f ! -name SHA256SUMS ! -name SHA256SUMS.cosign.bundle | sed 's#^\./##' | LC_ALL=C sort \
  | while IFS= read -r file; do "${SUM[@]}" "$file"; done | sed 's/^\([0-9a-f]\{64\}\) \*/\1  /' > SHA256SUMS
"${SUM[@]}" -c SHA256SUMS > /dev/null      # proof in the build
