#!/usr/bin/env bash
#
# The coverage gate (decisions threshold-75-hard-gate, covered-assemblies), evaluated independently per target.
#
# Approved rule (Tim, 2026-11-28): "75% is independently per OS AND CPU across macOS-x64, macOS-arm64, Windows-x64,
# Windows-arm64, Linux-x64, Linux-arm64. Multiple runs may combine only within the same target and matching source/build
# evidence. Intel Mac coverage cannot raise Windows or Apple Silicon coverage; no cross-architecture pooling."
#
# For ONE target (--target) the gate merges only that target's ordinary cobertura reports with ReportGenerator and
# reports, at 75% line coverage:
#   aggregate                          Engine + Denice.Runtime + Denice.Updates + Denice.CrossPlatform + Denice.Boundaries.
#   the per-OS implementation          Denice.CrossPlatform.MacOS (osx-*), .Windows (win-*) or .Linux (linux-*).
#
# Target tag: every ordinary report must sit under TestResults/<rid>/ (Directory.Build.props puts each `dotnet test`
# run there when DENICE_COVERAGE_TARGET=<rid> is set). A report with no such tag, or tagged with another target, is
# REFUSED (exit 1) — reports of different targets are never pooled. vstest's TRX attachment copies
# (<deployment>/In/<host>/coverage.cobertura.xml, byte-identical to the <guid>/ report) are skipped, so each run counts
# once; of several runs of one test project, only the newest (the current run) is merged.
#
# Hardware coverage and the staged branch GPU policy (Tim): working = default merges (pull requests), no GPU;
# fast = internal releases, Linux GPU; dev = beta, Windows GPU; main = public, all GPU. The Windows and Linux per-OS
# implementations need a GPU to be exercised (their GPU fixtures run only on GPU runners); the macOS implementation's
# fixtures run on the standard hosted macOS runner, so macOS targets and the aggregate never depend on hardware.
#   - Stage does not run that GPU   -> "NOT EVALUATED" with the ordinary-only number; not a pass, not a failure.
#   - Stage runs that GPU           -> needs one --hardware report for the SAME target and the SAME source tree
#                                      (binding.json: target, tree_sha == `git rev-parse HEAD^{tree}`). Ordinary +
#                                      hardware >= 75% passes; absent -> "PENDING" (exit 3), never a pass.
#   - Targets with no GPU runner (win-arm64, linux-arm64: GitHub offers no arm64 GPU runner) say so explicitly in the
#     PENDING line: their hardware coverage cannot currently be produced, so they cannot pass at such a stage.
# Every report's file paths are normalized to repo-relative before merging (checkout roots differ between machines; the
# source-less hardware payload says `/_/`; generated files live under `obj/<cfg>/<tfm>[/<rid>]/`), so one source line is
# one coverable line whichever machine measured it. A hardware report whose coverable line set for the per-OS assembly
# differs from the ordinary one is a FAIL (it measured different code).
#
# The gate FAILS loudly when a required assembly is absent from the merged report, so an un-instrumented assembly can
# never be silently dropped from the number.
#
# Exit codes: 0 every evaluated check passed (NOT EVALUATED lines are not passes and are printed as such); 1 a check
# failed or an input was refused; 2 usage; 3 PENDING hardware coverage (only when nothing failed).
#
# Usage: eng/coverage-gate.sh --target <rid> --stage <working|fast|dev|main> [--hardware <dir>] <report-dir>
set -euo pipefail

# The coverage floor is a HARD 75% with NO environment override (decisions threshold-75-hard-gate,
# coverage-threshold-hard-no-override): a failing coverage run has the same standing as a failing test and cannot be
# knobbed down for a green build.
MIN=75

usage() {
  echo "usage: eng/coverage-gate.sh --target <osx-arm64|osx-x64|win-x64|win-arm64|linux-x64|linux-arm64> --stage <working|fast|dev|main> [--hardware <dir>] <report-dir>" >&2
  exit 2
}

TARGET="" STAGE="" HARDWARE_DIR="" REPORT_ARG=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --target) [ "$#" -ge 2 ] || usage; TARGET="$2"; shift 2 ;;
    --stage) [ "$#" -ge 2 ] || usage; STAGE="$2"; shift 2 ;;
    --hardware) [ "$#" -ge 2 ] && [ -z "$HARDWARE_DIR" ] || usage; HARDWARE_DIR="$2"; shift 2 ;;
    -*) usage ;;
    *) [ -z "$REPORT_ARG" ] || usage; REPORT_ARG="$1"; shift ;;
  esac
