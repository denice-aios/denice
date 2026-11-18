#!/bin/sh
# Copyright (c) Denice.ai. All rights reserved.
#
# Installs Denice for the current user on macOS or Linux (owner decision pkg-494745156d56). No administrator rights,
# no background service. Runs as:
#
#   curl -fsSL https://raw.githubusercontent.com/denice-aios/denice/dev/eng/install/install.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/denice-aios/denice/dev/eng/install/install.sh | sh -s -- --uninstall
#
# What it installs:
#   macOS  ~/Applications/Denice.app                                 the loader app
#          ~/Library/Application Support/Denice/versions/<version>/  the version (Denice manages the others)
#          ~/.local/bin/denice -> ~/Applications/Denice.app/Contents/MacOS/denice
#   Linux  $XDG_DATA_HOME/denice/denice  (default ~/.local/share)    the loader
#          $XDG_DATA_HOME/denice/versions/<version>/                 the version (Denice manages the others)
#          ~/.local/bin/denice -> the loader
#          $XDG_DATA_HOME/applications/denice.desktop                the menu entry
#
# PATH (owner decision pkg-d4e90c3ce176 install-path), unless --no-modify-path, the way rustup does it:
#   <data folder>/env (the versions/ folder's parent)  puts ~/.local/bin on PATH when it is not there yet
#   one marked line that sources it, added to ~/.profile (created when missing); to ~/.bash_profile, ~/.bash_login
#          and ~/.bashrc when they exist; and when zsh is here, to .zshenv ($ZDOTDIR's if it exists or ~/.zshenv
#          does not, else ~/.zshenv)
#   $XDG_CONFIG_HOME/fish/conf.d/denice.fish  (default ~/.config)   when fish is here
#
# Every download is verified before anything is installed (owner decision pkg-3480bb950ef7 download-verification,
# "Always verify"), with the rule Denice Runtime applies to every update:
#   - the zip matches its .zip.sha256 (always for a download; for --from when that file is beside the zip);
#   - versions/<version>/SHA256SUMS lists every file of the version, each `<hash>  <name>`, and every file matches;
#   - its cosign signature, versions/<version>/SHA256SUMS.cosign.bundle, was made by Denice's release workflow
#     (ci.yml on dev or main, signed keyless through GitHub Actions; Sigstore's public transparency log);
#   - the loader at the top of the zip equals the loader the version carries (so it is covered by SHA256SUMS too).
# The signature check uses `cosign` from PATH when it is version 3.0.4 or newer (older ones accept forged bundles,
# GHSA-whqx-f9j3-ch6m). Otherwise it downloads Sigstore's own cosign v3.1.3 for this computer from GitHub (about
# 133-149 MB), checks it against the SHA-256 written below, uses it once from the temporary folder and deletes it.
# Without a usable cosign (none installed and the download fails, e.g. offline with --from) nothing is installed.
# Any failed check refuses the install and leaves nothing behind.
#
# Denice updates itself afterwards: it downloads new versions into versions/ and can replace the loader.
# --uninstall removes exactly the items above (the whole versions/ folder, the marked profile lines and the line
# break or file added with them) and never settings.yaml.
#
# Test-only environment overrides (not for users):
#   DENICE_INSTALL_OS=osx|linux   pretend to run on that OS (lets the Linux branch be tested on a Mac)
#   DENICE_INSTALL_LIST_URL=<url>  the signed release list to read, with its signature at <url>.cosign.bundle (tests
#                                 point it at a file:// URL)
#   DENICE_INSTALL_COSIGN_URL=<url>  where to download cosign-<os>-<arch> from (tests point it at a file:// URL; the
#                                 download must still match the SHA-256 written in this script)
#
# POSIX sh only: it runs piped into `sh`, which is dash on many Linux systems.
set -eu

REPO="denice-aios/denice"
# The release list: GitHub's own list of releases, which CI publishes and signs at every release on the permanent
# release-list release, so this script never calls GitHub's API and its 60-requests-per-hour limit (pkg-05bdc04efda0
# github-limit "c"; src/Denice.Updates/UpdateService.cs ReleasesUrl reads the same file).
LIST_URL="${DENICE_INSTALL_LIST_URL:-https://github.com/$REPO/releases/download/release-list/releases.json}"

