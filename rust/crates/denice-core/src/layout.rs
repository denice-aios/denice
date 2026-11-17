// Copyright (c) Denice.ai. All rights reserved.

//! The install layout (Tim's answers, pkg-a0206b25f619 and pkg-f543b9586645): one install folder holds the
//! loader and `versions/<version>/`; each version folder holds that version's entry, Denice Runtime and a copy
//! of the loader it carries. A version is complete when its entry exists; `.partial` folders are still being
//! downloaded and are never chosen.

use crate::platform;
use crate::protocol::{
    BAD_MARKER, LOADER_BACKUP_SUFFIX, LOADER_STAGING_SUFFIX, PARTIAL_SUFFIX, RUNNING_LOCK, STARTED_MARKER, VERSIONS_DIR,
};
use crate::version::Version;
use std::ffi::OsString;
use std::io;
use std::path::{Path, PathBuf};

/// Keeps a version marked as running until dropped (the open, share-locked `.running` file is the mark).
#[derive(Debug)]
pub struct RunningLock {
    _file: std::fs::File,
}

/// What [`InstalledVersion::hold_for_start`] found.
#[derive(Debug)]
pub enum StartHold {
    /// The version is still installed and is marked as running while this lives (`None`: the lock could not be
    /// taken, and the version runs unmarked).
    Installed(Option<RunningLock>),
    /// Another Denice removed the version after it was chosen; choose again.
    Removed,
}

/// One installed version folder.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstalledVersion {
    /// The version.
    pub version: Version,
    /// Its folder, `versions/<version>`.
    pub dir: PathBuf,
}

impl InstalledVersion {
    /// The versioned entry executable inside the folder.
    #[must_use]
    pub fn entry_path(&self) -> PathBuf {
        self.dir.join(platform::entry_file_name(&self.version.to_string()))
    }

    /// The loader this version carries (used to replace an older installed loader): `denice`, or `Denice.app` on macOS.
    #[must_use]
    pub fn carried_loader_path(&self) -> PathBuf {
        self.dir.join(platform::loader_artifact_name())
    }

    /// Whether Denice Runtime of this version has started successfully at least once.
    #[must_use]
    pub fn has_started(&self) -> bool {
        self.dir.join(STARTED_MARKER).is_file()
    }

    /// Whether this version failed to start and must not be chosen again.
    #[must_use]
    pub fn is_bad(&self) -> bool {
        self.dir.join(BAD_MARKER).is_file()
    }

    /// Marks this version as running for as long as the returned lock lives (held by the versioned entry for its whole
    /// run). Several processes may hold it at once.
    ///
    /// # Errors
    /// Returns the file system error if the lock file cannot be opened or locked.
    pub fn hold_running(&self) -> io::Result<RunningLock> {
        Self::running_lock_in(&self.dir).ok_or_else(|| io::Error::other("cannot take the running lock"))
    }

    /// Holds the running lock of the version folder `dir` (used by the entry, which knows only its own folder); `None`
    /// when it cannot be taken, in which case the version simply runs unmarked.
    #[must_use]
    pub fn running_lock_in(dir: &Path) -> Option<RunningLock> {
        let file = open_running_lock(dir).ok()?;
        file.lock_shared().ok()?;
        Some(RunningLock { _file: file })
    }

    /// Marks this version as running before it is started, then confirms it is still installed. A Denice removing a
    /// version holds this lock exclusively while it deletes the entry, so either the removal waits for this run, or
    /// this finds the entry gone and the loader chooses again.
    #[must_use]
    pub fn hold_for_start(&self) -> StartHold {
        let lock = self.hold_running().ok();
        if self.entry_path().is_file() { StartHold::Installed(lock) } else { StartHold::Removed }
    }

    /// Takes this version for removal: its running lock, exclusively, so no Denice can start it meanwhile. `None` when
    /// a Denice is running it.
    fn take_for_removal(&self) -> io::Result<Option<RunningLock>> {
        let file = open_running_lock(&self.dir)?;
        match file.try_lock() {
            Ok(()) => Ok(Some(RunningLock { _file: file })),
            Err(std::fs::TryLockError::WouldBlock) => Ok(None),
            Err(std::fs::TryLockError::Error(e)) => Err(e),
        }
    }

    /// Whether any process is running this version now.
    #[must_use]
    pub fn is_running(&self) -> bool {
        let Ok(file) = std::fs::OpenOptions::new().write(true).open(self.dir.join(RUNNING_LOCK)) else { return false };
        match file.try_lock() {
            Ok(()) => {
                let _ = file.unlock();
                false
            }
            Err(std::fs::TryLockError::WouldBlock) => true,
            Err(std::fs::TryLockError::Error(_)) => false,
        }
    }

    /// Records that this version failed to start.
    ///
    /// # Errors
    /// Returns the file system error if the marker cannot be written.
    pub fn mark_bad(&self) -> io::Result<()> {
        let marker = self.dir.join(BAD_MARKER);
        std::fs::write(&marker, b"")?;
        // A version another Denice has started meanwhile is not bad. The marker is written before `.started` is read,
        // and the removal of older versions reads `.bad` after `.started` was written, so of two Denice racing on one
        // version, either this takes the marker back or that removal keeps the older versions.
        if self.has_started() {
            remove_file_if_present(&marker)?;
        }
        Ok(())
    }
}

/// The install folder and the paths inside it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallLayout {
    root: PathBuf,
    loader: PathBuf,
}

impl InstallLayout {
    /// The layout rooted at `root` (the folder holding the loader and `versions`).
    #[must_use]
    pub fn new(root: impl Into<PathBuf>) -> Self {
        let root = root.into();
        let loader = root.join(platform::loader_artifact_name());
        Self { root, loader }
    }

