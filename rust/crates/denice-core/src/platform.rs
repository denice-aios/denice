// Copyright (c) Denice.ai. All rights reserved.

//! Platform facts. This is the only module in the Rust code that branches on the target OS or architecture
//! (the Rust counterpart of the C# rule that OS branching lives only in the cross-platform layer).

use std::path::{Path, PathBuf};

/// The .NET runtime identifier of this build's target (the same names the release files use).
#[must_use]
pub const fn rid() -> &'static str {
    RID
}

#[cfg(all(target_os = "macos", target_arch = "aarch64"))]
const RID: &str = "osx-arm64";
#[cfg(all(target_os = "macos", target_arch = "x86_64"))]
const RID: &str = "osx-x64";
#[cfg(all(target_os = "windows", target_arch = "aarch64"))]
const RID: &str = "win-arm64";
#[cfg(all(target_os = "windows", target_arch = "x86_64"))]
const RID: &str = "win-x64";
#[cfg(all(target_os = "linux", target_arch = "aarch64"))]
const RID: &str = "linux-arm64";
#[cfg(all(target_os = "linux", target_arch = "x86_64"))]
const RID: &str = "linux-x64";

/// The executable file suffix on this platform (`.exe` on Windows, empty elsewhere).
#[must_use]
pub const fn exe_suffix() -> &'static str {
    EXE_SUFFIX
}

#[cfg(windows)]
const EXE_SUFFIX: &str = ".exe";
#[cfg(not(windows))]
const EXE_SUFFIX: &str = "";

/// The file name of the .NET host resolver library that a self-contained Denice Runtime ships beside itself.
#[must_use]
pub const fn hostfxr_file_name() -> &'static str {
    HOSTFXR
}

#[cfg(windows)]
const HOSTFXR: &str = "hostfxr.dll";
#[cfg(target_os = "macos")]
const HOSTFXR: &str = "libhostfxr.dylib";
#[cfg(target_os = "linux")]
const HOSTFXR: &str = "libhostfxr.so";

/// The loader program's file name: `denice`, or `denice.exe` on Windows (Tim, 16 November 2026).
#[must_use]
pub fn loader_program_name() -> String {
    format!("denice{}", exe_suffix())
}

/// What the install holds as "the loader" and what a newer version replaces as a whole: on macOS the app
/// `Denice.app` (pkg-697310396f74, "App beside versions"), elsewhere the loader program itself.
#[must_use]
pub fn loader_artifact_name() -> String {
    LOADER_ARTIFACT.map_or_else(loader_program_name, str::to_owned)
}

/// The loader program inside the loader artifact (`Denice.app/Contents/MacOS/denice` on macOS).
#[must_use]
pub fn loader_program_in(artifact: &Path) -> PathBuf {
    LOADER_ARTIFACT
        .map_or_else(|| artifact.to_path_buf(), |_| artifact.join(BUNDLE_PROGRAM_DIR).join(loader_program_name()))
}

/// The loader artifact (what a swap replaces) the running loader program belongs to: `Denice.app` on macOS, the
/// program itself elsewhere.
#[must_use]
///
/// Returns `None` unless the path really is a loader program: named `denice` (`denice.exe` on Windows) and, on macOS,
/// inside an app's `Contents/MacOS`. The swap renames this artifact, so a wrong path must never reach it (a wrong
/// path once made a test rename the system temp folder).
pub fn loader_artifact_of(loader_program: &Path) -> Option<PathBuf> {
    if loader_program.file_name()? != loader_program_name().as_str() {
        return None;
    }
    if LOADER_ARTIFACT.is_none() {
        return Some(loader_program.to_path_buf());
    }
    let macos = loader_program.parent()?;
    let contents = macos.parent()?;
    let app = contents.parent()?;
    let is_app = app.extension().is_some_and(|extension| extension == "app");
    (macos.file_name()? == "MacOS" && contents.file_name()? == "Contents" && is_app).then(|| app.to_path_buf())
}

/// The running program's real path. On macOS and Linux it resolves links (on macOS `current_exe()` returns the
/// `~/.local/bin/denice` link it was started through). On Windows it is left as is: Windows starts Denice through a
/// Start Menu shortcut, and resolving would turn the path into the `\\?\C:\...` form that .NET and users see.
///
/// # Errors
/// Returns the error if the program's own path cannot be found or resolved.
pub fn running_program() -> std::io::Result<PathBuf> {
    let path = std::env::current_exe()?;
    if cfg!(windows) { Ok(path) } else { std::fs::canonicalize(path) }
}

/// Whether `path` names a loader artifact: an `.app` on macOS, a file named `denice` / `denice.exe` elsewhere.
#[must_use]
pub fn is_loader_artifact(path: &Path) -> bool {
    if LOADER_ARTIFACT.is_some() {
        path.extension().is_some_and(|extension| extension == "app")
    } else {
        path.file_name().is_some_and(|name| name == loader_program_name().as_str())
    }
}

/// The folder that holds `versions/` for the running loader program. On macOS it is
/// `~/Library/Application Support/Denice` (Tim, pkg-494745156d56 macos-versions: "Application Support"), so `Denice.app`
/// can live anywhere; elsewhere it is the loader's own folder (the per-user install folder).
#[must_use]
pub fn data_root_of(loader_program: &Path) -> Option<PathBuf> {
    data_root_for(loader_program, std::env::var_os("HOME").map(PathBuf::from).as_deref())
}

/// [`data_root_of`] with the home folder given (macOS only reads it).
#[must_use]
pub fn data_root_for(loader_program: &Path, home: Option<&Path>) -> Option<PathBuf> {
    if LOADER_ARTIFACT.is_some() {
        home.map(|home| home.join(MAC_DATA_FOLDER))
    } else {
        loader_program.parent().map(Path::to_path_buf)
    }
}

/// Where macOS keeps Denice's versions, under the home folder.
const MAC_DATA_FOLDER: &str = "Library/Application Support/Denice";

#[cfg(target_os = "macos")]
const LOADER_ARTIFACT: Option<&str> = Some("Denice.app");
#[cfg(not(target_os = "macos"))]
const LOADER_ARTIFACT: Option<&str> = None;

/// Where a macOS app keeps its program.
const BUNDLE_PROGRAM_DIR: &str = "Contents/MacOS";

/// The versioned entry's file name: `denice-v<version>-<rid>`, `.exe` on Windows.
#[must_use]
pub fn entry_file_name(version: &str) -> String {
    format!("denice-v{version}-{}{}", rid(), exe_suffix())
}

/// Ends this process with `code`, keeping the full exit code (32-bit on Windows, so a version's crash code reaches the
/// shell unchanged), and running the C runtime's exit handlers first. On Windows `std::process::exit` ends the process
/// without them, which loses whatever they flush (for example coverage data); the C runtime's own `exit` runs them.
pub fn exit_process(code: i32) -> ! {
    exit_through_c_runtime(code)
}

#[cfg(not(windows))]
fn exit_through_c_runtime(code: i32) -> ! {
    // On Unix the standard library already exits through the C runtime's `exit`.
    std::process::exit(code)
}

#[cfg(windows)]
#[allow(unsafe_code)]
fn exit_through_c_runtime(code: i32) -> ! {
    unsafe extern "C" {
        fn exit(code: std::ffi::c_int) -> !;
    }
    // SAFETY: the C runtime's exit runs the registered exit handlers and never returns.
    unsafe { exit(code) }
}

#[cfg(test)]
#[path = "platform_tests.rs"]
mod tests;