done
[ -n "$TARGET" ] && [ -n "$STAGE" ] && [ -n "$REPORT_ARG" ] || usage

# The six targets and, per target, its per-OS implementation and the GPU its hardware-dependent classes need.
case "$TARGET" in
  osx-arm64|osx-x64) PER_OS=Denice.CrossPlatform.MacOS; NEEDS_GPU="" ;;   # macOS fixtures run on standard hosted runners
  win-x64|win-arm64) PER_OS=Denice.CrossPlatform.Windows; NEEDS_GPU=windows ;;
  linux-x64|linux-arm64) PER_OS=Denice.CrossPlatform.Linux; NEEDS_GPU=linux ;;
  *) echo "::error::coverage-gate: unknown target '$TARGET'." >&2; usage ;;
esac
# Where a GPU runner exists to produce hardware coverage (.github/workflows/desktop-capability.yml): win-x64 and
# linux-x64 only.
case "$TARGET" in
  win-x64|linux-x64) HARDWARE_RUNNER=yes ;;
  *) HARDWARE_RUNNER=no ;;
esac
# The staged branch GPU policy: which GPUs each stage runs.
stage_gpus() {
  case "$1" in
    working) echo "" ;;
    fast) echo "linux" ;;
    dev) echo "windows" ;;
    main) echo "windows linux" ;;
    *) return 1 ;;
  esac
}
STAGE_GPUS="$(stage_gpus "$STAGE")" || { echo "::error::coverage-gate: unknown stage '$STAGE'." >&2; usage; }
runs_gpu() { [ -n "$NEEDS_GPU" ] && case " $1 " in *" $NEEDS_GPU "*) true ;; *) false ;; esac; }
REQUIRED_AT=""
for s in working fast dev main; do
  if runs_gpu "$(stage_gpus "$s")"; then REQUIRED_AT="${REQUIRED_AT:+$REQUIRED_AT, }$s"; fi
done
STAGE_RUNS_GPU=no
if runs_gpu "$STAGE_GPUS"; then STAGE_RUNS_GPU=yes; fi

# Resolve the report directory to an absolute path so report paths survive the ReportGenerator run below regardless of
# the working directory.
REPORT_DIR="$(cd "$REPORT_ARG" && pwd)"
[ -z "$HARDWARE_DIR" ] || HARDWARE_DIR="$(cd "$HARDWARE_DIR" && pwd)"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
# The exact source tree this gate runs on: hardware coverage is accepted only when bound to the same tree.
TREE="$(git -C "$REPO_ROOT" rev-parse 'HEAD^{tree}' 2>/dev/null || echo unknown)"

# The aggregate counted set — the single source of truth, shared by the counted-set self-check below and the gate math
# at the end. Denice Runtime builds as `Denice.Runtime.dll` (assembly `Denice.Runtime`).
AGGREGATE=(Denice.Engine Denice.Runtime Denice.Updates Denice.CrossPlatform Denice.Boundaries)

# Resolve the Python used for the self-check, report selection, and the gate math below.
PYTHON="python3"
command -v python3 >/dev/null 2>&1 || PYTHON="python"

# Counted-set consistency self-check (decision counted-set-consistency-check). Every instrumented product assembly must
# be gated SOMEWHERE, or it is measured and then silently ignored (under-measured). Parse the .runsettings <Include>
# set, split it into the per-OS implementations (the .MacOS/.Linux/.Windows suffix, each gated on its own targets) and
# everything else, and assert "everything else" is exactly the AGGREGATE list — so the instrumented set equals the union
# of the aggregate-gated set and the per-OS-gated set. Runs before any report work so a drift fails the gate fast, with
# no coverage run needed. This is the direction the report-presence check does NOT catch (that one fails on a gated but
# un-instrumented assembly; this fails on an instrumented but un-gated one).
RUNSETTINGS="$REPO_ROOT/.runsettings"
"$PYTHON" - "$RUNSETTINGS" "${AGGREGATE[*]}" <<'PY'
import re, sys

runsettings_path, aggregate_joined = sys.argv[1], sys.argv[2]
aggregate = set(aggregate_joined.split())

