#!/usr/bin/env bash
# The Rust quality gate, the counterpart of the .NET build+test+coverage legs (rust/REQUIREMENTS.md F4, F7, F8), run
# identically in CI and locally:
#   1. formatting (rustfmt --check);
#   2. lint with warnings as errors (clippy, workspace lints in rust/Cargo.toml: pedantic, no unwrap/expect/panic);
#   3. every test (unit, rule and real-process integration tests) with coverage, failing under the same 75% line
#      threshold as the .NET gate (decision threshold-75-hard-gate). Test files are excluded from the measured set,
#      as the .NET gate excludes *.Tests assemblies.
# Usage: eng/rust-checks.sh [<lcov-output-path>]
# Environment: CARGO (default: cargo). Requires cargo-llvm-cov (CI pins the version).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CARGO="${CARGO:-cargo}"
LCOV="${1:-$REPO_ROOT/evidence/rust-coverage.lcov}"
mkdir -p "$(dirname "$LCOV")"
cd "$REPO_ROOT/rust"

echo "rust-checks: formatting"
"$CARGO" fmt --all -- --check

echo "rust-checks: lint (warnings are errors)"
RUSTFLAGS="-D warnings" "$CARGO" clippy --workspace --all-targets --locked

echo "rust-checks: tests with coverage (min 75% of product lines)"
"$CARGO" llvm-cov clean --workspace
"$CARGO" llvm-cov --workspace --locked --ignore-filename-regex '(_tests\.rs|/tests/)' \
  --fail-under-lines 75 --lcov --output-path "$LCOV"
"$CARGO" llvm-cov report --ignore-filename-regex '(_tests\.rs|/tests/)' --summary-only
echo "rust-checks: PASS"
