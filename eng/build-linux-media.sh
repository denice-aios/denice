#!/usr/bin/env bash
# Build stock Linux media dependencies on matching native Linux hardware.
# No host packages, user configuration or environment files are modified.
set -euo pipefail
usage() { echo 'Usage: build-linux-media.sh --rid linux-x64|linux-arm64 --output DIR [--work-dir DIR] [--toolchain-notices DIR] [--verify-output]'; }
die() { echo "$*" >&2; exit 1; }
rid= output= work= notices= verify_only=false
while (($#)); do
  case "$1" in
    --rid|--output|--work-dir|--toolchain-notices)
      (($# >= 2)) || { usage >&2; exit 2; }
      case "$1" in --rid) rid=$2;; --output) output=$2;; --work-dir) work=$2;; --toolchain-notices) notices=$2;; esac
      shift 2;;
    --verify-output) verify_only=true; shift;;
    --help) usage; exit 0;; *) usage >&2; exit 2;;
  esac
done
[[ -n "$output" ]] || { usage >&2; exit 2; }
case "$rid" in
  linux-x64) machine=x86_64; triplet=x86_64-linux-gnu; elf_machine='Advanced Micro Devices X86-64';;
  linux-arm64) machine=aarch64; triplet=aarch64-linux-gnu; elf_machine=AArch64;;
  *) usage >&2; exit 2;;
esac
# Share the required roots between staging and verification.
plugin_names=(coreelements app opengl videoconvertscale audioconvert audioresample volume typefindfunctions audioparsers isomp4 videoparsersbad codectimestamper nvcodec va libav)
api_names=(gstreamer gstapp gstvideo gstgl gstcuda)
command -v sha256sum >/dev/null || die 'Missing prerequisite: sha256sum'
script_sha256=$(sha256sum "$0" | cut -d' ' -f1)
verify_output() {
  local output=${1:-$output}
  command -v jq >/dev/null || die 'Missing verification prerequisite: jq'
  [[ -d "$output" && ! -L "$output" ]] || die 'Media output must be a real directory.'
  [[ -z $(find "$output" ! -type d ! -type f -print -quit) ]] || die 'Media output contains a symlink or special file.'
  for name in build-manifest.json SHA256SUMS source-lock.tsv elf-dependencies.tsv host-libraries.txt; do
    [[ -s "$output/$name" ]] || die "Required media inventory missing: $name"
  done
  jq -e --arg rid "$rid" --arg script "$script_sha256" \
    '.rid == $rid and .build_script_sha256 == $script and (.build_key | type == "string" and length > 0)' \
    "$output/build-manifest.json" >/dev/null || die 'Media manifest RID or build-script SHA256 does not match.'
  [[ -d "$output/licenses" && -n $(find "$output/licenses" -type f -print -quit) ]] || die 'Media license notices are missing.'
  for name in "${plugin_names[@]}"; do
    [[ -s "$output/lib/gstreamer-1.0/libgst$name.so" ]] || die "Required media plugin missing: $name"
  done
  for name in "${api_names[@]}"; do
    [[ -s "$output/lib/lib$name-1.0.so.0" ]] || die "Required media API library missing: $name"
  done
  # Check paths before passing the manifest to sha256sum, which otherwise accepts
  # paths outside the output. Staging uses normal relative, unescaped filenames.
  while IFS= read -r record || [[ -n "$record" ]]; do
    [[ "$record" =~ ^[[:xdigit:]]{64}[[:space:]][[:space:]]\./.+$ ]] || die 'Malformed media checksum record.'
    name=${record:66}
    case "$name" in */../*|*/./*|*//*|*/..|*/.|./SHA256SUMS) die "Unsafe media checksum path: $name";; esac
  done < "$output/SHA256SUMS"
  # Equality also rejects extra files, omitted files and duplicate records.
  diff -u <(cd "$output" && find . -type f ! -path ./SHA256SUMS -print | LC_ALL=C sort) \
    <(cut -c67- "$output/SHA256SUMS" | LC_ALL=C sort) || die 'Media checksum inventory does not cover exactly the output files.'
  (cd "$output" && sha256sum --check --quiet SHA256SUMS) || die 'Media output failed its integrity check.'
}
if [[ "$verify_only" == true || -e "$output" ]]; then
  verify_output
  printf '%s\n' "$output"
  exit 0
