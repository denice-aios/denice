#!/usr/bin/env bash
# Runs eng/install/test-real-install.sh as a NEW standard (non-administrator) user on a fresh CI machine (macOS or
# Linux), the clean-machine part of the release checks that needs no person (rust/REQUIREMENTS.md I10; the rest is
# docs/release-checks.md). The user can read only what it is given: the install scripts and the layout, copied to a
# shared folder.
# CI ONLY: it creates an operating-system account (sudo), so it refuses to run outside GitHub Actions.
# Usage: eng/install/test-standard-user.sh <rid> <layout-dir>
# Environment: DENICE_MACOS_SIGNING is passed on (test | developer-id).
set -euo pipefail

RID="$1"; LAYOUT="$(cd "$2" && pwd)"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
USER_NAME=denicestd
fail() { echo "standard-user: FAIL: $*" >&2; exit 1; }
[ "${GITHUB_ACTIONS:-}" = true ] || fail "this creates an operating-system user; it runs only on a GitHub Actions runner"

case "$RID" in
  osx-*)
    sudo sysadminctl -addUser "$USER_NAME" -password "Dn-$(uuidgen)" -home "/Users/$USER_NAME"
    sudo createhomedir -c -u "$USER_NAME" >/dev/null
    HOME_DIR="/Users/$USER_NAME"; SHARED=/Users/Shared/denice-standard-user
    if dseditgroup -o checkmember -m "$USER_NAME" admin >/dev/null; then fail "$USER_NAME is an administrator"; fi ;;
  linux-*)
    sudo useradd --create-home --shell /bin/bash "$USER_NAME"
    HOME_DIR="/home/$USER_NAME"; SHARED=/tmp/denice-standard-user
    case " $(id -nG "$USER_NAME") " in *" sudo "*|*" admin "*|*" wheel "*|*" root "*) fail "$USER_NAME is an administrator" ;; esac ;;
  *) fail "unsupported rid $RID (Windows has its own standard-user step in .github/workflows/ci.yml)" ;;
esac
echo "standard-user: $(id "$USER_NAME")"

rm -rf "$SHARED"; mkdir -p "$SHARED/eng"
cp -R "$REPO_ROOT/eng/install" "$SHARED/eng/install"
cp "$REPO_ROOT/eng/write-sha256sums.sh" "$SHARED/eng/"   # test-real-install.sh writes the layout's SHA256SUMS with it
cp -R "$LAYOUT" "$SHARED/layout"
chmod -R a+rX "$SHARED"
sudo chown -R "$USER_NAME" "$SHARED/layout"   # the user writes SHA256SUMS into its copy of the layout

cd /   # the runner's workspace is not the user's to read
sudo -u "$USER_NAME" -H env DENICE_MACOS_SIGNING="${DENICE_MACOS_SIGNING:-test}" \
  bash "$SHARED/eng/install/test-real-install.sh" "$RID" "$SHARED/layout" "$HOME_DIR/real-install"
echo "standard-user: ok: installed, ran and uninstalled as the standard user $USER_NAME"
