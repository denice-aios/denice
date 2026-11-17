// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `layout` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::*;
use crate::platform::entry_file_name;

fn install(root: &Path, version: &str) -> PathBuf {
    let dir = root.join(VERSIONS_DIR).join(version);
    std::fs::create_dir_all(&dir).unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(dir.join(entry_file_name(version)), b"entry").unwrap_or_else(|e| unreachable!("{e}"));
    dir
}

fn temp() -> tempfile::TempDir {
    tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"))
}

fn names(layout: &InstallLayout) -> Vec<String> {
    layout
        .usable_versions()
        .unwrap_or_else(|e| unreachable!("{e}"))
        .into_iter()
        .map(|v| v.version.to_string())
        .collect()
}

#[test]
fn no_versions_folder_means_no_versions() {
    let root = temp();
    assert!(InstallLayout::new(root.path()).highest_usable_version().unwrap_or_else(|e| unreachable!("{e}")).is_none());
}

#[test]
fn highest_complete_version_is_chosen() {
    let root = temp();
    let _ = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    let _ = install(root.path(), "0.1.300-pre-release+ccccccc");
    let _ = install(root.path(), "0.1.200-pre-release+bbbbbbb");
    assert_eq!(
        names(&InstallLayout::new(root.path())),
        ["0.1.300-pre-release+ccccccc", "0.1.200-pre-release+bbbbbbb", "0.1.100-pre-release+aaaaaaa"]
    );
}

#[test]
fn partial_downloads_are_never_chosen() {
    let root = temp();
    let _ = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    let partial = root.path().join(VERSIONS_DIR).join("0.1.900-pre-release+fffffff.partial");
    std::fs::create_dir_all(&partial).unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(names(&InstallLayout::new(root.path())), ["0.1.100-pre-release+aaaaaaa"]);
}

#[test]
fn a_folder_without_its_entry_is_not_complete() {
    let root = temp();
    let _ = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    std::fs::create_dir_all(root.path().join(VERSIONS_DIR).join("0.1.200-pre-release+bbbbbbb"))
        .unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(names(&InstallLayout::new(root.path())), ["0.1.100-pre-release+aaaaaaa"]);
}

#[test]
fn bad_versions_are_skipped() {
    let root = temp();
    let _ = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    let _ = install(root.path(), "0.1.200-pre-release+bbbbbbb");
    let layout = InstallLayout::new(root.path());
    let highest =
        layout.highest_usable_version().unwrap_or_else(|e| unreachable!("{e}")).unwrap_or_else(|| unreachable!());
    highest.mark_bad().unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(names(&layout), ["0.1.100-pre-release+aaaaaaa"]);
}

#[test]
fn older_versions_stay_until_the_new_one_has_started() {
    let root = temp();
    let old = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    let _ = install(root.path(), "0.1.200-pre-release+bbbbbbb");
    let layout = InstallLayout::new(root.path());
    let current =
        layout.highest_usable_version().unwrap_or_else(|e| unreachable!("{e}")).unwrap_or_else(|| unreachable!());
    assert_eq!(
        layout.remove_versions_older_than(&current).unwrap_or_else(|e| unreachable!("{e}")),
        [] as [std::path::PathBuf; 0]
    );
    assert!(old.is_dir());

    std::fs::write(current.dir.join(STARTED_MARKER), b"").unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(
        layout.remove_versions_older_than(&current).unwrap_or_else(|e| unreachable!("{e}")),
        std::slice::from_ref(&old)
    );
    assert!(!old.exists());
    assert_eq!(names(&layout), ["0.1.200-pre-release+bbbbbbb"]);
}

#[test]
fn loader_swap_paths_follow_tims_names() {
    let layout = InstallLayout::new("/install");
    let loader = platform::loader_artifact_name();
    assert_eq!(layout.loader_path(), Path::new("/install").join(&loader));
    assert_eq!(layout.loader_staging_path(), Path::new("/install").join(format!("{loader}.ver")));
    assert_eq!(layout.loader_backup_path(), Path::new("/install").join(format!("{loader}.old")));
}

#[test]
fn a_version_another_denice_is_running_is_not_removed_until_it_stops() {
    let root = temp();
    let old = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    let _ = install(root.path(), "0.1.200-pre-release+bbbbbbb");
    let layout = InstallLayout::new(root.path());
    let versions = layout.usable_versions().unwrap_or_else(|e| unreachable!("{e}"));
    let (current, older) = (&versions[0], &versions[1]);
    std::fs::write(current.dir.join(STARTED_MARKER), b"").unwrap_or_else(|e| unreachable!("{e}"));

    let held = older.hold_running().unwrap_or_else(|e| unreachable!("{e}"));
    assert!(older.is_running());
    assert_eq!(
        layout.remove_versions_older_than(current).unwrap_or_else(|e| unreachable!("{e}")),
        Vec::<PathBuf>::new()
    );
    assert!(old.is_dir(), "a running version is kept");

    drop(held);
    assert!(!older.is_running());
    assert_eq!(
        layout.remove_versions_older_than(current).unwrap_or_else(|e| unreachable!("{e}")),
        std::slice::from_ref(&old)
    );
}

#[test]
fn several_processes_may_run_one_version_at_once() {
    let root = temp();
    let _ = install(root.path(), "0.1.100-pre-release+aaaaaaa");
    let version = InstallLayout::new(root.path())
        .highest_usable_version()
        .unwrap_or_else(|e| unreachable!("{e}"))
        .unwrap_or_else(|| unreachable!());
    let first = version.hold_running().unwrap_or_else(|e| unreachable!("{e}"));
    let second = version.hold_running().unwrap_or_else(|e| unreachable!("{e}"));
    assert!(version.is_running());
    drop(first);
    assert!(version.is_running(), "still running in the second process");
    drop(second);
    assert!(!version.is_running());
}
