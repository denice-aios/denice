# Running the alpha (self-signed) builds

Denice's alpha releases are **signed with our own self-signed certificate**, not a paid Apple/Microsoft
identity and not notarized. The signature proves the binary came from us and has not been tampered with, but
because the certificate is not issued by a public authority, macOS Gatekeeper and Windows SmartScreen will
warn the first time you run it. This page shows the one-time steps to trust and launch an alpha build.

Every release attaches, per platform:

- the install zip — `denice-v<version>-<rid>.zip`, e.g. `denice-v<version>-osx-arm64.zip`, holding:
  - `denice` (`denice.exe` on Windows) — the loader, the program you run;
  - `versions/<version>/` — that version's files: the versioned entry `denice-v<version>-<rid>`, Denice Runtime and
    its .NET runtime, the loader copy it carries, and `SHA256SUMS` with its cosign signature
    `SHA256SUMS.cosign.bundle` (keyless, recorded in the public Rekor transparency log);
- the zip's checksum — `denice-v<version>-<rid>.zip.sha256` (GNU two-column format);
- the public certificate — `denice-macos-public.cer` and `denice-windows-public.cer`;
- the trust scripts — `trust-cert.sh` (macOS) and `trust-cert.ps1` (Windows).

Unzip it anywhere and run `denice`: it starts the highest version in its `versions` folder.

Pick the file whose `<rid>` matches your machine: `osx-arm64` (Apple Silicon Mac), `osx-x64` (Intel Mac),
`win-x64` / `win-arm64` (Windows), `linux-x64` / `linux-arm64` (Linux).

---

## Verify the download first (optional but recommended)

**Checksum** — confirms the zip is intact:

```sh
# macOS
shasum -a 256 -c denice-v<version>-osx-arm64.zip.sha256
# Linux
sha256sum -c denice-v<version>-linux-x64.zip.sha256
# Windows (PowerShell) — compare the printed hash against the first column of the .zip.sha256 file
Get-FileHash -Algorithm SHA256 denice-v<version>-win-x64.zip
```

After unzipping, `versions/<version>/SHA256SUMS` lists every file of that version:

```sh
cd versions/<version> && shasum -a 256 -c SHA256SUMS     # sha256sum -c SHA256SUMS on Linux
```

