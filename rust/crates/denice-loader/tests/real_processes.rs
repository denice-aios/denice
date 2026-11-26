// Copyright (c) Denice.ai. All rights reserved.

//! The real `denice` loader binary driving real child processes. This test program is also the stand-in version:
//! copied into a version folder under the entry's name, it reads `behaviour` from its folder and acts it out
//! (exit code, started marker, installing a newer version). A custom harness (`harness = false`) lets one binary
//! play both roles.

#![allow(clippy::unwrap_used, clippy::panic, clippy::print_stdout, missing_docs)]

use denice_core::layout::InstallLayout;
use denice_core::platform::{data_root_for, entry_file_name, loader_artifact_of};
use denice_core::protocol::{
    BAD_MARKER, ENV_INSTALL_ROOT, ENV_LOADER_PATH, ENV_LOADER_VERSION, ENV_VERSION_DIR, EXIT_LOADER_REPLACED,
    EXIT_RESTART_INTO_NEWER_VERSION, STARTED_MARKER, VERSIONS_DIR,
};
use std::path::{Path, PathBuf};
use std::process::Command;

const ROLE: &str = "DENICE_TEST_ROLE_FAKE_VERSION";
const BEHAVIOUR_FILE: &str = "behaviour";
/// A waiting stand-in version writes `ready.<pid>` once it runs, and exits when `go` appears beside it.
const READY: &str = "ready.";
const GO: &str = "go";
const A: &str = "0.1.100-pre-release+aaaaaaa";
const B: &str = "0.1.200-pre-release+bbbbbbb";

