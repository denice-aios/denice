#!/usr/bin/env bash
# End-to-end test of the Denice loader with real builds (rust/REQUIREMENTS.md E1). Runs on macOS, Linux and Windows
# (Git Bash). Each step uses the real loader, real versioned entries and real Denice Runtime:
#   1. install version A and run it through the loader: it starts and records that it started;
#   2. add a higher version B (as a finished download leaves it): the loader picks B, B replaces the older loader in
#      Tim's order (denice.ver, then denice.old, then rename), the old loader hands over at once, B runs, A is removed;
#   3. add a higher version C that cannot start: the loader marks C bad and falls back to B;
#   4. run again: C is never chosen again, and the previous loader file is gone.
# Usage: eng/loader-e2e-test.sh <rid> <work-dir>
set -euo pipefail

RID="$1"
WORK="$2"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "$RID" in win-*) EXE=".exe" ;; *) EXE="" ;; esac
case "$RID" in osx-*) LOADER="Denice.app"; PROGRAM="Denice.app/Contents/MacOS/denice" ;; *) LOADER="denice$EXE"; PROGRAM="denice$EXE" ;; esac

fail() { echo "loader-e2e: FAIL: $*" >&2; exit 1; }
pass() { echo "loader-e2e: ok: $*"; }
build() { # build <out>  -> prints the version it built
  local out="$1"
  "$REPO_ROOT/eng/build-install-layout.sh" "$RID" "$out" dev > "$out.log" 2>&1 || { cat "$out.log" >&2; fail "build of $out"; }
  sed -n 's/^version=//p' "$out.log" | tail -1
}
semver_of_run() { sed -n 's/^SemVer: //p' <<< "$1" | tr -d '\r'; }
hash_of() { sha256sum "$1" 2>/dev/null | cut -d' ' -f1 || shasum -a 256 "$1" | cut -d' ' -f1; }

# Each build computes its own id, so B is genuinely newer than A even inside a CI job that exports one id.
unset DENICE_DAY DENICE_TIME DENICE_COMBINED DENICE_HASH DENICE_CHANNEL DENICE_TAG DENICE_VERSION

rm -rf "$WORK"
mkdir -p "$WORK"
# The loader lives in INSTALL; the versions live in VROOT. On macOS they differ (pkg-494745156d56 macos-versions:
# "Application Support"), under a temporary HOME so the real one is never touched.
INSTALL="$WORK/install"
TEST_HOME="$WORK/home"
TEST_LOCALAPPDATA="$WORK/localappdata"
case "$RID" in osx-*) VROOT="$TEST_HOME/Library/Application Support/Denice" ;; *) VROOT="$INSTALL" ;; esac
# Denice runs with the temporary home (and Windows' per-user data folder), with its background update check switched off
# through its own Settings, so the test never reaches the network.
for settings in "$TEST_HOME/Library/Application Support/Denice" "$TEST_HOME/.config/denice" "$TEST_LOCALAPPDATA/Denice"; do
  mkdir -p "$settings" && printf 'updates.enabled: false\n' > "$settings/settings.yaml"
done
run_denice() { HOME="$TEST_HOME" LOCALAPPDATA="$TEST_LOCALAPPDATA" "$INSTALL/$PROGRAM" "$@"; }
install_from() { # install_from <layout>: the loader into INSTALL, its versions into VROOT
  mkdir -p "$INSTALL" "$VROOT/versions"
  cp -R "$1/$LOADER" "$INSTALL/"
  cp -R "$1/versions/." "$VROOT/versions/"
}

# 1. Version A.
A=$(build "$WORK/A")
install_from "$WORK/A"
out=$(run_denice version) || fail "A did not run"
[ "$(semver_of_run "$out")" = "$A" ] || fail "expected A ($A), got: $out"
[ -f "$VROOT/versions/$A/.started" ] || fail "A did not record that it started"
pass "1: the loader started A ($A) and A recorded that it started"

# 2. Version B arrives (built at least 2 s later, so its version is higher).
sleep 3
B=$(build "$WORK/B")
cp -R "$WORK/B/versions/$B" "$VROOT/versions/$B"
out=$(run_denice version) || fail "B did not run"
[ "$(semver_of_run "$out")" = "$B" ] || fail "expected B ($B), got: $out"
[ "$(hash_of "$INSTALL/$PROGRAM")" = "$(hash_of "$WORK/B/$PROGRAM")" ] || fail "the loader was not replaced by B's loader"
[ ! -e "$INSTALL/$LOADER.ver" ] || fail "$LOADER.ver was left behind"
[ ! -e "$VROOT/versions/$A" ] || fail "A was not removed after B started"
pass "2: B ($B) replaced the loader, the new loader took over at once, B ran, A was removed"

# 3. Version C cannot start (its Denice Runtime is missing).
C="${B%%-*}"; C="${C%.*}.$(( ${C##*.} + 1 ))-pre-release+0000000"
cp -R "$VROOT/versions/$B" "$VROOT/versions/$C"
rm -f "$VROOT/versions/$C/.started" "$VROOT/versions/$C/Denice.Runtime.dll"
mv "$VROOT/versions/$C/denice-v$B-$RID$EXE" "$VROOT/versions/$C/denice-v$C-$RID$EXE"
out=$(run_denice version 2> "$WORK/fallback.err") || fail "the fallback run failed"
[ "$(semver_of_run "$out")" = "$B" ] || fail "expected the fallback to B, got: $out"
[ -f "$VROOT/versions/$C/.bad" ] || fail "C was not marked bad"
grep -q "failed to start" "$WORK/fallback.err" || fail "the fallback was not reported"
pass "3: C ($C) failed to start, was marked bad, and the loader fell back to B"

# 4. Again: C is skipped, B runs, and the previous loader is gone.
out=$(run_denice version 2> "$WORK/again.err") || fail "the run after the fallback failed"
[ "$(semver_of_run "$out")" = "$B" ] || fail "expected B again, got: $out"
! grep -q "failed to start" "$WORK/again.err" || fail "C was tried again"
[ ! -e "$INSTALL/$LOADER.old" ] || fail "$LOADER.old was not deleted"
pass "4: C is never chosen again; $LOADER.old is gone"
echo "loader-e2e: all steps passed on $RID"
