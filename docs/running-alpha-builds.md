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
