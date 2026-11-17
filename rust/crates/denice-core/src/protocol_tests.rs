// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `protocol` (kept in their own file so coverage counts only product code, as on the .NET side).

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
    let expected: [(&str, String); 14] = [
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
        ("runningLock", RUNNING_LOCK.to_owned()),
        ("loaderStagingSuffix", LOADER_STAGING_SUFFIX.to_owned()),
        ("loaderBackupSuffix", LOADER_BACKUP_SUFFIX.to_owned()),
        ("runtimeAssembly", RUNTIME_ASSEMBLY.to_owned()),
    ];
    for (key, value) in expected {
        assert!(has(&spec, key, &value), "eng/loader-protocol.json: {key} is not {value}");
    }
}
