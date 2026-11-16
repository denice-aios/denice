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
/// Suffix of the new loader while it is being unpacked (Tim: "Unpack first (to denice.ver)").
pub const LOADER_STAGING_SUFFIX: &str = ".ver";
/// Suffix the running loader is renamed to during the swap (Tim: "rename current to old").
pub const LOADER_BACKUP_SUFFIX: &str = ".old";
/// The Denice Runtime assembly the entry runs.
pub const RUNTIME_ASSEMBLY: &str = "Denice.Runtime.dll";

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn spec() -> String {
        let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../eng/loader-protocol.json");
        std::fs::read_to_string(&path).unwrap_or_else(|e| unreachable!("{}: {e}", path.display()))
    }

    fn has(spec: &str, key: &str, value: &str) -> bool {
        spec.contains(&format!("\"{key}\": {value}")) || spec.contains(&format!("\"{key}\": \"{value}\""))
    }

    #[test]
    fn constants_equal_the_shared_protocol_file() {
        let spec = spec();
        let expected: [(&str, String); 13] = [
            ("restartIntoNewerVersion", EXIT_RESTART_INTO_NEWER_VERSION.to_string()),
            ("loaderReplaced", EXIT_LOADER_REPLACED.to_string()),
            ("installRoot", ENV_INSTALL_ROOT.to_owned()),
            ("loaderPath", ENV_LOADER_PATH.to_owned()),
            ("loaderVersion", ENV_LOADER_VERSION.to_owned()),
            ("versionDirectory", ENV_VERSION_DIR.to_owned()),
            ("versionsDirectory", VERSIONS_DIR.to_owned()),
            ("partialSuffix", PARTIAL_SUFFIX.to_owned()),
            ("startedMarker", STARTED_MARKER.to_owned()),
            ("badMarker", BAD_MARKER.to_owned()),
            ("loaderStagingSuffix", LOADER_STAGING_SUFFIX.to_owned()),
            ("loaderBackupSuffix", LOADER_BACKUP_SUFFIX.to_owned()),
            ("runtimeAssembly", RUNTIME_ASSEMBLY.to_owned()),
        ];
        for (key, value) in expected {
            assert!(has(&spec, key, &value), "eng/loader-protocol.json: {key} is not {value}");
        }
    }
}
