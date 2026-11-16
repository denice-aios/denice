// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `platform` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::*;
use std::path::Path;

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
fn loader_program_is_named_denice() {
    assert_eq!(loader_program_name(), format!("denice{}", exe_suffix()));
}

#[test]
fn the_loader_artifact_is_the_app_on_macos_and_the_program_elsewhere() {
    let expected = if cfg!(target_os = "macos") { "Denice.app".to_owned() } else { loader_program_name() };
    assert_eq!(loader_artifact_name(), expected);
}

#[test]
fn the_install_root_is_found_from_the_loader_program() {
    let root = Path::new("/home/user/Denice");
    let program = loader_program_in(&root.join(loader_artifact_name()));
    assert_eq!(install_root_of(&program).as_deref(), Some(root));
    assert!(program.ends_with(loader_program_name()));
}
