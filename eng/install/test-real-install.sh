#!/usr/bin/env bash
# Installs a REAL built layout with eng/install/install.sh (macOS / Linux) under a temporary HOME, runs Denice through
# the ~/.local/bin/denice link the script creates, checks that a new login shell finds `denice` on PATH through the
# shell profiles the script changed (pkg-d4e90c3ce176 install-path), then uninstalls and checks nothing it installed
# remains (rust/REQUIREMENTS.md I3 I4 I5 I6). Updates are switched off through Denice's own Settings, so nothing reaches the
# network.
# Usage: eng/install/test-real-install.sh <rid> <layout-dir> <work-dir>
set -euo pipefail

RID="$1"; LAYOUT="$(cd "$2" && pwd)"; WORK="$3"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "real-install: FAIL: $*" >&2; exit 1; }

rm -rf "$WORK"; mkdir -p "$WORK/home"; WORK="$(cd "$WORK" && pwd)"
VERSION="$(ls "$LAYOUT/versions" | head -1)"
ZIP="$WORK/denice-v$VERSION-$RID.zip"
case "$RID" in osx-*) LOADER="Denice.app" ;; *) LOADER="denice" ;; esac
( cd "$LAYOUT" && zip -q -r -X -y "$ZIP" "$LOADER" versions )
( cd "$WORK" && { command -v sha256sum >/dev/null && sha256sum "$(basename "$ZIP")" || shasum -a 256 "$(basename "$ZIP")"; } > "$ZIP.sha256" )

export HOME="$WORK/home"
unset XDG_DATA_HOME
case "$RID" in
  osx-*) SETTINGS="$HOME/Library/Application Support/Denice" ;;
  *)     SETTINGS="$HOME/.config/denice" ;;
esac
mkdir -p "$SETTINGS" && printf 'updates.enabled: false\n' > "$SETTINGS/settings.yaml"

sh "$REPO_ROOT/eng/install/install.sh" --from "$ZIP"
out="$("$HOME/.local/bin/denice" version)" || fail "the installed Denice did not run"
case "$out" in *"SemVer: $VERSION"*) ;; *) fail "expected $VERSION, got: $out" ;; esac
echo "real-install: ok: installed $VERSION and ran it through ~/.local/bin/denice"

# A new login shell, given only HOME and a minimal PATH, finds denice through the profile and runs it.
for login in sh bash zsh; do
  command -v "$login" >/dev/null || { echo "real-install: skip $login -l: not installed"; continue; }
  out="$(env -i HOME="$HOME" PATH="/usr/bin:/bin:/usr/sbin:/sbin" "$login" -l -c 'command -v denice && denice version' < /dev/null)" \
    || fail "a new '$login -l' shell did not find and run denice: $out"
  case "$out" in "$HOME/.local/bin/denice"*"SemVer: $VERSION"*) ;; *) fail "'$login -l' found the wrong denice: $out" ;; esac
  echo "real-install: ok: a new '$login -l' shell runs denice from PATH"
done

sh "$REPO_ROOT/eng/install/install.sh" --uninstall
[ ! -e "$HOME/.local/bin/denice" ] || fail "the link was left behind"
case "$RID" in
  osx-*) [ ! -e "$HOME/Applications/Denice.app" ] || fail "Denice.app was left behind"
         [ ! -e "$SETTINGS/versions" ] || fail "the versions were left behind" ;;
  *)     [ ! -e "$HOME/.local/share/denice" ] || fail "the install folder was left behind" ;;
esac
[ -f "$SETTINGS/settings.yaml" ] || fail "uninstall removed the user's settings"
[ ! -e "$HOME/.profile" ] || fail "uninstall left the ~/.profile the install created"
! grep -sqF "added by the Denice installer" "$HOME/.zshenv" "$HOME/.bashrc" || fail "uninstall left a PATH line in a profile"
echo "real-install: ok: uninstalled; settings kept"
