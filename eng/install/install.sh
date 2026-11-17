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
# Denice updates itself afterwards: it downloads new versions into versions/ and can replace the loader.
# --uninstall removes exactly the items above (the whole versions/ folder) and never settings.yaml.
#
# Test-only environment overrides (not for users):
#   DENICE_INSTALL_OS=osx|linux   pretend to run on that OS (lets the Linux branch be tested on a Mac)
#   DENICE_INSTALL_API_URL=<url>  the GitHub releases API URL to read (tests point it at a file:// URL)
#
# POSIX sh only: it runs piped into `sh`, which is dash on many Linux systems.
set -eu

REPO="denice-aios/denice"
API_URL="${DENICE_INSTALL_API_URL:-https://api.github.com/repos/$REPO/releases?per_page=100}"

say() { printf 'denice-install: %s\n' "$*"; }
fail() { printf 'denice-install: error: %s\n' "$*" >&2; exit 1; }

usage() {
    cat <<'EOF'
Usage: install.sh [--version <version>] [--from <zip>] [--no-run]
       install.sh --uninstall
       install.sh --help

Installs Denice for the current user (no administrator rights needed).

  (no option)          install the newest Denice release (pre-releases included)
  --version <version>  install that version, e.g. 0.1.108520000-pre-release or 0.1.108520000-pre-release+abc1234
  --from <zip>         install from a downloaded release zip (denice-v<version>-<rid>.zip); a .zip.sha256 file
                       beside it is checked when present
  --no-run             do not run `denice version` at the end
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
while [ $# -gt 0 ]; do
    case "$1" in
        --version) [ $# -ge 2 ] || fail "--version needs a version"; want_version="$2"; shift 2 ;;
        --version=*) want_version="${1#--version=}"; shift ;;
        --from) [ $# -ge 2 ] || fail "--from needs a zip file"; from_zip="$2"; shift 2 ;;
        --from=*) from_zip="${1#--from=}"; shift ;;
        --no-run) run_after=no; shift ;;
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

bin_dir="$HOME/.local/bin"
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
if command -v sha256sum >/dev/null 2>&1; then sha256_of() { sha256sum "$1" | cut -d' ' -f1; }
elif command -v shasum >/dev/null 2>&1; then sha256_of() { shasum -a 256 "$1" | cut -d' ' -f1; }
else fail "'sha256sum' or 'shasum' is needed to check the download but neither was found"; fi

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

# ---------------------------------------------------------------- get the release zip
if [ -n "$from_zip" ]; then
    [ -f "$from_zip" ] || fail "no such file: $from_zip"
    zip="$from_zip"
    if [ -f "$from_zip.sha256" ]; then check_sum "$zip" "$from_zip.sha256"
    else say "no $(basename "$from_zip").sha256 beside the zip: installing without a checksum check"; fi
else
    need curl
    say "looking up Denice releases for $rid"
    curl -fsSL -H 'Accept: application/vnd.github+json' -o "$tmp/releases.json" "$API_URL" \
        || fail "cannot read the release list from $API_URL"
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
case ":${PATH:-}:" in
    *":$bin_dir:"*) ;;
    *) say "hint: $bin_dir is not on your PATH; add it (export PATH=\"\$HOME/.local/bin:\$PATH\") to type 'denice'" ;;
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
