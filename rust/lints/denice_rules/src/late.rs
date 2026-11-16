// Copyright (c) Denice.ai. All rights reserved.

//! Rules that need the compiler's resolved paths and types.

use crate::places::{file_of, is_product, on_list};
use crate::{Config, WORKSPACE_CRATES, config};
use rustc_hir::def::Res;
use rustc_hir::{Expr, ExprKind, ForeignItem, ForeignItemKind, HirId, Item, ItemKind, Path};
use rustc_lint::{LateContext, LateLintPass};
use rustc_middle::ty::{self, Ty};
use rustc_lint::Lint;
use rustc_session::{declare_lint, impl_lint_pass};
use rustc_span::Span;

declare_lint! {
    /// R2 (C# DN0102/DN0103): native signatures use only plain C types: integers, floats, raw pointers, `#[repr(C)]`
    /// types; never `bool`, `char`, references, strings or slices.
    pub R2_NON_C_NATIVE_SIGNATURE,
    Deny,
    "a native (`extern`) signature may use only integers, floats, raw pointers and #[repr(C)] types"
}

declare_lint! {
    /// R4 (C# DN0011): processes and environment variables only in their owner modules.
    pub R4_RAW_PRIMITIVE_OUTSIDE_OWNER,
    Deny,
    "starting processes and reading environment variables are allowed only in their owner modules (rust/dylint.toml)"
}

declare_lint! {
    /// R5 (C# DN0020): no environment-dependent paths in product code; the program's own path only in `main.rs`.
    pub R5_ENVIRONMENT_PATH,
    Deny,
    "product code may not read the temp, current or home folder; current_exe only where rust/dylint.toml allows it"
}

declare_lint! {
    /// R6 (C# DN0015/DN0038): no clock reads or sleeps in product code.
    pub R6_CLOCK_OR_SLEEP,
    Deny,
    "product code may not read the clock or sleep"
}

declare_lint! {
    /// R10 (C# DN0018): test-only crates only in test code.
    pub R10_TEST_CRATE_IN_PRODUCT,
    Deny,
    "a test-only crate may be used only from test code"
}

declare_lint! {
    /// R11 (C# DN0023/DN0029/DN0040/DN0041): workspace crates depend only in the allowed direction.
    pub R11_CRATE_DIRECTION,
    Deny,
    "a workspace crate may use only the workspace crates rust/dylint.toml allows (crate_dependencies)"
}

impl_lint_pass!(Rules => [
    R2_NON_C_NATIVE_SIGNATURE,
    R4_RAW_PRIMITIVE_OUTSIDE_OWNER,
    R5_ENVIRONMENT_PATH,
    R6_CLOCK_OR_SLEEP,
    R10_TEST_CRATE_IN_PRODUCT,
    R11_CRATE_DIRECTION,
]);

/// The late rules.
pub static LINTS: &[&Lint] = &[
    R2_NON_C_NATIVE_SIGNATURE,
    R4_RAW_PRIMITIVE_OUTSIDE_OWNER,
    R5_ENVIRONMENT_PATH,
    R6_CLOCK_OR_SLEEP,
    R10_TEST_CRATE_IN_PRODUCT,
    R11_CRATE_DIRECTION,
];

const TEST_ONLY_CRATES: [&str; 1] = ["tempfile"];
const PROCESS: [&str; 1] = ["std::process::Command::new"];
const ENV_READS: [&str; 4] = ["std::env::var", "std::env::var_os", "std::env::vars", "std::env::vars_os"];
const ENV_PATHS: [&str; 3] = ["std::env::temp_dir", "std::env::current_dir", "std::env::home_dir"];
const CURRENT_EXE: &str = "std::env::current_exe";
const CLOCK: [&str; 3] = ["std::time::SystemTime::now", "std::time::Instant::now", "std::thread::sleep"];

/// The late rules' pass.
pub struct Rules {
    config: Config,
}

impl Rules {
    pub fn new() -> Self {
        Self { config: config() }
    }

    fn report(cx: &LateContext<'_>, lint: &'static Lint, span: Span, message: String) {
        clippy_utils::diagnostics::span_lint(cx, lint, span, message);
    }

    fn product_file(cx: &LateContext<'_>, span: Span) -> Option<String> {
        file_of(cx, span).filter(|file| is_product(file))
    }

