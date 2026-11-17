#!/usr/bin/env bash
# Copyright (c) Denice.ai. All rights reserved.
#
# Tests eng/install/install.sh without the network and without touching the real home folder: every run gets a
# temporary HOME (and TMPDIR), the release zips are fakes built here, and the "GitHub" release list is a file:// URL.
# The macOS branch runs natively on a Mac; the Linux branch runs everywhere through the test-only DENICE_INSTALL_OS.
# Each scenario runs under every POSIX shell found (/bin/sh, and /bin/dash when present).
#
# Usage: eng/install/test-install.sh
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
INSTALL="$HERE/install.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

V1="0.1.100-pre-release+aaa1111"
V2="0.1.200-pre-release+bbb2222"
case "$(uname -m)" in arm64|aarch64) ARCH=arm64 ;; *) ARCH=x64 ;; esac
if [ "$(uname -s)" = Darwin ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then ARCH=arm64; fi
if command -v sha256sum >/dev/null 2>&1; then SUM=(sha256sum); else SUM=(shasum -a 256); fi

passed=0
pass() { passed=$((passed + 1)); printf '  ok   %s\n' "$*"; }
die() { printf '  FAIL %s\n' "$*" >&2; exit 1; }
check() { local what="$1"; shift; if "$@"; then pass "$what"; else die "$what"; fi; }
not() { ! "$@"; }
quiet() { "$@" >/dev/null 2>&1; }
is_link_to() { [ -L "$1" ] && [ "$(readlink "$1")" = "$2" ]; }
contains() { grep -qF -- "$2" "$1"; }
count_is() { [ "$(grep -cF -- "$2" "$1")" = "$3" ]; }
TEST_PATH="/usr/bin:/bin:/usr/sbin:/sbin"
MARK="# added by the Denice installer"
# finds <home> [VAR=value...] <shell> <options>: a fresh shell given only that HOME (and VARs) and a minimal PATH
# resolves `denice` to the ~/.local/bin link, through the profiles the install changed.
finds() {
    local home="$1"; shift
    [ "$(env -i HOME="$home" PATH="$TEST_PATH" "$@" 'command -v denice' 2>/dev/null < /dev/null | tail -n 1)" = "$home/.local/bin/denice" ]
}
HAVE_ZSH=no; PATH="$TEST_PATH" command -v zsh >/dev/null 2>&1 && HAVE_ZSH=yes
HAVE_FISH=no; command -v fish >/dev/null 2>&1 && HAVE_FISH=yes

# make_zip <os> <version> <out-dir>: a fake release zip and its .zip.sha256, in the CI layout.
make_zip() {
    local os="$1" version="$2" out="$3" rid="$1-$ARCH" stage loader
    stage="$WORK/stage-$os-$version"
    rm -rf "$stage"; mkdir -p "$stage/versions/$version/runtime" "$out"
    if [ "$os" = osx ]; then
        loader="$stage/Denice.app/Contents/MacOS/denice"
        mkdir -p "$(dirname "$loader")"
        printf '<plist><string>%s</string></plist>\n' "$version" > "$stage/Denice.app/Contents/Info.plist"
    else
        loader="$stage/denice"
    fi
    printf '#!/bin/sh\necho "fake denice %s: $*"\n' "$version" > "$loader"
    chmod 755 "$loader"
    printf '#!/bin/sh\necho entry\n' > "$stage/versions/$version/denice-v$version-$rid"
    chmod 755 "$stage/versions/$version/denice-v$version-$rid"
    echo "runtime $version" > "$stage/versions/$version/runtime/Denice.Runtime.dll"
    if [ "$os" = osx ]; then cp -Rp "$stage/Denice.app" "$stage/versions/$version/"; else cp -p "$loader" "$stage/versions/$version/"; fi
    echo "sums" > "$stage/versions/$version/SHA256SUMS"
    echo "bundle" > "$stage/versions/$version/SHA256SUMS.cosign.bundle"
    local name="denice-v$version-$rid.zip" top
    if [ "$os" = osx ]; then top="Denice.app"; else top="denice"; fi
    rm -f "$out/$name"
    (cd "$stage" && zip -q -r -X -y "$out/$name" "$top" versions)
    (cd "$out" && "${SUM[@]}" "$name" > "$name.sha256")
    echo "$out/$name"
}

# The environment install.sh sees: a minimal PATH plus the test-only overrides that are set.
envs() {
    ENVS=(PATH="$TEST_PATH")
    [ -n "${XDG:-}" ] && ENVS+=(XDG_DATA_HOME="$XDG")
    [ -n "${TEST_OS:-}" ] && ENVS+=(DENICE_INSTALL_OS="$TEST_OS")
    [ -n "${API:-}" ] && ENVS+=(DENICE_INSTALL_API_URL="$API")
    [ -n "${LOGIN_SHELL:-}" ] && ENVS+=(SHELL="$LOGIN_SHELL")
    [ -n "${ZDOT:-}" ] && ENVS+=(ZDOTDIR="$ZDOT")
    [ -n "${XDGC:-}" ] && ENVS+=(XDG_CONFIG_HOME="$XDGC")
    return 0
}

# run_install <shell> <home> [args...]: runs install.sh with a clean environment and a temporary HOME and TMPDIR.
run_install() {
    local shell="$1" home="$2"; shift 2
    envs
    mkdir -p "$home/tmp"
    env -i "${ENVS[@]}" HOME="$home" TMPDIR="$home/tmp" "$shell" "$INSTALL" "$@"
}

# Everything below the home folder, except the scratch TMPDIR, as sorted relative paths.
tree_of() { (cd "$1" && find . -path ./tmp -prune -o -print | LC_ALL=C sort); }

echo "== syntax"
check "sh -n install.sh" sh -n "$INSTALL"
[ -x /bin/dash ] && check "dash -n install.sh" /bin/dash -n "$INSTALL"
check "no bash-only [[ in install.sh" not grep -n '\[\[' "$INSTALL"
check "--help exits 0" bash -c "sh '$INSTALL' --help | grep -q 'Usage: install.sh'"

SHELLS=(/bin/sh)
[ -x /bin/dash ] && SHELLS+=(/bin/dash)

ZIPS="$WORK/zips"
for os in osx linux; do make_zip "$os" "$V1" "$ZIPS" >/dev/null; make_zip "$os" "$V2" "$ZIPS" >/dev/null; done

# ---------------------------------------------------------------- macOS layout
test_osx() {
    local shell="$1" H="$WORK/home-osx-$(basename "$shell")"
    local APP="$H/Applications/Denice.app" VERS="$H/Library/Application Support/Denice/versions"
    local PROG="$APP/Contents/MacOS/denice" out
    echo "== macOS branch under $shell (HOME=$H)"
    unset TEST_OS XDG API
    [ "$(uname -s)" = Darwin ] || TEST_OS=osx
    out="$(run_install "$shell" "$H" --from "$ZIPS/denice-v$V1-osx-$ARCH.zip" --no-run)"
    check "first install reports a verified checksum" contains <(echo "$out") "checksum verified"
    check "Denice.app is in ~/Applications" test -d "$APP"
    check "the loader program is executable" test -x "$PROG"
    check "app is the one from the zip" contains "$APP/Contents/Info.plist" "$V1"
    check "version folder is in Application Support" test -x "$VERS/$V1/denice-v$V1-osx-$ARCH"
    check "version carries its loader app" test -x "$VERS/$V1/Denice.app/Contents/MacOS/denice"
    check "version's runtime file is there" test -f "$VERS/$V1/runtime/Denice.Runtime.dll"
    check "~/.local/bin/denice links to the app's program" is_link_to "$H/.local/bin/denice" "$PROG"
    check "no Denice.app.ver / .old left" not test -e "$APP.ver" -o -e "$APP.old"
    check "no .partial left" not test -e "$VERS/$V1.partial"
    check "no desktop entry on macOS" not test -e "$H/.local/share/applications/denice.desktop"
    check "says it added ~/.local/bin to PATH in ~/.profile" contains <(echo "$out") "added $H/.local/bin to PATH in $H/.profile"
    check "says a new terminal finds denice" contains <(echo "$out") "a new terminal finds 'denice'"
    check "temp folder cleaned up" test -z "$(ls -A "$H/tmp")"

    touch "$VERS/$V1/.started"
    out="$(run_install "$shell" "$H" --from "$ZIPS/denice-v$V2-osx-$ARCH.zip" --no-run)"
    check "newer install keeps the first version" test -f "$VERS/$V1/.started"
    check "newer install adds the second version" test -d "$VERS/$V2"
    check "newer install replaced the app" contains "$APP/Contents/Info.plist" "$V2"
    check "replacement leaves no .ver / .old" not test -e "$APP.ver" -o -e "$APP.old"

    touch "$VERS/$V2/marker-from-denice"
    out="$(run_install "$shell" "$H" --from "$ZIPS/denice-v$V2-osx-$ARCH.zip" --no-run)"
    check "same version again is left as it is" contains <(echo "$out") "already in"
    check "same version folder untouched" test -f "$VERS/$V2/marker-from-denice"

    out="$(run_install "$shell" "$H" --from "$ZIPS/denice-v$V2-osx-$ARCH.zip")"
    check "the run step prints the installed program's 'version' output" contains <(echo "$out") "fake denice $V2: version"

    envs
    out="$(cat "$INSTALL" | env -i "${ENVS[@]}" HOME="$H" TMPDIR="$H/tmp" "$shell" -s -- --from "$ZIPS/denice-v$V2-osx-$ARCH.zip")"
    check "piped 'cat install.sh | sh -s -- …' installs and runs" contains <(echo "$out") "fake denice $V2: version"

    echo "user: settings" > "$H/Library/Application Support/Denice/settings.yaml"
    echo "mine" > "$H/.local/bin/other-tool"
    out="$(run_install "$shell" "$H" --uninstall)"
    check "uninstall removes Denice.app" not test -e "$APP"
    check "uninstall removes versions/" not test -e "$VERS"
    check "uninstall removes the link" not test -e "$H/.local/bin/denice" -o -L "$H/.local/bin/denice"
    check "uninstall keeps settings.yaml" contains "$H/Library/Application Support/Denice/settings.yaml" "user: settings"
    check "uninstall keeps other files in ~/.local/bin" test -f "$H/.local/bin/other-tool"
    check "uninstall keeps ~/Applications" test -d "$H/Applications"
    check "uninstall says settings are kept" contains <(echo "$out") "kept Denice's settings"
    check "uninstall removes the ~/.profile the install created" not test -e "$H/.profile"
    check "uninstall removes the env file" not test -e "$H/Library/Application Support/Denice/env"
    check "a second uninstall is harmless" quiet run_install "$shell" "$H" --uninstall

    local B="$WORK/home-osx-bad-$(basename "$shell")" badzip="$WORK/bad/denice-v$V1-osx-$ARCH.zip"
    mkdir -p "$B" "$WORK/bad"
    cp "$ZIPS/denice-v$V1-osx-$ARCH.zip" "$badzip"
    echo "0000000000000000000000000000000000000000000000000000000000000000  $(basename "$badzip")" > "$badzip.sha256"
    check "bad checksum is refused" not quiet run_install "$shell" "$B" --from "$badzip" --no-run
    check "bad checksum installs nothing" test "$(tree_of "$B")" = "."
    check "the refusal names the checksum mismatch" contains <(run_install "$shell" "$B" --from "$badzip" --no-run 2>&1 || true) "checksum mismatch"

    check "a Linux zip is refused on macOS" not quiet run_install "$shell" "$B" --from "$ZIPS/denice-v$V1-linux-$ARCH.zip" --no-run

    local L="$WORK/home-osx-userlink-$(basename "$shell")"
    mkdir -p "$L/.local/bin"; echo "user's own" > "$L/.local/bin/denice"
    out="$(run_install "$shell" "$L" --from "$ZIPS/denice-v$V1-osx-$ARCH.zip" --no-run)"
    check "a user's own ~/.local/bin/denice file is not overwritten" contains "$L/.local/bin/denice" "user's own"
    run_install "$shell" "$L" --uninstall >/dev/null
    check "...nor removed by uninstall" contains "$L/.local/bin/denice" "user's own"
}

# ---------------------------------------------------------------- Linux layout
test_linux() {
    local shell="$1" xdg_mode="$2" H="$WORK/home-linux-$2-$(basename "$shell")" DATA out
    TEST_OS=linux; unset API
    if [ "$xdg_mode" = set ]; then XDG="$H/my data \$dir"; DATA="$XDG"; else unset XDG; DATA="$H/.local/share"; fi
    local LOADER="$DATA/denice/denice" VERS="$DATA/denice/versions" DESK="$DATA/applications/denice.desktop"
    echo "== Linux branch under $shell, XDG_DATA_HOME $xdg_mode (DENICE_INSTALL_OS=linux, HOME=$H)"
    out="$(run_install "$shell" "$H" --from "$ZIPS/denice-v$V1-linux-$ARCH.zip" --no-run)"
    check "loader installed and executable" test -x "$LOADER"
    check "version folder installed next to the loader" test -x "$VERS/$V1/denice-v$V1-linux-$ARCH"
    check "~/.local/bin/denice links to the loader" is_link_to "$H/.local/bin/denice" "$LOADER"
    check "desktop entry exists" test -f "$DESK"
    check "desktop entry: Type=Application" contains "$DESK" "Type=Application"
    check "desktop entry: Name=Denice" contains "$DESK" "Name=Denice"
    check "desktop entry: Terminal=true" contains "$DESK" "Terminal=true"
    if [ "$xdg_mode" = set ]; then
        check "desktop entry: Exec quotes the path and escapes \$" contains "$DESK" "Exec=\"$H/my data \\\\\$dir/denice/denice\""
    else
        check "desktop entry: Exec is the absolute loader path" contains "$DESK" "Exec=\"$LOADER\""
    fi
    [ "$xdg_mode" = set ] && check "nothing written to ~/.local/share when XDG_DATA_HOME is set" not test -e "$H/.local/share"
    check "a login shell finds denice through ~/.profile and the env file in the install folder" finds "$H" /bin/sh -l -c
    check "no .ver / .old left" not test -e "$LOADER.ver" -o -e "$LOADER.old"

    touch "$VERS/$V1/.started"
    out="$(run_install "$shell" "$H" --from "$ZIPS/denice-v$V2-linux-$ARCH.zip")"
    check "newer install keeps the first version" test -f "$VERS/$V1/.started"
    check "newer install adds the second version" test -d "$VERS/$V2"
    check "newer install replaced the loader" contains "$LOADER" "$V2"
    check "replacement leaves no .ver / .old" not test -e "$LOADER.ver" -o -e "$LOADER.old"
    check "the run step prints the installed program's 'version' output" contains <(echo "$out") "fake denice $V2: version"

    mkdir -p "$H/.config/denice"; echo "user: settings" > "$H/.config/denice/settings.yaml"
    out="$(run_install "$shell" "$H" --uninstall)"
    check "uninstall removes the loader" not test -e "$LOADER"
    check "uninstall removes versions/" not test -e "$VERS"
    check "uninstall removes the empty install folder" not test -e "$DATA/denice"
    check "uninstall removes the desktop entry" not test -e "$DESK"
    check "uninstall removes the link" not test -e "$H/.local/bin/denice" -o -L "$H/.local/bin/denice"
    check "uninstall keeps settings.yaml" contains "$H/.config/denice/settings.yaml" "user: settings"

    local B="$WORK/home-linux-bad-$xdg_mode-$(basename "$shell")" badzip="$WORK/badl/denice-v$V1-linux-$ARCH.zip"
    mkdir -p "$B" "$WORK/badl"
    cp "$ZIPS/denice-v$V1-linux-$ARCH.zip" "$badzip"
    echo "1111111111111111111111111111111111111111111111111111111111111111  x" > "$badzip.sha256"
    check "bad checksum is refused" not quiet run_install "$shell" "$B" --from "$badzip" --no-run
    check "bad checksum installs nothing" test "$(tree_of "$B")" = "."
    check "a macOS zip is refused on Linux" not quiet run_install "$shell" "$B" --from "$ZIPS/denice-v$V1-osx-$ARCH.zip" --no-run
    unset XDG
}

# ---------------------------------------------------------------- PATH in shell profiles (pkg-d4e90c3ce176 install-path)
test_profiles() {
    local shell="$1" os="$2" tag="$2-$(basename "$1")" out f
    local H="$WORK/home-path-$tag" O="$WORK/orig-path-$tag" zip="$ZIPS/denice-v$V1-$2-$ARCH.zip"
    local ENV_REF ENV_FILE FISH="$H/.config/fish/conf.d/denice.fish"
    unset API XDG ZDOT XDGC LOGIN_SHELL TEST_OS
    if [ "$os" = osx ]; then ENV_REF='$HOME/Library/Application Support/Denice/env'; else ENV_REF='$HOME/.local/share/denice/env'; fi
    ENV_FILE="$H/${ENV_REF#\$HOME/}"
    [ "$os" = osx ] && [ "$(uname -s)" = Darwin ] || TEST_OS="$os"
    echo "== PATH in shell profiles, $os branch under $shell (HOME=$H; zsh here: $HAVE_ZSH, fish here: $HAVE_FISH)"
    mkdir -p "$H" "$O"
    printf 'export USER_MARK=1\n' > "$H/.profile"
    printf "alias ll='ls -l'" > "$H/.bashrc"          # no final line break
    printf '# my zshenv\n\n' > "$H/.zshenv"
    cp -p "$H/.profile" "$H/.bashrc" "$H/.zshenv" "$O/"
    LOGIN_SHELL=/opt/fish/bin/fish                    # the fish setup runs whether or not fish is installed here
    out="$(run_install "$shell" "$H" --from "$zip" --no-run)"
    check "env file written in the Denice data folder" test -f "$ENV_FILE"
    check "env file adds ~/.local/bin only when it is missing" contains "$ENV_FILE" '*:"$HOME/.local/bin":*) ;;'
    check "~/.profile: user's content kept, then the line" test "$(cat "$H/.profile")" = "export USER_MARK=1
. \"$ENV_REF\" $MARK"
    check "~/.bashrc (no final line break): a line break, then the line" test "$(cat "$H/.bashrc")" = "alias ll='ls -l'
. \"$ENV_REF\" $MARK, with the line break before it"
    check "~/.bash_profile is not created" not test -e "$H/.bash_profile"
    check "~/.bash_login is not created" not test -e "$H/.bash_login"
    if [ "$HAVE_ZSH" = yes ]; then check "~/.zshenv gets the line (zsh is here)" count_is "$H/.zshenv" "$MARK" 1; fi
    check "fish: conf.d/denice.fish adds ~/.local/bin when it is missing" contains "$FISH" 'if not contains -- "$HOME/.local/bin" $PATH'
    check "says it changed ~/.profile" contains <(echo "$out") "added $H/.local/bin to PATH in $H/.profile"
    check "says it changed ~/.bashrc" contains <(echo "$out") "added $H/.local/bin to PATH in $H/.bashrc"
    check "says it added the fish file" contains <(echo "$out") "added $H/.local/bin to PATH in $FISH"
    check "says a new terminal picks it up" contains <(echo "$out") "a new terminal finds 'denice'"

    for f in .profile .bashrc .zshenv; do cp -p "$H/$f" "$O/$f.once"; done
    cp -p "$FISH" "$O/fish.once"
    out="$(run_install "$shell" "$H" --from "$zip" --no-run)"
    for f in .profile .bashrc .zshenv; do check "re-install leaves $f as it was" cmp -s "$H/$f" "$O/$f.once"; done
    check "re-install leaves the fish file as it was" cmp -s "$FISH" "$O/fish.once"
    check "re-install reports no profile change" not contains <(echo "$out") "to PATH in"

    check "sh -l -c finds denice" finds "$H" /bin/sh -l -c
    [ -x /bin/dash ] && check "dash -l -c finds denice" finds "$H" /bin/dash -l -c
    check "bash -l -c finds denice" finds "$H" bash -l -c
    check "bash -i -c finds denice (~/.bashrc)" finds "$H" bash -i -c
    if [ "$HAVE_ZSH" = yes ]; then
        check "zsh -c finds denice (~/.zshenv)" finds "$H" zsh -c
        check "zsh -i -c finds denice" finds "$H" zsh -i -c
        check "zsh -l -c finds denice" finds "$H" zsh -l -c
    else
        echo "  skip zsh shells: zsh is not installed here"
    fi
    if [ "$HAVE_FISH" = yes ]; then check "fish -c finds denice" finds "$H" "$(command -v fish)" -c
    else echo "  skip fish -c: fish is not installed here (the fish file's content is checked above)"; fi
    check "sourcing the env file twice puts ~/.local/bin on PATH once" test "$(env -i HOME="$H" PATH="$TEST_PATH" /bin/sh -c \
        ". \"\$1\"; . \"\$1\"; echo \"\$PATH\"" sh "$ENV_FILE" | tr ':' '\n' | grep -cxF "$H/.local/bin")" = 1

    out="$(run_install "$shell" "$H" --uninstall)"
    for f in .profile .bashrc .zshenv; do check "uninstall restores $f byte for byte" cmp -s "$H/$f" "$O/$f"; done
    check "uninstall removes the fish file" not test -e "$FISH"
    check "uninstall removes the env file" not test -e "$ENV_FILE"
    check "uninstall says which profile it changed" contains <(echo "$out") "removed the Denice PATH line from $H/.bashrc"

    # The user wrote more after the line: uninstall keeps the line break it added, so lines never run together.
    run_install "$shell" "$H" --from "$zip" --no-run >/dev/null
    printf 'export AFTER=1\n' >> "$H/.bashrc"
    run_install "$shell" "$H" --uninstall >/dev/null
    check "uninstall keeps what the user added after the line" test "$(cat "$H/.bashrc")" = "alias ll='ls -l'
export AFTER=1"

    local N="$WORK/home-path-nomod-$tag"
    out="$(run_install "$shell" "$N" --from "$zip" --no-run --no-modify-path)"
    check "--no-modify-path: no profile or fish file is written" not test -e "$N/.profile" -o -e "$N/.zshenv" -o -e "$N/.config"
    check "--no-modify-path: no env file" not test -e "$N/${ENV_REF#\$HOME/}"
    check "--no-modify-path: prints the PATH hint instead" contains <(echo "$out") "is not on your PATH (--no-modify-path)"

    local Z="$WORK/home-path-zdot-$tag"
    ZDOT="$Z/z dot"; LOGIN_SHELL=/bin/zsh
    out="$(run_install "$shell" "$Z" --from "$zip" --no-run)"
    check "ZDOTDIR: the line goes to \$ZDOTDIR/.zshenv" count_is "$ZDOT/.zshenv" "$MARK" 1
    check "ZDOTDIR: ~/.zshenv is not created" not test -e "$Z/.zshenv"
    [ "$HAVE_ZSH" = yes ] && check "ZDOTDIR: zsh -c finds denice" finds "$Z" ZDOTDIR="$ZDOT" zsh -c
    run_install "$shell" "$Z" --uninstall >/dev/null
    check "ZDOTDIR: uninstall removes the \$ZDOTDIR/.zshenv it created" not test -e "$ZDOT/.zshenv"
    Z="$WORK/home-path-zdot-home-$tag"; ZDOT="$Z/zdot"
    mkdir -p "$Z"; printf 'ZDOTDIR=~/zdot\n' > "$Z/.zshenv"
    run_install "$shell" "$Z" --from "$zip" --no-run >/dev/null
    check "ZDOTDIR: an existing ~/.zshenv wins over a missing \$ZDOTDIR/.zshenv (as rustup)" count_is "$Z/.zshenv" "$MARK" 1
    unset ZDOT

    local C="$WORK/home-path-xdgc-$tag"
    XDGC="$C/my config"; LOGIN_SHELL=/opt/fish/bin/fish
    run_install "$shell" "$C" --from "$zip" --no-run >/dev/null
    check "XDG_CONFIG_HOME: the fish file goes to \$XDG_CONFIG_HOME/fish/conf.d" test -f "$XDGC/fish/conf.d/denice.fish"
    check "XDG_CONFIG_HOME: nothing in ~/.config" not test -e "$C/.config"
    run_install "$shell" "$C" --uninstall >/dev/null
    check "XDG_CONFIG_HOME: uninstall removes the fish file" not test -e "$XDGC/fish/conf.d/denice.fish"
    unset XDGC LOGIN_SHELL TEST_OS
}

