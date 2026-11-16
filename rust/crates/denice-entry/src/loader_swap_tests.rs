// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `loader_swap` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::*;
use denice_core::platform::loader_artifact_name;

struct Install {
    _dir: tempfile::TempDir,
    root: PathBuf,
    version_dir: PathBuf,
}

fn install() -> Install {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    let root = dir.path().join("Denice");
    let version_dir = root.join("versions/0.1.200-pre-release+bbbbbbb");
    std::fs::create_dir_all(&version_dir).unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(root.join(loader_artifact_name()), b"old").unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(version_dir.join(loader_artifact_name()), b"new").unwrap_or_else(|e| unreachable!("{e}"));
    Install { _dir: dir, root, version_dir }
}

fn version(text: &str) -> Version {
    text.parse().unwrap_or_else(|e| unreachable!("{e}"))
}

fn installed_loader(i: &Install) -> String {
    std::fs::read_to_string(i.root.join(loader_artifact_name())).unwrap_or_else(|e| unreachable!("{e}"))
}

#[test]
fn an_older_loader_is_replaced() {
    let i = install();
    let context = LoaderContext { install_root: i.root.clone(), loader_version: "0.1.100-pre-release+aaaaaaa".into() };
    let outcome = decide_and_swap(&context, &version("0.1.200-pre-release+bbbbbbb"), &i.version_dir);
    assert!(matches!(outcome, Ok(Outcome::Replaced)));
    assert_eq!(installed_loader(&i), "new");
}

#[test]
fn the_same_or_a_newer_loader_is_kept() {
    let i = install();
    for loader in ["0.1.200-pre-release+bbbbbbb", "0.1.300-pre-release+ccccccc"] {
        let context = LoaderContext { install_root: i.root.clone(), loader_version: loader.into() };
        let outcome = decide_and_swap(&context, &version("0.1.200-pre-release+bbbbbbb"), &i.version_dir);
        assert!(matches!(outcome, Ok(Outcome::NotNeeded)), "{loader}");
    }
    assert_eq!(installed_loader(&i), "old");
}

#[test]
fn a_version_without_a_carried_loader_reports_it_and_keeps_the_old_one() {
    let i = install();
    std::fs::remove_file(i.version_dir.join(loader_artifact_name())).unwrap_or_else(|e| unreachable!("{e}"));
    let context = LoaderContext { install_root: i.root.clone(), loader_version: "0.1.100-pre-release+aaaaaaa".into() };
    let outcome = decide_and_swap(&context, &version("0.1.200-pre-release+bbbbbbb"), &i.version_dir);
    assert!(matches!(outcome, Err(Error::MissingCarriedLoader(_))));
    assert_eq!(installed_loader(&i), "old");
}