# The release signer, exactly as Denice Runtime pins it (src/Denice.Updates/ReleaseSignature.cs DeniceWorkflowPattern
# and GitHubActionsIssuer; eng/install/test-install.sh checks the two stay equal).
SIGNER_IDENTITY='^https://github\.com/denice-aios/denice/\.github/workflows/ci\.yml@refs/heads/(dev|main)$'
SIGNER_ISSUER='https://token.actions.githubusercontent.com'
# The cosign CI signs with (.github/workflows/ci.yml cosign-release), downloaded when no usable cosign is installed.
COSIGN_VERSION="3.1.3"
COSIGN_URL="${DENICE_INSTALL_COSIGN_URL:-https://github.com/sigstore/cosign/releases/download/v$COSIGN_VERSION}"
# <sha256> <bytes> of each cosign v3.1.3 binary, from that release's official checksum file
# https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign_checksums.txt (sizes from the release's assets).
cosign_pin() {
    case "$1" in
        cosign-darwin-arm64) echo "5cf948c2f4dfe59687bdd0b8523709067383e03982cc543475c8a7dc70e92a76 139618946" ;;
        cosign-darwin-amd64) echo "2347488e5d5b25336644024dfeca5601b190e91197a71a917bda44744aff106c 148611192" ;;
        cosign-linux-arm64)  echo "c5d324e091826b0d7a78eb16fef316450b4eb9aaec045611c08ba06f5e73220a 132747403" ;;
        cosign-linux-amd64)  echo "4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71 141178250" ;;
        *) return 1 ;;
    esac
}

say() { printf 'denice-install: %s\n' "$*"; }
fail() { printf 'denice-install: error: %s\n' "$*" >&2; exit 1; }

usage() {
    cat <<'EOF'
Usage: install.sh [--version <version>] [--from <zip>] [--no-run] [--no-modify-path]
       install.sh --uninstall
       install.sh --help

Installs Denice for the current user (no administrator rights needed). Every download (and every --from zip) is
verified first: the checksums of all its files and their cosign signature by Denice's release workflow. This uses
cosign 3.0.4 or newer when installed; otherwise it downloads Sigstore's cosign (about 140 MB) for this one check and
deletes it. Nothing is installed when a check fails or cosign is unavailable.

  (no option)          install the newest Denice release (pre-releases included)
  --version <version>  install that version, e.g. 0.1.108520000-pre-release or 0.1.108520000-pre-release+abc1234
  --from <zip>         install from a downloaded release zip (denice-v<version>-<rid>.zip); a .zip.sha256 file
                       beside it is checked when present
  --no-run             do not run `denice version` at the end
  --no-modify-path     do not add ~/.local/bin to PATH in your shell profiles (zsh, bash, fish, ~/.profile)
  --uninstall          remove what this script installs; settings.yaml is kept
  --help               show this help

Piped use:  curl -fsSL <url>/install.sh | sh -s -- --version <version>
EOF
}

# The whole run is one function called on the last line, so `curl | sh` executes nothing until the script has fully
# arrived (a cut-off download cannot run half a script), and no command can read the rest of the script from stdin.
main() {
# ---------------------------------------------------------------- arguments
action=install
want_version=""
from_zip=""
run_after=yes
modify_path=yes
while [ $# -gt 0 ]; do
    case "$1" in
        --version) [ $# -ge 2 ] || fail "--version needs a version"; want_version="$2"; shift 2 ;;
        --version=*) want_version="${1#--version=}"; shift ;;
        --from) [ $# -ge 2 ] || fail "--from needs a zip file"; from_zip="$2"; shift 2 ;;
        --from=*) from_zip="${1#--from=}"; shift ;;
        --no-run) run_after=no; shift ;;
        --no-modify-path) modify_path=no; shift ;;
        --uninstall) action=uninstall; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; fail "unknown option: $1" ;;
    esac
done
[ -n "$want_version" ] && [ -n "$from_zip" ] && fail "use either --version or --from, not both"
[ -n "${HOME:-}" ] || fail "HOME is not set"

