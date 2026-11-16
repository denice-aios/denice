// Copyright (c) Denice.ai. All rights reserved.

//! Replacing the installed loader, in Tim's order (pkg-a0206b25f619 note): "Unpack first (to denice.ver)
//! then rename current to old and .ver to empty and it's safer." Each step that fails puts back what the
//! steps before it changed, so the install always keeps a working `denice`.

use crate::layout::InstallLayout;
use std::fmt;
use std::io;
use std::path::{Path, PathBuf};

/// The step of the swap that failed, and the file system error.
#[derive(Debug)]
pub struct SwapError {
    /// What the swap was doing.
    pub step: SwapStep,
    /// The underlying error.
    pub source: io::Error,
}

/// The steps of the swap.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SwapStep {
    /// Removing a stale `denice.ver` or `denice.old` left by an earlier attempt.
    ClearLeftovers,
    /// Unpacking the new loader to `denice.ver`.
    Unpack,
    /// Renaming the current `denice` to `denice.old`.
    BackUpCurrent,
    /// Renaming `denice.ver` to `denice`.
    Activate,
}

impl fmt::Display for SwapError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "loader swap failed at {:?}: {}", self.step, self.source)
    }
}

impl std::error::Error for SwapError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        Some(&self.source)
    }
}

/// Replaces the installed loader with `new_loader`:
/// 1. unpack (copy) `new_loader` to `denice.ver`;
/// 2. rename `denice` to `denice.old`;
/// 3. rename `denice.ver` to `denice`.
///
/// The running loader keeps running from its renamed file (allowed on every supported OS). `denice.old` is
/// left for the new loader to delete once the old one has exited.
///
/// # Errors
/// Returns the failed step; the installed `denice` is then the original one.
pub fn swap_in_loader(layout: &InstallLayout, new_loader: &Path) -> Result<(), SwapError> {
    swap_with(&RealFiles, layout, new_loader)
}

/// The file operations the swap uses, so tests can make any one of them fail.
trait SwapFiles {
    fn copy_tree(&self, from: &Path, to: &Path) -> io::Result<()>;
    fn rename(&self, from: &Path, to: &Path) -> io::Result<()>;
    fn remove_if_present(&self, path: &Path) -> io::Result<()>;
}

struct RealFiles;

impl SwapFiles for RealFiles {
    fn copy_tree(&self, from: &Path, to: &Path) -> io::Result<()> {
        copy_tree(from, to)
    }

    fn rename(&self, from: &Path, to: &Path) -> io::Result<()> {
        std::fs::rename(from, to)
    }

    fn remove_if_present(&self, path: &Path) -> io::Result<()> {
        remove_if_present(path)
    }
}

fn swap_with(files: &dyn SwapFiles, layout: &InstallLayout, new_loader: &Path) -> Result<(), SwapError> {
    let current = layout.loader_path();
    let staging = layout.loader_staging_path();
    let backup = layout.loader_backup_path();
    let step = |step: SwapStep| move |source: io::Error| SwapError { step, source };

    files.remove_if_present(&staging).map_err(step(SwapStep::ClearLeftovers))?;
    files.remove_if_present(&backup).map_err(step(SwapStep::ClearLeftovers))?;

    if let Err(e) = files.copy_tree(new_loader, &staging) {
        let _ = files.remove_if_present(&staging);
        return Err(step(SwapStep::Unpack)(e));
    }
    if let Err(e) = files.rename(&current, &backup) {
        let _ = files.remove_if_present(&staging);
        return Err(step(SwapStep::BackUpCurrent)(e));
    }
    if let Err(e) = files.rename(&staging, &current) {
        let _ = files.rename(&backup, &current);
        let _ = files.remove_if_present(&staging);
        return Err(step(SwapStep::Activate)(e));
    }
    Ok(())
}

/// Deletes the previous loader (`denice.old`) if it is no longer running. On Windows a running program's
/// file cannot be deleted, so while the old loader is still waiting this fails quietly and the next start
/// deletes it.
///
/// Returns whether a leftover was removed.
#[must_use]
pub fn remove_previous_loader(layout: &InstallLayout) -> bool {
    let backup = layout.loader_backup_path();
    backup.exists() && remove_if_present(&backup).is_ok()
}

fn remove_if_present(path: &Path) -> io::Result<()> {
    match std::fs::symlink_metadata(path) {
        Ok(meta) if meta.is_dir() => std::fs::remove_dir_all(path),
        Ok(_) => std::fs::remove_file(path),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e),
    }
}

/// Copies a file, or a folder tree (a macOS app bundle), keeping permissions.
fn copy_tree(from: &Path, to: &Path) -> io::Result<()> {
    if std::fs::metadata(from)?.is_dir() {
        std::fs::create_dir_all(to)?;
        for entry in std::fs::read_dir(from)? {
            let entry = entry?;
            let target: PathBuf = to.join(entry.file_name());
            copy_tree(&entry.path(), &target)?;
        }
        Ok(())
    } else {
        std::fs::copy(from, to).map(|_| ())
    }
}

#[cfg(test)]
mod tests {
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

    #[test]
    fn previous_loader_is_removed_once_it_is_not_running() {
        let f = fixture();
        swap_in_loader(&f.layout, &f.new_loader).unwrap_or_else(|e| unreachable!("{e}"));
        assert!(remove_previous_loader(&f.layout));
        assert!(!f.layout.loader_backup_path().exists());
        assert!(!remove_previous_loader(&f.layout));
    }
}