text = open(runsettings_path).read()
match = re.search(r"<Include>(.*?)</Include>", text, re.DOTALL)
if not match:
    sys.stderr.write("::error::counted-set: could not find <Include> in %s.\n" % runsettings_path)
    sys.exit(1)

# Each instrumented assembly appears as `[Name]*`.
included = set(re.findall(r"\[([^\]]+)\]\*", match.group(1)))
per_os = {name for name in included if name.endswith((".MacOS", ".Linux", ".Windows"))}
everything_else = included - per_os

if everything_else != aggregate:
    sys.stderr.write(
        "::error::counted-set consistency: the non-per-OS Include set %s does not equal the AGGREGATE set %s "
        "(an instrumented assembly would be measured but gated nowhere).\n"
        % (sorted(everything_else), sorted(aggregate)))
    sys.exit(1)

sys.stderr.write(
    "coverage-gate: counted-set consistency OK (non-per-OS Include == AGGREGATE == %s; per-OS == %s).\n"
    % (sorted(aggregate), sorted(per_os)))
PY

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Select this target's ordinary reports; refuse untagged and other-target reports.
"$PYTHON" - "$REPORT_DIR" "$TARGET" "$WORK/reports.txt" <<'PY'
import os, sys

report_dir, target, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
TARGETS = {"osx-arm64", "osx-x64", "win-x64", "win-arm64", "linux-x64", "linux-arm64"}
newest = {}   # TestResults/RID/PROJECT directory: (mtime, path) of its latest report
stale, refused = [], []
for root, _dirs, files in os.walk(report_dir):
    if "coverage.cobertura.xml" not in files:
        continue
    path = os.path.join(root, "coverage.cobertura.xml")
    parts = os.path.normpath(path).split(os.sep)
    # The vstest TRX logger copies each attachment to RESULTS/DEPLOYMENT/In/HOST/; the GUID/ report is the original.
    if len(parts) >= 3 and parts[-3] == "In":
        sys.stderr.write("coverage-gate: skipping TRX attachment copy %s\n" % path)
        continue
    tag = None
    if "TestResults" in parts:
        i = len(parts) - 1 - parts[::-1].index("TestResults")
        tag = parts[i + 1] if i + 1 < len(parts) - 1 else None
    if tag not in TARGETS:
        refused.append("untagged report %s (not under TestResults/RID/; run dotnet test with DENICE_COVERAGE_TARGET=RID)" % path)
        continue
    if tag != target:
        refused.append("report %s is tagged %s, not %s (no cross-target pooling)" % (path, tag, target))
        continue
    # .../TestResults/RID/PROJECT/GUID/coverage.cobertura.xml: group on the project's results directory, so each
    # test project contributes exactly its latest run and re-runs of one project never double-count.
    group = os.path.dirname(os.path.dirname(path))
    mtime = os.path.getmtime(path)
    if group not in newest:
        newest[group] = (mtime, path)
    elif mtime > newest[group][0]:
        stale.append(newest[group][1])
        newest[group] = (mtime, path)
    else:
        stale.append(path)

if refused:
    for r in refused:
        sys.stderr.write("::error::coverage-gate %s: refused %s.\n" % (target, r))
    sys.exit(1)
selected = sorted(p for _m, p in newest.values())
for p in sorted(stale):
    sys.stderr.write("coverage-gate: skipping stale report %s\n" % p)
sys.stderr.write("coverage-gate: merging %d current %s cobertura report(s):\n" % (len(selected), target))
with open(out_path, "w") as out:
    for p in selected:
        sys.stderr.write("  %s\n" % p)
        out.write(p + "\n")
PY
REPORTS=()
while IFS= read -r file; do
  [ -n "$file" ] && REPORTS+=("$file")
done < "$WORK/reports.txt"
if [ "${#REPORTS[@]}" -eq 0 ]; then
  echo "::error::no $TARGET cobertura reports found under '$REPORT_DIR' — coverage was not collected for this target." >&2
  exit 1
fi

# Hardware input: one downloaded, already-validated artifact holding exactly one report and its binding.json, which must
# name this target and this exact source tree.
HW_REPORT=""
USE_HARDWARE=no
if [ -n "$HARDWARE_DIR" ]; then
  found=()
  while IFS= read -r f; do [ -n "$f" ] && found+=("$f"); done < <(find "$HARDWARE_DIR" -type f -name coverage.cobertura.xml)
  if [ "${#found[@]}" -ne 1 ] || [ ! -f "$HARDWARE_DIR/binding.json" ]; then
    echo "::error::hardware coverage dir '$HARDWARE_DIR' must hold exactly one coverage.cobertura.xml (found ${#found[@]}) and binding.json." >&2
    exit 1
  fi
  "$PYTHON" - "$HARDWARE_DIR/binding.json" "$TARGET" "$TREE" <<'PY'
