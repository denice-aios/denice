// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `version` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::Version;

fn v(text: &str) -> Version {
    text.parse().unwrap_or_else(|e| unreachable!("{e}"))
}

#[test]
fn higher_combined_number_wins() {
    assert!(v("0.1.108430999-pre-release+abcdef0") > v("0.1.108430176-pre-release+732fc53"));
}

#[test]
fn release_outranks_its_own_pre_release() {
    assert!(v("0.1.5") > v("0.1.5-pre-release+aaaaaaa"));
}

#[test]
fn higher_minor_wins_over_higher_combined() {
    assert!(v("0.2.1-pre-release+a") > v("0.1.999999999-pre-release+b"));
}

#[test]
fn same_version_different_hash_is_ordered_but_not_equal() {
    let a = v("0.1.7-pre-release+aaaaaaa");
    let b = v("0.1.7-pre-release+bbbbbbb");
    assert_ne!(a, b);
    assert!(a < b);
}

#[test]
fn display_round_trips() {
    let text = "0.1.108430176-pre-release+732fc53";
    assert_eq!(v(text).to_string(), text);
}

#[test]
fn rejects_non_versions() {
    assert!("versions".parse::<Version>().is_err());
    assert!("0.1.5.partial".parse::<Version>().is_err());
}

#[test]
fn this_build_has_a_valid_version() {
    assert!(Version::of_this_build().is_ok());
}
