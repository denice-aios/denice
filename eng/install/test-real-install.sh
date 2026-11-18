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
# A locally built layout is not signed yet (CI signs it later, on a push only), so this mode writes its SHA256SUMS and
# lets the test-only fake cosign (eng/install/fake-cosign/cosign) "sign" and check it; the real signature check is the
# --release mode.
# With --release=<tag> instead of a layout, it installs a PUBLISHED release the way a user does (rust/REQUIREMENTS.md I5
# I6): `curl -fsSL <release>/install.sh | sh -s -- --version <tag>`, which downloads the release's zip from GitHub and
# checks its .zip.sha256, its SHA256SUMS and their real cosign signature by Denice's release workflow (I11; with the
# cosign on PATH when it is 3.0.4 or newer, else with Sigstore's cosign downloaded into a temporary folder); Denice then
# runs `denice update` against GitHub Releases, and the release's own install.sh uninstalls. Then two altered copies of
# the release zip, each with a fresh .zip.sha256, must be refused by `install.sh --from` with nothing left behind: one
# with a changed byte (SHA256SUMS catches it) and one whose SHA256SUMS was rewritten to match (the real cosign check
# catches it).
# [<install.sh>] pipes that script instead of the release's own (to prove a script change against a published release).
# Usage: eng/install/test-real-install.sh <rid> <layout-dir>|--release=<tag> <work-dir> [<install.sh>]
set -euo pipefail

RID="$1"; SOURCE="$2"; WORK="$3"; OWN_SCRIPT="${4:-}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "real-install: FAIL: $*" >&2; exit 1; }

rm -rf "$WORK"; mkdir -p "$WORK/home"; WORK="$(cd "$WORK" && pwd)"
case "$SOURCE" in
  --release=*)
    TAG="${SOURCE#--release=}"; VERSION="$TAG"
    SCRIPT="$WORK/install.sh"
    INSTALL_URL="https://github.com/denice-aios/denice/releases/download/$TAG/install.sh"
    if [ -n "$OWN_SCRIPT" ]; then
      cp "$OWN_SCRIPT" "$SCRIPT"
      install_denice() { sh -s -- --version "$TAG" < "$SCRIPT"; }
    else
      curl -fsSL -o "$SCRIPT" "$INSTALL_URL" || fail "could not download $INSTALL_URL"
      install_denice() { curl -fsSL "$INSTALL_URL" | sh -s -- --version "$TAG"; }
    fi
    ;;
  *)
    LAYOUT="$WORK/layout"; TAG=""
    cp -Rp "$SOURCE" "$LAYOUT"
    SCRIPT="${OWN_SCRIPT:-$REPO_ROOT/eng/install/install.sh}"
    VERSION="$(ls "$LAYOUT/versions" | head -1)"
    ZIP="$WORK/denice-v$VERSION-$RID.zip"
    case "$RID" in osx-*) LOADER="Denice.app" ;; *) LOADER="denice" ;; esac
    FAKE_COSIGN_DIR="$REPO_ROOT/eng/install/fake-cosign"
    "$REPO_ROOT/eng/write-sha256sums.sh" "$LAYOUT/versions/$VERSION" > /dev/null
    "$FAKE_COSIGN_DIR/cosign" sign-blob --yes --bundle "$LAYOUT/versions/$VERSION/SHA256SUMS.cosign.bundle" \
      "$LAYOUT/versions/$VERSION/SHA256SUMS"
    ( cd "$LAYOUT" && zip -q -r -X -y "$ZIP" "$LOADER" versions )
    ( cd "$WORK" && { command -v sha256sum >/dev/null && sha256sum "$(basename "$ZIP")" || shasum -a 256 "$(basename "$ZIP")"; } > "$ZIP.sha256" )
    install_denice() { PATH="$FAKE_COSIGN_DIR:$PATH" sh "$SCRIPT" --from "$ZIP"; }
    ;;
esac

export HOME="$WORK/home"
unset XDG_DATA_HOME
case "$RID" in
  osx-*) SETTINGS="$HOME/Library/Application Support/Denice" ;;
  *)     SETTINGS="$HOME/.config/denice" ;;
esac
mkdir -p "$SETTINGS" && printf 'updates.enabled: false\n' > "$SETTINGS/settings.yaml"

