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
mod tests {
    use super::Version;

    fn v(text: &str) -> Version {
        text.parse().unwrap_or_else(|e| unreachable!("{e}"))
    }

    #[test]
    fn higher_combined_number_wins() {
        assert!(v("0.1.108430999-pre-release+abcdef0") > v("0.1.108430176-pre-release+732fc53"));
    }

    #[test]
    fn release_outranks_its_own_pre_release() {
        assert!(v("0.1.5") > v("0.1.5-pre-release+aaaaaaa"));
    }

    #[test]
    fn higher_minor_wins_over_higher_combined() {
        assert!(v("0.2.1-pre-release+a") > v("0.1.999999999-pre-release+b"));
    }

    #[test]
    fn same_version_different_hash_is_ordered_but_not_equal() {
        let a = v("0.1.7-pre-release+aaaaaaa");
        let b = v("0.1.7-pre-release+bbbbbbb");
        assert_ne!(a, b);
        assert!(a < b);
    }

    #[test]
    fn display_round_trips() {
        let text = "0.1.108430176-pre-release+732fc53";
        assert_eq!(v(text).to_string(), text);
    }

    #[test]
    fn rejects_non_versions() {
        assert!("versions".parse::<Version>().is_err());
        assert!("0.1.5.partial".parse::<Version>().is_err());
    }

    #[test]
    fn this_build_has_a_valid_version() {
        assert!(Version::of_this_build().is_ok());
    }
}
