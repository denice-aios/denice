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
    /// The target is not a loader (`denice` / `denice.exe`, or an `.app` on macOS); nothing was touched.
    NotALoader,
    /// Taking the install lock (`versions/.lock`) failed; nothing was touched.
    Lock,
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
/// The swap runs under the install lock, so two Denice never swap at once (pkg-db4b63e03ad4). One that waited for
/// the lock finds `new_loader` already installed by the other and changes nothing.
///
/// # Errors
/// Returns the failed step; the installed `denice` is then the original one.
pub fn swap_in_loader(layout: &InstallLayout, new_loader: &Path) -> Result<(), SwapError> {
    let _lock = layout.lock_install().map_err(|source| SwapError { step: SwapStep::Lock, source })?;
    if same_tree(&layout.loader_path(), new_loader) {
        return Ok(());
    }
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
    if !crate::platform::is_loader_artifact(&current) {
        return Err(SwapError {
            step: SwapStep::NotALoader,
            source: io::Error::other(format!("{} is not a Denice loader", current.display())),
        });
    }
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
/// The removal runs under the install lock, so it never meets a swap that is renaming the loader to `denice.old`.
///
/// Returns whether a leftover was removed.
#[must_use]
pub fn remove_previous_loader(layout: &InstallLayout) -> bool {
    let backup = layout.loader_backup_path();
    if !backup.exists() {
        return false;
    }
    let Ok(_lock) = layout.lock_install() else { return false };
    // Checked again under the lock: another Denice may have removed it while this one waited.
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

/// Whether `a` and `b` hold the same file, or the same folder tree (a macOS app bundle), byte for byte.
fn same_tree(a: &Path, b: &Path) -> bool {
    let (Ok(meta_a), Ok(meta_b)) = (std::fs::metadata(a), std::fs::metadata(b)) else { return false };
    if meta_a.is_dir() && meta_b.is_dir() {
        let (Some(names_a), Some(names_b)) = (sorted_names(a), sorted_names(b)) else { return false };
        names_a == names_b && names_a.iter().all(|name| same_tree(&a.join(name), &b.join(name)))
    } else if meta_a.is_file() && meta_b.is_file() {
        meta_a.len() == meta_b.len() && matches!((std::fs::read(a), std::fs::read(b)), (Ok(x), Ok(y)) if x == y)
    } else {
        false
    }
}

fn sorted_names(dir: &Path) -> Option<Vec<std::ffi::OsString>> {
    let mut names =
        std::fs::read_dir(dir).ok()?.map(|e| e.map(|e| e.file_name())).collect::<io::Result<Vec<_>>>().ok()?;
    names.sort();
    Some(names)
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
#[path = "swap_tests.rs"]
mod tests;
