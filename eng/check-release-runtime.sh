#!/usr/bin/env bash
# rust/REQUIREMENTS.md E3 on a PUBLISHED release: Denice Runtime, as the release ships it, accepts the genuine release
# file of this machine's platform (SHA256SUMS, its cosign bundle against Denice's CI identity, and on macOS / Windows the
# OS code signatures by a pinned Denice certificate) and refuses altered copies, leaving no version behind.
# eng/release-probe is compiled against, and runs, the Denice assemblies inside the release's own version folder, and
# hands each file to Denice.Updates.VersionInstaller exactly as `denice update` does after its download.
#
# Usage: eng/check-release-runtime.sh <rid> <assets-dir> <work-dir>
#   <rid>         this machine's platform (the OS code-signature check is the running OS's): osx-*, win-*, linux-*
#   <assets-dir>  the release's downloaded assets (eng/check-release.sh leaves them in <its work-dir>/assets)
# Requires: the pinned .NET SDK (global.json), unzip (or 7z on Windows).
set -euo pipefail

[ "$#" -eq 3 ] || { echo "usage: check-release-runtime.sh <rid> <assets-dir> <work-dir>" >&2; exit 2; }
RID="$1"; ASSETS="$(cd "$2" && pwd)"; WORK="$3"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rm -rf "$WORK"; mkdir -p "$WORK"; WORK="$(cd "$WORK" && pwd)"

zips=( "$ASSETS"/denice-v*-"$RID".zip )
[ "${#zips[@]}" -eq 1 ] && [ -f "${zips[0]}" ] || { echo "release-runtime: FAIL: expected one denice-v*-$RID.zip in $ASSETS" >&2; exit 1; }
ZIP="${zips[0]}"
VERSION="$(basename "$ZIP")"; VERSION="${VERSION#denice-v}"; VERSION="${VERSION%-$RID.zip}"
# Another platform's genuine release supplies a genuine cosign bundle over a different SHA256SUMS.
case "$RID" in linux-x64) OTHER_RID=osx-x64 ;; *) OTHER_RID=linux-x64 ;; esac
OTHER="$ASSETS/denice-v$VERSION-$OTHER_RID.zip"

if command -v unzip > /dev/null 2>&1; then unzip -q "$ZIP" -d "$WORK/release"; else 7z x -bso0 -bsp0 "-o$WORK/release" "$ZIP"; fi
SHIPPED="$WORK/release/versions/$VERSION"
case "$RID" in win-*) to_native() { cygpath -w "$1"; } ;; *) to_native() { echo "$1"; } ;; esac

dotnet build "$(to_native "$REPO_ROOT/eng/release-probe/ReleaseProbe.csproj")" -nologo -v q \
  -o "$(to_native "$WORK/probe")" "-p:DeniceReleaseVersionDir=$(to_native "$SHIPPED")"
mkdir -p "$WORK/cases"
dotnet "$(to_native "$WORK/probe/ReleaseProbe.dll")" "$(to_native "$SHIPPED")" "$(to_native "$WORK/cases")" \
  "$(to_native "$ZIP")" "$(to_native "$OTHER")"
