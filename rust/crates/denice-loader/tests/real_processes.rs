// Copyright (c) Denice.ai. All rights reserved.

//! The real `denice` loader binary driving real child processes. This test program is also the stand-in version:
//! copied into a version folder under the entry's name, it reads `behaviour` from its folder and acts it out
//! (exit code, started marker, installing a newer version). A custom harness (`harness = false`) lets one binary
//! play both roles.

#![allow(clippy::unwrap_used, clippy::panic, clippy::print_stdout, missing_docs)]

use denice_core::layout::InstallLayout;
use denice_core::platform::{entry_file_name, loader_artifact_name};
use denice_core::protocol::{
    BAD_MARKER, ENV_INSTALL_ROOT, ENV_LOADER_VERSION, ENV_VERSION_DIR, EXIT_LOADER_REPLACED,
    EXIT_RESTART_INTO_NEWER_VERSION, STARTED_MARKER, VERSIONS_DIR,
};
use std::path::{Path, PathBuf};
use std::process::Command;

const ROLE: &str = "DENICE_TEST_ROLE_FAKE_VERSION";
const BEHAVIOUR_FILE: &str = "behaviour";
const A: &str = "0.1.100-pre-release+aaaaaaa";
const B: &str = "0.1.200-pre-release+bbbbbbb";

fn main() {
    if std::env::var_os(ROLE).is_some() {
        std::process::exit(act_as_version());
    }
    let tests: [(&str, fn()); 6] = [
        ("runs the highest version with the arguments and returns its code", runs_highest_with_arguments),
        ("passes the protocol environment", passes_protocol_environment),
        ("restarts into a newer version", restarts_into_newer_version),
        ("falls back from a version that fails to start", falls_back_from_failed_start),
        ("hands over to the replaced loader", hands_over_to_replaced_loader),
        ("reports when nothing is installed", reports_nothing_installed),
    ];
    for (name, test) in tests {
        test();
        println!("test {name} ... ok");
    }
    println!("test result: ok. {} passed", tests.len());
}

/// The stand-in version's behaviour, read from `behaviour` in its own folder.
fn act_as_version() -> i32 {
    let me = std::env::current_exe().unwrap();
    let dir = me.parent().unwrap();
    let behaviour = std::fs::read_to_string(dir.join(BEHAVIOUR_FILE)).unwrap();
    let mut words = behaviour.split_whitespace();
    let started = |dir: &Path| std::fs::write(dir.join(STARTED_MARKER), b"").unwrap();
    let args: Vec<String> = std::env::args().skip(1).collect();
    std::fs::write(dir.join("args"), args.join("\n")).unwrap();
    let env = [ENV_INSTALL_ROOT, ENV_LOADER_VERSION, ENV_VERSION_DIR]
        .map(|k| format!("{k}={}", std::env::var(k).unwrap_or_default()))
        .join("\n");
    std::fs::write(dir.join("env"), env).unwrap();
    match words.next() {
        Some("run") => {
            started(dir);
            words.next().unwrap().parse().unwrap()
        }
        Some("fail") => words.next().unwrap().parse().unwrap(),
        Some("install-then-restart") => {
            started(dir);
            let newer = words.next().unwrap();
            let root = PathBuf::from(std::env::var_os(ENV_INSTALL_ROOT).unwrap());
            let _ = install(&root, newer, "run 0");
            EXIT_RESTART_INTO_NEWER_VERSION
        }
        Some("replace-loader") => {
            started(dir);
            // Swap in a loader that is this same test program acting as a version marker writer.
            let root = PathBuf::from(std::env::var_os(ENV_INSTALL_ROOT).unwrap());
            let layout = InstallLayout::new(&root);
            std::fs::rename(layout.loader_path(), root.join(format!("{}.old", loader_artifact_name()))).unwrap();
            let program = layout.loader_program_path();
            std::fs::create_dir_all(program.parent().unwrap()).unwrap();
            let _ = std::fs::copy(&me, &program).unwrap();
            std::fs::write(program.parent().unwrap().join(BEHAVIOUR_FILE), "run 33").unwrap();
            EXIT_LOADER_REPLACED
        }
        other => panic!("unknown behaviour {other:?}"),
    }
}

