// Copyright (c) Denice.ai. All rights reserved.

//! The versioned Denice entry, `denice-v<version>-<rid>`. The build names this file itself
//! (`cargo rustc -- --emit=link=<path>`, eng/build-rust.sh). Started by the loader, it first replaces an older
//! loader with the one its version carries, then hosts .NET and runs Denice Runtime.

mod hosting;
mod loader_swap;

use std::ffi::OsString;

fn main() {
    let args: Vec<OsString> = std::env::args_os().skip(1).collect();
    // Keeps the full exit code (Windows codes are 32-bit) and runs the C runtime's exit handlers.
    denice_core::platform::exit_process(run(&args));
}

fn run(args: &[OsString]) -> i32 {
    let host_path = match std::env::current_exe() {
        Ok(path) => path,
        Err(e) => return report(&format!("cannot find this program's own path: {e}")),
    };
    let Some(version_dir) = host_path.parent().map(std::path::Path::to_path_buf) else {
        return report("this program has no containing folder");
    };

    match loader_swap::replace_older_loader(&version_dir) {
        Ok(loader_swap::Outcome::Replaced) => return denice_core::protocol::EXIT_LOADER_REPLACED,
        Ok(loader_swap::Outcome::NotNeeded) => {}
        Err(e) => {
            // The old loader stays in place (the swap restores it on failure); running on is safe.
            eprintln!("denice: the loader was not replaced: {e}");
        }
    }

    let request = hosting::HostRequest { host_path: &host_path, version_dir: &version_dir, args };
    match hosting::run(&request) {
        Ok(code) => code,
        Err(e) => report(&e.to_string()),
    }
}

/// The exit code for a start failure (the loader then falls back to the previous version).
const EXIT_START_FAILED: i32 = 1;

fn report(message: &str) -> i32 {
    eprintln!("denice: {message}");
    EXIT_START_FAILED
}
