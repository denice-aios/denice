#!/usr/bin/env bash
# Copyright (c) Denice.ai. All rights reserved.
# Read-only inspection on a Linux host or guest. Never installs drivers or changes device ownership.
set -euo pipefail
if [[ $# -ne 1 ]]; then echo 'usage: inspect-linux-gpu.sh OUTPUT_DIRECTORY' >&2; exit 2; fi
if [[ $(uname -s) != Linux ]]; then echo 'Linux is required.' >&2; exit 2; fi
output=$1
mkdir -p "$output"
{
  uname -srmo
  if [[ -r /etc/os-release ]]; then cat /etc/os-release; fi
  if command -v systemd-detect-virt >/dev/null; then systemd-detect-virt || true; fi
  if command -v lscpu >/dev/null; then lscpu; fi
  if command -v lspci >/dev/null; then lspci -nnk | sed -n '/VGA compatible controller\|3D controller\|Display controller/,+3p'; fi
  shopt -s nullglob
  groups=(/sys/kernel/iommu_groups/*)
  printf 'Visible IOMMU group count=%s (not a passthrough compatibility verdict)\n' "${#groups[@]}"
  printf 'DISPLAY=%s\nWAYLAND_DISPLAY=%s\nXDG_SESSION_TYPE=%s\n' "${DISPLAY:-unset}" "${WAYLAND_DISPLAY:-unset}" "${XDG_SESSION_TYPE:-unset}"
  for device in /dev/dri/card* /dev/dri/renderD*; do
    [[ -e "$device" ]] || continue
    printf '%s readable=%s writable=%s\n' "$device" "$([[ -r "$device" ]] && echo yes || echo no)" "$([[ -w "$device" ]] && echo yes || echo no)"
  done
} > "$output/linux-host.txt" 2>&1
for probe in nvidia-smi vulkaninfo eglinfo glxinfo vainfo; do
  if ! command -v "$probe" >/dev/null; then printf '%s unavailable\n' "$probe" >> "$output/tools.txt"; continue; fi
  if ! command -v timeout >/dev/null; then printf '%s not run: timeout unavailable\n' "$probe" >> "$output/tools.txt"; continue; fi
  case "$probe" in
    nvidia-smi) args=(--query-gpu=name,driver_version,memory.total --format=csv) ;;
    vulkaninfo) args=(--summary) ;;
    glxinfo) args=(-B) ;;
    *) args=() ;;
  esac
  set +e
  timeout 30 "$probe" "${args[@]}" > "$output/$probe.txt" 2>&1
  result=$?
  set -e
  printf '%s exit=%s\n' "$probe" "$result" >> "$output/tools.txt"
done
printf '%s\n' 'Native DENICE rendering, CEF native-buffer import, and hardware encoding: NOT_TESTED.' > "$output/proof-status.txt"
echo 'Linux inventory saved. Review tool exits and device details; this is not an application GPU proof.'
