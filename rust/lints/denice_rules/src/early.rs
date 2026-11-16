// Copyright (c) Denice.ai. All rights reserved.

//! Rules that read the source before macros expand: `cfg` attributes and `cfg!`/`file!` calls disappear afterwards.

use crate::places::{file_of, is_product, on_list};
use crate::{Config, config};
use rustc_ast::{self as ast, AttrKind, BlockCheckMode, FnRetTy, ItemKind, Safety, TyKind, UnsafeSource};
use rustc_lint::{EarlyContext, EarlyLintPass};
use rustc_lint::Lint;
use rustc_session::{declare_lint, impl_lint_pass};
use rustc_span::Span;

declare_lint! {
    /// R1 (C# DN0008/DN0102): native code (`unsafe`) only in the allow-listed native modules.
    pub R1_UNSAFE_OUTSIDE_NATIVE_MODULES,
    Deny,
    "`unsafe` is allowed only in the native modules listed in rust/dylint.toml (unsafe_allowed)"
}

declare_lint! {
    /// R3 (C# DN0009/DN0101): branching on the target OS or architecture only in the allow-listed platform modules.
    pub R3_OS_BRANCHING_OUTSIDE_PLATFORM_MODULES,
    Deny,
    "OS or architecture `cfg` is allowed only in the platform modules listed in rust/dylint.toml (os_branching_allowed)"
}

declare_lint! {
    /// R7 (C# DN0002): no function in product code returns a tuple.
    pub R7_TUPLE_RETURN,
    Deny,
    "return a named struct, not a tuple"
}

declare_lint! {
    /// R8 (C# DN0026): no test is skipped (`#[ignore]`, or `ignore` under `cfg_attr`).
    pub R8_IGNORED_TEST,
    Deny,
    "tests must run on every platform; do not ignore them"
}

declare_lint! {
    /// R9 (C# DN0032): no coverage opt-out.
    pub R9_COVERAGE_OPT_OUT,
    Deny,
    "code may not opt out of coverage"
}

declare_lint! {
    /// R12 (C# DN0039): no caller file or line in product code.
    pub R12_CALLER_LOCATION,
    Deny,
    "product code may not record source file or line (`file!`, `line!`, `column!`, `#[track_caller]`)"
}

impl_lint_pass!(Rules => [
    R1_UNSAFE_OUTSIDE_NATIVE_MODULES,
    R3_OS_BRANCHING_OUTSIDE_PLATFORM_MODULES,
    R7_TUPLE_RETURN,
    R8_IGNORED_TEST,
    R9_COVERAGE_OPT_OUT,
    R12_CALLER_LOCATION,
]);

/// The early rules.
pub static LINTS: &[&Lint] = &[
    R1_UNSAFE_OUTSIDE_NATIVE_MODULES,
    R3_OS_BRANCHING_OUTSIDE_PLATFORM_MODULES,
    R7_TUPLE_RETURN,
    R8_IGNORED_TEST,
    R9_COVERAGE_OPT_OUT,
    R12_CALLER_LOCATION,
];

const OS_CFG_KEYS: [&str; 6] = ["target_os", "target_arch", "target_family", "target_env", "unix", "windows"];

/// The early rules' pass.
pub struct Rules {
    config: Config,
}

impl Rules {
    pub fn new() -> Self {
        Self { config: config() }
    }

    fn product_file(cx: &EarlyContext<'_>, span: Span) -> Option<String> {
        file_of(cx, span).filter(|file| is_product(file))
    }

    fn report(cx: &EarlyContext<'_>, lint: &'static Lint, span: Span, message: String) {
        clippy_utils::diagnostics::span_lint(cx, lint, span, message);
    }

    fn check_unsafe(&self, cx: &EarlyContext<'_>, span: Span) {
        if let Some(file) = file_of(cx, span)
            && is_product(&file)
            && !on_list(&file, &self.config.unsafe_allowed)
        {
            Self::report(cx, R1_UNSAFE_OUTSIDE_NATIVE_MODULES, span, format!("`unsafe` in {file}, which is not a native module (rust/dylint.toml unsafe_allowed)"));
        }
    }
}

fn mentions_os(tokens: &str) -> bool {
    OS_CFG_KEYS.iter().any(|key| {
        tokens.match_indices(key).any(|(at, _)| {
            let before = tokens[..at].chars().next_back();
            let after = tokens[at + key.len()..].chars().next();
            !before.is_some_and(|c| c.is_alphanumeric() || c == '_') && !after.is_some_and(|c| c.is_alphanumeric() || c == '_')
        })
    })
}