fn main() {
    if std::env::var_os(ROLE).is_some() {
        std::process::exit(act_as_version());
    }
    #[cfg_attr(not(unix), allow(unused_mut))]
    let mut tests: Vec<(&str, fn())> = vec![
        ("runs the highest version with the arguments and returns its code", runs_highest_with_arguments),
        ("passes the protocol environment", passes_protocol_environment),
        ("restarts into a newer version", restarts_into_newer_version),
        ("falls back from a version that fails to start", falls_back_from_failed_start),
        ("hands over to the replaced loader", hands_over_to_replaced_loader),
        ("reports when nothing is installed", reports_nothing_installed),
        ("two Denice run one version at once", two_denice_run_one_version_at_once),
        ("keeps a version another Denice still runs", keeps_a_version_another_denice_runs),
    ];
    // macOS and Linux users start Denice through the ~/.local/bin/denice link; Windows users through a Start Menu
    // shortcut, which runs denice.exe itself.
    #[cfg(unix)]
    tests.push(("finds its install when started through a link", started_through_a_link));
    #[cfg(target_os = "macos")]
    tests.push(("removes external library overrides from the version process", removes_library_override));
    let count = tests.len();
    for (name, test) in tests {
        test();
        println!("test {name} ... ok");
    }
    println!("test result: ok. {count} passed");
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
    std::fs::write(dir.join("library-override"), std::env::var("DYLD_LIBRARY_PATH").unwrap_or_default()).unwrap();
    match words.next() {
        Some("run") => {
            started(dir);
            words.next().unwrap().parse().unwrap()
        }
        Some("fail") => words.next().unwrap().parse().unwrap(),
        Some("wait") => {
            // Starts, says so, and runs until the test creates `go` in its folder.
            started(dir);
            std::fs::write(dir.join(format!("{READY}{}", std::process::id())), b"").unwrap();
            until(|| dir.join(GO).exists());
            words.next().unwrap().parse().unwrap()
        }
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
            let running = PathBuf::from(std::env::var_os(ENV_LOADER_PATH).unwrap());
            let artifact = loader_artifact_of(&running).unwrap();
            let layout =
                InstallLayout::with_loader(PathBuf::from(std::env::var_os(ENV_INSTALL_ROOT).unwrap()), &artifact);
            std::fs::rename(layout.loader_path(), layout.loader_backup_path()).unwrap();
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

/// A fresh install: the real loader program in an app folder, and the folder that holds `versions/` where the loader
/// looks for it (on macOS `~/Library/Application Support/Denice` under a temporary home; elsewhere the app folder).
struct Install {
    _dir: tempfile::TempDir,
    root: PathBuf,
    home: PathBuf,
    program: PathBuf,
}

fn fresh_install() -> Install {
    let dir = tempfile::tempdir().unwrap();
    let home = dir.path().join("home");
    let program = InstallLayout::new(dir.path().join("apps")).loader_program_path();
    std::fs::create_dir_all(program.parent().unwrap()).unwrap();
    let _ = std::fs::copy(env!("CARGO_BIN_EXE_denice"), &program).unwrap();
    let root = data_root_for(&program, Some(&home)).unwrap();
    std::fs::create_dir_all(&root).unwrap();
    Install { _dir: dir, root, home, program }
}

fn run_loader(install: &Install, args: &[&str]) -> std::process::Output {
    Command::new(&install.program).args(args).env(ROLE, "1").env("HOME", &install.home).output().unwrap()
}

fn runs_highest_with_arguments() {
    let i = fresh_install();
    let root = i.root.clone();
    let _ = install(&root, A, "run 0");
    let b = install(&root, B, "run 7");
    let out = run_loader(&i, &["version", "--two words"]);
    assert_eq!(out.status.code(), Some(7), "{out:?}");
    assert_eq!(std::fs::read_to_string(b.join("args")).unwrap(), "version\n--two words");
    assert!(b.join(STARTED_MARKER).exists());
    assert!(!root.join(VERSIONS_DIR).join(A).exists(), "A is removed once B has started");
}

fn passes_protocol_environment() {
    let i = fresh_install();
    let root = i.root.clone();
    let a = install(&root, A, "run 0");
    let out = run_loader(&i, &[]);
    assert_eq!(out.status.code(), Some(0), "{out:?}");
    let env = std::fs::read_to_string(a.join("env")).unwrap();
    assert!(env.contains(&format!("{ENV_INSTALL_ROOT}={}", root.display())), "{env}");
    assert!(env.contains(&format!("{ENV_VERSION_DIR}={}", a.display())), "{env}");
    assert!(env.contains(&format!("{ENV_LOADER_VERSION}=")), "{env}");
}

#[cfg(target_os = "macos")]
fn removes_library_override() {
    let i = fresh_install();
    let a = install(&i.root, A, "run 0");
    let output = Command::new(&i.program)
        .env(ROLE, "1")
        .env("HOME", &i.home)
        .env("DYLD_LIBRARY_PATH", "/usr/local/lib")
        .output()
        .unwrap();
    assert_eq!(output.status.code(), Some(0), "{output:?}");
    assert_eq!(std::fs::read_to_string(a.join("library-override")).unwrap(), "");
}

fn restarts_into_newer_version() {
    let i = fresh_install();
    let root = i.root.clone();
    let _ = install(&root, A, &format!("install-then-restart {B}"));
    let out = run_loader(&i, &[]);
    assert_eq!(out.status.code(), Some(0), "{out:?}");
    assert!(root.join(VERSIONS_DIR).join(B).join(STARTED_MARKER).exists());
    assert!(!root.join(VERSIONS_DIR).join(A).exists());
}

fn falls_back_from_failed_start() {
    let i = fresh_install();
    let root = i.root.clone();
    let _ = install(&root, A, "run 0");
    let b = install(&root, B, "fail 3");
    let out = run_loader(&i, &[]);
    assert_eq!(out.status.code(), Some(0), "{out:?}");
    assert!(b.join(BAD_MARKER).exists());
    assert!(String::from_utf8_lossy(&out.stderr).contains("failed to start"));
}

fn hands_over_to_replaced_loader() {
    let i = fresh_install();
    let root = i.root.clone();
    let _ = install(&root, A, "replace-loader");
    // After the swap the "new loader" is this test program; it reads `behaviour` beside itself and exits 33.
    let out = run_loader(&i, &[]);
    assert_eq!(out.status.code(), Some(33), "the old loader handed over to the new one at once: {out:?}");
}

fn reports_nothing_installed() {
    let i = fresh_install();
    let out = run_loader(&i, &[]);
    assert_eq!(out.status.code(), Some(denice_loader::EXIT_NO_USABLE_VERSION));
    assert!(String::from_utf8_lossy(&out.stderr).contains("no usable Denice version"));
}

#[cfg(unix)]
fn started_through_a_link() {
    let i = fresh_install();
    let _ = install(&i.root, A, "run 5");
    let link = i.home.join(".local").join("bin").join("denice");
    std::fs::create_dir_all(link.parent().unwrap()).unwrap();
    std::os::unix::fs::symlink(&i.program, &link).unwrap();
    let out = Command::new(&link).env(ROLE, "1").env("HOME", &i.home).output().unwrap();
    assert_eq!(out.status.code(), Some(5), "the loader started through a link found its versions: {out:?}");
}

/// Waits up to a minute for `condition`.
fn until(condition: impl Fn() -> bool) {
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(60);
    while !condition() {
        assert!(std::time::Instant::now() < deadline, "timed out");
        std::thread::sleep(std::time::Duration::from_millis(20));
    }
}

fn running_copies(dir: &Path) -> usize {
    std::fs::read_dir(dir)
        .unwrap()
        .filter(|e| e.as_ref().unwrap().file_name().to_string_lossy().starts_with(READY))
        .count()
}

fn start_loader(install: &Install) -> std::process::Child {
    Command::new(&install.program).env(ROLE, "1").env("HOME", &install.home).spawn().unwrap()
}

fn two_denice_run_one_version_at_once() {
    let i = fresh_install();
    let a = install(&i.root, A, "run 0");
    let b = install(&i.root, B, "wait 0");
    let first = start_loader(&i);
    let second = start_loader(&i);
    until(|| running_copies(&b) == 2);
    assert!(!b.join(BAD_MARKER).exists(), "neither marks the version the other runs as bad");
    std::fs::write(b.join(GO), b"").unwrap();
    for mut loader in [first, second] {
        assert_eq!(loader.wait().unwrap().code(), Some(0));
    }
    assert!(b.join(STARTED_MARKER).exists() && !b.join(BAD_MARKER).exists(), "the version is kept and not bad");
    assert!(!a.exists(), "the older version is removed once the shared version has started");
}

fn keeps_a_version_another_denice_runs() {
    let i = fresh_install();
    let a = install(&i.root, A, "wait 0");
    let mut first = start_loader(&i);
    until(|| running_copies(&a) == 1);
    // A newer version arrives and a second Denice starts it while the first still runs the older one.
    let _ = install(&i.root, B, "run 0");
    assert_eq!(run_loader(&i, &[]).status.code(), Some(0));
    assert!(a.join(entry_file_name(A)).is_file(), "the version another Denice runs is kept whole");
    std::fs::write(a.join(GO), b"").unwrap();
    assert_eq!(first.wait().unwrap().code(), Some(0));
    assert_eq!(run_loader(&i, &[]).status.code(), Some(0));
    assert!(!a.exists(), "a later start removes it once nobody runs it");
}
