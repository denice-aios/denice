// Copyright (c) Denice.ai. All rights reserved.

//! The real `denice-entry` binary: it replaces an older installed loader with the one its version carries and
//! returns the hand-over code; it keeps a loader that is not older; it reports a missing Denice Runtime as a start
//! failure. Running Denice Runtime itself needs a published .NET runtime and is covered by eng/loader-e2e-test.sh.

#![allow(clippy::unwrap_used, missing_docs)]

use denice_core::platform::loader_artifact_name;
use denice_core::protocol::{
    ENV_INSTALL_ROOT, ENV_LOADER_PATH, ENV_LOADER_VERSION, ENV_VERSION_DIR, EXIT_LOADER_REPLACED,
};
use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use std::sync::{Mutex, MutexGuard, PoisonError};

/// Copying an executable on one test thread while another forks makes Linux refuse to run the copy ("Text file busy":
/// the forked child briefly holds the copy's write handle). Each test holds this lock from its copy to its run.
static COPY_AND_RUN: Mutex<()> = Mutex::new(());

fn serialized() -> MutexGuard<'static, ()> {
    COPY_AND_RUN.lock().unwrap_or_else(PoisonError::into_inner)
}

const THIS_BUILD: &str = denice_core::version::BUILD_VERSION;
/// Lower than every build: a numeric pre-release identifier sorts below any alphanumeric one (`local`, `pre-release`).
const OLDER: &str = "0.0.0-0+0000000";

struct Install {
    _dir: tempfile::TempDir,
    root: PathBuf,
    version_dir: PathBuf,
    entry: PathBuf,
}

fn install() -> Install {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("Denice");
    let version_dir = root.join("versions").join(THIS_BUILD);
    std::fs::create_dir_all(&version_dir).unwrap();
    std::fs::write(root.join(loader_artifact_name()), b"installed loader").unwrap();
    std::fs::write(version_dir.join(loader_artifact_name()), b"carried loader").unwrap();
    let entry = version_dir.join("entry-under-test");
    let _ = std::fs::copy(env!("CARGO_BIN_EXE_denice-entry"), &entry).unwrap();
    Install { _dir: dir, root, version_dir, entry }
}

fn run(install: &Install, loader_version: Option<&str>) -> Output {
    let mut command = Command::new(&install.entry);
    let _ = command.arg("version");
    if let Some(loader_version) = loader_version {
        let _ = command
            .env(ENV_INSTALL_ROOT, &install.root)
            .env(ENV_LOADER_PATH, denice_core::layout::InstallLayout::new(&install.root).loader_program_path())
            .env(ENV_LOADER_VERSION, loader_version)
            .env(ENV_VERSION_DIR, &install.version_dir);
    }
    command.output().unwrap()
}

fn read(path: &Path) -> String {
    std::fs::read_to_string(path).unwrap()
}

#[test]
fn an_older_loader_is_replaced_and_the_entry_asks_for_the_hand_over() {
    let _serial = serialized();
    let i = install();
    let out = run(&i, Some(OLDER));
    assert_eq!(out.status.code(), Some(EXIT_LOADER_REPLACED), "{out:?}");
    assert_eq!(read(&i.root.join(loader_artifact_name())), "carried loader");
    assert_eq!(read(&i.root.join(format!("{}.old", loader_artifact_name()))), "installed loader");
}

#[test]
fn a_loader_that_is_not_older_is_kept_and_a_missing_runtime_is_a_start_failure() {
    let _serial = serialized();
    let i = install();
    let out = run(&i, Some(THIS_BUILD));
    assert_eq!(out.status.code(), Some(1), "{out:?}");
    assert_eq!(read(&i.root.join(loader_artifact_name())), "installed loader");
    assert!(String::from_utf8_lossy(&out.stderr).contains("Denice Runtime is missing"));
}

#[test]
fn started_directly_without_a_loader_it_only_runs_the_runtime() {
    let _serial = serialized();
    let i = install();
    let out = run(&i, None);
    assert_eq!(out.status.code(), Some(1), "{out:?}");
    assert_eq!(read(&i.root.join(loader_artifact_name())), "installed loader");
}

#[test]
fn a_version_without_a_carried_loader_keeps_the_old_one_and_still_runs() {
    let _serial = serialized();
    let i = install();
    std::fs::remove_file(i.version_dir.join(loader_artifact_name())).unwrap();
    let out = run(&i, Some(OLDER));
    assert_eq!(out.status.code(), Some(1), "{out:?}");
    let stderr = String::from_utf8_lossy(&out.stderr);
    assert!(stderr.contains("the loader was not replaced"), "{stderr}");
    assert_eq!(read(&i.root.join(loader_artifact_name())), "installed loader");
}

#[test]
fn an_unreadable_loader_version_is_reported_and_the_loader_kept() {
    let _serial = serialized();
    let i = install();
    let out = run(&i, Some("not-a-version"));
    assert_eq!(out.status.code(), Some(1), "{out:?}");
    assert!(String::from_utf8_lossy(&out.stderr).contains("not a Denice version"));
    assert_eq!(read(&i.root.join(loader_artifact_name())), "installed loader");
}
