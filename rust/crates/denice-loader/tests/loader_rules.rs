// Copyright (c) Denice.ai. All rights reserved.

//! The loader's rules, with a stand-in for real processes: each installed version is scripted to behave a certain
//! way, and the test checks which versions the loader started, in which order, and what it returned.

#![allow(clippy::unwrap_used, missing_docs)]

use denice_core::layout::InstallLayout;
use denice_core::platform::{entry_file_name, loader_artifact_name};
use denice_core::protocol::{
    BAD_MARKER, ENV_INSTALL_ROOT, ENV_LOADER_PATH, ENV_LOADER_VERSION, ENV_VERSION_DIR, EXIT_LOADER_REPLACED,
    EXIT_RESTART_INTO_NEWER_VERSION, STARTED_MARKER, VERSIONS_DIR,
};
use denice_loader::{EXIT_NO_USABLE_VERSION, Launcher, Loader};
use std::cell::RefCell;
use std::collections::HashMap;
use std::ffi::OsString;
use std::path::{Path, PathBuf};

#[derive(Clone)]
enum Behaves {
    /// Starts (writes the started marker) and exits with the code.
    Runs(i32),
    /// Exits with the code before starting.
    FailsToStart(i32),
    /// Starts, installs `version`, and asks for a restart.
    InstallsThenRestarts(&'static str),
    /// Starts and asks for a restart without installing anything newer.
    RestartsWithoutNewer,
    /// Starts and reports that it replaced the loader.
    ReplacesLoader,
    /// Cannot be launched at all.
    CannotLaunch,
}

struct Scripted {
    root: PathBuf,
    script: HashMap<String, Behaves>,
    started: RefCell<Vec<String>>,
    envs: RefCell<Vec<Vec<(&'static str, OsString)>>>,
    handed_over: RefCell<Option<PathBuf>>,
    hand_over_fails: bool,
    reports: RefCell<Vec<String>>,
}

impl Scripted {
    fn version_of(program: &Path) -> String {
        program.parent().unwrap().file_name().unwrap().to_string_lossy().into_owned()
    }
}

impl Launcher for Scripted {
    fn run(&self, program: &Path, _args: &[OsString], env: &[(&'static str, OsString)]) -> std::io::Result<i32> {
        let version = Self::version_of(program);
        self.started.borrow_mut().push(version.clone());
        self.envs.borrow_mut().push(env.to_vec());
        let dir = program.parent().unwrap();
        let mark_started = || std::fs::write(dir.join(STARTED_MARKER), b"").unwrap();
        match self.script[&version].clone() {
            Behaves::Runs(code) => {
                mark_started();
                Ok(code)
            }
            Behaves::FailsToStart(code) => Ok(code),
            Behaves::InstallsThenRestarts(newer) => {
                mark_started();
                let _ = install(&self.root, newer);
                Ok(EXIT_RESTART_INTO_NEWER_VERSION)
            }
            Behaves::RestartsWithoutNewer => {
                mark_started();
                Ok(EXIT_RESTART_INTO_NEWER_VERSION)
            }
            Behaves::ReplacesLoader => {
                mark_started();
                Ok(EXIT_LOADER_REPLACED)
            }
            Behaves::CannotLaunch => Err(std::io::Error::other("cannot launch")),
        }
    }

    fn hand_over(&self, loader: &Path, _args: &[OsString]) -> std::io::Result<i32> {
        *self.handed_over.borrow_mut() = Some(loader.to_path_buf());
        if self.hand_over_fails {
            return Err(std::io::Error::other("cannot start the new loader"));
        }
        Ok(42)
    }

    fn report(&self, message: &str) {
        self.reports.borrow_mut().push(message.to_owned());
    }
}

fn install(root: &Path, version: &str) -> PathBuf {
    let dir = root.join(VERSIONS_DIR).join(version);
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(dir.join(entry_file_name(version)), b"entry").unwrap();
    dir
}

struct Case {
    _dir: tempfile::TempDir,
    launcher: Scripted,
}

fn case(installed: &[&'static str], script: &[(&'static str, Behaves)]) -> Case {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("Denice");
    std::fs::create_dir_all(&root).unwrap();
    for version in installed {
        let _ = install(&root, version);
    }
    let launcher = Scripted {
        root,
        script: script.iter().map(|(v, b)| ((*v).to_owned(), b.clone())).collect(),
        started: RefCell::new(Vec::new()),
        envs: RefCell::new(Vec::new()),
        handed_over: RefCell::new(None),
        hand_over_fails: false,
        reports: RefCell::new(Vec::new()),
    };
    Case { _dir: dir, launcher }
}

fn run(case: &Case) -> i32 {
    let loader = Loader {
        layout: InstallLayout::new(&case.launcher.root),
        loader_path: InstallLayout::new(&case.launcher.root).loader_program_path(),
        loader_version: "0.1.100-pre-release+aaaaaaa".parse().unwrap(),
        launcher: &case.launcher,
    };
    loader.run(&[OsString::from("version")])
}

const A: &str = "0.1.100-pre-release+aaaaaaa";
const B: &str = "0.1.200-pre-release+bbbbbbb";

#[test]
fn starts_the_highest_known_version_and_returns_its_exit_code() {
    let c = case(&[A, B], &[(A, Behaves::Runs(0)), (B, Behaves::Runs(7))]);
    assert_eq!(run(&c), 7);
    assert_eq!(*c.launcher.started.borrow(), [B]);
}

#[test]
fn passes_the_protocol_environment_to_the_version() {
    let c = case(&[A], &[(A, Behaves::Runs(0))]);
    let _ = run(&c);
    let env = &c.launcher.envs.borrow()[0];
    let get = |key: &str| env.iter().find(|(k, _)| *k == key).map(|(_, v)| v.clone()).unwrap();
    assert_eq!(get(ENV_INSTALL_ROOT), c.launcher.root.as_os_str());
    assert_eq!(get(ENV_LOADER_PATH), InstallLayout::new(&c.launcher.root).loader_program_path().as_os_str());
    assert_eq!(get(ENV_LOADER_VERSION), "0.1.100-pre-release+aaaaaaa");
    assert_eq!(get(ENV_VERSION_DIR), c.launcher.root.join(VERSIONS_DIR).join(A).as_os_str());
}

#[test]
fn reloads_into_a_newer_version_when_asked() {
    let c = case(&[A], &[(A, Behaves::InstallsThenRestarts(B)), (B, Behaves::Runs(0))]);
    assert_eq!(run(&c), 0);
    assert_eq!(*c.launcher.started.borrow(), [A, B]);
}

#[test]
fn the_previous_version_is_kept_until_the_new_one_has_started_then_removed() {
    let c = case(&[A, B], &[(A, Behaves::Runs(0)), (B, Behaves::FailsToStart(3))]);
    let _ = run(&c);
    let versions = c.launcher.root.join(VERSIONS_DIR);
    assert!(versions.join(A).exists(), "A is kept: B never started");

    let c = case(&[A], &[(A, Behaves::InstallsThenRestarts(B)), (B, Behaves::Runs(0))]);
    assert_eq!(run(&c), 0);
    let versions = c.launcher.root.join(VERSIONS_DIR);
    assert!(!versions.join(A).exists(), "A is removed once B has started");
    assert!(versions.join(B).exists());
}

#[test]
fn a_restart_without_a_newer_version_stops_instead_of_looping() {
    let c = case(&[A], &[(A, Behaves::RestartsWithoutNewer)]);
    assert_eq!(run(&c), EXIT_RESTART_INTO_NEWER_VERSION);
    assert_eq!(*c.launcher.started.borrow(), [A]);
    assert!(c.launcher.reports.borrow().iter().any(|r| r.contains("none is installed")));
}

#[test]
fn falls_back_to_the_previous_version_when_the_new_one_fails_to_start() {
    let c = case(&[A, B], &[(A, Behaves::Runs(0)), (B, Behaves::FailsToStart(3))]);
    assert_eq!(run(&c), 0);
    assert_eq!(*c.launcher.started.borrow(), [B, A]);
    assert!(c.launcher.root.join(VERSIONS_DIR).join(B).join(BAD_MARKER).exists());
    // The failed version is never chosen again.
    assert_eq!(run(&c), 0);
    assert_eq!(*c.launcher.started.borrow(), [B, A, A]);
}

#[test]
fn falls_back_when_the_new_version_cannot_even_be_launched() {
    let c = case(&[A, B], &[(A, Behaves::Runs(0)), (B, Behaves::CannotLaunch)]);
    assert_eq!(run(&c), 0);
    assert_eq!(*c.launcher.started.borrow(), [B, A]);
}

#[test]
fn a_version_that_started_and_then_failed_is_not_abandoned() {
    let c = case(&[A, B], &[(A, Behaves::Runs(0)), (B, Behaves::Runs(9))]);
    assert_eq!(run(&c), 9);
    assert_eq!(*c.launcher.started.borrow(), [B]);
    assert!(!c.launcher.root.join(VERSIONS_DIR).join(B).join(BAD_MARKER).exists());
}

#[test]
fn when_every_version_fails_to_start_the_last_failure_is_returned() {
    let c = case(&[A, B], &[(A, Behaves::FailsToStart(4)), (B, Behaves::FailsToStart(3))]);
    assert_eq!(run(&c), 4);
    assert_eq!(*c.launcher.started.borrow(), [B, A]);
}

#[test]
fn with_nothing_installed_it_says_so() {
    let c = case(&[], &[]);
    assert_eq!(run(&c), EXIT_NO_USABLE_VERSION);
    assert!(c.launcher.reports.borrow().iter().any(|r| r.contains("no usable Denice version")));
}

#[test]
fn hands_over_to_the_installed_loader_right_after_a_swap() {
    let c = case(&[B], &[(B, Behaves::ReplacesLoader)]);
    assert_eq!(run(&c), 42);
    assert_eq!(*c.launcher.handed_over.borrow(), Some(InstallLayout::new(&c.launcher.root).loader_program_path()));
}

#[test]
fn the_previous_loader_is_deleted_on_start() {
    let c = case(&[A], &[(A, Behaves::Runs(0))]);
    let old = c.launcher.root.join(format!("{}.old", loader_artifact_name()));
    std::fs::write(&old, b"old loader").unwrap();
    let _ = run(&c);
    assert!(!old.exists());
}

#[test]
fn a_hand_over_that_cannot_start_the_new_loader_is_reported() {
    let mut c = case(&[B], &[(B, Behaves::ReplacesLoader)]);
    c.launcher.hand_over_fails = true;
    assert_eq!(run(&c), EXIT_NO_USABLE_VERSION);
    assert!(c.launcher.reports.borrow().iter().any(|r| r.contains("cannot start the new loader")));
}

#[test]
fn an_unreadable_versions_folder_is_reported() {
    let c = case(&[], &[]);
    std::fs::write(c.launcher.root.join(VERSIONS_DIR), b"not a folder").unwrap();
    assert_eq!(run(&c), EXIT_NO_USABLE_VERSION);
    assert!(c.launcher.reports.borrow().iter().any(|r| r.contains("cannot read")));
}
