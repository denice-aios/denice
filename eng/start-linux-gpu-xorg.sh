#!/usr/bin/env bash
# Copyright (c) Denice.ai. All rights reserved.
#
# Ephemeral NVIDIA-backed Xorg for the hosted Linux GPU fixture job. Never Xvfb or a software renderer.
#
#   probe <evidence-dir>            record driver source and X/EGL components; change nothing; exit 3 when any is missing
#   config <busid>                  print the headless xorg.conf for an X BusID such as PCI:0@1:0:0
#   start <evidence-dir> <display>  start Xorg on the NVIDIA GPU, wait for readiness, assert the NVIDIA X driver and GLX;
#                                   on any failure stop only the processes this invocation launched
#   stop <evidence-dir>             stop the Xorg recorded by start, from any later shell
#
# Sources: NVIDIA 590.48.01 README chapters 5 (installed components), 6 (X config), 7 (BusID format, log checks),
# 8 (headless operation, xdpyinfo GLX/NV-GLX), appendix B (UseDisplayDevice, AllowEmptyInitialConfiguration).
#
# Test seams (defaults are the real system): DENICE_XORG_MODULE_DIRS (colon-separated), DENICE_X_LOCK_DIR,
# DENICE_XORG_READY_SECONDS.
set -euo pipefail

IFS=':' read -r -a xorg_module_dirs <<<"${DENICE_XORG_MODULE_DIRS:-/usr/lib/xorg/modules:/usr/lib/x86_64-linux-gnu/xorg/modules}"
x_lock_dir=${DENICE_X_LOCK_DIR:-/tmp}
ready_seconds=${DENICE_XORG_READY_SECONDS:-30}

find_module() {
  local relative=$1 dir
  for dir in "${xorg_module_dirs[@]}"; do
    [[ -e "$dir/$relative" ]] && { printf '%s\n' "$dir/$relative"; return 0; }
  done
  return 1
}

# Captured once; later reads use here-strings, so no early-exiting reader can SIGPIPE a producer under pipefail.
shared_libraries() { ldconfig -p 2>/dev/null || true; }

library_path() {
  local name=$1 line
  while IFS= read -r line; do
    [[ "$line" == *"$name "* ]] && { printf '%s\n' "${line##* => }"; return 0; }
  done <<<"$libraries"
  return 1
}

nvidia_busid() {
  # lspci -D prints domain:bus:device.function in hex; Xorg wants PCI:bus@domain:device:function in decimal (README 7.2).
  local devices line address="" domain bus device function
  devices=$(lspci -D -d 10de: 2>/dev/null || true)
  while IFS= read -r line; do
    if [[ "$line" == *"3D controller"* || "$line" == *"VGA compatible controller"* ]]; then
      address=${line%% *}
      break
    fi
  done <<<"$devices"
  [[ -n "$address" ]] || return 1
  IFS=':.' read -r domain bus device function <<<"$address"
  printf 'PCI:%d@%d:%d:%d\n' "0x$bus" "0x$domain" "0x$device" "0x$function"
}

write_config() {
  cat <<EOF
Section "ServerLayout"
    Identifier "denice-gpu"
    Screen 0 "denice-screen"
EndSection

Section "Module"
    Load "glx"
EndSection

Section "Device"
    Identifier "denice-nvidia"
    Driver "nvidia"
    BusID "$1"
    Option "AllowEmptyInitialConfiguration" "true"
    Option "UseDisplayDevice" "none"
EndSection

Section "Screen"
    Identifier "denice-screen"
    Device "denice-nvidia"
    DefaultDepth 24
    SubSection "Display"
        Depth 24
        Virtual 1920 1080
    EndSubSection
EOF
  echo "EndSection"
}

