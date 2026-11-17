#!/usr/bin/env bash
# Installs a REAL built layout with eng/install/install.sh (macOS / Linux) under a temporary HOME, runs Denice through
# the ~/.local/bin/denice link the script creates, checks that a new login shell finds `denice` on PATH through the
# shell profiles the script changed (pkg-d4e90c3ce176 install-path), then uninstalls and checks nothing it installed
# remains (rust/REQUIREMENTS.md I3 I4 I5 I6). Updates are switched off through Denice's own Settings, so nothing reaches the
# network.
# Release checks a clean machine can prove without a person (rust/REQUIREMENTS.md I10; the rest is docs/release-checks.md):
#   macOS  the zip carries com.apple.quarantine, as a browser download does; nothing installed keeps it (so Gatekeeper
#          never asks about the installed copy and the app is never translocated); Gatekeeper's verdict on the app is
#          printed, and must be "accepted" once the build is Developer ID signed (DENICE_MACOS_SIGNING=developer-id).
#   Linux  the menu entry is where GNOME and KDE read it ($XDG_DATA_HOME/applications, default ~/.local/share), passes
#          desktop-file-validate when the machine has it, and its Exec program runs.
# CI runs it as a new standard (non-administrator) user too.
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

case "$RID" in osx-*)
  # As a browser download: Safari's quarantine record (flags;time;agent;id).
  xattr -w com.apple.quarantine "0081;$(printf '%x' "$(date +%s)");Safari;$(uuidgen)" "$ZIP"
  xattr -p com.apple.quarantine "$ZIP" >/dev/null || fail "the test zip carries no quarantine attribute"
  echo "real-install: ok: the zip carries com.apple.quarantine, as a browser download" ;;
esac

sh "$REPO_ROOT/eng/install/install.sh" --from "$ZIP"
out="$("$HOME/.local/bin/denice" version)" || fail "the installed Denice did not run"
case "$out" in *"SemVer: $VERSION"*) ;; *) fail "expected $VERSION, got: $out" ;; esac
echo "real-install: ok: installed $VERSION and ran it through ~/.local/bin/denice"

case "$RID" in
  osx-*)
    APP="$HOME/Applications/Denice.app"
    for item in "$APP" "$SETTINGS/versions"; do
      case "$(xattr -r -l "$item")" in *com.apple.quarantine*) fail "$item keeps com.apple.quarantine after install" ;; esac
    done
    echo "real-install: ok: nothing installed is quarantined, so Gatekeeper never asks and the app is never translocated"
    verdict="$(spctl --assess --type execute -vv "$APP" 2>&1)" && accepted=yes || accepted=no
    echo "real-install: Gatekeeper on the installed app: $verdict"
    if [ "${DENICE_MACOS_SIGNING:-test}" = developer-id ]; then
      [ "$accepted" = yes ] || fail "Gatekeeper rejects the Developer ID signed app: $verdict"
      echo "real-install: ok: Gatekeeper accepts the Developer ID signed app"
    else
      echo "real-install: not Developer ID signed (F13): Gatekeeper would block a quarantined copy (docs/release-checks.md)"
    fi ;;
  *)
    # GNOME and KDE read menu entries from $XDG_DATA_HOME/applications (unset here, so ~/.local/share).
    DESK="$HOME/.local/share/applications/denice.desktop"
    [ -f "$DESK" ] || fail "no menu entry at $DESK"
    if command -v desktop-file-validate >/dev/null; then
      desktop-file-validate "$DESK" || fail "desktop-file-validate rejects $DESK"
      echo "real-install: ok: desktop-file-validate accepts the menu entry"
    else
      echo "real-install: skip desktop-file-validate: not on this machine"
    fi
    exec_program="$(sed -n 's/^Exec="\(.*\)"$/\1/p' "$DESK")"
    [ "$exec_program" = "$HOME/.local/share/denice/denice" ] || fail "the menu entry runs '$exec_program', not the loader"
    out="$("$exec_program" version)" || fail "the menu entry's program did not run"
    case "$out" in *"SemVer: $VERSION"*) ;; *) fail "the menu entry's program printed: $out" ;; esac
    echo "real-install: ok: the menu entry is where GNOME and KDE read it, and its program runs" ;;
esac

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
