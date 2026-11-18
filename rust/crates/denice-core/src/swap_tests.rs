// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `swap` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::*;

struct Fixture {
    _dir: tempfile::TempDir,
    layout: InstallLayout,
    new_loader: PathBuf,
}

fn fixture() -> Fixture {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    let layout = InstallLayout::new(dir.path().join("install"));
    std::fs::create_dir_all(layout.root()).unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(layout.loader_path(), b"old loader").unwrap_or_else(|e| unreachable!("{e}"));
    let new_loader = dir.path().join("carried-denice");
    std::fs::write(&new_loader, b"new loader").unwrap_or_else(|e| unreachable!("{e}"));
    Fixture { _dir: dir, layout, new_loader }
}

fn read(path: &Path) -> String {
    std::fs::read_to_string(path).unwrap_or_else(|e| unreachable!("{}: {e}", path.display()))
}

#[test]
fn swap_follows_tims_order_and_keeps_the_old_loader_as_old() {
    let f = fixture();
    swap_in_loader(&f.layout, &f.new_loader).unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(read(&f.layout.loader_path()), "new loader");
    assert_eq!(read(&f.layout.loader_backup_path()), "old loader");
    assert!(!f.layout.loader_staging_path().exists());
}

#[test]
fn a_failed_unpack_leaves_the_current_loader_untouched() {
    let f = fixture();
    let missing = f.layout.root().join("no-such-loader");
    let err = swap_in_loader(&f.layout, &missing).err().unwrap_or_else(|| unreachable!());
    assert_eq!(err.step, SwapStep::Unpack);
    assert_eq!(read(&f.layout.loader_path()), "old loader");
    assert!(!f.layout.loader_staging_path().exists());
    assert!(!f.layout.loader_backup_path().exists());
}

/// Real file operations, except that renaming onto `fail_target` fails.
struct FailRenameOnto(PathBuf);

impl SwapFiles for FailRenameOnto {
    fn copy_tree(&self, from: &Path, to: &Path) -> io::Result<()> {
        RealFiles.copy_tree(from, to)
    }

    fn rename(&self, from: &Path, to: &Path) -> io::Result<()> {
        if to == self.0 {
            return Err(io::Error::other("injected rename failure"));
        }
        RealFiles.rename(from, to)
    }

    fn remove_if_present(&self, path: &Path) -> io::Result<()> {
        RealFiles.remove_if_present(path)
    }
}

/// Real file operations, except that the first rename onto `.1` fails.
struct FailFirstActivation(std::cell::Cell<bool>, PathBuf);
impl SwapFiles for FailFirstActivation {
    fn copy_tree(&self, from: &Path, to: &Path) -> io::Result<()> {
        RealFiles.copy_tree(from, to)
    }
    fn rename(&self, from: &Path, to: &Path) -> io::Result<()> {
        if to == self.1 && !self.0.replace(true) {
            return Err(io::Error::other("injected activation failure"));
        }
        RealFiles.rename(from, to)
    }
    fn remove_if_present(&self, path: &Path) -> io::Result<()> {
        RealFiles.remove_if_present(path)
    }
}

#[test]
fn a_failed_activation_puts_the_old_loader_back() {
    let f = fixture();
    // Fail only step 3 (`denice.ver` -> `denice`); the restore (`denice.old` -> `denice`) must still work,
    // so fail the first rename onto `denice` and let the second through.
    let files = FailFirstActivation(std::cell::Cell::new(false), f.layout.loader_path());
    let err = swap_with(&files, &f.layout, &f.new_loader).err().unwrap_or_else(|| unreachable!());
    assert_eq!(err.step, SwapStep::Activate);
    assert_eq!(read(&f.layout.loader_path()), "old loader");
    assert!(!f.layout.loader_staging_path().exists());
    assert!(!f.layout.loader_backup_path().exists());
}

#[test]
fn a_failed_backup_leaves_the_current_loader_and_no_staging() {
    let f = fixture();
    let files = FailRenameOnto(f.layout.loader_backup_path());
    let err = swap_with(&files, &f.layout, &f.new_loader).err().unwrap_or_else(|| unreachable!());
    assert_eq!(err.step, SwapStep::BackUpCurrent);
    assert_eq!(read(&f.layout.loader_path()), "old loader");
    assert!(!f.layout.loader_staging_path().exists());
}

#[test]
fn leftovers_from_an_interrupted_swap_are_cleared_first() {
    let f = fixture();
    std::fs::write(f.layout.loader_staging_path(), b"stale staging").unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(f.layout.loader_backup_path(), b"stale backup").unwrap_or_else(|e| unreachable!("{e}"));
    swap_in_loader(&f.layout, &f.new_loader).unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(read(&f.layout.loader_path()), "new loader");
    assert_eq!(read(&f.layout.loader_backup_path()), "old loader");
}

#[test]
fn a_bundle_folder_is_swapped_as_a_whole() {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    let layout = InstallLayout::new(dir.path().join("install"));
    std::fs::create_dir_all(layout.root()).unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(layout.loader_path(), b"old loader").unwrap_or_else(|e| unreachable!("{e}"));
    let bundle = dir.path().join("carried");
    std::fs::create_dir_all(bundle.join("Contents/MacOS")).unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(bundle.join("Contents/MacOS/denice"), b"bundled").unwrap_or_else(|e| unreachable!("{e}"));
    swap_in_loader(&layout, &bundle).unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(read(&layout.loader_path().join("Contents/MacOS/denice")), "bundled");
}

/// Real file operations, except that removing anything fails.
struct FailRemove;

impl SwapFiles for FailRemove {
    fn copy_tree(&self, from: &Path, to: &Path) -> io::Result<()> {
        RealFiles.copy_tree(from, to)
    }