# ---------------------------------------------------------------- platform
os="${DENICE_INSTALL_OS:-}"
if [ -z "$os" ]; then
    case "$(uname -s)" in
        Darwin) os=osx ;;
        Linux) os=linux ;;
        *) fail "unsupported system '$(uname -s)': this script installs Denice on macOS and Linux; on Windows use denice-setup-v<version>-<rid>.exe from the release page" ;;
    esac
fi
case "$os" in osx|linux) ;; *) fail "DENICE_INSTALL_OS must be osx or linux, not '$os'" ;; esac

case "$(uname -m)" in
    arm64|aarch64) arch=arm64 ;;
    x86_64|amd64) arch=x64 ;;
    *) fail "unsupported processor '$(uname -m)': Denice is built for x64 and arm64" ;;
esac
# A Terminal running under Rosetta reports x86_64 on an Apple silicon Mac; install the native build there.
if [ "$os" = osx ] && [ "$arch" = x64 ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then
    arch=arm64
fi
rid="$os-$arch"

bin_rel=".local/bin"
bin_dir="$HOME/$bin_rel"
link="$bin_dir/denice"
if [ "$os" = osx ]; then
    loader_name="Denice.app"
    loader_dir="$HOME/Applications"
    data_dir="$HOME/Library/Application Support/Denice"
    desktop_file=""
else
    xdg_data="${XDG_DATA_HOME:-$HOME/.local/share}"
    loader_name="denice"
    loader_dir="$xdg_data/denice"
    data_dir="$loader_dir"
    desktop_file="$xdg_data/applications/denice.desktop"
fi
loader="$loader_dir/$loader_name"
versions_dir="$data_dir/versions"
if [ "$os" = osx ]; then program="$loader/Contents/MacOS/denice"; else program="$loader"; fi

# ---------------------------------------------------------------- PATH in shell profiles (pkg-d4e90c3ce176 install-path)
# Every profile line this script adds ends with $path_marker; uninstall removes exactly the lines carrying it. The two
# longer forms also record what else was added with the line, so uninstall can take that away too.
path_marker="# added by the Denice installer"
marker_new_file="$path_marker, with this file"
marker_after_break="$path_marker, with the line break before it"
env_file="$data_dir/env"
fish_file="${XDG_CONFIG_HOME:-$HOME/.config}/fish/conf.d/denice.fish"
zdotdir="${ZDOTDIR:-}"
owned_file_header="# Puts ~/$bin_rel on PATH for 'denice'. $path_marker; install.sh --uninstall removes this file."

# Escapes \ " ` $ for use inside a double-quoted shell string.
sh_quote() { printf '%s' "$1" | sed 's/[\\"`$]/\\&/g'; }
case "$env_file" in
    "$HOME"/*) env_ref="\$HOME/$(sh_quote "${env_file#"$HOME"/}")" ;;
    *) env_ref="$(sh_quote "$env_file")" ;;
esac
source_line=". \"$env_ref\""

# True when <shell> is the login shell or is installed (rustup's test).
has_shell() {
    case "${SHELL:-}" in *"$1"*) return 0 ;; esac
    command -v "$1" >/dev/null 2>&1
}

# each_profile <command> <all>: runs <command> <file> for every profile that gets the line, chosen as rustup does.
# With <all>=all it visits every file that could hold one (uninstall), whatever exists or is installed now.
each_profile() {
    "$1" "$HOME/.profile"
    for rc in .bash_profile .bash_login .bashrc; do
        if [ "$2" = all ] || [ -f "$HOME/$rc" ]; then "$1" "$HOME/$rc"; fi
    done
    if [ "$2" = all ]; then
        "$1" "$HOME/.zshenv"
        if [ -n "$zdotdir" ]; then "$1" "$zdotdir/.zshenv"; fi
    elif has_shell zsh; then
        # zsh reads $ZDOTDIR/.zshenv when ZDOTDIR is set, else ~/.zshenv; an existing file wins.
        if [ -n "$zdotdir" ] && { [ -f "$zdotdir/.zshenv" ] || [ ! -f "$HOME/.zshenv" ]; }; then
            "$1" "$zdotdir/.zshenv"
        else
            "$1" "$HOME/.zshenv"
        fi
    fi
}

# Appends the marked source line to <profile> once.
add_path_line() {
    if [ -f "$1" ] && grep -qF "$path_marker" "$1"; then return 0; fi
    if [ ! -e "$1" ]; then
        mkdir -p "$(dirname "$1")" && printf '%s %s\n' "$source_line" "$marker_new_file" > "$1"
    elif [ -n "$(tail -c 1 "$1")" ]; then
        printf '\n%s %s\n' "$source_line" "$marker_after_break" >> "$1"
    else
        printf '%s %s\n' "$source_line" "$path_marker" >> "$1"
    fi || { say "could not add $bin_dir to PATH in $1"; return 0; }
    say "added $bin_dir to PATH in $1"
}

# Removes the marked lines from <profile>, and the line break or the whole file added with them; every other byte of
# the file stays as it is. A line break added before the line stays when the user wrote more after it.
remove_path_line() {
    [ -f "$1" ] && grep -qF "$path_marker" "$1" || return 0
    if [ -z "$(tail -c 1 "$1")" ]; then ends_in_break=yes; else ends_in_break=no; fi
    DENICE_M="$path_marker" DENICE_B="$marker_after_break" DENICE_N="$marker_new_file" DENICE_E="$ends_in_break" awk '
        BEGIN { m = ENVIRON["DENICE_M"]; b = ENVIRON["DENICE_B"]; n = ENVIRON["DENICE_N"] }
        index($0, n) { created = 1 }
        index($0, b) { drop_break = 1; next }
        index($0, m) { next }
        { printf "%s%s", sep, $0; sep = "\n"; drop_break = 0; last = NR }
        END {
            if (sep != "" && !drop_break && (last < NR || ENVIRON["DENICE_E"] == "yes")) printf "\n"
            if (created && sep == "") exit 3
        }' "$1" > "$1.denice-tmp" && status=0 || status=$?
    case "$status" in
        0) cat "$1.denice-tmp" > "$1"; rm -f "$1.denice-tmp"; say "removed the Denice PATH line from $1" ;;
        3) rm -f "$1.denice-tmp" "$1"; say "removed $1 (the install script created it)" ;;
        *) rm -f "$1.denice-tmp"; say "could not remove the Denice PATH line from $1" ;;
    esac
}

# Removes the link only when it is a symbolic link (a file the user put there is left alone).
remove_link() {
    if [ -L "$link" ]; then
        rm -f "$link"; say "removed $link"
    elif [ -e "$link" ]; then
        say "left $link alone: it is not a link this script made"
    fi
}

# ---------------------------------------------------------------- uninstall
if [ "$action" = uninstall ]; then
    say "uninstalling Denice ($rid)"
    for item in "$loader" "$loader.ver" "$loader.old" "$versions_dir"; do
        if [ -e "$item" ] || [ -L "$item" ]; then rm -rf "$item"; say "removed $item"; fi
    done
    remove_link
    each_profile remove_path_line all
    if [ -f "$fish_file" ] && grep -qF "$path_marker" "$fish_file"; then rm -f "$fish_file"; say "removed $fish_file"; fi
    if [ -f "$env_file" ]; then rm -f "$env_file"; say "removed $env_file"; fi
    if [ -n "$desktop_file" ] && [ -f "$desktop_file" ]; then rm -f "$desktop_file"; say "removed $desktop_file"; fi
    # Linux: the install folder is this script's own; it goes when nothing else (such as settings) is in it.
    if [ "$os" = linux ] && [ -d "$loader_dir" ]; then rmdir "$loader_dir" 2>/dev/null || say "kept $loader_dir: it holds other files"; fi
    say "kept Denice's settings (settings.yaml) and data; delete them yourself if you want them gone"
    say "Denice is uninstalled"
    exit 0
fi

# ---------------------------------------------------------------- tools
need() { command -v "$1" >/dev/null 2>&1 || fail "'$1' is needed but not found; install it and run this again"; }
need unzip
need diff
if command -v sha256sum >/dev/null 2>&1; then sha256_tool="sha256sum"
elif command -v shasum >/dev/null 2>&1; then sha256_tool="shasum -a 256"
else fail "'sha256sum' or 'shasum' is needed to check the download but neither was found"; fi
sha256_of() { $sha256_tool "$1" | cut -d' ' -f1; }

tmp="$(mktemp -d 2>/dev/null || mktemp -d -t denice-install)" || fail "cannot create a temporary folder"
trap 'rm -rf "$tmp"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# Checks <zip> against the GNU checksum file <sha256> ("<hex>  <name>"); refuses on mismatch.
check_sum() {
    expected="$(cut -d' ' -f1 < "$2" | head -n 1 | tr 'ABCDEF' 'abcdef')"
    [ -n "$expected" ] || fail "the checksum file $2 is empty"
    actual="$(sha256_of "$1" | tr 'ABCDEF' 'abcdef')"
    [ "$expected" = "$actual" ] || fail "checksum mismatch for $1 (expected $expected, got $actual); nothing was installed"
    say "checksum verified ($actual)"
}

# Refuses the install: nothing has been installed yet, and the temporary folder goes at exit.
refuse() { fail "$1; nothing was installed"; }

# Checks <version-dir>/SHA256SUMS the way Denice Runtime does (VersionInstaller.ChecksumProblem): every line is
# `<hash>  <name>`, every file of the version is listed and matches, every listed file is there.
check_version_sums() {
    sums="$1/SHA256SUMS"
    [ -f "$sums" ] || refuse "the release has no versions/$version/SHA256SUMS"
    [ -f "$sums.cosign.bundle" ] || refuse "the release has no versions/$version/SHA256SUMS.cosign.bundle (its signature)"
    bad="$(grep -nvE '^[0-9a-f]{64}  [^ ]' "$sums" | head -n 1)" || true
    [ -z "$bad" ] || refuse "versions/$version/SHA256SUMS has a malformed line ($bad)"
    cut -c67- "$sums" | LC_ALL=C sort > "$tmp/listed"
    bad="$(grep -E '^/|(^|/)\.\.(/|$)' "$tmp/listed" | head -n 1)" || true
    [ -z "$bad" ] || refuse "versions/$version/SHA256SUMS names a file outside the version folder ($bad)"
    (cd "$1" && find . -type f ! -path ./SHA256SUMS ! -path ./SHA256SUMS.cosign.bundle) | sed 's#^\./##' \
        | LC_ALL=C sort > "$tmp/present"
    bad="$(LC_ALL=C comm -13 "$tmp/listed" "$tmp/present" | head -n 1)"
    [ -z "$bad" ] || refuse "'$bad' in the release is not listed in its SHA256SUMS"
    bad="$(LC_ALL=C comm -23 "$tmp/listed" "$tmp/present" | head -n 1)"
    [ -z "$bad" ] || refuse "'$bad' is listed in the release's SHA256SUMS but missing"
    (cd "$1" && $sha256_tool -c SHA256SUMS) > "$tmp/sums.log" 2>&1 || {
        bad="$(grep -v ': OK$' "$tmp/sums.log" | head -n 1)"
        refuse "a file of the release does not match its SHA256SUMS ($bad)"
    }
    say "all $(wc -l < "$tmp/listed" | tr -d ' ') files match versions/$version/SHA256SUMS"
}

# Prints the version of cosign <program> when it is 3.0.4 or newer (3.x); fails otherwise.
cosign_usable() {
    cv="$("$1" version 2>/dev/null | sed -n 's/^GitVersion: *v\{0,1\}\([0-9][0-9.]*\).*/\1/p' | head -n 1)" || true
    case "$cv" in [0-9]*.[0-9]*.[0-9]*) ;; *) return 1 ;; esac
    major="${cv%%.*}"; rest="${cv#*.}"; minor="${rest%%.*}"; patch="${rest#*.}"; patch="${patch%%.*}"
    [ "$major" = 3 ] && { [ "$minor" -gt 0 ] || [ "$patch" -ge 4 ]; } || return 1
    printf '%s\n' "$cv"
}

