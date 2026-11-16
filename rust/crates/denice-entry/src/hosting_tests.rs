// Copyright (c) Denice.ai. All rights reserved.

//! Tests for `hosting` (kept in their own file so coverage counts only product code, as on the .NET side).

use super::*;

#[test]
fn missing_runtime_is_reported_before_loading_anything() {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    let request = HostRequest { host_path: Path::new("/x/denice-entry"), version_dir: dir.path(), args: &[] };
    assert!(matches!(run(&request), Err(HostError::MissingRuntime(_))));
}

#[test]
fn missing_host_resolver_is_reported() {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(dir.path().join(RUNTIME_ASSEMBLY), b"not really").unwrap_or_else(|e| unreachable!("{e}"));
    let request = HostRequest { host_path: Path::new("/x/denice-entry"), version_dir: dir.path(), args: &[] };
    assert!(matches!(run(&request), Err(HostError::LoadHostResolver(..))));
}

#[test]
fn nul_in_an_argument_is_refused() {
    let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
    std::fs::write(dir.path().join(RUNTIME_ASSEMBLY), b"x").unwrap_or_else(|e| unreachable!("{e}"));
    let args = [OsString::from("a\0b")];
    let request = HostRequest { host_path: Path::new("/x/denice-entry"), version_dir: dir.path(), args: &args };
    assert!(matches!(run(&request), Err(HostError::InvalidText(_))));
}
