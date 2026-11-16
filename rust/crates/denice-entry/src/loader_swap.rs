// Copyright (c) Denice.ai. All rights reserved.

//! "a higher-numbered version can rename the loader and unpack a new loader to replace it" (Tim, 14 November
//! 2026). The loader tells the entry its own path and version; when this version is higher, the entry swaps in
//! the loader it carries and returns, so the old loader can hand over to the new one right away
//! (pkg-f543b9586645, "Hand over now").

use denice_core::layout::{InstallLayout, InstalledVersion};
use denice_core::protocol::{ENV_INSTALL_ROOT, ENV_LOADER_VERSION};
use denice_core::swap::{SwapError, swap_in_loader};
use denice_core::version::{ParseVersionError, Version};
use std::fmt;
use std::path::{Path, PathBuf};

/// What the entry did about the loader.
#[derive(Debug, PartialEq, Eq)]
pub enum Outcome {
    /// The installed loader is not older (or this entry was not started by a loader).
    NotNeeded,
    /// A newer loader was swapped in; the old loader must hand over now.
    Replaced,
}

/// Why the loader could not be replaced. The installed loader is then still the old, working one.
#[derive(Debug)]
pub enum Error {
    /// A version could not be read.
    Version(ParseVersionError),
    /// This version does not carry a loader.
    MissingCarriedLoader(PathBuf),
    /// The swap failed (and restored the old loader).
    Swap(SwapError),
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Version(e) => e.fmt(f),
            Self::MissingCarriedLoader(path) => {
                write!(f, "this version carries no loader at {}", path.display())
            }
            Self::Swap(e) => e.fmt(f),
        }
    }
}

/// What the loader told this entry through the environment.
pub struct LoaderContext {
    /// The install root.
    pub install_root: PathBuf,
    /// The running loader's version.
    pub loader_version: String,
}

impl LoaderContext {
    /// Reads the loader's context, or `None` when this entry was started directly (not by a loader).
    #[must_use]
    pub fn from_environment() -> Option<Self> {
        let install_root = std::env::var_os(ENV_INSTALL_ROOT)?;
        let loader_version = std::env::var(ENV_LOADER_VERSION).ok()?;
        Some(Self { install_root: PathBuf::from(install_root), loader_version })
    }
}

/// Replaces the installed loader if this version is higher than the loader that started it.
///
/// # Errors
/// Returns why the replacement did not happen; the installed loader is then unchanged.
pub fn replace_older_loader(version_dir: &Path) -> Result<Outcome, Error> {
    let Some(context) = LoaderContext::from_environment() else {
        return Ok(Outcome::NotNeeded);
    };
    let this_version = Version::of_this_build().map_err(Error::Version)?;
    decide_and_swap(&context, &this_version, version_dir)
}

fn decide_and_swap(context: &LoaderContext, this_version: &Version, version_dir: &Path) -> Result<Outcome, Error> {
    let loader_version: Version = context.loader_version.parse().map_err(Error::Version)?;
    if &loader_version >= this_version {
        return Ok(Outcome::NotNeeded);
    }
    let this = InstalledVersion { version: this_version.clone(), dir: version_dir.to_path_buf() };
    let carried = this.carried_loader_path();
    if !carried.exists() {
        return Err(Error::MissingCarriedLoader(carried));
    }
    swap_in_loader(&InstallLayout::new(&context.install_root), &carried).map_err(Error::Swap)?;
    Ok(Outcome::Replaced)
}

#[cfg(test)]
mod tests {
    use super::*;
    use denice_core::platform::loader_file_name;

    struct Install {
        _dir: tempfile::TempDir,
        root: PathBuf,
        version_dir: PathBuf,
    }

    fn install() -> Install {
        let dir = tempfile::tempdir().unwrap_or_else(|e| unreachable!("{e}"));
        let root = dir.path().join("Denice");
        let version_dir = root.join("versions/0.1.200-pre-release+bbbbbbb");
        std::fs::create_dir_all(&version_dir).unwrap_or_else(|e| unreachable!("{e}"));
        std::fs::write(root.join(loader_file_name()), b"old").unwrap_or_else(|e| unreachable!("{e}"));
        std::fs::write(version_dir.join(loader_file_name()), b"new").unwrap_or_else(|e| unreachable!("{e}"));
        Install { _dir: dir, root, version_dir }
    }

    fn version(text: &str) -> Version {
        text.parse().unwrap_or_else(|e| unreachable!("{e}"))
    }

    fn installed_loader(i: &Install) -> String {
        std::fs::read_to_string(i.root.join(loader_file_name())).unwrap_or_else(|e| unreachable!("{e}"))
    }

    #[test]
    fn an_older_loader_is_replaced() {
        let i = install();
        let context =
            LoaderContext { install_root: i.root.clone(), loader_version: "0.1.100-pre-release+aaaaaaa".into() };
        let outcome = decide_and_swap(&context, &version("0.1.200-pre-release+bbbbbbb"), &i.version_dir);
        assert!(matches!(outcome, Ok(Outcome::Replaced)));
        assert_eq!(installed_loader(&i), "new");
    }

    #[test]
    fn the_same_or_a_newer_loader_is_kept() {
        let i = install();
        for loader in ["0.1.200-pre-release+bbbbbbb", "0.1.300-pre-release+ccccccc"] {
            let context = LoaderContext { install_root: i.root.clone(), loader_version: loader.into() };
            let outcome = decide_and_swap(&context, &version("0.1.200-pre-release+bbbbbbb"), &i.version_dir);
            assert!(matches!(outcome, Ok(Outcome::NotNeeded)), "{loader}");
        }
        assert_eq!(installed_loader(&i), "old");
    }

    #[test]
    fn a_version_without_a_carried_loader_reports_it_and_keeps_the_old_one() {
        let i = install();
        std::fs::remove_file(i.version_dir.join(loader_file_name())).unwrap_or_else(|e| unreachable!("{e}"));
        let context =
            LoaderContext { install_root: i.root.clone(), loader_version: "0.1.100-pre-release+aaaaaaa".into() };
        let outcome = decide_and_swap(&context, &version("0.1.200-pre-release+bbbbbbb"), &i.version_dir);
        assert!(matches!(outcome, Err(Error::MissingCarriedLoader(_))));
        assert_eq!(installed_loader(&i), "old");
    }
}
