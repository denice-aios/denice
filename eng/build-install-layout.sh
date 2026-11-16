#!/usr/bin/env bash
# Builds one platform's Denice install layout, exactly as a release ships it and an update unpacks it:
#
#   <out>/denice[.exe]                                   the loader (Tim: "Name the loader “denice”"); on macOS the app
#                                                        <out>/Denice.app/Contents/MacOS/denice (pkg-697310396f74)
#   <out>/versions/<version>/denice-v<version>-<rid>     the versioned Rust entry, named BY THE BUILD (--emit=link)
#   <out>/versions/<version>/Denice.Runtime.dll + the self-contained .NET runtime (no .NET executable)
#   <out>/versions/<version>/denice[.exe] | Denice.app   the loader this version carries (to replace an older one)
#
# Usage: eng/build-install-layout.sh <rid> <out-dir> [channel]
#   <rid>      osx-arm64 | osx-x64 | win-x64 | win-arm64 | linux-x64 | linux-arm64
#   [channel]  passed to eng/compute-build-id.sh when the build id is not supplied (dev | pr-<n> | empty)
# The ONE build id stamps both halves identically (compute-version-once): CI supplies the `version` job's outputs as
# DENICE_DAY, DENICE_TIME, DENICE_COMBINED, DENICE_HASH, DENICE_CHANNEL, DENICE_TAG, DENICE_VERSION; when they are not
# all set, this script runs eng/compute-build-id.sh once itself. .NET gets them as the -p:Denice* globals, Rust as
# DENICE_VERSION.
# Environment: DOTNET (default: dotnet), CARGO (default: cargo), DENICE_DOTNET_ARGS (extra args for dotnet publish,
#   e.g. CI's signing switches).
set -euo pipefail

if [ $# -lt 2 ] || [ $# -gt 3 ]; then
  echo "usage: $0 <rid> <out-dir> [channel]" >&2
  exit 2
fi
RID="$1"
OUT="$2"
CHANNEL_ARG="${3:-}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
CARGO="${CARGO:-cargo}"

if [ -z "${DENICE_DAY:-}" ] || [ -z "${DENICE_TIME:-}" ] || [ -z "${DENICE_COMBINED:-}" ] || [ -z "${DENICE_HASH:-}" ] \
   || [ -z "${DENICE_TAG:-}" ] || [ -z "${DENICE_VERSION:-}" ]; then
  while IFS='=' read -r key value; do
    case "$key" in
      day) DENICE_DAY="$value" ;; time) DENICE_TIME="$value" ;; combined) DENICE_COMBINED="$value" ;;
      hash) DENICE_HASH="$value" ;; channel) DENICE_CHANNEL="$value" ;; tag) DENICE_TAG="$value" ;;
      version) DENICE_VERSION="$value" ;;
    esac
  done < <(GITHUB_OUTPUT='' "$REPO_ROOT/eng/compute-build-id.sh" "$CHANNEL_ARG")
fi
VERSION="$DENICE_VERSION"
VERSION_ARGS="-p:DeniceDay=$DENICE_DAY -p:DeniceTime=$DENICE_TIME -p:DeniceCombined=$DENICE_COMBINED -p:DeniceHash=$DENICE_HASH -p:DeniceChannel=${DENICE_CHANNEL:-} -p:DeniceTag=$DENICE_TAG -p:DeniceVersion=$DENICE_VERSION"

case "$RID" in
  osx-arm64)   TARGET=aarch64-apple-darwin;      EXE="" ;;
  osx-x64)     TARGET=x86_64-apple-darwin;       EXE="" ;;
  win-x64)     TARGET=x86_64-pc-windows-msvc;    EXE=".exe" ;;
  win-arm64)   TARGET=aarch64-pc-windows-msvc;   EXE=".exe" ;;
  linux-x64)   TARGET=x86_64-unknown-linux-gnu;  EXE="" ;;
  linux-arm64) TARGET=aarch64-unknown-linux-gnu; EXE="" ;;
  *) echo "build-install-layout: unknown rid '$RID'" >&2; exit 2 ;;
esac

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"   # absolute: the Rust step runs in rust/ and rustc writes beside the output path
VERSION_DIR="$OUT/versions/$VERSION"
ENTRY="$VERSION_DIR/denice-v$VERSION-$RID$EXE"
mkdir -p "$VERSION_DIR"

# 1. Denice Runtime: self-contained .NET, no apphost (Denice.Runtime.csproj removes it and fails if it remains).
#    -m:1: a self-contained RID publish builds some projects twice into the same output, and parallel nodes race on
#    deps.json ("GenerateDepsFile ... used by another process"); the same workaround as eng/check-single-platform-lib.sh
#    (agent-guard issue #56).
# shellcheck disable=SC2086
"$DOTNET" publish "$REPO_ROOT/src/Denice.Runtime/Denice.Runtime.csproj" -c Release -r "$RID" --self-contained -m:1 \
  -o "$VERSION_DIR" $VERSION_ARGS ${DENICE_DOTNET_ARGS:-}

# 2. The versioned entry: the build itself writes the versioned file name (tested 15 November 2026; one program per
#    invocation, version passed in before the build).
( cd "$REPO_ROOT/rust" && DENICE_VERSION="$VERSION" "$CARGO" rustc --release --locked --target "$TARGET" \
    -p denice-entry --bin denice-entry -- --emit=link="$ENTRY" )

# 3. The loader, built once, placed at the root and carried inside the version folder. On macOS it is the app
#    Denice.app holding the loader program (pkg-697310396f74 macos-bundle).
( cd "$REPO_ROOT/rust" && DENICE_VERSION="$VERSION" "$CARGO" build --release --locked --target "$TARGET" -p denice-loader --bin denice )
LOADER_BIN="$REPO_ROOT/rust/target/$TARGET/release/denice$EXE"
place_loader() { # place_loader <folder>
  case "$RID" in
    osx-*)
      local app="$1/Denice.app"
      rm -rf "$app"; mkdir -p "$app/Contents/MacOS"
      cp "$LOADER_BIN" "$app/Contents/MacOS/denice"
      sed -e "s/@VERSION@/$VERSION/" -e "s/@SHORT_VERSION@/${VERSION%%[-+]*}/" \
        "$REPO_ROOT/eng/macos/Info.plist.in" > "$app/Contents/Info.plist"
      ;;
    *) cp "$LOADER_BIN" "$1/denice$EXE" ;;
  esac
}
place_loader "$OUT"
place_loader "$VERSION_DIR"

# 4. Nothing else ships: no .NET executable, and the entry carries exactly the requested name.
if [ -e "$VERSION_DIR/Denice.Runtime$EXE" ]; then
  echo "build-install-layout: a .NET executable is in the version folder; Denice Runtime must ship only as a DLL" >&2
  exit 1
fi
[ -f "$ENTRY" ] || { echo "build-install-layout: the entry was not written as $ENTRY" >&2; exit 1; }
echo "build-install-layout: $OUT ($RID, $VERSION)"
echo "version=$VERSION"