import json, sys
path, target, tree = sys.argv[1:]
b = json.load(open(path))
if b.get("target") != target:
    sys.exit("::error::hardware coverage refused: binding target %r is not %s (no cross-target pooling)." % (b.get("target"), target))
if b.get("tree_sha") != tree:
    sys.exit("::error::hardware coverage refused: binding tree %r is not this source tree %s." % (b.get("tree_sha"), tree))
sys.stderr.write("coverage-gate: hardware coverage for %s bound to tree %s (GPU run %s).\n" % (target, tree, b.get("gpu_run_id")))
PY
  HW_REPORT="${found[0]}"
  if [ "$STAGE_RUNS_GPU" = yes ]; then
    USE_HARDWARE=yes
  else
    echo "coverage-gate: hardware report supplied but not used: $PER_OS on $TARGET needs no hardware coverage at stage $STAGE." >&2
  fi
fi

# Normalize every report into $WORK/n (ordinary) and $WORK/h (hardware); ReportGenerator merges only normalized copies.
HW_ARGS=()
[ "$USE_HARDWARE" = no ] || HW_ARGS=("$HW_REPORT")
"$PYTHON" - "$WORK" "${#REPORTS[@]}" "${REPORTS[@]}" ${HW_ARGS[@]+"${HW_ARGS[@]}"} <<'PY'
import os, re, sys, xml.etree.ElementTree as ET
work, n_ord, paths = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
def normalize(name):
    p = name.replace('\\', '/')
    m = re.search(r'((?:src|tests)/Denice\.[^/]+/.*)$', p)
    p = m.group(1) if m else p
    return re.sub(r'/obj/[^/]+/[^/]+/(?:(?:win|linux|osx)-(?:x64|arm64)/)?', '/obj/', p)
for i, path in enumerate(paths):
    tree = ET.parse(path)
    root = tree.getroot()
    for source in root.iter('source'):
        source.text = '/'
    for cls in root.iter('class'):
        cls.set('filename', normalize(cls.get('filename', '')))
    kind = 'n' if i < n_ord else 'h'
    os.makedirs(os.path.join(work, kind), exist_ok=True)
    tree.write(os.path.join(work, kind, '%03d.xml' % i), encoding='utf-8', xml_declaration=True)
PY

# Restore ReportGenerator at the single pinned version from the committed tool manifest (.config/dotnet-tools.json,
# decision reportgenerator-pinned) — one pin, no drift, and no on-demand global install. Every run uses the identical
# version the manifest names, and CI needs no separate install step.
echo "coverage-gate: restoring pinned tools from .config/dotnet-tools.json" >&2
dotnet tool restore --tool-manifest "$REPO_ROOT/.config/dotnet-tools.json" >&2

# Run the pinned local tool from the manifest root so `dotnet tool run` finds .config/dotnet-tools.json regardless of
# the caller's working directory; the report paths and $WORK are absolute, so the subshell cd is safe.
summarize() { # <out-dir> <report-glob-dirs...>
  local out="$1" joined
  shift
  joined="$(printf '%s;' "$@")"
  (cd "$REPO_ROOT" && dotnet tool run reportgenerator "-reports:${joined%;}" "-targetdir:$out" "-reporttypes:JsonSummary") >/dev/null
}
summarize "$WORK/ordinary" "$WORK/n/*.xml"
[ "$USE_HARDWARE" = no ] || summarize "$WORK/merged" "$WORK/n/*.xml" "$WORK/h/*.xml"

"$PYTHON" - "$WORK" "$MIN" "${AGGREGATE[*]}" "$TARGET" "$STAGE" "$PER_OS" "$NEEDS_GPU" "$STAGE_RUNS_GPU" "$REQUIRED_AT" \
  "$HARDWARE_RUNNER" "$USE_HARDWARE" "$TREE" <<'PY'
import glob, json, os, sys, xml.etree.ElementTree as ET

