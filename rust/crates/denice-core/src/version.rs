// Copyright (c) Denice.ai. All rights reserved.

//! The Denice product version: `MAJOR.MINOR.COMBINED[-channel]+hash`, ordered by `SemVer` precedence.

use std::cmp::Ordering;
use std::fmt;
use std::str::FromStr;

/// The version this binary was built with (from `DENICE_VERSION`, see build.rs).
pub const BUILD_VERSION: &str = env!("DENICE_BUILD_VERSION");

/// A Denice product version. Ordering is `SemVer` precedence; build metadata (`+hash`) breaks ties only so
/// that the order is total and stable, never to prefer one build over another of the same version.
#[derive(Clone, Debug, PartialEq, Eq, Hash)]
pub struct Version(semver::Version);

impl Version {
    /// The version this binary was built with.
    ///
    /// # Errors
    /// Returns an error if the stamped build version is not valid `SemVer` (a broken build).
    pub fn of_this_build() -> Result<Self, ParseVersionError> {
        BUILD_VERSION.parse()
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> Ordering {
        self.0.cmp_precedence(&other.0).then_with(|| self.0.build.cmp(&other.0.build))
    }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl fmt::Display for Version {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        self.0.fmt(f)
    }
}

/// The text was not a `SemVer` version.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParseVersionError(String);

impl fmt::Display for ParseVersionError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "not a Denice version: {}", self.0)
    }
}

impl std::error::Error for ParseVersionError {}

impl FromStr for Version {
    type Err = ParseVersionError;

    fn from_str(text: &str) -> Result<Self, Self::Err> {
        semver::Version::parse(text).map(Version).map_err(|_| ParseVersionError(text.to_owned()))
    }
}

#[cfg(test)]
#[path = "version_tests.rs"]
mod tests;