**cosign** — confirms it was built and signed by our official CI workflow (needs
[cosign](https://docs.sigstore.dev/system_config/installation/) installed). Use `refs/heads/main` for a full
release or `refs/heads/dev` for a dev preview:

```sh
cosign verify-blob \
  --certificate-identity 'https://github.com/denice-aios/denice/.github/workflows/ci.yml@refs/heads/main' \
  --certificate-oidc-issuer 'https://token.actions.githubusercontent.com' \
  --bundle versions/<version>/SHA256SUMS.cosign.bundle \
  versions/<version>/SHA256SUMS
```

---

## macOS

macOS quarantines files downloaded from the internet and Gatekeeper blocks un-notarized binaries by default.
Do the one-time trust step, then use any one of the launch options.

**1. Trust our certificate (once):**

```sh
chmod +x trust-cert.sh
./trust-cert.sh denice-macos-public.cer
```

This adds the certificate as a **code-signing anchor in your login keychain only** — never a system root. It
is reversible with `security remove-trusted-cert denice-macos-public.cer`.

**2. Make the binary executable and launch it — pick one:**

- **Remove the quarantine flag** from the whole unzipped folder in the terminal (the loader starts the versioned
  entry, so both must be released):

  ```sh
  xattr -dr com.apple.quarantine denice-v<version>-osx-arm64
  ./denice-v<version>-osx-arm64/denice version
  ```

- **Open Anyway** from **System Settings → Privacy & Security**: try to run `denice` once, then click **Open Anyway**
  in that pane, and run it again (repeat once for the versioned entry it starts).

Confirm it launched:

```sh
./denice version
```

---

## Windows

Windows SmartScreen warns on downloaded executables that lack a widely-recognized reputation. Trust our
certificate, then click through the one-time SmartScreen prompt.

**1. Trust our certificate (once):**

```powershell
.\trust-cert.ps1 denice-windows-public.cer
```

This installs the certificate into your **CurrentUser Trusted Publishers** store — the narrow code-signing
trust bin, never a Root CA. No administrator rights are needed.

**2. Launch past SmartScreen:**

When you first run `denice.exe` from the unzipped folder, SmartScreen may show *"Windows protected your PC."* Click
**More info**, then **Run anyway**. (If your browser flagged the download, choose **Keep** first.)

```powershell
.\denice.exe version
```


---

## Linux

Linux has no OS-level code-signing gate. Unzip and run the loader; verify with the checksum and cosign steps above
if you want provenance.

```sh
unzip denice-v<version>-linux-x64.zip -d denice && ./denice/denice version
```

---

These steps are only needed for the self-signed alpha builds. Once Denice ships with a notarized Apple
identity and an EV/OV Windows certificate, Gatekeeper and SmartScreen will accept the binaries without any of
the trust steps above.

---

## Installing Denice

Instead of unzipping by hand, install Denice for your user account. Nothing needs administrator rights and nothing
runs in the background. After the first install Denice updates itself: it downloads new versions into its
`versions` folder and can replace its own loader.

### macOS and Linux

```sh
curl -fsSL https://raw.githubusercontent.com/denice-aios/denice/dev/eng/install/install.sh | sh
```

The script finds the newest release for your machine (pre-releases included), downloads the zip and its
`.zip.sha256`, refuses the zip if the checksum does not match, installs it, and runs `denice version` to show it
works. Options go after `sh -s --`:

```sh
curl -fsSL https://raw.githubusercontent.com/denice-aios/denice/dev/eng/install/install.sh | sh -s -- --version <version>
sh install.sh --from denice-v<version>-<rid>.zip     # install a zip you already downloaded (offline)
sh install.sh --no-modify-path                       # leave your shell profiles alone
```

Where everything goes:

| | macOS | Linux |
|---|---|---|
| Loader | `~/Applications/Denice.app` | `~/.local/share/denice/denice` |
| Versions | `~/Library/Application Support/Denice/versions/<version>/` | `~/.local/share/denice/versions/<version>/` |
| Terminal command | `~/.local/bin/denice` → `~/Applications/Denice.app/Contents/MacOS/denice` | `~/.local/bin/denice` → the loader |
| Menu entry | — | `~/.local/share/applications/denice.desktop` |
| PATH setup | `~/Library/Application Support/Denice/env` | `~/.local/share/denice/env` |

On Linux `~/.local/share` is `$XDG_DATA_HOME` when that is set.

So you can type `denice` in any new terminal, the script puts `~/.local/bin` on your `PATH` the way rustup does. The
`env` file in the table adds `~/.local/bin` to `PATH` only when it is missing, and the script adds one line that
sources it, ending in `# added by the Denice installer`, to:

- `~/.profile` (created when missing), and to `~/.bash_profile`, `~/.bash_login` and `~/.bashrc` when they exist;
- zsh's `.zshenv` when zsh is installed: `$ZDOTDIR/.zshenv` when `ZDOTDIR` is set (unless only `~/.zshenv` exists),
  otherwise `~/.zshenv`;
- for fish, when fish is installed, it writes `~/.config/fish/conf.d/denice.fish` (`$XDG_CONFIG_HOME` when set).

It names each file it changed. Open a new terminal to pick it up, or run the `. "…/env"` command it prints in the
current one. Installing again adds nothing twice. With `--no-modify-path` it changes no profile and only says when
`~/.local/bin` is not on your `PATH`.

macOS stays on this script until Denice is notarized: files curl downloads carry no quarantine flag, so the
self-signed build runs without the Gatekeeper steps above (the script also clears the flag from a zip you downloaded
in a browser and install with `--from`). Once Denice is notarized, macOS gets a DMG to drag into Applications.

Uninstall:

```sh
curl -fsSL https://raw.githubusercontent.com/denice-aios/denice/dev/eng/install/install.sh | sh -s -- --uninstall
```

This removes the loader (`Denice.app` on macOS), the whole `versions` folder, the `~/.local/bin/denice` link, the
Linux menu entry, the `env` file, the fish file and the marked line in each profile (with the line break or the file
the install added along with it); the rest of each profile stays exactly as it was. It keeps your settings
(`settings.yaml`) and Denice's other data.

### Windows

Download `denice-setup-v<version>-<rid>.exe` (`win-x64` or `win-arm64`) from the release page and run it. It asks for
no administrator rights and installs into `%LOCALAPPDATA%\Programs\Denice` (`denice.exe` and
`versions\<version>\`), adds a **Denice** Start Menu shortcut and an entry in **Settings → Apps → Installed apps**,
and adds that folder to your user `PATH` (once), so a new terminal runs `denice`.
Running a newer setup over an existing install keeps the versions Denice downloaded itself.

Uninstall from **Installed apps**: it removes `%LOCALAPPDATA%\Programs\Denice` entirely, including downloaded
versions, and takes that folder off your user `PATH`, leaving the rest of it unchanged. Your settings in
`%LOCALAPPDATA%\Denice` stay.

The setup file is not yet signed with a recognised certificate, so SmartScreen may warn as described above.