    fn check_call(&self, cx: &LateContext<'_>, callee: &str, span: Span) {
        let Some(file) = Self::product_file(cx, span) else { return };
        if PROCESS.contains(&callee) && !on_list(&file, &self.config.process_allowed) {
            Self::report(cx, R4_RAW_PRIMITIVE_OUTSIDE_OWNER, span, format!("`{callee}` in {file}: processes start only in rust/dylint.toml process_allowed"));
        }
        if ENV_READS.contains(&callee) && !on_list(&file, &self.config.env_read_allowed) {
            Self::report(cx, R4_RAW_PRIMITIVE_OUTSIDE_OWNER, span, format!("`{callee}` in {file}: environment variables are read only in rust/dylint.toml env_read_allowed"));
        }
        if ENV_PATHS.contains(&callee) {
            Self::report(cx, R5_ENVIRONMENT_PATH, span, format!("`{callee}` depends on the environment; product code may not use it"));
        }
        if callee == CURRENT_EXE && !on_list(&file, &self.config.current_exe_allowed) {
            Self::report(cx, R5_ENVIRONMENT_PATH, span, format!("`{callee}` in {file}: only rust/dylint.toml current_exe_allowed may ask for the program's path"));
        }
        if CLOCK.contains(&callee) {
            Self::report(cx, R6_CLOCK_OR_SLEEP, span, format!("`{callee}` reads the clock or sleeps; product code may not"));
        }
    }

    fn check_native_types<'tcx>(cx: &LateContext<'tcx>, sig: ty::FnSig<'tcx>, span: Span) {
        for ty in sig.inputs().iter().chain(std::iter::once(&sig.output())) {
            if let Some(why) = non_c(cx, *ty) {
                Self::report(cx, R2_NON_C_NATIVE_SIGNATURE, span, format!("native signature uses `{ty}`: {why}"));
            }
        }
    }
}

fn non_c<'tcx>(cx: &LateContext<'tcx>, ty: Ty<'tcx>) -> Option<&'static str> {
    match ty.kind() {
        ty::Int(_) | ty::Uint(_) | ty::Float(_) | ty::RawPtr(..) | ty::Never | ty::FnPtr(..) => None,
        ty::Tuple(elements) if elements.is_empty() => None,
        ty::Adt(adt, _) if adt.repr().c() || adt.repr().transparent() => None,
        ty::Adt(adt, args) if cx.tcx.is_diagnostic_item(rustc_span::sym::Option, adt.did()) => {
            // Option<fn(..)> is the C nullable function pointer.
            matches!(args.type_at(0).kind(), ty::FnPtr(..)).then_some(()).map_or(Some("only Option<fn> is C-compatible"), |()| None)
        }
        ty::Bool => Some("a C bool's size differs by platform (C# DN0103); use an integer"),
        ty::Char => Some("Rust char is not a C type"),
        ty::Ref(..) => Some("references are not C types; use a raw pointer"),
        _ => Some("not a C type"),
    }
}

impl<'tcx> LateLintPass<'tcx> for Rules {
    fn check_expr(&mut self, cx: &LateContext<'tcx>, expr: &'tcx Expr<'tcx>) {
        if let ExprKind::Call(callee, _) = expr.kind
            && let ExprKind::Path(qpath) = &callee.kind
            && let Res::Def(_, def_id) = cx.qpath_res(qpath, callee.hir_id)
        {
            self.check_call(cx, &cx.tcx.def_path_str(def_id), expr.span);
        }
    }

    fn check_path(&mut self, cx: &LateContext<'tcx>, path: &Path<'tcx>, _: HirId) {
        let Some(def_id) = path.res.opt_def_id() else { return };
        if def_id.is_local() {
            return;
        }
        let used = cx.tcx.crate_name(def_id.krate).to_string();
        let Some(file) = file_of(cx, path.span) else { return };
        if TEST_ONLY_CRATES.contains(&used.as_str()) && is_product(&file) {
            Self::report(cx, R10_TEST_CRATE_IN_PRODUCT, path.span, format!("`{used}` is a test-only crate; {file} is product code"));
        }
        let this = cx.tcx.crate_name(rustc_span::def_id::LOCAL_CRATE).to_string();
        if WORKSPACE_CRATES.contains(&used.as_str()) && WORKSPACE_CRATES.contains(&this.as_str()) {
            let allowed = self.config.crate_dependencies.iter().find(|c| c.krate == this).is_some_and(|c| c.may_use.contains(&used));
            if !allowed {
                Self::report(cx, R11_CRATE_DIRECTION, path.span, format!("`{this}` may not use `{used}` (rust/dylint.toml crate_dependencies)"));
            }
        }
    }

    fn check_foreign_item(&mut self, cx: &LateContext<'tcx>, item: &'tcx ForeignItem<'tcx>) {
        if let ForeignItemKind::Fn(..) = item.kind {
            let sig = cx.tcx.fn_sig(item.owner_id.to_def_id()).instantiate_identity().skip_binder();
            Self::check_native_types(cx, sig, item.span);
        }
    }

    fn check_item(&mut self, cx: &LateContext<'tcx>, item: &'tcx Item<'tcx>) {
        if let ItemKind::TyAlias(..) = item.kind
            && let ty::FnPtr(sig_tys, header) = cx.tcx.type_of(item.owner_id.to_def_id()).instantiate_identity().skip_normalization().kind()
            && header.abi() != rustc_abi::ExternAbi::Rust
        {
            let sig = sig_tys.with(*header).skip_binder();
            Self::check_native_types(cx, sig, item.span);
        }
    }
}
