// Copyright (c) Denice.ai. All rights reserved.

//! Platform facts. This is the only module in the Rust code that branches on the target OS or architecture
//! (the Rust counterpart of the C# rule that OS branching lives only in the cross-platform layer).

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

/// The loader's file name: `denice`, or `denice.exe` on Windows (Tim, 16 November 2026).
#[must_use]
pub fn loader_file_name() -> String {
    format!("denice{}", exe_suffix())
}

/// The versioned entry's file name: `denice-v<version>-<rid>`, `.exe` on Windows.
#[must_use]
pub fn entry_file_name(version: &str) -> String {
    format!("denice-v{version}-{}{}", rid(), exe_suffix())
}

#[cfg(test)]
#[path = "platform_tests.rs"]
mod tests;