device_nodes() {
  local node found=0
  shopt -s nullglob
  for node in /dev/nvidia* /dev/dri/*; do
    found=1
    printf '  %s %s\n' "$(stat -c '%A %U:%G' "$node" 2>/dev/null || stat -f '%Sp %Su:%Sg' "$node" 2>/dev/null || echo unreadable)" "$node"
  done
  shopt -u nullglob
  ((found)) || echo "  none"
}

probe() {
  local out=$1 report packages xorg_version busid
  local -a missing=()
  mkdir -p "$out"
  report="$out/xorg-prerequisites.txt"
  libraries=$(shared_libraries)
  packages=$(dpkg-query -W -f '${Package} ${Version} ${Status}\n' 'nvidia-*' 'libnvidia-*' 'xserver-xorg-video-nvidia*' 'xserver-xorg-core' 'x11-utils' 2>/dev/null || true)
  xorg_version=$( { Xorg -version 2>&1 || true; } )
  busid=$(nvidia_busid || echo unavailable)
  {
    echo "kernel_driver=$(head -n 1 /proc/driver/nvidia/version 2>/dev/null || echo unavailable)"
    echo "nvidia_smi_driver=$(nvidia-smi --query-gpu=driver_version --format=csv,noheader 2>/dev/null || echo unavailable)"
    echo "installed_packages:"
    grep ' installed$' <<<"$packages" | sed 's/^/  /' || echo "  none"
    echo "busid=$busid"
    echo "xorg_binary=$(command -v Xorg || echo missing)"
    echo "xorg_version=$(grep -m 1 'X.Org X Server' <<<"$xorg_version" || echo unavailable)"
    echo "nvidia_drv=$(find_module drivers/nvidia_drv.so || echo missing)"
    echo "libglxserver_nvidia=$(find_module extensions/libglxserver_nvidia.so || echo missing)"
    echo "libEGL_nvidia=$(library_path libEGL_nvidia.so.0 || echo missing)"
    echo "libGLX_nvidia=$(library_path libGLX_nvidia.so.0 || echo missing)"
    echo "xdpyinfo=$(command -v xdpyinfo || echo missing)"
    echo "sudo_noninteractive=$(sudo -n true 2>/dev/null && echo yes || echo no)"
    echo "device_nodes:"
    device_nodes
  } > "$report"
  command -v Xorg >/dev/null || missing+=("Xorg server (xserver-xorg-core)")
  find_module drivers/nvidia_drv.so >/dev/null || missing+=("NVIDIA X driver nvidia_drv.so (xserver-xorg-video-nvidia-<branch>)")
  find_module extensions/libglxserver_nvidia.so >/dev/null || missing+=("NVIDIA GLX server module libglxserver_nvidia.so")
  library_path libEGL_nvidia.so.0 >/dev/null || missing+=("NVIDIA EGL vendor library libEGL_nvidia.so.0 (libnvidia-gl-<branch>)")
  command -v xdpyinfo >/dev/null || missing+=("xdpyinfo (x11-utils)")
  sudo -n true 2>/dev/null || missing+=("non-interactive sudo to start Xorg")
  [[ "$busid" != unavailable ]] || missing+=("NVIDIA PCI device")
  if ((${#missing[@]})); then
    printf 'missing=%s\n' "${missing[@]}" >> "$report"
    printf 'Xorg prerequisites missing:\n' >&2
    printf '  %s\n' "${missing[@]}" >&2
    return 3
  fi
  echo "missing=none" >> "$report"
}

lock_file() { printf '%s/.X%s-lock\n' "$x_lock_dir" "${1#:}"; }

# The X server writes its own PID into the display lock file. Accept it only while that process is an Xorg started
# with this invocation's configuration, so a reused PID or another server is never signalled.
our_xorg_pid() {
  local out=$1 display=$2 pid args
  [[ -s "$(lock_file "$display")" ]] || return 1
  pid=$(tr -dc '0-9' < "$(lock_file "$display")")
  [[ -n "$pid" ]] || return 1
  args=$(ps -o args= -p "$pid" 2>/dev/null || true)
  [[ "$args" == *Xorg* && "$args" == *"-config $out/xorg-denice.conf"* ]] || return 1
  printf '%s\n' "$pid"
}

alive() { kill -0 "$1" 2>/dev/null || ps -p "$1" >/dev/null 2>&1; }

terminate() {
  local pid=$1 _
  alive "$pid" || return 0
  sudo -n kill -TERM "$pid" 2>/dev/null || true
  for _ in $(seq 1 10); do alive "$pid" || return 0; sleep 1; done
  sudo -n kill -KILL "$pid" 2>/dev/null || true
  for _ in $(seq 1 5); do alive "$pid" || return 0; sleep 1; done
  return 1
}

# Stops only what start recorded: the Xorg identified through its lock file, then the sudo wrapper launched here.
cleanup() {
  local out=$1 display pid status=0
  [[ -s "$out/xorg-display" ]] || return 0
  display=$(<"$out/xorg-display")
  if pid=$(our_xorg_pid "$out" "$display"); then
    terminate "$pid" || { echo "Xorg $pid did not stop." >&2; status=1; }
  fi
  if [[ -s "$out/xorg-launcher.pid" ]]; then
    pid=$(<"$out/xorg-launcher.pid")
    if [[ "$(ps -o args= -p "$pid" 2>/dev/null || true)" == *"-config $out/xorg-denice.conf"* ]]; then
      terminate "$pid" || { echo "Xorg launcher $pid did not stop." >&2; status=1; }
    fi
  fi
  sudo -n chmod a+r "$out/Xorg${display#:}.log" 2>/dev/null || true
  echo "xorg_stopped=$([[ $status -eq 0 ]] && echo yes || echo no)" > "$out/xorg-exit.txt"
  return "$status"
}

fail_start() {
  local out=$1 code=$2 message=$3
  echo "$message" >&2
  cleanup "$out" || true
  return "$code"
}

start() {
  local out=$1 display=$2 config log launcher deadline pid
  probe "$out"
  [[ ! -e "$(lock_file "$display")" ]] || { echo "Display $display is already locked by $(lock_file "$display"); refusing to start." >&2; return 6; }
  config="$out/xorg-denice.conf"
  log="$out/Xorg${display#:}.log"
  write_config "$(nvidia_busid)" > "$config"
  echo "$display" > "$out/xorg-display"
  sudo -n Xorg "$display" -config "$config" -noreset -nolisten tcp -logfile "$log" \
    > "$out/xorg.stdout.txt" 2> "$out/xorg.stderr.txt" &
  launcher=$!
  echo "$launcher" > "$out/xorg-launcher.pid"
  deadline=$((SECONDS + ready_seconds))
  until DISPLAY=$display xdpyinfo > "$out/xdpyinfo.txt" 2>&1; do
    alive "$launcher" || { fail_start "$out" 4 "Xorg exited before readiness; see $log"; return; }
    ((SECONDS < deadline)) || { fail_start "$out" 4 "Xorg not ready within $ready_seconds seconds; see $log"; return; }
    sleep 1
  done
  pid=$(our_xorg_pid "$out" "$display") || { fail_start "$out" 5 "Display $display answered, but its lock file does not name this invocation's Xorg."; return; }
  echo "$pid" > "$out/xorg.pid"
  sudo -n chmod a+r "$log" 2>/dev/null || true
  # README 7.2: the NVIDIA module must be loaded and its driver lines begin "(II) NVIDIA(0)". README 8: GLX and NV-GLX present.
  grep -q 'LoadModule: "nvidia"' "$log" || { fail_start "$out" 5 'Xorg did not load the NVIDIA X driver.'; return; }
  grep -qF '(II) NVIDIA(0)' "$log" || { fail_start "$out" 5 'Xorg has no NVIDIA(0) screen.'; return; }
  grep -qw 'GLX' "$out/xdpyinfo.txt" || { fail_start "$out" 5 'X display lacks the GLX extension.'; return; }
  grep -qF 'NV-GLX' "$out/xdpyinfo.txt" || { fail_start "$out" 5 'X display lacks NV-GLX; GLX is not the NVIDIA implementation.'; return; }
  echo "Xorg ready on $display with the NVIDIA X driver (Xorg pid $pid, launcher pid $launcher)."
}

# Xorg resolves a relative -config through its own search path, so every evidence path is made absolute once, here,
# for probe, start and stop alike.
absolute_dir() {
  mkdir -p "$1"
  (cd "$1" && pwd -P)
}

case "${1:-}" in
  probe) probe "$(absolute_dir "${2:?evidence directory}")" ;;
  config) write_config "${2:?BusID}" ;;
  start) start "$(absolute_dir "${2:?evidence directory}")" "${3:?display such as :99}" ;;
  stop) cleanup "$(absolute_dir "${2:?evidence directory}")" ;;
  *) echo "usage: $0 probe|config|start|stop ..." >&2; exit 2 ;;
esac