fi
[[ $(uname -s) == Linux && $(uname -m) == "$machine" ]] || die "Build $rid on matching native Linux hardware."
libc=$(getconf GNU_LIBC_VERSION) || die 'A glibc Linux build host is required.'
if [[ $(printf '%s\n' 2.27 "${libc#glibc }" | sort -V | tail -1) != 2.27 ]]; then
  command -v docker >/dev/null || die 'Docker is required to build the media ABI on this newer Linux host.'
  root=$(cd "$(dirname "$0")/.." && pwd)
  work=${work:-"$root/artifacts/linux-media-build"}
  mkdir -p "$work" "$(dirname "$output")"
  work=$(realpath "$work"); output=$(realpath -m "$output")
  case "$rid" in
    linux-x64) image=quay.io/pypa/manylinux2014_x86_64@sha256:8a79ff36ce155d8f242dc8013d73c5e8bbe937defb15de7d7afb342c1ab537f4;;
    linux-arm64) image=quay.io/pypa/manylinux2014_aarch64@sha256:382236bfd11d86894d9792a6fa5286190b8dd3d075a7032284e123c288711e4f;;
  esac
  mounts=(--mount "type=bind,src=$root,dst=/repo,readonly" --mount "type=bind,src=$work,dst=/build-cache" --mount "type=bind,src=$(dirname "$output"),dst=/media-output")
  container_args=(--rid "$rid" --work-dir /build-cache --output "/media-output/${output##*/}")
  if [[ -n "$notices" ]]; then
    mounts+=(--mount "type=bind,src=$(realpath "$notices"),dst=/toolchain-notices,readonly")
    container_args+=(--toolchain-notices /toolchain-notices)
  fi
  # Install build-only prerequisites inside the disposable, digest-pinned image.
  # Run the source build as the caller's uid so mounted artifacts retain ownership.
  docker run --rm -i "${mounts[@]}" "$image" /bin/bash -s -- "$(id -u)" "$(id -g)" "${container_args[@]}" <<'CONTAINER'
set -euo pipefail
build_uid=$1; build_gid=$2; shift 2
export PATH="/opt/python/cp311-cp311/bin:$PATH"
# The pinned images provide this upstream CentOS-7/altarch vault repair.
# Restrict installs to CentOS repositories: ARM has no EPEL, and an expired
# unrelated EPEL mirror must not block installation of graphics headers.
fixup-mirrors
yum --disablerepo='*' --enablerepo=base,updates install -y \
  mesa-libEGL-devel mesa-libGLES-devel libX11-devel libXext-devel libXfixes-devel \
  libxcb-devel binutils flex bison gettext-devel
# jq is not in the CentOS-7 base repository. Upstream publishes statically linked
# binaries for both native architectures; checksums are pinned release metadata.
case "$(uname -m)" in
  x86_64) jq_asset=jq-linux-amd64; jq_sha=020468de7539ce70ef1bceaf7cde2e8c4f2ca6c3afb84642aabc5c97d9fc2a0d;;
  aarch64) jq_asset=jq-linux-arm64; jq_sha=6bc62f25981328edd3cfcfe6fe51b073f2d7e7710d7ef7fcdac28d4e384fc3d4;;
  *) echo 'Unsupported native container architecture.' >&2; exit 1;;
esac
curl --fail --location --retry 3 "https://github.com/jqlang/jq/releases/download/jq-1.8.1/$jq_asset" -o /tmp/denice-jq
printf '%s  %s\n' "$jq_sha" /tmp/denice-jq | sha256sum --check
install -m 0755 /tmp/denice-jq /usr/local/bin/jq
python3 -m pip install --no-cache-dir --require-hashes -r /dev/stdin <<'REQUIREMENTS'
distro==1.9.0 --hash=sha256:7bffd925d65168f85027d8da9af6bddab658135b840670a223589bc0c8ef02b2
REQUIREMENTS
if [[ "$build_uid" == 0 ]]; then
  exec bash /repo/eng/build-linux-media.sh "$@"
fi
if ! getent group "$build_gid" >/dev/null; then groupadd -g "$build_gid" denice-build; fi
if ! getent passwd "$build_uid" >/dev/null; then useradd -m -d /tmp/denice-build-home -u "$build_uid" -g "$build_gid" denice-build; fi
build_user=$(getent passwd "$build_uid" | cut -d: -f1)
exec runuser -u "$build_user" -- env PATH="$PATH" bash /repo/eng/build-linux-media.sh "$@"
CONTAINER
  exit $?
