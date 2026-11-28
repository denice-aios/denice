#!/usr/bin/env bash
# Finds the approved hardware coverage for ONE target and ONE exact source tree, validates where it came from and that
# every required fixture passed, and extracts it to <out-dir>. Absent or untrusted -> nothing extracted, and
# eng/coverage-gate.sh then says PENDING wherever the staged GPU policy needs it. Never pools across targets: the
# artifact name, its binding.json and the gate all carry the target.
#
# Hardware coverage exists only where a GPU runner exists (.github/workflows/desktop-capability.yml): win-x64 and
# linux-x64. For every other target this prints that and extracts nothing.
#
# Usage: eng/find-hardware-coverage.sh <rid> <tree-sha> <out-dir>   (needs GH_TOKEN with actions:read and REPOSITORY)
set -euo pipefail
[ "$#" -eq 3 ] || { echo "usage: eng/find-hardware-coverage.sh <rid> <tree-sha> <out-dir>" >&2; exit 2; }
target=$1 tree=$2 out=$3
[[ $tree =~ ^[0-9a-f]{40}$ ]] || { echo "invalid tree sha '$tree'" >&2; exit 2; }
case "$target" in
  win-x64) job=windows ;;
  linux-x64) job=linux ;;
  osx-arm64|osx-x64|win-arm64|linux-arm64)
    echo "find-hardware-coverage: no GPU runner produces hardware coverage for $target; nothing to find." >&2
    exit 0 ;;
  *) echo "unknown target '$target'" >&2; exit 2 ;;
esac
: "${REPOSITORY:?REPOSITORY is required}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
name="hardware-coverage-$target-$tree"
repo_id=$(gh api "repos/$REPOSITORY" --jq .id)
gh api "repos/$REPOSITORY/actions/artifacts?name=$name&per_page=100" > "$work/artifacts.json"
for run in $(jq -r --argjson rid "$repo_id" '.artifacts | map(select(.expired == false and .workflow_run.head_repository_id == $rid))
               | sort_by(.created_at) | reverse | .[].workflow_run.id' "$work/artifacts.json"); do
  gh api "repos/$REPOSITORY/actions/runs/$run" > "$work/run.json"
  # Only the approved label path in ci.yml (gpu-proof) or a manual dispatch of desktop-capability.yml, from this repository.
  jq -e --arg r "$REPOSITORY" '.repository.full_name == $r and .head_repository.full_name == $r and
        ((.path == ".github/workflows/ci.yml" and .event == "pull_request") or
         (.path == ".github/workflows/desktop-capability.yml" and .event == "workflow_dispatch"))' "$work/run.json" >/dev/null || continue
  job_name=$job
  [[ $(jq -r .event "$work/run.json") == pull_request ]] && job_name="gpu-proof / $job"
  gh api --paginate --slurp "repos/$REPOSITORY/actions/runs/$run/jobs?per_page=100" \
    | jq -e --arg j "$job_name" '[.[].jobs[] | select(.name == $j and .conclusion == "success")] | length >= 1' >/dev/null || continue
  rm -rf "$out"
  gh run download "$run" --repo "$REPOSITORY" -n "$name" -D "$out"
  jq -e --arg t "$tree" --arg target "$target" '.tree_sha == $t and .target == $target' "$out/binding.json" >/dev/null || { rm -rf "$out"; continue; }
  # Re-run the SAME validators the GPU job used (one definition of "every required fixture passed").
  if [[ $target == linux-x64 ]]; then
    bash eng/run-linux-desktop-smoke.sh --fixture-results "$out" || { rm -rf "$out"; continue; }
  else
    pwsh -NoProfile -Command ". ./eng/windows/read-sandbox-result.ps1; Read-DeniceHardwareFixtureResult -OutputDirectory '$out' | Out-Null" || { rm -rf "$out"; continue; }
  fi
  echo "find-hardware-coverage: $target hardware coverage from GPU run $run, tree $tree." >&2
  [ -z "${GITHUB_STEP_SUMMARY:-}" ] || echo "Hardware coverage for $target: GPU run $run, tree $tree." >> "$GITHUB_STEP_SUMMARY"
  exit 0
done
echo "find-hardware-coverage: no approved $target hardware coverage for tree $tree." >&2
