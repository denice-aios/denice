#!/usr/bin/env bash
# Binds a GPU job's hardware coverage to the exact source tree it built, in the layout eng/find-hardware-coverage.sh
# reads. Shared by the Windows and Linux GPU jobs in .github/workflows/desktop-capability.yml.
# Usage: eng/bind-hardware-coverage.sh <target rid> <fixture mp4 name>
# Env: TREE, CHECKOUT (40-hex SHAs), BUILD_RUN_ID; GitHub's GITHUB_RUN_ID, GITHUB_RUN_ATTEMPT, GITHUB_EVENT_NAME.
set -euo pipefail
TARGET="$1" MP4="$2"
[[ $TARGET =~ ^(win|linux)-x64$ ]] || { echo "bind-hardware-coverage: unsupported target '$TARGET'" >&2; exit 2; }
[[ $TREE =~ ^[0-9a-f]{40}$ && $CHECKOUT =~ ^[0-9a-f]{40}$ && $BUILD_RUN_ID =~ ^[0-9]+$ && $GITHUB_RUN_ID =~ ^[0-9]+$ && $GITHUB_RUN_ATTEMPT =~ ^[0-9]+$ ]]
mkdir -p hardware-coverage/hardware-fixtures hardware-coverage/hardware-coverage
cp evidence/hardware.trx hardware-coverage/
cp "evidence/hardware-fixtures/$MP4" hardware-coverage/hardware-fixtures/
cp evidence/hardware-coverage/coverage.cobertura.xml hardware-coverage/hardware-coverage/
printf '{"target":"%s","tree_sha":"%s","checkout_sha":"%s","build_run_id":"%s","gpu_run_id":"%s","run_attempt":"%s","event":"%s"}\n' \
  "$TARGET" "$TREE" "$CHECKOUT" "$BUILD_RUN_ID" "$GITHUB_RUN_ID" "$GITHUB_RUN_ATTEMPT" "$GITHUB_EVENT_NAME" > hardware-coverage/binding.json
echo "$TARGET hardware coverage bound to tree $TREE (commit $CHECKOUT)." >> "$GITHUB_STEP_SUMMARY"