fi
for tool in python3 curl tar gcc g++ pkg-config patchelf readelf make jq sha256sum realpath ldd flex bison flock; do
  command -v "$tool" >/dev/null || die "Missing build prerequisite: $tool"
done
[[ ! -f "$HOME/.cerbero/cerbero.cbc" ]] || die 'Use a clean build account without a personal Cerbero configuration.'
pkg-config --exists egl glesv2 x11 xext xfixes x11-xcb xcb xcb-dri3 || die 'Install EGL/GLES/X11/XCB development prerequisites in the build image.'
root=$(cd "$(dirname "$0")/.." && pwd)
work=${work:-"$root/artifacts/linux-media-build"}
mkdir -p "$work" "$(dirname "$output")"
work=$(realpath "$work"); output=$(realpath -m "$output")
# Serialize a shared cache, including source downloads and extraction.
exec 9>"$work/.linux-media-build.lock"
flock 9
if [[ -e "$output" ]]; then
  verify_output
  printf '%s\n' "$output"
  exit 0
fi
# .cbc uses stock Python assignment syntax. Restrict build paths so shell-written
# quoted assignments remain literal, rather than generating executable code.
[[ "$work" != *"'"* && "$work" != *'\'* && "$work" != *$'\n'* ]] || die 'Build path cannot contain quotes, backslashes or newlines.'
key=$( { cat "$0"; gcc -v 2>&1; g++ -v 2>&1; sha256sum "$(command -v gcc)" "$(command -v g++)"; pkg-config --modversion egl glesv2 x11 xext xfixes x11-xcb xcb xcb-dri3; if command -v rpm >/dev/null; then rpm -qa | sort; fi; } | sha256sum | cut -d' ' -f1)
build="$work/$rid-$key"; prefix="$build/prefix"
mkdir -p "$build" "$work/downloads"
if [[ -n "$notices" ]]; then
  notices=$(realpath "$notices")
else
  notices="$build/toolchain-notices"; mkdir -p "$notices"
  while IFS= read -r -d '' notice; do
    cp "$notice" "$notices/$(printf '%s' "$notice" | tr '/' '_')"
  done < <(find /usr/share/doc /usr/share/licenses /opt/rh /opt/_internal -type f \( -name COPYING.RUNTIME -o -name COPYING3 -o -name COPYING3.LIB -o -path '*/libstdc++*/copyright' -o -path '*/libgcc*/copyright' -o -path '*/gcc-*/copyright' \) -print0 2>/dev/null || true)
fi
[[ -d "$notices" && -n $(find "$notices" -type f -print -quit) ]] || die 'Compiler runtime notices are missing; supply --toolchain-notices DIR.'
fetch() {
  local name=$1 url=$2 checksum=$3 archive="$work/downloads/$1"
  if [[ ! -f "$archive" ]]; then
    curl --fail --location --retry 3 "$url" -o "$archive.tmp"
    mv "$archive.tmp" "$archive"
  fi
  echo "$checksum  $archive" | sha256sum --check >&2
  if [[ ! -d "$build/src/$name" ]]; then
    mkdir -p "$build/src"
    local extracted
    extracted=$(mktemp -d "$build/src/$name.extract.XXXXXX")
    tar xf "$archive" -C "$extracted" --strip-components=1 || { rm -rf "$extracted"; return 1; }
    mv "$extracted" "$build/src/$name"
  fi
  printf '%s\t%s\t%s\n' "$name" "$checksum" "$url" >> "$build/source-lock.tsv"
}
: > "$build/source-lock.tsv"
fetch cerbero https://codeload.github.com/GStreamer/cerbero/tar.gz/refs/tags/1.28.9 9828ad201be3a6a81ba8478e98c3262df656ea1b4e4fdb66fc25fd474408359b
cat > "$build/media.cbc" <<CBC
home_dir = '$build/home'
prefix = '$prefix'
sources = '$build/cerbero-sources'
local_sources = '$build/cerbero-downloads'
# Cerbero injects its mutable Variants object; assigning a new list is ignored.
variants.override(['nopython', 'nogi', 'norust', 'noqt5', 'noqt6', 'nounwind', 'notestspackage'])
use_ccache = False
CBC
(
  cd "$build/src/cerbero"
  ./cerbero-uninstalled -c "$build/media.cbc" bootstrap --system=no --toolchains=no
  ./cerbero-uninstalled -c "$build/media.cbc" build glib orc zlib bzip2
  # Extract the unchanged pinned FFmpeg recipe and its upstream Meson port patch.
  ./cerbero-uninstalled -c "$build/media.cbc" buildone ffmpeg --steps fetch extract
)
# Process-local build search paths. No application/user environment is changed.
export PATH="$build/home/build-tools/bin:$PATH"
export CC="$(command -v gcc)" CXX="$(command -v g++)"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig:$prefix/lib64/pkgconfig:$prefix/lib/$triplet/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
export LD_LIBRARY_PATH="$prefix/lib:$prefix/lib64:$prefix/lib/$triplet${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
command -v meson >/dev/null || die 'Cerbero did not produce its Meson build tool.'
build_meson() {
  local name=$1 source=$2; shift 2
  if [[ -f "$build/meson-$name/meson-private/coredata.dat" ]]; then
    meson setup --reconfigure "$build/meson-$name" "$source" "$@"
  else
    meson setup "$build/meson-$name" "$source" --prefix="$prefix" --libdir=lib --buildtype=release --default-library=shared --wrap-mode=nofallback "$@"
  fi
  meson compile -C "$build/meson-$name"
  meson install -C "$build/meson-$name"
}
fetch libdrm https://dri.freedesktop.org/libdrm/libdrm-2.4.124.tar.xz ac36293f61ca4aafaf4b16a2a7afff312aa4f5c37c9fbd797de9e3c0863ca379
build_meson libdrm "$build/src/libdrm" -Dauto_features=disabled -Dtests=false
fetch libva https://codeload.github.com/intel/libva/tar.gz/refs/tags/2.22.0 467c418c2640a178c6baad5be2e00d569842123763b80507721ab87eb7af8735
build_meson libva "$build/src/libva" -Dwith_x11=yes -Dwith_glx=no -Dwith_wayland=no -Dwith_win32=no "-Ddriverdir=/usr/lib/$triplet/dri:/usr/lib64/dri:/usr/lib/dri"
ffmpeg_source="$build/cerbero-sources/ffmpeg-7.1"
[[ -d "$ffmpeg_source" ]] || die "Cerbero FFmpeg source was not extracted at $ffmpeg_source"
build_meson ffmpeg "$ffmpeg_source" -Dauto_features=disabled -Dgpl=disabled -Dnonfree=disabled -Dversion3=disabled -Dprograms=disabled -Dtests=disabled -Dnetwork=disabled -Davdevice=disabled -Dencoders=disabled -Ddecoders=disabled -Dmuxers=disabled -Ddemuxers=disabled -Dparsers=disabled -Dfilters=disabled -Daac_encoder=enabled
common=(-Dauto_features=disabled -Dtests=disabled -Dexamples=disabled)
fetch gstreamer https://gstreamer.freedesktop.org/src/gstreamer/gstreamer-1.28.9.tar.xz 6dbf744f583131a34845cc641e803843766a9ba80ce00378a9043fd6d8d5fb38
build_meson gstreamer "$build/src/gstreamer" "${common[@]}" -Dintrospection=disabled -Dtools=enabled
fetch gst-plugins-base https://gstreamer.freedesktop.org/src/gst-plugins-base/gst-plugins-base-1.28.9.tar.xz c1fda668b520e6aef763c0eb962c032c9fe52bce322e8b00c69b4e9e847bddc1
build_meson base "$build/src/gst-plugins-base" "${common[@]}" -Dintrospection=disabled -Dapp=enabled -Dgl=enabled -Dgl_api=gles2 -Dgl_platform=egl -Dgl_winsys=x11,surfaceless -Dx11=enabled -Ddrm=enabled -Dorc=enabled -Daudioconvert=enabled -Daudioresample=enabled -Dvideoconvertscale=enabled -Dtypefind=enabled -Dvolume=enabled
fetch gst-plugins-good https://gstreamer.freedesktop.org/src/gst-plugins-good/gst-plugins-good-1.28.9.tar.xz e518cd97483706a252e95e60312a22d23dec6f5e7673af6f5bde719c3a9ca7f4
build_meson good "$build/src/gst-plugins-good" "${common[@]}" -Daudioparsers=enabled -Disomp4=enabled
fetch gst-plugins-bad https://gstreamer.freedesktop.org/src/gst-plugins-bad/gst-plugins-bad-1.28.9.tar.xz 4a4e0b7bf84f868378c48f143a7ac3a98923b488a5b74f1a35ba46bc3c43f6b1
build_meson bad "$build/src/gst-plugins-bad" "${common[@]}" -Dintrospection=disabled -Dgpl=disabled -Dvideoparsers=enabled -Dcodectimestamper=enabled -Dnvcodec=enabled -Dgl=enabled -Dva=enabled -Ddrm=enabled -Dx11=enabled
fetch gst-libav https://gstreamer.freedesktop.org/src/gst-libav/gst-libav-1.28.9.tar.xz 4b3df21fa70972a80891d2298cf32c14a62c3db4483d545c7ac018230399186b
build_meson libav "$build/src/gst-libav" -Dauto_features=disabled -Dtests=disabled
stage=$(mktemp -d "${output}.staging.XXXXXX")
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/lib/gstreamer-1.0" "$stage/licenses"
declare -A libraries host
while IFS= read -r -d '' path; do libraries[${path##*/}]=$path; done < <(find "$prefix" \( -type f -o -type l \) -name '*.so*' -print0)
for name in libstdc++.so.6 libgcc_s.so.1 libatomic.so.1; do
  path=$(gcc -print-file-name="$name")
  [[ ! -f "$path" ]] || libraries[$name]=$path
done
# Only libc and display/driver ABI boundaries remain host dependencies.
for name in libc.so.6 libm.so.6 libdl.so.2 librt.so.1 libpthread.so.0 libresolv.so.2 libutil.so.1 ld-linux-x86-64.so.2 ld-linux-aarch64.so.1 libEGL.so.1 libGLESv2.so.2 libGL.so.1 libGLX.so.0 libGLdispatch.so.0 libgbm.so.1 libX11.so.6 libX11-xcb.so.1 libXext.so.6 libXfixes.so.3 libXdamage.so.1 libXau.so.6 libXdmcp.so.6 libxcb.so.1 libxcb-dri3.so.0; do host[$name]=1; done
queue=()
stage_library() {
  local name=$1 dest=$2
  [[ ! -e "$dest" ]] || return 0
  [[ -n ${libraries[$name]:-} ]] || die "Missing non-driver dependency: $name"
  cp -L "${libraries[$name]}" "$dest"
  queue+=("$dest")
}
for name in "${plugin_names[@]}"; do
  stage_library "libgst$name.so" "$stage/lib/gstreamer-1.0/libgst$name.so"
done
for name in "${api_names[@]}"; do
  stage_library "lib$name-1.0.so.0" "$stage/lib/lib$name-1.0.so.0"
done
: > "$stage/elf-dependencies.tsv"; : > "$stage/host-libraries.txt"
for ((i=0; i<${#queue[@]}; i++)); do
  path=${queue[$i]}
  readelf -h "$path" | grep -F "$elf_machine" >/dev/null || die "Wrong ELF architecture: $path"
  while IFS= read -r name; do
    [[ "$name" != */* ]] || die "Non-relocatable DT_NEEDED: $name"
    printf '%s\t%s\n' "${path#"$stage/"}" "$name" >> "$stage/elf-dependencies.tsv"
    if [[ -n ${host[$name]:-} ]]; then printf '%s\n' "$name" >> "$stage/host-libraries.txt"
    else stage_library "$name" "$stage/lib/$name"; fi
  done < <(readelf -d "$path" | sed -n 's/.*(NEEDED).*\[\(.*\)\].*/\1/p')
  maximum=$(readelf --version-info "$path" | sed -n '/Version needs section/,$p' | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | cut -d_ -f2 | sort -V | tail -1 || true)
  [[ -z "$maximum" || $(printf '%s\n' 2.27 "$maximum" | sort -V | tail -1) == 2.27 ]] || die "GLIBC_2.27 floor exceeded: $path requires $maximum"
  if [[ "$path" == "$stage/lib/gstreamer-1.0/"* ]]; then patchelf --set-rpath '$ORIGIN:$ORIGIN/..' "$path"
  else patchelf --set-rpath '$ORIGIN' "$path"; fi
done
# Check relocation without the build prefix on the loader search path.
for path in "${queue[@]}"; do
  resolved=$(env -u LD_LIBRARY_PATH ldd "$path") || die "Loader inspection failed: $path"
  [[ "$resolved" != *'not found'* ]] || die "Unresolved staged ELF dependency: $path: $resolved"
done
# Build-only smoke checks use process-local settings, never user environment files.
# Hardware elements are probed later on the actual GPU; plugin presence is insufficient.
for element in appsrc appsink glupload glcolorconvert gldownload h264parse aacparse avenc_aac mp4mux; do
  env LD_LIBRARY_PATH="$stage/lib" GST_PLUGIN_SYSTEM_PATH_1_0= \
    GST_PLUGIN_PATH_1_0="$stage/lib/gstreamer-1.0" GST_REGISTRY="$build/smoke-registry.bin" \
    GST_REGISTRY_FORK=no "$prefix/bin/gst-inspect-1.0" "$element" > "$build/smoke-$element.log"
done
sort -u "$stage/host-libraries.txt" -o "$stage/host-libraries.txt"
cp -R "$prefix/share/licenses/." "$stage/licenses/"
cp -R "$notices" "$stage/licenses/gcc-runtime"
# Sources retain their original per-file copyright notices. Retain the selected
# upstream source archives alongside license texts as corresponding source.
mkdir "$stage/licenses/source"
for name in libdrm libva gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-libav; do
  case "$name" in libva) suffix=tar.gz;; *) suffix=tar.xz;; esac
  cp "$work/downloads/$name" "$stage/licenses/source/$name.$suffix"
  while IFS= read -r -d '' notice; do
    cp "$notice" "$stage/licenses/$name-${notice##*/}"
  done < <(find "$build/src/$name" -maxdepth 1 -type f \( -iname 'copying*' -o -iname 'license*' \) -print0)
done
# FFmpeg includes Cerbero's stock Meson patch; archive exactly the patched source.
tar cJf "$stage/licenses/source/ffmpeg-7.1.tar.xz" --exclude=ffmpeg-7.1/.git -C "$build/cerbero-sources" ffmpeg-7.1
# Retain the unchanged Cerbero runtime recipes, patches and checked downloads.
mkdir -p "$stage/licenses/cerbero-recipes"
for name in glib orc zlib bzip2 libffi pcre2; do
  cp "$build/src/cerbero/recipes/$name.recipe" "$stage/licenses/cerbero-recipes/"
  if [[ -d "$build/src/cerbero/recipes/$name" ]]; then
    cp -R "$build/src/cerbero/recipes/$name" "$stage/licenses/cerbero-recipes/"
  fi
  while IFS= read -r -d '' archive; do
    cp "$archive" "$stage/licenses/source/$name-${archive##*/}"
  done < <(find "$build/cerbero-downloads" -type f -path "*/$name-*/*" \( -name '*.tar.gz' -o -name '*.tar.xz' -o -name '*.tar.bz2' -o -name '*.zip' \) -print0)
done
printf '%s\t%s\t%s\n' ffmpeg 40973d44970dbc83ef302b0609f2e74982be2d85916dd2ee7472d30678a7abe6 https://ffmpeg.org/releases/ffmpeg-7.1.tar.xz >> "$build/source-lock.tsv"
cp "$build/source-lock.tsv" "$stage/source-lock.tsv"
if command -v rpm >/dev/null; then rpm -qa | sort > "$stage/build-packages.txt"; fi
cp "$build/src/cerbero/recipes/ffmpeg.recipe" "$stage/licenses/ffmpeg.recipe"
cp -R "$build/src/cerbero/recipes/ffmpeg" "$stage/licenses/ffmpeg-patches"
jq -n --arg rid "$rid" --arg script "$script_sha256" --arg key "$key" --arg compiler "$(gcc --version)" '{rid:$rid,build_script_sha256:$script,build_key:$key,compiler:$compiler,gstreamer:"1.28.9",libva:"2.22.0",libdrm:"2.4.124",glibc_floor:"2.27",runtime_hardware_test:"not performed by this build",plugin_discovery:"gst_registry_fork_set_enabled(FALSE), then gst_registry_scan_path(<prefix>/lib/gstreamer-1.0)",dynamically_loaded_host_drivers:["EGL vendor driver","VA DRI driver","libcuda.so.1","libnvidia-encode.so.1","CUDA NVRTC when required by selected CUDA conversion elements"]}' > "$stage/build-manifest.json"
(cd "$stage" && find . -type f ! -path ./SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
verify_output "$stage"
mv "$stage" "$output"
trap - EXIT
printf '%s\n' "$output"
