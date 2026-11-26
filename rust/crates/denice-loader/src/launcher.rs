// Copyright (c) Denice.ai. All rights reserved.

//! Running real processes. The loader stays the parent: the version shares the console, and Ctrl+C reaches the
//! version (which decides what to do) while the loader keeps waiting for its exit code.

use crate::Launcher;
use std::ffi::OsString;
use std::path::Path;
use std::process::{Command, ExitStatus};

/// Runs real processes.
pub struct ProcessLauncher;

impl Launcher for ProcessLauncher {
    fn run(&self, program: &Path, args: &[OsString], env: &[(&'static str, OsString)]) -> std::io::Result<i32> {
        let mut command = Command::new(program);
        let _ = command.args(args).envs(env.iter().map(|(k, v)| (*k, v)));
        // ImageIO must resolve Apple's PNG library, not an inherited development override.
        // Limit the change to the child; the user's shell environment remains untouched.
        #[cfg(target_os = "macos")]
        let _ = command.env_remove("DYLD_LIBRARY_PATH");
        let mut child = command.spawn()?;
        let status = {
            let _ignore = interrupts::IgnoredWhileWaiting::start();
            child.wait()?
        };
        Ok(exit_code(status))
    }

    fn hand_over(&self, loader: &Path, args: &[OsString]) -> std::io::Result<i32> {
        hand_over(loader, args)
    }

    fn report(&self, message: &str) {
        eprintln!("denice: {message}");
    }
}

#[cfg(unix)]
fn hand_over(loader: &Path, args: &[OsString]) -> std::io::Result<i32> {
    use std::os::unix::process::CommandExt;
    // exec replaces this process with the new loader (same process id, same console); it only returns on failure.
    Err(Command::new(loader).args(args).exec())
}

#[cfg(windows)]
fn hand_over(loader: &Path, args: &[OsString]) -> std::io::Result<i32> {
    // Windows cannot replace a running process image: start the new loader, wait, and pass its exit code on.
    let mut child = Command::new(loader).args(args).spawn()?;
    let status = {
        let _ignore = interrupts::IgnoredWhileWaiting::start();
        child.wait()?
    };
    Ok(exit_code(status))
}

#[cfg(unix)]
fn exit_code(status: ExitStatus) -> i32 {
    use std::os::unix::process::ExitStatusExt;
    status.code().or_else(|| status.signal().map(|signal| 128 + signal)).unwrap_or(1)
}

#[cfg(windows)]
fn exit_code(status: ExitStatus) -> i32 {
    status.code().unwrap_or(1)
}

#[cfg(unix)]
mod interrupts {
    //! While the loader waits, Ctrl+C and Ctrl+\ go to the whole foreground process group; the version handles
    //! them and the loader must not die first and leave the shell waiting on nothing.

    #![allow(unsafe_code)]

    pub struct IgnoredWhileWaiting {
        previous: [libc::sighandler_t; 2],
    }

    const SIGNALS: [libc::c_int; 2] = [libc::SIGINT, libc::SIGQUIT];

    impl IgnoredWhileWaiting {
        /// Ignores the signals until dropped. Called after the child is spawned, so the child keeps the defaults.
        pub fn start() -> Self {
            // SAFETY: setting a signal disposition to SIG_IGN is async-signal-safe and has no other effects.
            let previous = SIGNALS.map(|signal| unsafe { libc::signal(signal, libc::SIG_IGN) });
            Self { previous }
        }
    }

    impl Drop for IgnoredWhileWaiting {
        fn drop(&mut self) {
            for (signal, handler) in SIGNALS.iter().zip(self.previous) {
                // SAFETY: restores the disposition saved in start().
                let _ = unsafe { libc::signal(*signal, handler) };
            }
        }
    }
}

#[cfg(windows)]
mod interrupts {
    //! On Windows, Ctrl+C is delivered to every process on the console. The loader ignores it while it waits
    //! (set after the child is spawned, so the version keeps the default and decides for itself), so the loader
    //! never ends before the version and leaves it orphaned.

    #![allow(unsafe_code)]

    use windows_sys::Win32::System::Console::SetConsoleCtrlHandler;

    pub struct IgnoredWhileWaiting;

    impl IgnoredWhileWaiting {
        pub fn start() -> Self {
            // SAFETY: a null handler with TRUE makes this process ignore Ctrl+C; no callback is registered.
            let _ = unsafe { SetConsoleCtrlHandler(None, 1) };
            Self
        }
    }

    impl Drop for IgnoredWhileWaiting {
        fn drop(&mut self) {
            // SAFETY: restores normal Ctrl+C processing for this process.
            let _ = unsafe { SetConsoleCtrlHandler(None, 0) };
        }
    }
}
