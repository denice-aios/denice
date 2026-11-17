// R8 ignored tests, R9 coverage opt-out, R12 caller locations.
#![allow(unused_attributes)]

#[test]
#[ignore]
fn skipped() {}

#[cfg_attr(windows, ignore)]
fn skipped_on_windows() {}

#[cfg_attr(coverage_nightly, coverage(off))]
fn hidden_from_coverage() {}

#[track_caller]
fn tracked() {}

fn main() {
    skipped_on_windows();
    hidden_from_coverage();
    tracked();
    let _ = file!();
    let _ = line!();
}