# Sets $cosign to a cosign that can check Denice's signature: the installed one when it is new enough, otherwise
# Sigstore's own release binary, downloaded into the temporary folder and checked against the pin above.
find_cosign() {
    [ -z "${cosign:-}" ] || return 0   # already found for the release list
    cosign_home=""
    if command -v cosign >/dev/null 2>&1; then
        if found="$(cosign_usable cosign)"; then
            cosign="cosign"; say "checking the signature with the installed cosign $found ($(command -v cosign))"; return 0
        fi
        say "the installed cosign ($(command -v cosign)) is not cosign 3.0.4 or a newer 3.x, which this check needs"
    fi
    case "$(uname -s)" in Darwin) c_os=darwin ;; Linux) c_os=linux ;; *) refuse "no cosign for $(uname -s)" ;; esac
    case "$arch" in arm64) c_arch=arm64 ;; *) c_arch=amd64 ;; esac
    c_name="cosign-$c_os-$c_arch"
    pin="$(cosign_pin "$c_name")" || refuse "no pinned cosign for $c_name"
    c_sum="${pin% *}"; c_size="${pin#* }"
    command -v curl >/dev/null 2>&1 || refuse "cosign is needed to check the release's signature and neither cosign nor curl (to download it) is installed"
    say "downloading Sigstore's cosign $COSIGN_VERSION ($c_name, $((c_size / 1000000)) MB) to check the release's signature; it is used for this check only and deleted afterwards"
    mkdir "$tmp/cosign-home"
    curl -fSL --progress-bar -o "$tmp/cosign" "$COSIGN_URL/$c_name" \
        || refuse "cannot download cosign from $COSIGN_URL/$c_name, and it is needed to check the release's signature (install cosign 3.0.4 or newer, https://docs.sigstore.dev/cosign/system_config/installation/, and run this again)"
    actual="$(sha256_of "$tmp/cosign")"
    [ "$actual" = "$c_sum" ] || refuse "the downloaded cosign does not match the SHA-256 written in this script (expected $c_sum, got $actual)"
    chmod 755 "$tmp/cosign"
    say "cosign download verified ($actual)"
    cosign="$tmp/cosign"; cosign_home="$tmp/cosign-home"
}