# ---------------------------------------------------------------- release lookup, against a file:// release list
test_lookup() {
    local shell="$1" H out rid="linux-$ARCH" R="$WORK/release-files"
    echo "== release lookup under $shell (file:// release list, DENICE_INSTALL_OS=linux)"
    TEST_OS=linux; unset XDG
    local V3="0.1.300-pre-release+ccc3333" V0="0.1.050-pre-release+zzz0000"
    mkdir -p "$R"
    make_zip linux "$V3" "$R" >/dev/null; make_zip linux "$V0" "$R" >/dev/null
    cp "$ZIPS/denice-v$V2-linux-$ARCH.zip" "$ZIPS/denice-v$V2-linux-$ARCH.zip.sha256" "$R/"
    cp "$ZIPS/denice-v$V1-linux-$ARCH.zip" "$R/"     # V1 without its .sha256: an older-layout release
    url() { local n="$1"; n="${n//+/%2B}"; echo "file://$R/$n"; }
    asset() { printf '{"name":"x","uploader":{"login":"bot"},"browser_download_url":"%s"}' "$(url "$1")"; }
    rel() {  # rel <draft> <version> <with-sha>
        local a; a="$(asset "denice-v$2-$rid.zip")"
        [ "$3" = yes ] && a="$a,$(asset "denice-v$2-$rid.zip.sha256")"
        a="$a,$(asset "denice-v$2-win-x64.zip")"
        printf '{"tag_name":"%s","author":{"login":"bot"},"draft":%s,"prerelease":true,"assets":[%s]}' "${2%%+*}" "$1" "$a"
    }
    # Newest first: a draft (skipped), a release without a checksum file (skipped), then V2, V0.
    printf '[%s,%s,%s,%s]' "$(rel true "$V3" yes)" "$(rel false "$V1" no)" "$(rel false "$V2" yes)" "$(rel false "$V0" yes)" > "$R/releases.json"
    API="file://$R/releases.json"

    H="$WORK/home-lookup-$(basename "$shell")"
    out="$(run_install "$shell" "$H" --no-run)"
    check "newest non-draft release with a checksum is chosen" test -d "$H/.local/share/denice/versions/$V2"
    check "the draft release is not installed" not test -e "$H/.local/share/denice/versions/$V3"
    check "the download's checksum is verified" contains <(echo "$out") "checksum verified"

    H="$WORK/home-lookup-v-$(basename "$shell")"
    run_install "$shell" "$H" --version "${V0%%+*}" --no-run >/dev/null
    check "--version without +hash finds that release" test -d "$H/.local/share/denice/versions/$V0"
    H="$WORK/home-lookup-vf-$(basename "$shell")"
    run_install "$shell" "$H" --version "$V0" --no-run >/dev/null
    check "--version with +hash finds that release" test -d "$H/.local/share/denice/versions/$V0"
    H="$WORK/home-lookup-none-$(basename "$shell")"
    check "--version of an unknown version fails" not quiet run_install "$shell" "$H" --version 9.9.9 --no-run
    check "...and installs nothing" test "$(tree_of "$H")" = "."

    H="$WORK/home-lookup-bad-$(basename "$shell")"
    cp "$R/denice-v$V2-linux-$ARCH.zip.sha256" "$R/keep.sha256"
    echo "2222222222222222222222222222222222222222222222222222222222222222  x" > "$R/denice-v$V2-linux-$ARCH.zip.sha256"
    check "a download with a wrong checksum is refused" not quiet run_install "$shell" "$H" --no-run
    check "...and installs nothing" test "$(tree_of "$H")" = "."
    mv "$R/keep.sha256" "$R/denice-v$V2-linux-$ARCH.zip.sha256"
    unset API
}

for shell in "${SHELLS[@]}"; do
    test_osx "$shell"
    test_linux "$shell" unset
    test_linux "$shell" set
    test_lookup "$shell"
    test_profiles "$shell" osx
    test_profiles "$shell" linux
done

echo "== $passed checks passed"
