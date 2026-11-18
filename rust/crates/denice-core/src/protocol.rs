// Copyright (c) Denice.ai. All rights reserved.

//! The contract between the loader, the entry and Denice Runtime. The single definition is
//! `eng/loader-protocol.json`; a test here (and one on the .NET side) proves these constants equal it.

/// Exit code Denice Runtime returns when a newer version is downloaded, checked and ready:
/// the loader rescans and starts the highest version with the same arguments.
pub const EXIT_RESTART_INTO_NEWER_VERSION: i32 = 75;

/// Exit code the entry returns right after it has swapped in a newer loader:
/// the old loader starts the new `denice` with the same arguments and exits.
pub const EXIT_LOADER_REPLACED: i32 = 76;

/// Environment variable: the install root (the folder holding the loader and `versions`).
pub const ENV_INSTALL_ROOT: &str = "DENICE_INSTALL_ROOT";
/// Environment variable: the full path of the running loader.
pub const ENV_LOADER_PATH: &str = "DENICE_LOADER_PATH";
/// Environment variable: the version the running loader was built with.
pub const ENV_LOADER_VERSION: &str = "DENICE_LOADER_VERSION";
/// Environment variable: the folder of the version being started.
pub const ENV_VERSION_DIR: &str = "DENICE_VERSION_DIR";

/// The folder, under the install root, that holds one folder per version.
pub const VERSIONS_DIR: &str = "versions";
/// Suffix of a version folder still being downloaded and checked.
pub const PARTIAL_SUFFIX: &str = ".partial";
/// Marker file Denice Runtime writes in its version folder once it has started successfully.
pub const STARTED_MARKER: &str = ".started";
/// Marker file the loader writes in a version folder that failed to start, so it is not chosen again.
pub const BAD_MARKER: &str = ".bad";
/// Lock file a running version holds (shared) for its whole run; the loader removes an older version only when it can
/// take this lock exclusively, so a version another Denice is still running is never deleted.
pub const RUNNING_LOCK: &str = ".running";
/// Lock file in the versions folder, held exclusively (by the loader, the entry and Denice Runtime alike) while a version
/// is renamed into place, while the loader is swapped, and while the replaced loader is removed (pkg-db4b63e03ad4).
pub const INSTALL_LOCK: &str = ".lock";
/// Suffix of the new loader while it is being unpacked (Tim: "Unpack first (to denice.ver)").
pub const LOADER_STAGING_SUFFIX: &str = ".ver";
/// Suffix the running loader is renamed to during the swap (Tim: "rename current to old").
pub const LOADER_BACKUP_SUFFIX: &str = ".old";
/// The Denice Runtime assembly the entry runs.
pub const RUNTIME_ASSEMBLY: &str = "Denice.Runtime.dll";
/// The start of the versioned entry's file name, `denice-v<version>-<rid>` (`.exe` on Windows); the rest is built by
/// `platform::entry_file_name`, and Denice Runtime's `LoaderProtocol.EntryFileName` builds the same name.
pub const ENTRY_PREFIX: &str = "denice-v";

#[cfg(test)]
#[path = "protocol_tests.rs"]
mod tests;