    fn rename(&self, from: &Path, to: &Path) -> io::Result<()> {
        RealFiles.rename(from, to)
    }

    fn remove_if_present(&self, _path: &Path) -> io::Result<()> {
        Err(io::Error::other("injected remove failure"))
    }
}

#[test]
fn leftovers_that_cannot_be_cleared_stop_the_swap_before_anything_changes() {
    let f = fixture();
    let err = swap_with(&FailRemove, &f.layout, &f.new_loader).err().unwrap_or_else(|| unreachable!());
    assert_eq!(err.step, SwapStep::ClearLeftovers);
    assert_eq!(read(&f.layout.loader_path()), "old loader");
    assert!(err.to_string().contains("ClearLeftovers"), "{err}");
    assert!(std::error::Error::source(&err).is_some());
}

#[test]
fn leftover_folders_from_an_interrupted_bundle_swap_are_cleared() {
    let f = fixture();
    std::fs::create_dir_all(f.layout.loader_staging_path().join("Contents")).unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::create_dir_all(f.layout.loader_backup_path().join("Contents")).unwrap_or_else(|e| unreachable!("{e}"));
    swap_in_loader(&f.layout, &f.new_loader).unwrap_or_else(|e| unreachable!("{e}"));
    assert_eq!(read(&f.layout.loader_path()), "new loader");
}

/// Real file operations, except that copying writes part of the staging file and then fails.
struct FailCopyHalfway;

impl SwapFiles for FailCopyHalfway {
    fn copy_tree(&self, _from: &Path, to: &Path) -> io::Result<()> {
        std::fs::write(to, b"half a loader")?;
        Err(io::Error::other("injected copy failure"))
    }

    fn rename(&self, from: &Path, to: &Path) -> io::Result<()> {
        RealFiles.rename(from, to)
    }

    fn remove_if_present(&self, path: &Path) -> io::Result<()> {
        RealFiles.remove_if_present(path)
    }
}

#[test]
fn a_failed_unpack_removes_the_half_written_staging_file() {
    let f = fixture();
    let err = swap_with(&FailCopyHalfway, &f.layout, &f.new_loader).err().unwrap_or_else(|| unreachable!());
    assert_eq!(err.step, SwapStep::Unpack);
    assert_eq!(read(&f.layout.loader_path()), "old loader");
    assert!(!f.layout.loader_staging_path().exists());
}

#[test]
fn previous_loader_is_removed_once_it_is_not_running() {
    let f = fixture();
    swap_in_loader(&f.layout, &f.new_loader).unwrap_or_else(|e| unreachable!("{e}"));
    assert!(remove_previous_loader(&f.layout));
    assert!(!f.layout.loader_backup_path().exists());
    assert!(!remove_previous_loader(&f.layout));
}

#[test]
fn a_target_that_is_not_a_loader_is_never_touched() {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    let precious = dir.path().join("T");
    std::fs::create_dir_all(precious.join("keep")).unwrap_or_else(|e| unreachable!("{e}"));
    let new_loader = dir.path().join("carried");
    std::fs::write(&new_loader, b"carried loader").unwrap_or_else(|e| unreachable!("{e}"));

    let err = swap_in_loader(&InstallLayout::with_loader(dir.path(), &precious), &new_loader)
        .err()
        .unwrap_or_else(|| unreachable!());

    assert_eq!(err.step, SwapStep::NotALoader);
    assert!(precious.join("keep").is_dir());
    assert!(!dir.path().join("T.old").exists());
    assert!(!dir.path().join("T.ver").exists());
}

/// How long the race tests give a waiting thread to (wrongly) get past the lock before checking it did not.
const WHILE_HELD: std::time::Duration = std::time::Duration::from_millis(300);

#[test]
fn two_denice_swapping_at_once_leave_one_new_loader_and_the_original_as_old() {
    let f = fixture();
    // Both swaps start while the lock is held, so they race for it the moment it is released.
    let held = f.layout.lock_install().unwrap_or_else(|e| unreachable!("{e}"));
    let results = std::thread::scope(|scope| {
        let swaps = [
            scope.spawn(|| swap_in_loader(&f.layout, &f.new_loader)),
            scope.spawn(|| swap_in_loader(&f.layout, &f.new_loader)),
        ];
        std::thread::sleep(WHILE_HELD);
        assert_eq!(read(&f.layout.loader_path()), "old loader", "a swap ran while another Denice held the lock");
        drop(held);
        swaps.map(|swap| swap.join().unwrap_or_else(|_| unreachable!()))
    });

    for result in results {
        result.unwrap_or_else(|e| unreachable!("{e}"));
    }
    assert_eq!(read(&f.layout.loader_path()), "new loader");
    // The second swap found the new loader in place and changed nothing (it would otherwise have backed up the new one).
    assert_eq!(read(&f.layout.loader_backup_path()), "old loader");
    assert!(!f.layout.loader_staging_path().exists());
}

#[test]
fn removing_the_previous_loader_waits_for_a_swap_in_progress() {
    let f = fixture();
    swap_in_loader(&f.layout, &f.new_loader).unwrap_or_else(|e| unreachable!("{e}"));
    let held = f.layout.lock_install().unwrap_or_else(|e| unreachable!("{e}"));
    let removed = std::thread::scope(|scope| {
        let removal = scope.spawn(|| remove_previous_loader(&f.layout));
        std::thread::sleep(WHILE_HELD);
        assert!(f.layout.loader_backup_path().exists(), "denice.old was removed while another Denice held the lock");
        drop(held);
        removal.join().unwrap_or_else(|_| unreachable!())
    });
    assert!(removed);
    assert!(!f.layout.loader_backup_path().exists());
}