# Runs $cosign; the downloaded one keeps its Sigstore trust data and any cache in the temporary folder.
run_cosign() {
    # cosign caches Sigstore's trust data under TUF_ROOT (default ~/.sigstore); keep it in this run's temporary folder
    # so a check never leaves files in the user's home, whichever cosign runs.
    if [ -n "$cosign_home" ]; then HOME="$cosign_home" TUF_ROOT="$cosign_home/sigstore" "$cosign" "$@"
    else TUF_ROOT="$tmp/sigstore" "$cosign" "$@"; fi
}

# verify_signed <file> <refusal>: <file> must be signed by Denice's release workflow (<file>.cosign.bundle); otherwise
# the install is refused with <refusal>.
verify_signed() {
    run_cosign verify-blob --certificate-identity-regexp "$SIGNER_IDENTITY" \
        --certificate-oidc-issuer "$SIGNER_ISSUER" --bundle "$1.cosign.bundle" "$1" > "$tmp/cosign.log" 2>&1 || {
        sed 's/^/  cosign: /' "$tmp/cosign.log" >&2
        refuse "$2"
    }
}

# Checks the unpacked release <root> holding versions/$version: SHA256SUMS, its cosign signature, the loader copy.
verify_release() {
    vdir="$1/versions/$version"
    check_version_sums "$vdir"
    diff -r "$1/$loader_name" "$vdir/$loader_name" > /dev/null 2>&1 \
        || refuse "the loader $loader_name at the top of the release differs from the one in versions/$version"
    find_cosign
    verify_signed "$vdir/SHA256SUMS" "the release's cosign signature is not a valid signature by Denice's release workflow"
    rm -rf "$tmp/cosign" "$tmp/cosign-home"
    say "signature verified: signed by Denice's release workflow ($SIGNER_IDENTITY)"
}

