# Switching CI from the test certificates to the real keys

Today CI signs with two self-signed test certificates. The real keys are an Apple Developer ID (macOS) and
SignPath Foundation's certificate (Windows). The signing steps are already the ones the real keys use. The switch is a
change of settings, not of code: set the new secrets, hand over the fingerprints, then flip one repository variable
per OS.

| OS      | Repository variable      | Unset / `test` (today)                               | Real keys                                                     |
|---------|--------------------------|------------------------------------------------------|---------------------------------------------------------------|
| macOS   | `DENICE_MACOS_SIGNING`   | codesign with the self-signed p12; not notarized     | `developer-id`: codesign `--timestamp`, notarize + staple     |
| Windows | `DENICE_WINDOWS_SIGNING` | the signing hand-off is signed by signtool + the PFX | `signpath`: the same hand-off is signed by SignPath           |

Both are set in GitHub: Settings > Secrets and variables > Actions > Variables. Any other value fails the build.

## 1. Secrets and variables to set

Secrets go in Settings > Secrets and variables > Actions > Secrets. Every base64 value is one line
(`base64 -i <file> | tr -d '\n'`), because CI decodes it with `openssl base64 -d -A`.

### macOS

| Name | Kind | What goes in it | Where it comes from |
|---|---|---|---|
| `DENICE_MACOS_CERT_P12` | secret (exists; **replace**) | base64 of the **Developer ID Application** certificate exported as .p12 **with its private key** | developer.apple.com > Certificates > `+` > Developer ID Application (only the Account Holder can create it). Install it in Keychain Access, then export the certificate and key as .p12. |
| `DENICE_MACOS_CERT_PASSWORD` | secret (exists; **replace**) | the .p12 export password | chosen at export |
| `APPLE_NOTARY_KEY` | secret (new) | the text of the App Store Connect API key file `AuthKey_<KEYID>.p8`, including the `-----BEGIN PRIVATE KEY-----` / `-----END PRIVATE KEY-----` lines (not base64) | App Store Connect > Users and Access > Integrations > App Store Connect API > Team Keys > `+` (Developer access). The .p8 downloads once only. |
| `APPLE_NOTARY_KEY_ID` | secret (new) | the key's Key ID | same page, Key ID column |
| `APPLE_NOTARY_ISSUER` | secret (new) | the Issuer ID | same page, above the key list |
| `DENICE_MACOS_SIGNING` | variable (new) | `developer-id` | — set it last (step 4) |

These are the `xcrun notarytool submit --key <p8> --key-id <id> --issuer <issuer>` credentials
(eng/macos/notarize.sh). The p12 keeps today's secret names, so the signing step itself does not change.

### Windows

| Name | Kind | What goes in it | Where it comes from |
|---|---|---|---|
| `SIGNPATH_API_TOKEN` | secret (new) | API token of a SignPath **CI user** that is a submitter on the signing policy | SignPath > Users > the CI user > API token |
| `SIGNPATH_ORGANIZATION_ID` | secret (new) | the SignPath organization id (a GUID) | SignPath > Settings |
| `SIGNPATH_PROJECT_SLUG` | variable (optional) | the SignPath project slug; default `denice` | SignPath > the project |
| `SIGNPATH_SIGNING_POLICY_SLUG` | variable (optional) | the signing policy slug; default `release-signing` | SignPath > the project > Signing policies |
| `SIGNPATH_FILES_CONFIGURATION_SLUG` | variable (optional) | artifact configuration for Denice's files; default `denice-files` | below |
| `SIGNPATH_SETUP_CONFIGURATION_SLUG` | variable (optional) | artifact configuration for the setup programs; default `denice-setup` | below |
| `DENICE_WINDOWS_SIGNING` | variable (new) | `signpath` | — set it last (step 4) |

SignPath project setup (once):

