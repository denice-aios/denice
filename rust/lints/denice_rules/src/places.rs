// Copyright (c) Denice.ai. All rights reserved.

//! Where a piece of code lives: product or test, and whether a file is on an allow-list.

use rustc_lint::LintContext;
use rustc_span::{FileName, Span};

/// The source file of `span`, with `/` separators, or `None` for code that has no real file (macro output).
pub fn file_of(cx: &impl LintContext, span: Span) -> Option<String> {
    match cx.sess().source_map().span_to_filename(span) {
        FileName::Real(real) => real.local_path().map(|path| path.to_string_lossy().replace('\\', "/")),
        _ => None,
    }
}

/// Test code: `*_tests.rs` files, anything under a `tests` folder, and build scripts are not product code.
pub fn is_product(file: &str) -> bool {
    !(file.ends_with("_tests.rs") || file.contains("/tests/") || file.ends_with("/build.rs") || file == "build.rs")
}

/// Whether `file` matches one of the path suffixes (`*` matches one folder name).
pub fn on_list(file: &str, list: &[String]) -> bool {
    let parts: Vec<&str> = file.split('/').collect();
    list.iter().any(|entry| {
        let wanted: Vec<&str> = entry.split('/').collect();
        parts.len() >= wanted.len()
            && parts[parts.len() - wanted.len()..].iter().zip(&wanted).all(|(have, want)| *want == "*" || have == want)
    })
}
