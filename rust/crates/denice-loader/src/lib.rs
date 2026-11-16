// Copyright (c) Denice.ai. All rights reserved.

//! The Denice loader, `denice`. Tim (14 November 2026): "Initially, its job is to find the highest known version
//! and load it. … the loader finds a higher-numbered version and reloads it". Per pkg-a0206b25f619 it stays
//! running as the parent of the version it starts, restarts into a newer version when Denice Runtime asks, and
//! falls back from a version that fails to start. Per pkg-f543b9586645 it hands over to a replaced loader at once.

pub mod launcher;

use denice_core::layout::{InstallLayout, InstalledVersion};
use denice_core::protocol::{
    ENV_INSTALL_ROOT, ENV_LOADER_PATH, ENV_LOADER_VERSION, ENV_VERSION_DIR, EXIT_LOADER_REPLACED,
    EXIT_RESTART_INTO_NEWER_VERSION,
};
use denice_core::swap::remove_previous_loader;
use denice_core::version::Version;
use std::ffi::OsString;
use std::path::PathBuf;

/// Exit code when no version could be started at all.
pub const EXIT_NO_USABLE_VERSION: i32 = 1;

/// Starts and supervises Denice versions. `L` runs processes, so tests can stand in for real versions.
pub trait Launcher {
    /// Runs `program` with `args` and the extra environment, inheriting the console, and returns its exit code.
    ///
    /// # Errors
    /// Returns the error if the program could not be started at all.
    fn run(
        &self,
        program: &std::path::Path,
        args: &[OsString],
        env: &[(&'static str, OsString)],
    ) -> std::io::Result<i32>;

    /// Hands over to the new loader at `loader` with `args`: on Unix this process becomes the new loader; on
    /// Windows it starts the new loader, waits and returns its exit code.
    ///
    /// # Errors
    /// Returns the error if the new loader could not be started.
    fn hand_over(&self, loader: &std::path::Path, args: &[OsString]) -> std::io::Result<i32>;

    /// Reports a problem to the user.
    fn report(&self, message: &str);
}

/// Who this loader is.
pub struct Loader<'a, L: Launcher> {
    /// The install folder this loader serves.
    pub layout: InstallLayout,
    /// This loader's own path.
    pub loader_path: PathBuf,
    /// The version this loader was built with.
    pub loader_version: Version,
    /// Runs processes.
    pub launcher: &'a L,
}

impl<L: Launcher> Loader<'_, L> {
    /// Runs Denice with `args` and returns the exit code for this process.
    #[must_use]
    pub fn run(&self, args: &[OsString]) -> i32 {
        let _ = remove_previous_loader(&self.layout);
        let mut last_failure: Option<i32> = None;
        let mut previous: Option<Version> = None;
        loop {
            let current = match self.layout.highest_usable_version() {
                Ok(Some(current)) => current,
                Ok(None) => {
                    self.launcher.report(&format!(
                        "no usable Denice version is installed in {}",
                        self.layout.versions_dir().display()
                    ));
                    return last_failure.unwrap_or(EXIT_NO_USABLE_VERSION);
                }
                Err(e) => {
                    self.launcher.report(&format!("cannot read {}: {e}", self.layout.versions_dir().display()));
                    return EXIT_NO_USABLE_VERSION;
                }
            };
            if let Some(previous) = &previous
                && current.version <= *previous
                && last_failure.is_none()
            {
                self.launcher
                    .report(&format!("Denice {previous} asked to restart into a newer version, but none is installed"));
                return EXIT_RESTART_INTO_NEWER_VERSION;
            }
            self.remove_older_than(&current);

            let code = match self.launcher.run(&current.entry_path(), args, &self.environment(&current)) {
                Ok(code) => code,
                Err(e) => {
                    self.launcher.report(&format!("cannot start {}: {e}", current.entry_path().display()));
                    self.give_up_on(&current);
                    last_failure = Some(EXIT_NO_USABLE_VERSION);
                    continue;
                }
            };
            // pkg-a0206b25f619: the previous version is kept until the new one has started once.
            self.remove_older_than(&current);
            match code {
                EXIT_RESTART_INTO_NEWER_VERSION => {
                    previous = Some(current.version);
                    last_failure = None;
                }
                EXIT_LOADER_REPLACED => {
                    return match self.launcher.hand_over(&self.layout.loader_program_path(), args) {
                        Ok(code) => code,
                        Err(e) => {
                            self.launcher.report(&format!("cannot start the new loader: {e}"));
                            EXIT_NO_USABLE_VERSION
                        }
                    };
                }
                code if !current.has_started() => {
                    self.launcher.report(&format!(
                        "Denice {} failed to start (exit code {code}); falling back to the previous version",
                        current.version
                    ));
                    self.give_up_on(&current);
                    last_failure = Some(code);
                    previous = None;
                }
                code => return code,
            }
        }
    }

    fn remove_older_than(&self, current: &InstalledVersion) {
        if let Err(e) = self.layout.remove_versions_older_than(current) {
            self.launcher.report(&format!("could not remove older versions: {e}"));
        }
    }

    fn give_up_on(&self, version: &InstalledVersion) {
        if let Err(e) = version.mark_bad() {
            self.launcher.report(&format!("cannot mark {} as failed: {e}", version.dir.display()));
        }
    }

    fn environment(&self, version: &InstalledVersion) -> Vec<(&'static str, OsString)> {
        vec![
            (ENV_INSTALL_ROOT, self.layout.root().as_os_str().to_owned()),
            (ENV_LOADER_PATH, self.loader_path.as_os_str().to_owned()),
            (ENV_LOADER_VERSION, OsString::from(self.loader_version.to_string())),
            (ENV_VERSION_DIR, version.dir.as_os_str().to_owned()),
        ]
    }
}
