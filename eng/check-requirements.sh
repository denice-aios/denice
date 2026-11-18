#!/usr/bin/env bash
# The finish line for the loader work (rust/REQUIREMENTS.md E2; Tim, 16 November 2026: "Do not defer that decision or
# cut the work short"). Every requirement row must be `done <commit> <evidence>`, where <commit> exists in this
# repository's history and <evidence> names the test, script or CI step that proves it. `open` and `blocked` rows are
# reported (and listed in the CI run's summary) but do not fail: CI is never red by design. A malformed row does fail:
# a status that is not `open`, `blocked <package>` or `done <commit> <evidence>`, a commit not in this repository, or
# done without evidence. There is no "not needed" or "later" — only Tim's recorded answer can remove a row, and the
# row's source then cites that answer.
#
# Usage: eng/check-requirements.sh [requirements-file]
set -euo pipefail

FILE="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/rust/REQUIREMENTS.md}"
fail=0; rows=0; done_rows=0; pending=""
while IFS= read -r line; do
  case "$line" in
    "| "[A-Z][0-9]*" |"*) ;;
    *) continue ;;
  esac
  rows=$((rows + 1))
  id=$(printf '%s' "$line" | awk -F'|' '{gsub(/ /,"",$2); print $2}')
  status=$(printf '%s' "$line" | awk -F'|' '{print $(NF-1)}' | sed 's/^ *//; s/ *$//')
  case "$status" in
    "done "*)
      commit=$(printf '%s' "$status" | awk '{print $2}')
      evidence=$(printf '%s' "$status" | cut -d' ' -f3-)
      if ! git -C "$(dirname "$FILE")" cat-file -e "${commit}^{commit}" 2>/dev/null; then
        echo "check-requirements: $id: commit '$commit' is not in this repository"; fail=1
      elif [ -z "$evidence" ] || [ "$evidence" = "$commit" ]; then
        echo "check-requirements: $id: done without evidence"; fail=1
      else
        done_rows=$((done_rows + 1))
      fi
      ;;
    open|"blocked "*) echo "check-requirements: $id: $status"; pending="$pending- $id: $status"$'\n' ;;
    *) echo "check-requirements: $id: '$status' is not a status (open | blocked <package> | done <commit> <evidence>)"; fail=1 ;;
  esac
done < "$FILE"

echo "check-requirements: $done_rows of $rows requirements done with evidence"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  { echo "### Loader requirements: $done_rows of $rows done"; [ -z "$pending" ] || printf 'Not done yet:\n%s' "$pending"; } \
    >> "$GITHUB_STEP_SUMMARY"
fi
[ "$rows" -gt 0 ] || { echo "check-requirements: no requirement rows found in $FILE"; exit 1; }
exit "$fail"
