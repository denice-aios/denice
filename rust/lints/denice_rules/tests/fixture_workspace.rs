// Copyright (c) Denice.ai. All rights reserved.

//! R10 and R11 need real crates, so they run over fixtures/workspace with `cargo dylint`: the fixture plants one
//! violation of each, plus one allowed use, and the test checks exactly which are reported.

use std::path::Path;
use std::process::Command;

#[test]
fn crate_rules_report_the_planted_violations_and_nothing_else() {
    let here = Path::new(env!("CARGO_MANIFEST_DIR"));
    let output = Command::new("cargo")
        .current_dir(here.join("fixtures/workspace"))
        .args(["dylint", "--path"])
        .arg(here)
        // --keep-going: without it cargo starts no new crate after the first error, so whichever planted violation is
        // checked first hides the other (CI run 46107412976: R11's denice_loader failed before product_user was checked).
        .args(["--all", "--", "--all-targets", "--keep-going"])
        .output()
        .expect("cargo dylint runs");
    let text = String::from_utf8_lossy(&output.stderr);
    assert!(!output.status.success(), "the planted violations must fail the check:\n{text}");
    assert!(text.contains("`tempfile` is a test-only crate"), "R10 not reported:\n{text}");
    assert!(text.contains("`denice_loader` may not use `denice_entry`"), "R11 not reported:\n{text}");
    assert!(!text.contains("may not use `denice_core`"), "an allowed dependency was reported:\n{text}");
}