(work, min_pct, aggregate, target, stage, per_os, needs_gpu, stage_runs_gpu, required_at, hardware_runner,
 use_hardware, tree) = sys.argv[1:]
min_pct, AGGREGATE = int(min_pct), aggregate.split()

def load(name):
    data = json.load(open(os.path.join(work, name, 'Summary.json')))
    return {a["name"]: (a["coveredlines"], a["coverablelines"]) for a in data["coverage"]["assemblies"]}

ordinary = load('ordinary')
failed = pending = False

def numbers(asm, names):
    return sum(asm.get(n, (0, 0))[0] for n in names), sum(asm.get(n, (0, 0))[1] for n in names)

def pct(c, t):
    return f"{c}/{t} = {100.0 * c / t:.2f}%" if t else f"{c}/{t}"

def gate(label, asm, names):
    global failed
    covered, coverable = numbers(asm, names)
    if coverable == 0:
        print(f"::error::per-target {target} {label}: no coverable lines (assemblies {names} absent from the report).")
        failed = True
        return
    # Exact integer comparison for ">= min%": covered/coverable >= min/100.
    ok = covered * 100 >= min_pct * coverable
    print(f"{'PASS' if ok else 'FAIL'} per-target {target} {label}: {pct(covered, coverable)} (min {min_pct}%)")
    if not ok:
        print(f"::error::per-target {target} {label} is below {min_pct}% line coverage.")
        failed = True

def lines(paths, name):
    """Coverable (file, line) pairs of one assembly, excluding generated obj/ sources."""
    out = set()
    for p in paths:
        for pkg in ET.parse(p).getroot().iter('package'):
            if pkg.get('name') != name:
                continue
            for cls in pkg.iter('class'):
                f = cls.get('filename')
                if '/obj/' in f:
                    continue
                out |= {(f, int(l.get('number'))) for l in cls.iter('line')}
    return out

# Aggregate: never hardware-dependent.
missing = [n for n in AGGREGATE if n not in ordinary]
if missing:
    # No PASS line for a set that was not fully measured.
    print(f"FAIL per-target {target} aggregate: required assemblies absent from the merged report: {missing}.")
    print(f"::error::per-target {target} aggregate: required assemblies absent from the merged report: {missing}.")
    failed = True
else:
    gate("aggregate(Engine+Runtime+Updates+CrossPlatform+Boundaries)", ordinary, AGGREGATE)

# The per-OS implementation.
if per_os not in ordinary:
    print(f"::error::per-target {target} {per_os}: absent from the merged report (it was not instrumented).")
    failed = True
elif not needs_gpu:
    gate(per_os, ordinary, [per_os])
else:
    alone = pct(*numbers(ordinary, [per_os]))
    if stage_runs_gpu != 'yes':
        print(f"NOT EVALUATED per-target {target} {per_os}: hardware coverage required at {required_at} (staged GPU policy); "
              f"this run is stage {stage}. Ordinary tests alone: {alone}. Not a pass; nothing is claimed.")
    elif use_hardware != 'yes':
        if hardware_runner == 'yes':
            why = f"no approved {target} hardware coverage exists for source tree {tree}"
        else:
            why = (f"it cannot currently be produced for {target}: no GPU runner exists for this target "
                   f"(GitHub offers no arm64 GPU runner), so this target cannot pass at this stage")
        print(f"PENDING per-target {target} {per_os}: hardware coverage required at {stage} (staged GPU policy), but {why}. "
              f"Ordinary tests alone: {alone}. Not a pass; nothing is claimed.")
        pending = True
    else:
        n = sorted(glob.glob(os.path.join(work, 'n', '*.xml')))
        h = sorted(glob.glob(os.path.join(work, 'h', '*.xml')))
        base, hw = lines(n, per_os), lines(h, per_os)
        if not hw:
            print(f"::error::per-target {target} {per_os}: the hardware report does not contain it (not instrumented on the GPU run).")
            failed = True
        elif hw != base:
            print(f"::error::per-target {target} {per_os}: hardware coverage was measured on different code "
                  f"(coverable lines differ: {len(hw - base)} only in hardware, {len(base - hw)} only in ordinary).")
            failed = True
        else:
            print(f"coverage-gate: {per_os} ordinary tests alone: {alone}; merging the same-target, same-tree hardware report.")
            gate(f"{per_os} (ordinary + hardware)", load('merged'), [per_os])

sys.exit(1 if failed else (3 if pending else 0))
PY
