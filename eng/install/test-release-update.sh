#!/usr/bin/env bash
# The real-download half of rust/REQUIREMENTS.md E1 (with I5 I6), on macOS / Linux under a temporary HOME: install the
# PREVIOUS published release in the install layout (A) with the release's own install.sh, then let Denice update itself
# from GitHub Releases to the release under test (B) with `denice update` (Denice Runtime downloads B, checks it and
# installs it, D1-D3), and run Denice: the loader starts B (L4), B replaces the loader (U1-U3), A is removed once B has
# started (L6), and the previous loader is gone. Meant to run right after B is published, while B is the newest release.
# When no earlier release in the install layout exists (B is the first), it says so and exits 0 without proving anything.
#
# Usage: eng/install/test-release-update.sh <rid> <tag-B> <work-dir>
# Requires: curl, gh (authenticated, or GH_TOKEN) to list the releases.
set -euo pipefail

[ "$#" -eq 3 ] || { echo "usage: test-release-update.sh <rid> <tag-B> <work-dir>" >&2; exit 2; }
RID="$1"; B="$2"; WORK="$3"
REPO="denice-aios/denice"
fail() { echo "release-update: FAIL: $*" >&2; exit 1; }
pass() { echo "release-update: ok: $*"; }
semver_of() { sed -n 's/^SemVer: //p' <<< "$1" | tr -d '\r'; }
hash_of() { { sha256sum "$1" 2>/dev/null || shasum -a 256 "$1"; } | cut -d' ' -f1; }

# A: the newest release published before B that is in the install layout (it ships install.sh and a checksummed zip
# for this platform, the marker install.sh itself uses).
A="$(gh api "repos/$REPO/releases?per_page=100" --jq '.[] | select(.draft | not) | [.tag_name, ([.assets[].name] | join(" "))] | @tsv' \
  | awk -F'\t' -v b="$B" -v rid="$RID" '
      $1 == b { seen = 1; next }
      seen && index(" " $2 " ", " install.sh ") && $2 ~ ("denice-v[^ ]*-" rid "\\.zip\\.sha256") { print $1; exit }')"
if [ -z "$A" ]; then
  echo "release-update: NOT PROVEN: no release before $B is in the install layout for $RID, so there is nothing to update from yet;"
  echo "release-update: the next release proves this (A = $B)."
  exit 0
fi
echo "release-update: A = $A (installed first), B = $B (downloaded by Denice)"

rm -rf "$WORK"; mkdir -p "$WORK/home"; WORK="$(cd "$WORK" && pwd)"
export HOME="$WORK/home"
unset XDG_DATA_HOME XDG_CONFIG_HOME ZDOTDIR
case "$RID" in
  osx-*) DATA="$HOME/Library/Application Support/Denice"; VERSIONS="$DATA/versions"
         LOADER="$HOME/Applications/Denice.app"; PROGRAM="$LOADER/Contents/MacOS/denice"; CARRIED="Denice.app/Contents/MacOS/denice" ;;
  *)     DATA="$HOME/.config/denice"; VERSIONS="$HOME/.local/share/denice/versions"
         LOADER="$HOME/.local/share/denice/denice"; PROGRAM="$LOADER"; CARRIED="denice" ;;
esac
# The background check at every start is switched off through Denice's own Settings, so only `denice update` downloads.
mkdir -p "$DATA" && printf 'updates.enabled: false\n' > "$DATA/settings.yaml"
INSTALL_URL="https://github.com/$REPO/releases/download/$B/install.sh"

curl -fsSL "$INSTALL_URL" | sh -s -- --version "$A" --no-run --no-modify-path
out="$("$PROGRAM" version)" || fail "A did not run: $out"
A_VERSION="$(semver_of "$out")"
case "$A_VERSION" in "$A"*) ;; *) fail "expected A ($A), got: $out" ;; esac
pass "1: installed A ($A_VERSION) from its release with the install script, and it runs"

out="$("$PROGRAM" update)" || fail "denice update failed: $out"
echo "$out"
B_VERSION="$(sed -n 's/^Denice \(.*\) is installed\. It starts the next time you run Denice\.$/\1/p' <<< "$out" | tr -d '\r')"
case "$B_VERSION" in "$B"*) ;; *) fail "denice update did not install B ($B): $out" ;; esac
[ -d "$VERSIONS/$B_VERSION" ] && [ ! -e "$VERSIONS/$B_VERSION.partial" ] || fail "B is not in $VERSIONS"
pass "2: denice update downloaded B ($B_VERSION) from GitHub Releases, checked it and installed it beside A"

out="$("$PROGRAM" version)" || fail "the run after the update failed: $out"
[ "$(semver_of "$out")" = "$B_VERSION" ] || fail "expected the loader to start B ($B_VERSION), got: $out"
[ "$(hash_of "$PROGRAM")" = "$(hash_of "$VERSIONS/$B_VERSION/$CARRIED")" ] || fail "B did not replace the loader"
[ ! -e "$LOADER.ver" ] || fail "$LOADER.ver was left behind"
pass "3: the loader started B, B replaced the loader with the one it carries, and the new loader ran B"

out="$("$PROGRAM" version)" || fail "the second run on B failed: $out"
[ "$(semver_of "$out")" = "$B_VERSION" ] || fail "expected B again, got: $out"
[ ! -e "$VERSIONS/$A_VERSION" ] || fail "A was not removed after B started"
[ ! -e "$LOADER.old" ] || fail "$LOADER.old was not deleted"
pass "4: B runs again, A was removed after B started, and the previous loader is gone"

# 5. A higher version C that cannot start (B's files without Denice Runtime): the real loader marks C bad, falls back to
# B, and never chooses C again (L5).
C="${B_VERSION%%-*}"; C="${C%.*}.$(( ${C##*.} + 1 ))-pre-release+0000000"
cp -R "$VERSIONS/$B_VERSION" "$VERSIONS/$C"
rm -f "$VERSIONS/$C/.started" "$VERSIONS/$C/Denice.Runtime.dll"
mv "$VERSIONS/$C/denice-v$B_VERSION-$RID" "$VERSIONS/$C/denice-v$C-$RID"
out="$("$PROGRAM" version 2> "$WORK/fallback.err")" || fail "the fallback run failed: $out"
[ "$(semver_of "$out")" = "$B_VERSION" ] || fail "expected the fallback to B ($B_VERSION), got: $out"
[ -f "$VERSIONS/$C/.bad" ] || fail "C was not marked bad"
grep -q "failed to start" "$WORK/fallback.err" || fail "the fallback was not reported"
out="$("$PROGRAM" version 2> "$WORK/again.err")" || fail "the run after the fallback failed: $out"
[ "$(semver_of "$out")" = "$B_VERSION" ] || fail "expected B again, got: $out"
! grep -q "failed to start" "$WORK/again.err" || fail "C was tried again"
pass "5: a broken C ($C) failed to start, was marked bad, the loader fell back to B and never chose C again"

curl -fsSL "$INSTALL_URL" | sh -s -- --uninstall
pass "updated $A -> $B through GitHub Releases on $RID"