    /// The layout whose versions live in `root` and whose loader artifact is `loader` (on macOS `Denice.app` lives
    /// wherever the user put it, and the versions in `~/Library/Application Support/Denice`).
    #[must_use]
    pub fn with_loader(root: impl Into<PathBuf>, loader: impl Into<PathBuf>) -> Self {
        Self { root: root.into(), loader: loader.into() }
    }

    /// The layout of the running loader program: its artifact, and the folder that holds `versions/`.
    #[must_use]
    pub fn of_loader_program(program: &Path) -> Option<Self> {
        Some(Self::with_loader(platform::data_root_of(program)?, platform::loader_artifact_of(program)?))
    }

    /// The folder that holds `versions/` (the install folder on Windows and Linux).
    #[must_use]
    pub fn root(&self) -> &Path {
        &self.root
    }

    /// The installed loader as a whole, the thing a swap replaces: `denice` / `denice.exe`, or `Denice.app` on macOS.
    #[must_use]
    pub fn loader_path(&self) -> PathBuf {
        self.loader.clone()
    }

    /// The installed loader program, the thing that is run (inside `Denice.app` on macOS).
    #[must_use]
    pub fn loader_program_path(&self) -> PathBuf {
        platform::loader_program_in(&self.loader_path())
    }

    /// Where a new loader is unpacked first: `denice.ver`.
    #[must_use]
    pub fn loader_staging_path(&self) -> PathBuf {
        with_suffix(&self.loader_path(), LOADER_STAGING_SUFFIX)
    }

    /// Where the replaced loader is renamed to: `denice.old`.
    #[must_use]
    pub fn loader_backup_path(&self) -> PathBuf {
        with_suffix(&self.loader_path(), LOADER_BACKUP_SUFFIX)
    }

    /// The folder holding one folder per version.
    #[must_use]
    pub fn versions_dir(&self) -> PathBuf {
        self.root.join(VERSIONS_DIR)
    }

    /// Every complete version folder that has not been marked bad, highest first.
    ///
    /// # Errors
    /// Returns the file system error if the versions folder exists but cannot be read.
    pub fn usable_versions(&self) -> io::Result<Vec<InstalledVersion>> {
        let mut found: Vec<InstalledVersion> = self.complete_versions()?.into_iter().filter(|v| !v.is_bad()).collect();
        found.sort_by(|a, b| b.version.cmp(&a.version));
        Ok(found)
    }

    /// The highest usable version, if any (Tim: "find the highest known version and load it").
    ///
    /// # Errors
    /// Returns the file system error if the versions folder exists but cannot be read.
    pub fn highest_usable_version(&self) -> io::Result<Option<InstalledVersion>> {
        Ok(self.usable_versions()?.into_iter().next())
    }

    /// Once `current` has started successfully (and is not marked bad), removes every version folder older than it
    /// (pkg-a0206b25f619: the previous version is kept until the new one has started once). Returns the folders it
    /// removed.
    ///
    /// # Errors
    /// Returns the first file system error; folders removed before it stay removed.
    pub fn remove_versions_older_than(&self, current: &InstalledVersion) -> io::Result<Vec<PathBuf>> {
        if !current.has_started() || current.is_bad() {
            return Ok(Vec::new());
        }
        let mut removed = Vec::new();
        // Incomplete folders too, so a removal cut short is finished by a later start.
        for older in self.version_folders()?.into_iter().filter(|v| v.version < current.version) {
            // A version another Denice is running is kept; a later start removes it.
            let Some(lock) = older.take_for_removal()? else { continue };
            // The entry goes first, under the lock: without it the folder is no longer a version, so no Denice can
            // start it while the rest is deleted (see `hold_for_start`).
            remove_file_if_present(&older.entry_path())?;
            drop(lock);
            std::fs::remove_dir_all(&older.dir)?;
            removed.push(older.dir);
        }
        Ok(removed)
    }

    fn complete_versions(&self) -> io::Result<Vec<InstalledVersion>> {
        Ok(self.version_folders()?.into_iter().filter(|v| v.entry_path().is_file()).collect())
    }

    /// Every folder named after a version, complete or not (never a `.partial` download).
    fn version_folders(&self) -> io::Result<Vec<InstalledVersion>> {
        let dir = self.versions_dir();
        let entries = match std::fs::read_dir(&dir) {
            Ok(entries) => entries,
            Err(e) if e.kind() == io::ErrorKind::NotFound => return Ok(Vec::new()),
            Err(e) => return Err(e),
        };
        let mut found = Vec::new();
        for entry in entries {
            let entry = entry?;
            let Some(name) = entry.file_name().to_str().map(str::to_owned) else {
                continue;
            };
            if name.ends_with(PARTIAL_SUFFIX) || !entry.file_type()?.is_dir() {
                continue;
            }
            let Ok(version) = name.parse::<Version>() else {
                continue;
            };
            found.push(InstalledVersion { version, dir: entry.path() });
        }
        Ok(found)
    }
}

fn open_running_lock(dir: &Path) -> io::Result<std::fs::File> {
    std::fs::OpenOptions::new().create(true).truncate(false).write(true).open(dir.join(RUNNING_LOCK))
}

fn remove_file_if_present(path: &Path) -> io::Result<()> {
    match std::fs::remove_file(path) {
        Err(e) if e.kind() != io::ErrorKind::NotFound => Err(e),
        _ => Ok(()),
    }
}

fn with_suffix(path: &Path, suffix: &str) -> PathBuf {
    let mut name: OsString = path.file_name().map_or_default(OsString::from);
    name.push(suffix);
    path.with_file_name(name)
}

#[cfg(test)]
#[path = "layout_tests.rs"]
mod tests;