# ---------------------------------------------------------------- get the release zip
if [ -n "$from_zip" ]; then
    [ -f "$from_zip" ] || fail "no such file: $from_zip"
    zip="$from_zip"
    if [ -f "$from_zip.sha256" ]; then check_sum "$zip" "$from_zip.sha256"
    else say "no $(basename "$from_zip").sha256 beside the zip: checking its signed contents only"; fi
else
    need curl
    say "looking up Denice releases for $rid"
    curl -fsSL -o "$tmp/releases.json" "$LIST_URL" || fail "cannot read the release list from $LIST_URL"
    curl -fsSL -o "$tmp/releases.json.cosign.bundle" "$LIST_URL.cosign.bundle" \
        || refuse "cannot read the release list's signature from $LIST_URL.cosign.bundle"
    find_cosign
    verify_signed "$tmp/releases.json" "the release list is not signed by Denice's release workflow"
    say "release list verified: signed by Denice's release workflow"
    # The release list is newest first. Keep the download links of releases that are not drafts, in order; GitHub
    # writes each release's "draft" field before its assets.
    grep -o '"draft": *[a-z]*\|"browser_download_url": *"[^"]*"' "$tmp/releases.json" \
        | awk '/"draft"/ { draft = ($0 ~ /true/); next } !draft { sub(/^"browser_download_url": *"/, ""); sub(/"$/, ""); print }' \
        > "$tmp/urls" || true
    zip_url=""
    while IFS= read -r url; do
        name="$(basename "$url" | sed 's/%2[Bb]/+/g')"
        case "$name" in "denice-v"*"-$rid.zip") ;; *) continue ;; esac
        if [ -n "$want_version" ]; then
            case "$name" in "denice-v$want_version-$rid.zip"|"denice-v$want_version+"*"-$rid.zip") ;; *) continue ;; esac
        fi
        grep -qxF "$url.sha256" "$tmp/urls" || continue   # releases without a checksum file use an older layout
        zip_url="$url"; zip_name="$name"; break
    done < "$tmp/urls"
    if [ -z "$zip_url" ]; then
        if [ -n "$want_version" ]; then fail "no release of version $want_version has a $rid download"
        else fail "no release has a $rid download yet"; fi
    fi
    zip="$tmp/$zip_name"
    say "downloading $zip_name"
    curl -fSL --progress-bar -o "$zip" "$zip_url" || fail "download failed: $zip_url"
    curl -fsSL -o "$zip.sha256" "$zip_url.sha256" || fail "download failed: $zip_url.sha256"
    check_sum "$zip" "$zip.sha256"
