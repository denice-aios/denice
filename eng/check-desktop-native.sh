#!/usr/bin/env bash
# Validate the published payload, independently of NuGet package names or the build host.
set -euo pipefail
if [ "$#" -ne 2 ]; then
  echo "usage: $0 <published-version-directory> <rid>" >&2
  exit 2
fi
ROOT="$1"
RID="$2"
case "$RID" in
  osx-x64) ARCH='x86_64'; KIND='Mach-O'; CEF='CEF/Chromium Embedded Framework.framework/Chromium Embedded Framework'; SKIA='libSkiaSharp.dylib'; RESOURCE='CEF/Chromium Embedded Framework.framework/Resources' ;;
  osx-arm64) ARCH='arm64'; KIND='Mach-O'; CEF='CEF/Chromium Embedded Framework.framework/Chromium Embedded Framework'; SKIA='libSkiaSharp.dylib'; RESOURCE='CEF/Chromium Embedded Framework.framework/Resources' ;;
  win-x64) ARCH='x86-64'; KIND='PE32+'; CEF='CEF/libcef.dll'; SKIA='libSkiaSharp.dll'; RESOURCE='CEF' ;;
  win-arm64) ARCH='Aarch64'; KIND='PE32+'; CEF='CEF/libcef.dll'; SKIA='libSkiaSharp.dll'; RESOURCE='CEF' ;;
  linux-x64) ARCH='x86-64'; KIND='ELF 64-bit'; CEF='CEF/libcef.so'; SKIA='libSkiaSharp.so'; RESOURCE='CEF' ;;
  linux-arm64) ARCH='aarch64'; KIND='ELF 64-bit'; CEF='CEF/libcef.so'; SKIA='libSkiaSharp.so'; RESOURCE='CEF' ;;
  *) echo "desktop-native: unsupported RID $RID" >&2; exit 2 ;;
esac
require_file() {
  [ -s "$ROOT/$1" ] || { echo "desktop-native: missing or empty $1 for $RID" >&2; exit 1; }
}
check_binary() {
  require_file "$1"
  local description
  description="$(file -b "$ROOT/$1")"
  if [[ "$description" != *"$KIND"* || ( "$description" != *"$ARCH"* && ! ( "$RID" == win-arm64 && "$description" == *ARM64* ) ) ]]; then
    echo "desktop-native: wrong native architecture for $RID: $1: $description" >&2
    exit 1
  fi
  echo "desktop-native: $RID $1: $description"
}
check_binary "$CEF"
check_binary "$SKIA"
while IFS= read -r -d '' binary; do
  [ "$binary" = "$ROOT/$CEF" ] || check_binary "${binary#"$ROOT/"}"
done < <(find "$ROOT/CEF" -type f \( -name '*.dll' -o -name '*.so' -o -name '*.so.*' -o -name '*.dylib' \) -print0)
require_file "$RESOURCE/icudtl.dat"
require_file "$RESOURCE/resources.pak"
case "$RID" in
  osx-*) require_file "$RESOURCE/en.lproj/locale.pak" ;;
  *) require_file "$RESOURCE/locales/en-US.pak" ;;
esac
require_file 'licenses/CEF-LICENSE.txt'
require_file 'licenses/SkiaSharp-LICENSE.txt'
require_file 'licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt'
case "$RID" in
  win-*) check_binary 'av_libglesv2.dll'; require_file 'licenses/ANGLE-LICENSE.txt' ;;
  linux-*)
    bash "$(dirname "$0")/build-linux-media.sh" --rid "$RID" --output "$ROOT/media" --verify-output
    while IFS= read -r -d '' binary; do
      check_binary "${binary#"$ROOT/"}"
    done < <(find "$ROOT/media/lib" -type f \( -name '*.so' -o -name '*.so.*' \) -print0)
    ;;
esac
echo "desktop-native: $RID payload architecture and resources passed; desktop execution remains a separate check"