- Link the project to the trusted build system "GitHub.com" and install the SignPath GitHub App on the repository
  (SignPath needs it to check the build's origin). Every job up to the signing request must run on GitHub-hosted
  runners — they do.
- Two artifact configurations. `actions/upload-artifact` zips the folder, so each root is a `<zip-file>`; paths are
  relative to the handed-off folder (see "The Windows hand-off" below). Both default to `min-matches`/`max-matches`
  = 1, so a missing or extra file fails the request.

`denice-files` (round 1, Denice's own files from both layouts):

```xml
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <pe-file-set>
      <include path="win-x64/denice.exe"/>
      <include path="win-arm64/denice.exe"/>
      <include path="win-x64/versions/*/denice-v*-win-x64.exe"/>
      <include path="win-arm64/versions/*/denice-v*-win-arm64.exe"/>
      <include path="win-x64/versions/*/Denice.*.dll" max-matches="unbounded"/>
      <include path="win-arm64/versions/*/Denice.*.dll" max-matches="unbounded"/>
      <for-each>
        <authenticode-sign/>
      </for-each>
    </pe-file-set>
  </zip-file>
</artifact-configuration>
```

`denice-setup` (round 2, the setup programs):

```xml
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <pe-file-set>
      <include path="denice-setup-v*-win-x64.exe"/>
      <include path="denice-setup-v*-win-arm64.exe"/>
      <for-each>
        <authenticode-sign/>
      </for-each>
    </pe-file-set>
  </zip-file>
</artifact-configuration>
```

After the switch `DENICE_WINDOWS_PFX` and `DENICE_WINDOWS_PFX_PASSWORD` are no longer read and can be deleted.

## 2. Hand over the fingerprints first

Denice Runtime accepts a downloaded version only if its signer's certificate is in
`src/Denice.Updates/DeniceSigningCertificates.cs`. Its renewal rule, quoted:

> Renewal rule. Each certificate lasts one year and is renewed, which is why this is a list and not one value: a new
> certificate's fingerprint is added in a release still signed by the old certificate, before CI switches to the
> new one — so an installed Denice Runtime already accepts the new signer when the first release signed with it
> arrives.

The same order applies to the real keys:

1. Get each real certificate's SHA-256 fingerprint in the form the file uses (`openssl x509 -noout -fingerprint -sha256`):
   - macOS: `eng/p12-to-pem.sh developer-id.p12 '<password>' devid.pem && openssl x509 -in devid.pem -noout -fingerprint -sha256`
   - Windows: download the signing policy's certificate from SignPath (.cer), then
     `openssl x509 -inform DER -in signpath.cer -noout -fingerprint -sha256`
2. Add both to `Sha256Fingerprints` (keep the test ones), update the doc comment that names the certificates, and merge.
3. Let that change ship in a release **still signed by the test certificates** (a push to dev publishes a
   pre-release; a push to main a release).
4. Only then flip the variables (section 4).

Note on SignPath Foundation: its certificate is issued to SignPath Foundation, not to Denice, and the Foundation signs
other open-source projects with it. Pinning its fingerprint therefore proves "signed through SignPath Foundation", not
"signed by Denice"; the cosign signature on `SHA256SUMS` (which Denice Runtime also checks) is what still ties the files
to Denice's CI. When SignPath renews that certificate, the hand-over above has to be repeated.

## 3. What changes in the release by itself

The release job reads the same variables, so none of this needs a code change:

- Release notes (`ci.yml`, step "Create the GitHub release"). While both OSes are on `test` the text is unchanged:
  > These binaries are signed with a self-signed, UNTRUSTED developer certificate — not a CA-issued or notarized identity. Trust our public certificate first with the attached tester trust script (`trust-cert.sh` on macOS, `trust-cert.ps1` on Windows); it trusts the cert narrowly for code signing only, never as a root.

  Once either OS has switched, each OS gets its own line: the test wording for an OS still on `test`, otherwise
  "The macOS binaries are signed with our Apple Developer ID and notarized by Apple." /
  "The Windows binaries are signed through SignPath: free code signing provided by SignPath.io, certificate by
  SignPath Foundation."
- Tester trust files: for a switched OS the release stops attaching its public certificate and trust script
  (`denice-macos-public.cer` + `trust-cert.sh`; `denice-windows-public.cer` + `trust-cert.ps1`) and stops exporting
  that certificate from its secret.
- The runner-only step "TEST CERTIFICATE ONLY — trust the self-signed cert on this runner" (it puts the test
  certificate in the Windows runner's `LocalMachine\Root` so `signtool verify /pa` chains) is skipped under
  `signpath`: SignPath's certificate chains to a public root.

Still to do by hand once both OSes have switched (each is a normal change, not part of the switch):

- `docs/running-alpha-builds.md`: the "Trust our certificate" sections and the self-signed wording.
- Delete `eng/signing/trust-cert.sh`, `eng/signing/trust-cert.ps1`, the test-only step and the test branch of
  `.github/actions/windows-sign` once nothing needs the test certificates.
- Decide whether the release publishes the notarized DMGs (artifact `dmg-macos`); today they are built and, under
  `developer-id`, notarized and uploaded as a workflow artifact only.
- Only `Denice.app` and the DMG are notarized. The versioned entry and .NET libraries in `versions/<version>/` carry a
  Developer ID signature with a secure timestamp but are not submitted to Apple.

## 4. The switch

1. Sections 1 and 2 done, and the fingerprint release is out.
2. Settings > Secrets and variables > Actions > Variables:
   - `DENICE_MACOS_SIGNING` = `developer-id`
   - `DENICE_WINDOWS_SIGNING` = `signpath`

   The two are independent; either can go first.
3. Push to dev. Check in the run: the macOS job shows no "notarization skipped" notice and `notarize.sh` ends
   "notarized, stapled and accepted by Gatekeeper"; the Windows job's hand-off steps show "Sign (SignPath)" running and
   "took back N signed file(s)"; both verify results end `RESULT: PASS`.

To go back, delete the variable (or set it to `test`) and make sure the test-certificate secrets still exist.

## The Windows hand-off

`.github/actions/windows-sign` is the one place signing happens on Windows, run twice by `ci.yml`:

1. hand off — the unsigned files are uploaded as a workflow artifact (`windows-sign-layouts`, then `windows-sign-setup`);
2. sign — `test`: the artifact is downloaded and `eng/windows/sign-with-test-cert.ps1` signs every file in it
   (signtool, DigiCert timestamp with Sectigo fallback, cert-expiry check); `signpath`:
   `signpath/github-action-submit-signing-request` (v3.0, pinned by commit) submits the artifact by id, waits, and
   downloads the signed result;
3. take back — the signed files must be exactly the handed-off files, each one changed, and are copied over the
   originals.

Round 1 hands off, per layout, `denice.exe`, `versions/<version>/denice-v<version>-<rid>.exe` and
`versions/<version>/Denice.*.dll`; afterwards the signed root `denice.exe` is copied into `versions/<version>/` (the
two must be equal). Round 2 hands off the `denice-setup-v<version>-<rid>.exe` programs built from the signed layouts.
