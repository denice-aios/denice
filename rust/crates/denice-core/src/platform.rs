// Copyright (c) Denice.ai. All rights reserved.

//! Platform facts. This is the only module in the Rust code that branches on the target OS or architecture
//! (the Rust counterpart of the C# rule that OS branching lives only in the cross-platform layer).

/// The .NET runtime identifier of this build's target (the same names the release files use).
#[must_use]
pub const fn rid() -> &'static str {
    if cfg!(all(target_os = "macos", target_arch = "aarch64")) {
        "osx-arm64"
    } else if cfg!(all(target_os = "macos", target_arch = "x86_64")) {
        "osx-x64"
    } else if cfg!(all(target_os = "windows", target_arch = "aarch64")) {
        "win-arm64"
    } else if cfg!(all(target_os = "windows", target_arch = "x86_64")) {
        "win-x64"
    } else if cfg!(all(target_os = "linux", target_arch = "aarch64")) {
        "linux-arm64"
    } else if cfg!(all(target_os = "linux", target_arch = "x86_64")) {
        "linux-x64"
    } else {
        "unsupported"
    }
}

/// The executable file suffix on this platform (`.exe` on Windows, empty elsewhere).
#[must_use]
pub const fn exe_suffix() -> &'static str {
    if cfg!(windows) { ".exe" } else { "" }
}

/// The file name of the .NET host resolver library that a self-contained Denice Runtime ships beside itself.
#[must_use]
pub const fn hostfxr_file_name() -> &'static str {
    if cfg!(windows) {
        "hostfxr.dll"
    } else if cfg!(target_os = "macos") {
        "libhostfxr.dylib"
    } else {
        "libhostfxr.so"
    }
}

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
mod tests {
    use super::*;

    #[test]
    fn this_platform_is_one_of_the_six_released() {
        let six = ["osx-arm64", "osx-x64", "win-arm64", "win-x64", "linux-arm64", "linux-x64"];
        assert!(six.contains(&rid()), "{}", rid());
    }

    #[test]
    fn entry_name_matches_the_release_naming_convention() {
        let name = entry_file_name("0.1.108430176-pre-release+732fc53");
        assert_eq!(name, format!("denice-v0.1.108430176-pre-release+732fc53-{}{}", rid(), exe_suffix()));
    }

    #[test]
    fn loader_name_is_denice() {
        assert_eq!(loader_file_name(), format!("denice{}", exe_suffix()));
    }
}
