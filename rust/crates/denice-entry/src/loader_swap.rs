// Copyright (c) Denice.ai. All rights reserved.

//! "a higher-numbered version can rename the loader and unpack a new loader to replace it" (Tim, 14 November
//! 2026). The loader tells the entry its own path and version; when this version is higher, the entry swaps in
//! the loader it carries and returns, so the old loader can hand over to the new one right away
//! (pkg-f543b9586645, "Hand over now").

use denice_core::layout::{InstallLayout, InstalledVersion};
use denice_core::protocol::{ENV_INSTALL_ROOT, ENV_LOADER_PATH, ENV_LOADER_VERSION};
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
    /// The folder that holds `versions/`.
    pub install_root: PathBuf,
    /// The running loader program.
    pub loader_path: PathBuf,
    /// The running loader's version.
    pub loader_version: String,
}

impl LoaderContext {
    /// Reads the loader's context, or `None` when this entry was started directly (not by a loader).
    #[must_use]
    pub fn from_environment() -> Option<Self> {
        let install_root = std::env::var_os(ENV_INSTALL_ROOT)?;
        let loader_path = std::env::var_os(ENV_LOADER_PATH)?;
        let loader_version = std::env::var(ENV_LOADER_VERSION).ok()?;
        Some(Self {
            install_root: PathBuf::from(install_root),
            loader_path: PathBuf::from(loader_path),
            loader_version,
        })
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
    let Some(installed_loader) = denice_core::platform::loader_artifact_of(&context.loader_path) else {
        return Err(Error::MissingCarriedLoader(context.loader_path.clone()));
    };
    swap_in_loader(&InstallLayout::with_loader(&context.install_root, installed_loader), &carried)
        .map_err(Error::Swap)?;
    Ok(Outcome::Replaced)
}

#[cfg(test)]
#[path = "loader_swap_tests.rs"]
mod tests;
