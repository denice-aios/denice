# Copyright (c) Denice.ai. All rights reserved.
#
# The TEST signer of the Windows signing hand-off (.github/actions/windows-sign): Authenticode-signs, in place, every
# file under -Directory with Denice's self-signed CI certificate (the DENICE_WINDOWS_PFX secret), timestamped
# RFC 3161 against DigiCert with a fallback to Sectigo (decision digicert-timestamp-sectigo-fallback).
#
# It stands where SignPath's signing service will stand: it gets the handed-off files, signs every one, and nothing
# else. Choosing WHICH files are Denice's is the hand-off's job, not this script's. When the repository variable
# DENICE_WINDOWS_SIGNING is `signpath`, this script is not used at all (docs/signing-switchover.md).
#
# Usage: eng\windows\sign-with-test-cert.ps1 -Directory <folder>
# Environment: PFX_B64 (the base64 .pfx), PFX_PW (its password).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Directory
)
$ErrorActionPreference = 'Stop'

if (-not $env:PFX_B64) { throw 'sign-with-test-cert: PFX_B64 is empty — set the DENICE_WINDOWS_PFX secret.' }
$work = Join-Path $env:RUNNER_TEMP 'winsign'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$pfx = Join-Path $work 'cert.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:PFX_B64))
try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 @($pfx, $env:PFX_PW)
    # ci-cert-expiry-check: fail the build if the CI signing cert is already expired.
    if ($cert.NotAfter -lt (Get-Date)) {
        throw "DENICE_WINDOWS_PFX is expired ($($cert.NotAfter)) — regenerate the CI signing cert (decision ci-cert-expiry-check)."
    }

    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw 'sign-with-test-cert: signtool.exe not found on the runner.' }

    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object FullName)
    if ($files.Count -eq 0) { throw "sign-with-test-cert: nothing to sign in $Directory" }
    foreach ($bin in $files) {
        & $signtool sign /fd sha256 /f $pfx /p $env:PFX_PW /tr http://timestamp.digicert.com /td sha256 $bin
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'DigiCert timestamp unavailable; falling back to Sectigo.'
            & $signtool sign /fd sha256 /f $pfx /p $env:PFX_PW /tr http://timestamp.sectigo.com /td sha256 $bin
            if ($LASTEXITCODE -ne 0) { throw "signtool sign failed for $bin on both timestamp servers." }
        }
    }
    Write-Host "sign-with-test-cert: signed $($files.Count) file(s) in $Directory"
}
finally {
    Remove-Item -Force -LiteralPath $pfx -ErrorAction SilentlyContinue
}