fn install(root: &Path, version: &str, behaviour: &str) -> PathBuf {
    let dir = root.join(VERSIONS_DIR).join(version);
    std::fs::create_dir_all(&dir).unwrap();
    let _ = std::fs::copy(std::env::current_exe().unwrap(), dir.join(entry_file_name(version))).unwrap();
    std::fs::write(dir.join(BEHAVIOUR_FILE), behaviour).unwrap();
    dir
}

/// A fresh install with the real loader binary at its root.
fn fresh_install() -> (tempfile::TempDir, PathBuf) {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("Denice");
    std::fs::create_dir_all(&root).unwrap();
    let program = InstallLayout::new(&root).loader_program_path();
    std::fs::create_dir_all(program.parent().unwrap()).unwrap();
    let _ = std::fs::copy(env!("CARGO_BIN_EXE_denice"), &program).unwrap();
    (dir, root)
}

fn run_loader(root: &Path, args: &[&str]) -> std::process::Output {
    Command::new(InstallLayout::new(root).loader_program_path()).args(args).env(ROLE, "1").output().unwrap()
}

fn runs_highest_with_arguments() {
    let (_d, root) = fresh_install();
    let _ = install(&root, A, "run 0");
    let b = install(&root, B, "run 7");
    let out = run_loader(&root, &["version", "--two words"]);
    assert_eq!(out.status.code(), Some(7), "{out:?}");
    assert_eq!(std::fs::read_to_string(b.join("args")).unwrap(), "version\n--two words");
    assert!(b.join(STARTED_MARKER).exists());
    assert!(!root.join(VERSIONS_DIR).join(A).exists(), "A is removed once B has started");
}

fn passes_protocol_environment() {
    let (_d, root) = fresh_install();
    let a = install(&root, A, "run 0");
    let out = run_loader(&root, &[]);
    assert_eq!(out.status.code(), Some(0), "{out:?}");
    let env = std::fs::read_to_string(a.join("env")).unwrap();
    assert!(env.contains(&format!("{ENV_INSTALL_ROOT}={}", root.display())), "{env}");
    assert!(env.contains(&format!("{ENV_VERSION_DIR}={}", a.display())), "{env}");
    assert!(env.contains(&format!("{ENV_LOADER_VERSION}=")), "{env}");
}

fn restarts_into_newer_version() {
    let (_d, root) = fresh_install();
    let _ = install(&root, A, &format!("install-then-restart {B}"));
    let out = run_loader(&root, &[]);
    assert_eq!(out.status.code(), Some(0), "{out:?}");
    assert!(root.join(VERSIONS_DIR).join(B).join(STARTED_MARKER).exists());
    assert!(!root.join(VERSIONS_DIR).join(A).exists());
}

fn falls_back_from_failed_start() {
    let (_d, root) = fresh_install();
    let _ = install(&root, A, "run 0");
    let b = install(&root, B, "fail 3");
    let out = run_loader(&root, &[]);
    assert_eq!(out.status.code(), Some(0), "{out:?}");
    assert!(b.join(BAD_MARKER).exists());
    assert!(String::from_utf8_lossy(&out.stderr).contains("failed to start"));
}

fn hands_over_to_replaced_loader() {
    let (_d, root) = fresh_install();
    let _ = install(&root, A, "replace-loader");
    // After the swap the "new loader" is this test program; it reads `behaviour` beside itself and exits 33.
    let out = run_loader(&root, &[]);
    assert_eq!(out.status.code(), Some(33), "the old loader handed over to the new one at once: {out:?}");
}

fn reports_nothing_installed() {
    let (_d, root) = fresh_install();
    let out = run_loader(&root, &[]);
    assert_eq!(out.status.code(), Some(denice_loader::EXIT_NO_USABLE_VERSION));
    assert!(String::from_utf8_lossy(&out.stderr).contains("no usable Denice version"));
}
