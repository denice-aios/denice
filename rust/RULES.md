# C# custom rules and their Rust counterparts (rust/REQUIREMENTS.md F6)

Tim, 14 November 2026: "Bring the full infrastructure into the Rust application and align it with the existing .NET
infrastructure, including custom-rule management and testing."

Every custom C# rule (analyzers/Denice.Analyzers) is listed. Each has a Rust counterpart (`R` id) that is built and
tested with the tool Tim chooses in pkg-697310396f74 decision 4, or a recorded reason it cannot apply to the Rust code.
A reason is about the Rust code's shape, never about effort. A rule marked "not applicable" becomes applicable the
moment the Rust code gains the construct it governs, and is then added here.

Already enforced without a custom rule: `unsafe_code = "deny"` (workspace lints), clippy pedantic with warnings as
errors, no `unwrap`/`expect`/`panic` in product code, rustfmt (eng/rust-checks.sh).

| C# rule | What it enforces | Rust counterpart | Status |
|---|---|---|---|
| DN0008 | Native interop only in the CrossPlatform libraries | R1: `#[allow(unsafe_code)]` only in the allow-listed native modules (`denice-entry/src/hosting.rs`, `denice-loader/src/launcher.rs`, `denice-core/src/platform.rs` for the C runtime exit) | built |
| DN0102 | P/Invoke signatures must be blittable | R2: `extern "C"` fn types and `extern` blocks use only primitive, raw-pointer and `#[repr(C)]` types | built |
| DN0103 | macOS native bool marshalled as I1 | R2 covers it: no `bool` in an `extern "C"` signature | built |
| DN0009 | No OS branching outside CrossPlatform | R3: `cfg(target_os/unix/windows/target_arch)` only in allow-listed platform modules (`platform.rs`, `launcher.rs`, `hosting.rs` native strings) | built |
| DN0101 | OS-divergent filesystem calls only in the per-OS file system | R3 covers it: OS-specific `std::os::*` imports only in the same allow-listed modules | built |
| DN0011 | Raw OS/CLR primitives only in their single owner | R4: `std::process::Command` only in `launcher.rs` and the entry's hosting; `std::env::var*` only in the entry's `LoaderContext` and `main.rs` files | built |
| DN0020 | `Path` members that read the environment only through their owner | R5: `std::env::temp_dir`, `current_dir`, `home_dir` not used in product code (`current_exe` only in `main.rs`) | built |
| DN0015 | Time read only through the injected clock | R6: no `SystemTime::now` / `Instant::now` in product code (the loader has no clock today; a clock module would be the single owner) | built |
| DN0038 | Timeouts through the injected TimeProvider | R6 covers it: no `thread::sleep` or timed waits in product code | built |
| DN0002 | Return types must not be tuples | R7: no function in product code returns a tuple | built |
| DN0026 | No OS-specific test skipping | R8: no `#[ignore]` and no `cfg_attr(..., ignore)` in tests | built |
| DN0032 | No coverage opt-out | R9: no `coverage(off)` attribute; eng/rust-checks.sh excludes only `_tests.rs` files and `tests/` folders | built |
| DN0018 | Test helpers only from test assemblies | Enforced by Cargo: `tempfile` is a dev-dependency only; R10 checks no test-only crate appears under `[dependencies]` | built |
| DN0023, DN0029, DN0040, DN0041 | One door between assemblies | R11: crate dependency direction: `denice-loader` and `denice-entry` depend only on `denice-core` among workspace crates; `denice-core` depends on none (Cargo already forbids cycles) | built |
| DN0039 | No caller file path | R12: no `file!()`, `line!()` or `#[track_caller]` in product code | built |
| DN0001, DN0003, DN0004, DN0005, DN0006, DN0007 | The C# contract pattern: interfaces in Abstractions, private constructors, no exposed concrete types | Not applicable: the Rust code has no contract assembly and no interface/implementation pairs reached by consumers; its one trait seam (`Launcher`, `SwapFiles`) exists for tests | n/a |
| DN0010, DN0017, DN0019, DN0022, DN0024, DN0025, DN0030, DN0031, DN0034 | The C# `ISystemServices` container: one composition point, one owner per interface, no static holders, no service parameters | Not applicable: the Rust programs have no service container; each `main.rs` is the only composition point and passes plain values | n/a |
| DN0035, DN0036 | Test fixtures must not seed literal fake roots or case modes | Not applicable: the Rust tests use real temporary folders (`tempfile`), never a seeded fake file system | n/a |

Built as dylint lints (Tim, pkg-697310396f74 rust-rules: "dylint now") in rust/lints/denice_rules, on the nightly pinned in
its rust-toolchain.toml. Allow-lists: rust/dylint.toml. Tests: rust/lints/denice_rules/ui (one fixture per rule with its
expected findings, and an allowed fixture that must stay silent) and tests/fixture_workspace.rs (R10, R11 over real
crates). eng/rust-checks.sh runs the rules' tests, then the rules over every product and test target, in every CI leg.