if [ -z "$TAG" ]; then
  case "$RID" in osx-*)
    # As a browser download: Safari's quarantine record (flags;time;agent;id).
    xattr -w com.apple.quarantine "0081;$(printf '%x' "$(date +%s)");Safari;$(uuidgen)" "$ZIP"
    xattr -p com.apple.quarantine "$ZIP" >/dev/null || fail "the test zip carries no quarantine attribute"
    echo "real-install: ok: the zip carries com.apple.quarantine, as a browser download" ;;
  esac
fi

install_denice 2>&1 | tee "$WORK/install.log" || fail "the install failed"
grep -qF "signature verified: signed by Denice's release workflow" "$WORK/install.log" \
  || fail "the install did not report a verified release signature"
if [ -n "$TAG" ]; then echo "real-install: ok: install.sh verified SHA256SUMS and its real cosign signature before installing"
else echo "real-install: ok: install.sh checked SHA256SUMS and the (test-only fake) signature of the unsigned local layout"; fi
out="$("$HOME/.local/bin/denice" version)" || fail "the installed Denice did not run"
case "$out" in *"SemVer: $VERSION"*) ;; *) fail "expected $VERSION, got: $out" ;; esac
echo "real-install: ok: installed $VERSION and ran it through ~/.local/bin/denice"
if [ -n "$TAG" ]; then
  # The manual update checks GitHub Releases now, whatever the off switch says (pkg-79dc8dd1e15d).
  out="$("$HOME/.local/bin/denice" update)" || fail "denice update failed: $out"
  case "$out" in *"is the newest version on your update channel"*|*"is installed. It starts the next time"*) ;;
    *) fail "unexpected denice update answer: $out" ;; esac
  echo "real-install: ok: denice update checked GitHub Releases: $out"
fi

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

sh "$SCRIPT" --uninstall
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

[ -n "$TAG" ] || exit 0
# Altered copies of the published release zip must be refused, with nothing left behind (I11).
ASSET="$(curl -fsSL -H 'Accept: application/vnd.github+json' "https://api.github.com/repos/denice-aios/denice/releases/tags/$TAG" \
  | grep -o '"browser_download_url": *"[^"]*-'"$RID"'\.zip"' | sed 's/.*"\(https[^"]*\)"$/\1/')" \
  || fail "could not find the $RID zip of release $TAG"
GENUINE="$WORK/genuine.zip"
curl -fsSL -o "$GENUINE" "$ASSET" || fail "could not download $ASSET"
change_byte() { printf 'X' | dd of="$1/Denice.Runtime.dll" bs=1 seek=100 conv=notrunc 2>/dev/null; }
forge_sums() {
  change_byte "$1"; cp "$1/SHA256SUMS.cosign.bundle" "$WORK/bundle.keep"
  "$REPO_ROOT/eng/write-sha256sums.sh" "$1" > /dev/null; mv "$WORK/bundle.keep" "$1/SHA256SUMS.cosign.bundle"
}
# refused <name> <change> <message>: the genuine zip with <change> applied to its version folder, zipped again with a
# fresh .zip.sha256, is refused by `install.sh --from` with <message>, and the (temporary) HOME stays empty.
refused() {
  local dir="$WORK/altered-$1" home="$WORK/altered-$1-home" zip
  rm -rf "$dir" "$home"; mkdir -p "$dir/x" "$home"
  unzip -q "$GENUINE" -d "$dir/x"
  "$2" "$(echo "$dir/x/versions"/*)"
  zip="$dir/$(basename "$ASSET" | sed 's/%2[Bb]/+/g')"
  ( cd "$dir/x" && zip -q -r -X -y "$zip" . )
  ( cd "$dir" && { command -v sha256sum >/dev/null && sha256sum "$(basename "$zip")" || shasum -a 256 "$(basename "$zip")"; } > "$zip.sha256" )
  if HOME="$home" sh "$SCRIPT" --from "$zip" --no-run > "$dir/out.log" 2>&1; then fail "install.sh accepted the $1 release"; fi
  grep -qF "$3" "$dir/out.log" || { cat "$dir/out.log" >&2; fail "the $1 release was refused for another reason"; }
  [ -z "$(ls -A "$home")" ] || fail "the refused $1 install left files in HOME: $(ls -A "$home" | tr '\n' ' ')"
  echo "real-install: ok: the $1 release zip is refused ($3) and nothing is left behind"
}
refused "changed-byte" change_byte "does not match its SHA256SUMS"
refused "re-summed" forge_sums "not a valid signature by Denice's release workflow"