fi

# ---------------------------------------------------------------- unpack and check the layout
mkdir "$tmp/x"
unzip -q "$zip" -d "$tmp/x" < /dev/null || fail "cannot unpack $zip"
[ -d "$tmp/x/versions" ] || fail "$zip is not a Denice release: it has no versions/ folder"
version=""
for dir in "$tmp/x/versions"/*; do
    [ -d "$dir" ] || continue
    [ -z "$version" ] || fail "$zip holds more than one version"
    version="$(basename "$dir")"
done
[ -n "$version" ] || fail "$zip holds no version"
src_loader="$tmp/x/$loader_name"
if [ "$os" = osx ]; then src_program="$src_loader/Contents/MacOS/denice"; else src_program="$src_loader"; fi
[ -f "$src_program" ] || fail "$zip is not a $rid release: it has no $loader_name loader"
[ -x "$src_program" ] || fail "the loader in $zip is not executable"
verify_release "$tmp/x"
say "installing Denice $version ($rid)"

# ---------------------------------------------------------------- version folder
mkdir -p "$versions_dir"
dest="$versions_dir/$version"
if [ -d "$dest" ]; then
    say "version $version is already in $versions_dir; left as it is"
else
    # Copied under the .partial name Denice never chooses, then renamed, so a half-copied version is never run.
    rm -rf "$dest.partial"
    cp -Rp "$tmp/x/versions/$version" "$dest.partial"
    mv "$dest.partial" "$dest"
    [ "$os" = osx ] && [ -n "$from_zip" ] && { xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true; }
    say "installed $dest"
fi

# ---------------------------------------------------------------- loader, replaced the way Denice replaces it:
# copy to <loader>.ver, rename the current one to <loader>.old, rename .ver into place, remove .old.
mkdir -p "$loader_dir"
rm -rf "$loader.ver" "$loader.old"
cp -Rp "$src_loader" "$loader.ver"
[ "$os" = osx ] && [ -n "$from_zip" ] && { xattr -dr com.apple.quarantine "$loader.ver" 2>/dev/null || true; }
if [ -e "$loader" ] || [ -L "$loader" ]; then
    mv "$loader" "$loader.old" || { rm -rf "$loader.ver"; fail "cannot move the installed $loader aside"; }
fi
if ! mv "$loader.ver" "$loader"; then
    [ -e "$loader.old" ] && mv "$loader.old" "$loader"
    fail "cannot put the new loader in place; the previous one was restored"
fi
rm -rf "$loader.old"
say "installed $loader"

# ---------------------------------------------------------------- Terminal link
mkdir -p "$bin_dir"
if [ -e "$link" ] && [ ! -L "$link" ]; then
    say "left $link alone: a file that is not a link is already there; run $program directly"
else
    ln -sfn "$program" "$link"
    say "linked $link -> $program"
fi

# ---------------------------------------------------------------- PATH (pkg-d4e90c3ce176 install-path)
if [ "$modify_path" = yes ]; then
    # The env file only adds ~/.local/bin when it is missing, so sourcing it twice changes nothing.
    cat > "$env_file" <<EOF
#!/bin/sh
$owned_file_header
case ":\${PATH}:" in
    *:"\$HOME/$bin_rel":*) ;;
    *) export PATH="\$HOME/$bin_rel:\$PATH" ;;
esac
EOF
    say "wrote $env_file"
    each_profile add_path_line some
    if has_shell fish; then
        if [ -f "$fish_file" ] && ! grep -qF "$path_marker" "$fish_file"; then
            say "left $fish_file alone: it is not a file this script made"
        else
            [ -f "$fish_file" ] && fish_note="" || fish_note="added $bin_dir to PATH in $fish_file"
            mkdir -p "$(dirname "$fish_file")"
            cat > "$fish_file" <<EOF
$owned_file_header
if not contains -- "\$HOME/$bin_rel" \$PATH
    set -gx PATH "\$HOME/$bin_rel" \$PATH
end
EOF
            [ -z "$fish_note" ] || say "$fish_note"
        fi
    fi
fi
case ":${PATH:-}:" in
    *":$bin_dir:"*) ;;
    *) if [ "$modify_path" = yes ]; then
           say "a new terminal finds 'denice'; to use it in this one, run: . \"$env_ref\""
       else
           say "hint: $bin_dir is not on your PATH (--no-modify-path); add it (export PATH=\"\$HOME/$bin_rel:\$PATH\") to type 'denice'"
       fi ;;
esac

# ---------------------------------------------------------------- menu entry (Linux)
if [ -n "$desktop_file" ]; then
    mkdir -p "$(dirname "$desktop_file")"
    # Desktop Entry Specification: inside a quoted Exec argument escape \ " ` $ with a backslash (then \ again,
    # as the file's string escaping), and write % as %%.
    exec_path="$(printf '%s' "$program" | sed -e 's/\\/\\\\\\\\/g' -e 's/["`$]/\\\\&/g' -e 's/%/%%/g')"
    cat > "$desktop_file.tmp" <<EOF
[Desktop Entry]
Type=Application
Name=Denice
Comment=Denice command-line program
Exec="$exec_path"
Terminal=true
Categories=Development;
EOF
    mv "$desktop_file.tmp" "$desktop_file"
    command -v update-desktop-database >/dev/null 2>&1 \
        && { update-desktop-database "$(dirname "$desktop_file")" >/dev/null 2>&1 || true; }
    say "added the menu entry $desktop_file"
fi

# ---------------------------------------------------------------- show it works
say "Denice $version is installed"
if [ "$run_after" = yes ]; then
    say "running: denice version"
    "$program" version < /dev/null || fail "the installed Denice did not start ('$program version' failed)"
fi
}

main "$@"
