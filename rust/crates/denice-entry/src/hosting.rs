// Copyright (c) Denice.ai. All rights reserved.

//! Runs Denice Runtime with .NET's own host resolver (`hostfxr`), exactly as .NET's apphost does: the
//! self-contained runtime sits beside Denice Runtime in the version folder, and `hostfxr_main_startupinfo`
//! runs the managed entry point with the arguments and returns its exit code. This is the only module that
//! calls native code, so it is the only one allowed `unsafe`.

#![allow(unsafe_code)]

use denice_core::platform;
use denice_core::protocol::RUNTIME_ASSEMBLY;
use std::ffi::OsString;
use std::fmt;
use std::path::{Path, PathBuf};

/// Why Denice Runtime could not be started.
#[derive(Debug)]
pub enum HostError {
    /// The host resolver library is missing or could not be loaded.
    LoadHostResolver(PathBuf, String),
    /// The host resolver does not export the start function.
    MissingStartFunction(String),
    /// Denice Runtime is not in the version folder.
    MissingRuntime(PathBuf),
    /// An argument or path contains a NUL character and cannot be passed to the host.
    InvalidText(OsString),
}

impl fmt::Display for HostError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::LoadHostResolver(path, why) => write!(f, "cannot load the .NET host resolver {}: {why}", path.display()),
            Self::MissingStartFunction(why) => write!(f, "the .NET host resolver has no hostfxr_main_startupinfo: {why}"),
            Self::MissingRuntime(path) => write!(f, "Denice Runtime is missing: {}", path.display()),
            Self::InvalidText(text) => write!(f, "cannot pass {} to .NET: it contains a NUL character", text.to_string_lossy()),
        }
    }
}

impl std::error::Error for HostError {}

/// What to run: the entry executable (the host), the version folder (the .NET root) and the arguments.
pub struct HostRequest<'a> {
    /// This entry executable's own path.
    pub host_path: &'a Path,
    /// The version folder that holds the self-contained runtime and Denice Runtime.
    pub version_dir: &'a Path,
    /// The arguments after the program name.
    pub args: &'a [OsString],
}

impl HostRequest<'_> {
    /// Denice Runtime's assembly path.
    #[must_use]
    pub fn runtime_path(&self) -> PathBuf {
        self.version_dir.join(RUNTIME_ASSEMBLY)
    }

    /// The host resolver library path.
    #[must_use]
    pub fn hostfxr_path(&self) -> PathBuf {
        self.version_dir.join(platform::hostfxr_file_name())
    }
}

/// Runs Denice Runtime and returns its exit code.
///
/// # Errors
/// Returns why the runtime could not be started; once started, every outcome is its exit code.
pub fn run(request: &HostRequest<'_>) -> Result<i32, HostError> {
    let runtime = request.runtime_path();
    if !runtime.is_file() {
        return Err(HostError::MissingRuntime(runtime));
    }
    let hostfxr_path = request.hostfxr_path();
    let mut argv: Vec<NativeString> = Vec::with_capacity(request.args.len() + 1);
    argv.push(NativeString::new(request.host_path.as_os_str())?);
    for arg in request.args {
        argv.push(NativeString::new(arg)?);
    }
    let host_path = NativeString::new(request.host_path.as_os_str())?;
    let dotnet_root = NativeString::new(request.version_dir.as_os_str())?;
    let app_path = NativeString::new(runtime.as_os_str())?;
    let argv_ptrs: Vec<*const NativeChar> = argv.iter().map(NativeString::as_ptr).collect();
    let argc = i32::try_from(argv_ptrs.len()).map_err(|_| HostError::InvalidText(OsString::from("too many arguments")))?;

    // SAFETY: the library is .NET's hostfxr from the version folder; it stays loaded for the whole call.
    let library = unsafe { libloading::Library::new(&hostfxr_path) }
        .map_err(|e| HostError::LoadHostResolver(hostfxr_path.clone(), e.to_string()))?;
    // SAFETY: hostfxr_main_startupinfo has exactly this signature in every supported .NET version
    // (dotnet/runtime src/native/corehost/fxr/hostfxr.h).
    let start: libloading::Symbol<'_, StartupInfoFn> =
        unsafe { library.get(b"hostfxr_main_startupinfo\0") }.map_err(|e| HostError::MissingStartFunction(e.to_string()))?;
    // SAFETY: every pointer points into a NUL-terminated buffer owned above that outlives the call.
    let code = unsafe { start(argc, argv_ptrs.as_ptr(), host_path.as_ptr(), dotnet_root.as_ptr(), app_path.as_ptr()) };
    Ok(code)
}

#[cfg(windows)]
type NativeChar = u16;
#[cfg(not(windows))]
type NativeChar = std::ffi::c_char;

type StartupInfoFn = unsafe extern "C" fn(
    argc: i32,
    argv: *const *const NativeChar,
    host_path: *const NativeChar,
    dotnet_root: *const NativeChar,
    app_path: *const NativeChar,
) -> i32;

/// A NUL-terminated string in the host's native character type (UTF-16 on Windows, bytes elsewhere).
struct NativeString(Vec<NativeChar>);

impl NativeString {
    #[cfg(windows)]
    fn new(text: &std::ffi::OsStr) -> Result<Self, HostError> {
        use std::os::windows::ffi::OsStrExt;
        let mut units: Vec<u16> = text.encode_wide().collect();
        if units.contains(&0) {
            return Err(HostError::InvalidText(text.to_owned()));
        }
        units.push(0);
        Ok(Self(units))
    }

    #[cfg(not(windows))]
    fn new(text: &std::ffi::OsStr) -> Result<Self, HostError> {
        use std::os::unix::ffi::OsStrExt;
        let bytes = text.as_bytes();
        if bytes.contains(&0) {
            return Err(HostError::InvalidText(text.to_owned()));
        }
        #[allow(clippy::cast_possible_wrap)]
        let mut chars: Vec<NativeChar> = bytes.iter().map(|&b| b as NativeChar).collect();
        chars.push(0);
        Ok(Self(chars))
    }

    fn as_ptr(&self) -> *const NativeChar {
        self.0.as_ptr()
    }
}

#[cfg(test)]
mod tests {
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
}
