// Copyright (c) Denice.ai. All rights reserved.

//! The install layout (Tim's answers, pkg-a0206b25f619 and pkg-f543b9586645): one install folder holds the
//! loader and `versions/<version>/`; each version folder holds that version's entry, Denice Runtime and a copy
//! of the loader it carries. A version is complete when its entry exists; `.partial` folders are still being
//! downloaded and are never chosen.

use crate::platform;
use crate::protocol::{
    BAD_MARKER, LOADER_BACKUP_SUFFIX, LOADER_STAGING_SUFFIX, PARTIAL_SUFFIX, STARTED_MARKER, VERSIONS_DIR,
};
use crate::version::Version;
use std::ffi::OsString;
use std::io;
use std::path::{Path, PathBuf};

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

    /// The loader copy this version carries (used to replace an older installed loader).
    #[must_use]
    pub fn carried_loader_path(&self) -> PathBuf {
        self.dir.join(platform::loader_file_name())
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

    /// Records that this version failed to start.
    ///
    /// # Errors
    /// Returns the file system error if the marker cannot be written.
    pub fn mark_bad(&self) -> io::Result<()> {
        std::fs::write(self.dir.join(BAD_MARKER), b"")
    }
}

/// The install folder and the paths inside it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallLayout {
    root: PathBuf,
}

impl InstallLayout {
    /// The layout rooted at `root` (the folder holding the loader and `versions`).
    #[must_use]
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self { root: root.into() }
    }

    /// The install root.
    #[must_use]
    pub fn root(&self) -> &Path {
        &self.root
    }

    /// The installed loader (`denice` / `denice.exe`).
    #[must_use]
    pub fn loader_path(&self) -> PathBuf {
        self.root.join(platform::loader_file_name())
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

    /// Once `current` has started successfully, removes every version folder older than it
    /// (pkg-a0206b25f619: the previous version is kept until the new one has started once).
    /// Returns the folders it removed.
    ///
    /// # Errors
    /// Returns the first file system error; folders removed before it stay removed.
    pub fn remove_versions_older_than(&self, current: &InstalledVersion) -> io::Result<Vec<PathBuf>> {
        if !current.has_started() {
            return Ok(Vec::new());
        }
        let mut removed = Vec::new();
        for older in self.complete_versions()?.into_iter().filter(|v| v.version < current.version) {
            std::fs::remove_dir_all(&older.dir)?;
            removed.push(older.dir);
        }
        Ok(removed)
    }

    fn complete_versions(&self) -> io::Result<Vec<InstalledVersion>> {
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
            let candidate = InstalledVersion { version, dir: entry.path() };
            if candidate.entry_path().is_file() {
                found.push(candidate);
            }
        }
        Ok(found)
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
