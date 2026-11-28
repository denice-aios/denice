#!/usr/bin/env bash
# rust/REQUIREMENTS.md F12: the Linux Rust programs (loader + versioned entry) must run on every Linux that Denice
# Runtime's .NET runs on. .NET's own native libraries in the same layout (libcoreclr.so, libSystem.Native.so, ...) state
# the oldest glibc .NET supports; the Rust programs may not require a newer glibc than the newest version those
# libraries require. Reads the GLIBC_x.y symbol versions each ELF file needs (objdump -T).
# Usage: eng/check-glibc.sh <layout-dir> <rid>
set -euo pipefail

LAYOUT="$1"; RID="$2"
VERSION_DIR="$(ls -d "$LAYOUT"/versions/*/ | head -1)"
OBJDUMP="${OBJDUMP:-objdump}"

max_glibc() { # max_glibc <elf files...> -> the highest GLIBC_x.y any of them needs
  for f in "$@"; do "$OBJDUMP" -T "$f" 2>/dev/null; done \
    | grep -oE 'GLIBC_[0-9]+\.[0-9]+(\.[0-9]+)?' | sed 's/GLIBC_//' | sort -t. -k1,1n -k2,2n -k3,3n | tail -1
}

# Derive the .NET baseline from its runtime-pack manifest. A wildcard would include
# newly bundled CEF/media libraries and could silently raise the baseline itself.
dotnet_files=()
while IFS= read -r native; do
  dotnet_files+=("$VERSION_DIR/$native")
done < <(jq -r '.targets[] | to_entries[] | select(.key | startswith("runtimepack.Microsoft.NETCore.App.Runtime.")) | (.value.native // {}) | keys[]' "$VERSION_DIR/Denice.Runtime.deps.json" | sort -u)
[ "${#dotnet_files[@]}" -gt 0 ] || { echo "check-glibc: .NET runtime-pack native manifest is missing" >&2; exit 1; }
DOTNET_MAX=$(max_glibc "${dotnet_files[@]}")
RUST_MAX=$(max_glibc "$LAYOUT/denice" "$VERSION_DIR"/denice-v*-"$RID")
echo "check-glibc: $RID: .NET's native libraries need up to glibc $DOTNET_MAX; the Rust programs need up to glibc $RUST_MAX"
[ -n "$DOTNET_MAX" ] && [ -n "$RUST_MAX" ] || { echo "check-glibc: could not read the glibc requirements" >&2; exit 1; }
if [ "$(printf '%s\n%s\n' "$RUST_MAX" "$DOTNET_MAX" | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)" != "$DOTNET_MAX" ]; then
  echo "check-glibc: FAIL — the Rust programs need a newer glibc ($RUST_MAX) than Denice Runtime's .NET ($DOTNET_MAX)" >&2
  exit 1
fi

# Inspect nested browser/media plugins too. These dependencies must not quietly
# increase the established native baseline when a new release is assembled.
native_files=()
while IFS= read -r -d '' native; do
  if "$OBJDUMP" -f "$native" 2>/dev/null | grep -q 'file format elf'; then
    native_files+=("$native")
  fi
done < <(find "$VERSION_DIR" -type f -print0)
PAYLOAD_MAX=$(max_glibc "${native_files[@]}")
if [ "$(printf '%s\n%s\n' "$PAYLOAD_MAX" "$DOTNET_MAX" | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)" != "$DOTNET_MAX" ]; then
  echo "check-glibc: FAIL — bundled desktop libraries need glibc $PAYLOAD_MAX, newer than the .NET baseline $DOTNET_MAX" >&2
  exit 1
fi
echo "check-glibc: complete ELF payload needs up to glibc $PAYLOAD_MAX"
echo "check-glibc: PASS"
