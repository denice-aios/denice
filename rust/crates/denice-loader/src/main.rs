// Copyright (c) Denice.ai. All rights reserved.

//! `denice`: the Denice loader. Finds the install it belongs to, then starts and supervises the highest
//! installed version (see the library for the rules).

use denice_core::layout::InstallLayout;
use denice_core::version::Version;
use denice_loader::Loader;
use denice_loader::launcher::ProcessLauncher;
use std::ffi::OsString;

fn main() {
    let args: Vec<OsString> = std::env::args_os().skip(1).collect();
    denice_core::platform::exit_process(run(&args));
}

fn run(args: &[OsString]) -> i32 {
    let launcher = ProcessLauncher;
    // Resolve links: on macOS current_exe() returns the path it was started through, and the Terminal link
    // ~/.local/bin/denice must still lead to the loader inside Denice.app.
    let loader_path = match std::env::current_exe().and_then(std::fs::canonicalize) {
        Ok(path) => path,
        Err(e) => {
            eprintln!("denice: cannot find this program's own path: {e}");
            return denice_loader::EXIT_NO_USABLE_VERSION;
        }
    };
    let Some(layout) = InstallLayout::of_loader_program(&loader_path) else {
        eprintln!("denice: this program is not inside a Denice install");
        return denice_loader::EXIT_NO_USABLE_VERSION;
    };
    let loader_version = match Version::of_this_build() {
        Ok(version) => version,
        Err(e) => {
            eprintln!("denice: {e}");
            return denice_loader::EXIT_NO_USABLE_VERSION;
        }
    };
    let loader = Loader { layout, loader_path, loader_version, launcher: &launcher };
    loader.run(args)
}
