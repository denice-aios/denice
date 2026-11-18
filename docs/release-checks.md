# Release checks on clean machines

rust/REQUIREMENTS.md I10: "Release checks on clean machines: macOS standard user (quarantine, translocation,
app-management prompt), Windows standard user (SmartScreen, Smart App Control), Linux GNOME/KDE menu and PATH".

CI proves what a fresh runner can prove on every run. The rest needs a person at a real machine, because the
prompts below only appear in a desktop session. Run the manual checks once per release candidate, as a **new
standard (non-administrator) account** on a machine that has never had Denice. Write down the result of each step.

## What CI proves

| Check | Where | What it proves |
|---|---|---|
| macOS standard user | `eng/install/test-standard-user.sh osx-arm64` (macOS job) | A new non-admin user installs a release zip that carries `com.apple.quarantine` (like a Safari download) with `install.sh --from`, runs it, finds it from a new login shell, and uninstalls. On push this runs on the signed layout. |
| Quarantine and translocation | `eng/install/test-real-install.sh` (macOS) | Nothing installed keeps `com.apple.quarantine`. Without it, Gatekeeper never asks about the installed copy and macOS never translocates the app. |
| Gatekeeper's verdict | same | Prints `spctl --assess` for the installed app. With `DENICE_MACOS_SIGNING=developer-id` it fails unless the verdict is "accepted". |
| Windows standard user | "Install and run the win-x64 setup as a standard user" (Windows job) | A new non-admin user installs a setup that carries the Mark of the Web (`Zone.Identifier`, ZoneId=3) silently and runs `denice version`. No installed file carries the Mark of the Web. The Authenticode status of the setup and of `denice.exe` is printed. |
| Linux standard user, menu and PATH | `eng/install/test-standard-user.sh linux-arm64` (Linux job) | A new non-sudo user installs. The menu entry is in `~/.local/share/applications`, where GNOME and KDE read it. It passes `desktop-file-validate` when the runner has that tool, and its `Exec` program runs. New `sh`, `bash` and `zsh` login shells find `denice` on PATH. Uninstall removes everything. |

CI cannot show the SmartScreen, Smart App Control, Gatekeeper or App Management prompts. It also cannot show a GNOME or
KDE menu. Those are the manual checks below.

## Expected blocks until real signing

- **macOS:** Gatekeeper blocks a quarantined copy of a test-signed build ("Apple could not verify…"). This is expected
  until Apple Developer ID signing and notarization exist (REQUIREMENTS F13). `install.sh` clears the quarantine flag,
  so builds installed with it run. Do the Gatekeeper steps M1–M3 with a Developer ID build. With a test build, only
  write down what you saw.
- **Windows:** SmartScreen ("Windows protected your PC", "Unknown publisher") and Smart App Control block test-signed
  builds by design. That stays until trusted Windows signing exists (JARVIS work item work-e0f84110e096; REQUIREMENTS
  F13, SignPath). Do W1 and W3 with a trusted-signed build. With a test build, write down the block and the publisher
  name shown.

## macOS: standard user (latest macOS, Apple silicon)

| # | Do | Expected |
|---|---|---|
| M1 | In Safari, download `denice-v<version>-osx-arm64.zip` and its `.sha256` from the release page. In Terminal, run `xattr -p com.apple.quarantine ~/Downloads/denice-v<version>-osx-arm64.zip`. | It prints a quarantine value. The download really is quarantined. |
| M2 | Run `curl -fsSL <release>/install.sh \| sh -s -- --from ~/Downloads/denice-v<version>-osx-arm64.zip`. Open a new Terminal window and run `denice version`. | Neither step shows a dialog. The version is printed. |
| M3 | In Finder, double-click `~/Applications/Denice.app`. Then run `xattr -l ~/Applications/Denice.app`. | No Gatekeeper dialog appears. It starts with no window (I8). `xattr` lists no `com.apple.quarantine`, so the app is not translocated. |
| M4 | App Management: install an older release with `install.sh --version <older>`. Then run `denice update` in Terminal, so the newer version replaces `Denice.app`. Run `~/Applications/Denice.app/Contents/MacOS/denice version`. | No "… would like to modify apps on your Mac" prompt or "… was prevented from modifying apps" notice. The new version is printed. If a prompt appears, write down the app it names and what Allow / Don't Allow does. |
| M5 | Run `sh install.sh --uninstall`. | `~/Applications/Denice.app`, `~/.local/bin/denice` and `~/Library/Application Support/Denice/versions` are gone. `settings.yaml` stays. |

## Windows: standard user (Windows 11, fresh install, Smart App Control available)

| # | Do | Expected |
|---|---|---|
| W1 | In Edge, download `denice-setup-v<version>-win-x64.exe`. Its Properties show "This file came from another computer". Double-click it. | No UAC prompt (it installs per user). No SmartScreen "Windows protected your PC". A new certificate may still lack reputation; if a prompt appears, it must name Denice's publisher, never "Unknown publisher". |
| W2 | Open a new Terminal and run `denice version`. Then start Denice from the Start Menu shortcut. | The version is printed. No SmartScreen prompt, because installed files carry no Mark of the Web. |
| W3 | Turn Smart App Control on (Windows Security > App & browser control > Smart App Control). Repeat W1 and W2, then run `denice update` so the loader is replaced. | Nothing is blocked: not the setup, `denice.exe`, the `denice-v<version>-win-x64.exe` entry, the Denice DLLs, or the new loader. |
| W4 | Uninstall Denice from Settings > Apps > Installed apps. | The app folder, the Start Menu shortcut and the PATH entry are gone. |

## Linux: GNOME and KDE (for example Ubuntu 24.04 Desktop and Kubuntu / Fedora KDE)

Run on each desktop.

| # | Do | Expected |
|---|---|---|
| L1 | Run `curl -fsSL <release>/install.sh \| sh`. Open a new terminal window from the desktop (an interactive non-login shell) and run `denice version`. | The version is printed without editing PATH. |
| L2 | GNOME: open Activities and type "Denice". KDE: open the Application Launcher and type "Denice". Start the entry. | The entry is listed, possibly only after logging out and in; write down which. It opens a terminal window that runs Denice. |
| L3 | Run `sh install.sh --uninstall`, then search the menu again. | The entry is gone, and so is `denice` in a new terminal. |
