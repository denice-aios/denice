// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `platform` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::*;

#[test]
fn this_platform_is_one_of_the_six_released() {
    let six = ["osx-arm64", "osx-x64", "win-arm64", "win-x64", "linux-arm64", "linux-x64"];
    assert!(six.contains(&rid()), "{}", rid());
}

#[test]
fn entry_name_matches_the_release_naming_convention() {
    let name = entry_file_name("0.1.108430176-pre-release+732fc53");
    assert_eq!(name, format!("denice-v0.1.108430176-pre-release+732fc53-{}{}", rid(), exe_suffix()));
}

#[test]
fn loader_name_is_denice() {
    assert_eq!(loader_file_name(), format!("denice{}", exe_suffix()));
}
