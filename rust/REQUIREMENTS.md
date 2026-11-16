# Denice loader: requirements and finish line

Every line comes from Tim's own words or a recorded package answer (source in brackets).
A line is closed only by evidence: the commit and the test (or CI job) that proves it.
No line may be marked "not needed", "later" or "out of scope" by the implementer.
Only Tim's recorded answer can remove or change a line; the answer is then cited here.
`eng/check-requirements.sh` fails CI while any line lacks evidence.

Status values: `open`, `done <commit> <test or CI job>`, `blocked <package id>` (waiting on Tim's answer).

Sources:
- T14: Tim, 14 November 2026, architecture request (quoted in pkg-d435af7f8061).
- P-name: pkg-d435af7f8061 managed-dll-name = denice-runtime.
- T16: Tim, 16 November 2026: "Name the loader “denice” (or “denice.exe”, adjusted for the operating system), so the entry point is always Denice, including when launched from the Start Menu."
- P-store: pkg-a0206b25f619 version-storage = folders-no-pointer; reload-mechanics = parent-restarts, with Tim's note "Unpack first (to denice.ver) then rename current to old and .ver to empty and it's safer."
- P-install: pkg-f543b9586645 loader-takeover = right-after-swap; install-scope = per-user; per-user-folders = platform-folders.
- T16b: Tim, 16 November 2026: "Start the loader implementation now as one complete effort, including the macOS app-bundle decision. Do not defer that decision or cut the work short."
- R7: global rule 7: "Settings must replace .env configuration. Nothing ships in this project unless it can be fixed in Settings."

## Components and names
| ID | Requirement | Source | Status |
|---|---|---|---|
| N1 | Rust loader executable named `denice` (`denice.exe` on Windows) | T16 | open |
| N2 | Rust entry executable named `denice-v<version>-<rid>` (`.exe` on Windows), the name produced by the build itself, no later rename | T14 | open |
| N3 | Today's .NET CLI becomes a DLL named Denice Runtime, hosted by the Rust entry | T14, P-name | open |
| N4 | Everything else of today's .NET code is kept | T14 | open |

## Loading
| ID | Requirement | Source | Status |
|---|---|---|---|
| L1 | Loader finds the highest known version (SemVer order of `versions/<version>/`, complete folders only) and loads it | T14, P-store | open |
| L2 | Loader stays running as parent; starts the entry as a child with the same arguments; passes through stdin/stdout/stderr and the exit code | P-store | open |
| L3 | Entry hosts .NET and runs Denice Runtime with the arguments; returns its exit code | T14 | open |
| L4 | On Denice Runtime's reserved "restart into newer version" exit code, loader rescans and starts the new highest version with the same arguments | T14, P-store | open |
| L5 | If a new version fails to start, loader starts the previous version and marks the failed one so it is not chosen again | P-store | open |
| L6 | Previous version kept until the new one has started successfully once; then older versions removed | P-store | open |

## Downloading (in .NET)
| ID | Requirement | Source | Status |
|---|---|---|---|
| D1 | Denice Runtime performs the downloading; pre-download happens before loading | T14 | open |
| D2 | Download goes to `versions/<version>.partial/`, is checked, then renamed to `versions/<version>/` in one rename | P-store | open |
| D3 | What proves a download genuine | pkg (decision 2) | blocked pending package |
| D4 | Update source, channel and check timing, controllable in Settings | pkg (decision 3), R7 | blocked pending package |

## Loader replacement
| ID | Requirement | Source | Status |
|---|---|---|---|
| U1 | A higher-numbered version carries a newer loader and replaces the installed one | T14 | open |
| U2 | Order: unpack to `denice.ver`; rename `denice` to `denice.old`; rename `denice.ver` to `denice` (failure at any step restores the old loader) | P-store note | open |
| U3 | Hand over right after the swap: version exits with the reserved "loader replaced" code; old loader starts new `denice` with the same arguments and exits; new loader deletes `denice.old` | P-install | open |

## Installation
| ID | Requirement | Source | Status |
|---|---|---|---|
| I1 | Per-user install; no administrator rights, no service | P-install | open |
| I2 | Windows: `%LOCALAPPDATA%\Programs\Denice\` with Start Menu shortcut to `denice.exe` | P-install, T16 | open |
| I3 | macOS: `~/Applications/Denice/`; app-bundle shape | P-install, pkg (decision 1) | blocked pending package |
| I4 | Linux: `~/.local/share/denice/` with `~/.local/bin/denice` link | P-install | open |
| I5 | First install from a release download (the user gets Denice from a release and it installs into I2/I3/I4) | T14 "both update directions", P-install | open |

## Infrastructure parity (Rust aligned with .NET)
| ID | Requirement | Source | Status |
|---|---|---|---|
| F1 | Pinned Rust toolchain (as global.json pins the .NET SDK) | T14 | open |
| F2 | Version computed once (eng/compute-build-id.sh) and passed to both builds | T14 | open |
| F3 | Build for all six platforms: osx-arm64, osx-x64, win-x64, win-arm64, linux-x64, linux-arm64 | T14 | open |
| F4 | Formatting and lint gate with warnings as errors (as TreatWarningsAsErrors + analyzers) | T14 | open |
| F5 | Custom-rule management: Rust custom rules with their own tests, enforced in CI; tool per package decision 4 | T14, pkg (decision 4) | blocked pending package |
| F6 | Each applicable C# custom rule has a Rust counterpart or a recorded reason it cannot apply to Rust (listed in rust/RULES.md) | T14 | open |
| F7 | Unit and integration tests run in CI on every platform that runs tests today | T14 | open |
| F8 | Coverage gate at the same 75% threshold | T14 | open |
| F9 | Platform signing of the Rust executables (codesign hardened runtime / Authenticode) | T14 | open |
| F10 | sha256 + cosign for release files, verified before publishing | T14 | open |
| F11 | Release contains the new layout for all six platforms | T14 | open |

## Finish line
| ID | Requirement | Source | Status |
|---|---|---|---|
| E1 | End-to-end test in CI on macOS, Windows and Linux: install version A; make version B available; loader updates to B (L4); B replaces the loader (U1-U3); a broken version C falls back (L5) | T16b | open |
| E2 | Every line above closed with evidence; independent review of this file against the source packages finds nothing missing | T16b | open |