impl EarlyLintPass for Rules {
    fn check_attribute(&mut self, cx: &EarlyContext<'_>, attr: &ast::Attribute) {
        let AttrKind::Normal(normal) = &attr.kind else { return };
        let name = normal.item.path.segments.iter().map(|s| s.ident.name.to_string()).collect::<Vec<_>>().join("::");
        let args = rustc_ast_pretty::pprust::attribute_to_string(attr);
        let Some(file) = file_of(cx, attr.span) else { return };
        match name.as_str() {
            "cfg" | "cfg_attr" if mentions_os(&args) && is_product(&file) && !on_list(&file, &self.config.os_branching_allowed) => {
                Self::report(cx, R3_OS_BRANCHING_OUTSIDE_PLATFORM_MODULES, attr.span, format!("OS or architecture `{name}` in {file}, which is not a platform module (rust/dylint.toml os_branching_allowed)"));
            }
            _ => {}
        }
        if name == "ignore" || (name == "cfg_attr" && args.contains("ignore")) {
            Self::report(cx, R8_IGNORED_TEST, attr.span, "a test may not be ignored; it must run on every platform".to_owned());
        }
        if name == "coverage" || (name == "cfg_attr" && args.contains("coverage")) {
            Self::report(cx, R9_COVERAGE_OPT_OUT, attr.span, "code may not opt out of coverage".to_owned());
        }
        if name == "track_caller" && is_product(&file) {
            Self::report(cx, R12_CALLER_LOCATION, attr.span, "`#[track_caller]` records the caller's file and line".to_owned());
        }
    }

    fn check_mac(&mut self, cx: &EarlyContext<'_>, mac: &ast::MacCall) {
        let name = mac.path.segments.last().map(|s| s.ident.name.to_string()).unwrap_or_default();
        let Some(file) = Self::product_file(cx, mac.span()) else { return };
        if name == "cfg"
            && mentions_os(&rustc_ast_pretty::pprust::tts_to_string(&mac.args.tokens))
            && !on_list(&file, &self.config.os_branching_allowed)
        {
            Self::report(cx, R3_OS_BRANCHING_OUTSIDE_PLATFORM_MODULES, mac.span(), format!("OS or architecture `cfg!` in {file}, which is not a platform module (rust/dylint.toml os_branching_allowed)"));
        }
        if matches!(name.as_str(), "file" | "line" | "column") {
            Self::report(cx, R12_CALLER_LOCATION, mac.span(), format!("`{name}!` records a source location"));
        }
    }

    fn check_block(&mut self, cx: &EarlyContext<'_>, block: &ast::Block) {
        if matches!(block.rules, BlockCheckMode::Unsafe(UnsafeSource::UserProvided)) {
            self.check_unsafe(cx, block.span);
        }
    }

    fn check_item(&mut self, cx: &EarlyContext<'_>, item: &ast::Item) {
        match &item.kind {
            ItemKind::ForeignMod(_) => self.check_unsafe(cx, item.span),
            ItemKind::Impl(imp) if imp.of_trait.as_ref().is_some_and(|t| matches!(t.safety, Safety::Unsafe(_))) => self.check_unsafe(cx, item.span),
            ItemKind::Fn(function) => {
                if matches!(function.sig.header.safety, Safety::Unsafe(_)) {
                    self.check_unsafe(cx, item.span);
                }
                if let FnRetTy::Ty(ty) = &function.sig.decl.output
                    && matches!(&ty.kind, TyKind::Tup(elements) if !elements.is_empty())
                    && Self::product_file(cx, item.span).is_some()
                {
                    Self::report(cx, R7_TUPLE_RETURN, ty.span, "this function returns a tuple; return a named struct".to_owned());
                }
            }
            _ => {}
        }
    }

    fn check_impl_item(&mut self, cx: &EarlyContext<'_>, item: &ast::AssocItem) {
        if let ast::AssocItemKind::Fn(function) = &item.kind {
            if matches!(function.sig.header.safety, Safety::Unsafe(_)) {
                self.check_unsafe(cx, item.span);
            }
            if let FnRetTy::Ty(ty) = &function.sig.decl.output
                && matches!(&ty.kind, TyKind::Tup(elements) if !elements.is_empty())
                && Self::product_file(cx, item.span).is_some()
            {
                Self::report(cx, R7_TUPLE_RETURN, ty.span, "this method returns a tuple; return a named struct".to_owned());
            }
        }
    }
}
