// Copyright (c) Denice.ai. All rights reserved.

//! Denice's custom Rust rules R1-R12 (rust/RULES.md), the counterparts of the C# analyzers in analyzers/Denice.Analyzers.
//! Tim, 14 November 2026: "Bring the full infrastructure into the Rust application and align it with the existing .NET
//! infrastructure, including custom-rule management and testing." Tool: dylint (pkg-697310396f74 rust-rules).
//!
//! Product code is everything except test files (`*_tests.rs`, anything under a `tests` folder) and build scripts.
//! The allow-lists live in rust/dylint.toml, one owner per list.

#![feature(rustc_private)]
#![warn(unused_extern_crates)]

extern crate rustc_abi;
extern crate rustc_ast;
extern crate rustc_ast_pretty;
extern crate rustc_hir;
extern crate rustc_lint;
extern crate rustc_middle;
extern crate rustc_session;
extern crate rustc_span;

mod early;
mod late;
mod places;

use serde::Deserialize;

dylint_linting::dylint_library!();

/// The allow-lists (rust/dylint.toml, table `denice_rules`). Each entry is a path suffix such as
/// `crates/denice-entry/src/hosting.rs`; a `*` segment matches any one folder name.
#[derive(Debug, Default, Deserialize)]
#[serde(default)]
pub struct Config {
    /// R1: files that may contain `unsafe` (native calls).
    pub unsafe_allowed: Vec<String>,
    /// R3: files that may branch on the target OS or architecture.
    pub os_branching_allowed: Vec<String>,
    /// R4: files that may start processes (`std::process::Command`).
    pub process_allowed: Vec<String>,
    /// R4: files that may read environment variables.
    pub env_read_allowed: Vec<String>,
    /// R5: files that may ask for this program's own path (`std::env::current_exe`).
    pub current_exe_allowed: Vec<String>,
    /// R11: for each crate, the workspace crates it may use.
    pub crate_dependencies: Vec<CrateDependencies>,
}

/// One crate's allowed workspace dependencies (R11).
#[derive(Debug, Default, Deserialize)]
#[serde(default)]
pub struct CrateDependencies {
    /// The crate (as Rust names it, with underscores).
    pub krate: String,
    /// The workspace crates it may use.
    pub may_use: Vec<String>,
}

/// Every Denice workspace crate (R11 governs uses among these).
pub const WORKSPACE_CRATES: [&str; 3] = ["denice_core", "denice_loader", "denice_entry"];

#[allow(clippy::no_mangle_with_rust_abi)]
#[unsafe(no_mangle)]
pub fn register_lints(sess: &rustc_session::Session, lint_store: &mut rustc_lint::LintStore) {
    dylint_linting::init_config(sess);
    lint_store.register_lints(early::LINTS);
    lint_store.register_lints(late::LINTS);
    lint_store.register_pre_expansion_lint_pass(Box::new(|| Box::new(early::Rules::new())));
    lint_store.register_late_lint_pass(Box::new(|_| Box::new(late::Rules::new())));
}

fn config() -> Config {
    dylint_linting::config_or_default::<Config>(env!("CARGO_PKG_NAME"))
}

#[test]
fn ui() {
    dylint_testing::ui::Test::src_base(env!("CARGO_PKG_NAME"), "ui")
        .dylint_toml(include_str!("../ui/dylint.toml"))
        .run();
}
