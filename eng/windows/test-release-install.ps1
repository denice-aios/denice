# Copyright (c) Denice.ai. All rights reserved.
#
# The Windows half of the PUBLISHED-release checks (rust/REQUIREMENTS.md F9 F14 I5 I6, and the real-download half of E1),
# run on a Windows runner after the release job, as this (non-administrator-installed, per-user) runner user:
#   1. Windows' own verifier: `signtool verify /pa` and Get-AuthenticodeSignature on the two setup programs and on every
#      Denice file CI signs in both layouts (denice.exe at the root and in the version, the versioned entry, the
#      Denice.*.dll assemblies), each signed by a pinned Denice certificate (src/Denice.Updates/DeniceSigningCertificates.cs).
#      The test certificate is self-signed, so it is trusted on this runner only, after its fingerprint is checked
#      against the pinned list (as the windows job does before it verifies);
#   2. first install from the release download (I5 I6): the release's win-x64 setup program installs silently per user,
#      `denice version` prints the release's version, `denice update` checks GitHub Releases, and uninstall removes it;
#   3. update through GitHub Releases (E1, real download): the previous release in the install layout (A) is installed
#      with its own setup program, `denice update` downloads and installs this release (B), the loader starts B, B
#      replaces denice.exe, A is removed and denice.exe.old is gone. Without an earlier release in the install layout it
#      says so and proves nothing for this part.
#
# Usage: eng\windows\test-release-install.ps1 -Tag <tag> -Work <dir>
# Requires: gh (GH_TOKEN), signtool (Windows SDK).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Work
)
$ErrorActionPreference = 'Stop'
$repo = 'denice-aios/denice'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$pinned = [regex]::Matches((Get-Content -Raw (Join-Path $repoRoot 'src\Denice.Updates\DeniceSigningCertificates.cs')),
    '"((?:[0-9A-F]{2}:){31}[0-9A-F]{2})"') | ForEach-Object { $_.Groups[1].Value.Replace(':', '') }
if ($pinned.Count -lt 1) { throw 'no pinned certificates read from DeniceSigningCertificates.cs' }
$app = Join-Path $env:LOCALAPPDATA 'Programs\Denice'
$exe = Join-Path $app 'denice.exe'

function Get-Sha256Thumbprint([Security.Cryptography.X509Certificates.X509Certificate2]$cert) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cert.RawData))
}

function Save-Release([string]$tag, [string]$dir, [string[]]$patterns) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $ghArgs = @('release', 'download', $tag, '--repo', $repo, '--dir', $dir, '--clobber')
    foreach ($p in $patterns) { $ghArgs += @('--pattern', $p) }
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release download $tag failed" }
}

function Install-Setup([string]$setup) {
    $p = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "$setup exited with $($p.ExitCode)" }
    if (-not (Test-Path $exe)) { throw "$setup did not install $exe" }
    & $exe settings set updates.enabled false | Out-Null   # only `denice update` reaches the network
}

function Uninstall-Denice {
    $uninstaller = Get-ChildItem $app -Filter 'unins*.exe' | Select-Object -First 1 -ExpandProperty FullName
    $u = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    if ($u.ExitCode -ne 0) { throw "uninstall exited with $($u.ExitCode)" }
    Start-Sleep -Seconds 3
    if (Test-Path $app) { throw "uninstall left $app" }
}

function Get-SemVer {
    $out = & $exe version
    if ($LASTEXITCODE -ne 0) { throw "denice version failed: $out" }
    (($out | Where-Object { $_ -like 'SemVer: *' }) -replace '^SemVer: ', '').Trim()
}

Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue
$assets = Join-Path $Work 'assets'
Save-Release $Tag $assets @('denice-setup-v*.exe', 'denice-v*-win-x64.zip', 'denice-v*-win-arm64.zip', 'denice-windows-public.cer')

# ---- 1. Windows' own Authenticode verifier on every Denice file CI signs.
$cer = Join-Path $assets 'denice-windows-public.cer'
if (Test-Path $cer) {
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cer)
    if ($pinned -notcontains (Get-Sha256Thumbprint $cert)) { throw "the release's denice-windows-public.cer is not a pinned Denice certificate" }
    # TEST CERTIFICATE ONLY: this runner trusts it so `signtool verify /pa` chains (a SignPath signature needs nothing).
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
}
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'signtool.exe not found' }
$signed = @(Get-ChildItem $assets -Filter 'denice-setup-v*.exe')
foreach ($zip in Get-ChildItem $assets -Filter 'denice-v*-win-*.zip') {
    $dir = Join-Path $Work ('unpacked\' + $zip.BaseName)
    Expand-Archive -Path $zip.FullName -DestinationPath $dir
    $signed += Get-ChildItem $dir -Recurse -File | Where-Object { $_.Name -like 'denice*.exe' -or ($_.Name -like 'Denice.*.dll') }
}
$failed = 0
foreach ($file in $signed) {
    & $signtool verify /pa /q $file.FullName | Out-Null
    $verified = $LASTEXITCODE
    $sig = Get-AuthenticodeSignature -FilePath $file.FullName
    $thumb = if ($sig.SignerCertificate) { Get-Sha256Thumbprint $sig.SignerCertificate } else { '' }
    if ($verified -eq 0 -and $sig.Status -eq 'Valid' -and $pinned -contains $thumb) {
        Write-Host "authenticode PASS (signtool verify /pa, Get-AuthenticodeSignature Valid, signer $thumb pinned): $($file.Name)"
    } else {
        Write-Host "authenticode FAIL (signtool exit $verified, status $($sig.Status), signer '$thumb'): $($file.FullName)"
        $failed++
    }
}
if ($failed -gt 0) { throw "$failed Denice files are not validly signed by a pinned Denice certificate" }
Write-Host "release-install: ok: $($signed.Count) Denice files (setup programs, loaders, entries, Denice.*.dll) are signed by Denice"

# ---- 2. First install from the release download.
$setupB = Get-ChildItem $assets -Filter 'denice-setup-v*-win-x64.exe' | Select-Object -First 1 -ExpandProperty FullName
Install-Setup $setupB
$semver = Get-SemVer
if (-not $semver.StartsWith($Tag)) { throw "expected $Tag, got $semver" }
$out = & $exe update
if ($LASTEXITCODE -ne 0) { throw "denice update failed: $out" }
Write-Host "release-install: ok: installed $semver with the release's setup program; denice update: $out"
Uninstall-Denice
Write-Host 'release-install: ok: uninstalled'

# ---- 3. Update from the previous release in the install layout (A) to this one (B) through GitHub Releases.
$releases = gh api "repos/$repo/releases?per_page=100" | ConvertFrom-Json
$index = [array]::FindIndex([object[]]$releases, [Predicate[object]] { param($r) $r.tag_name -eq $Tag })
$a = $releases | Select-Object -Skip ($index + 1) | Where-Object {
    -not $_.draft -and ($_.assets.name -like 'denice-setup-v*-win-x64.exe') -and ($_.assets.name -like 'denice-v*-win-x64.zip.sha256')
} | Select-Object -First 1
if (-not $a) {
    Write-Host "release-update: NOT PROVEN: no release before $Tag is in the install layout for win-x64; the next release proves this."
    exit 0
}
$assetsA = Join-Path $Work 'assets-a'
Save-Release $a.tag_name $assetsA @('denice-setup-v*-win-x64.exe')
Install-Setup (Get-ChildItem $assetsA -Filter 'denice-setup-v*-win-x64.exe' | Select-Object -First 1 -ExpandProperty FullName)
$versionA = Get-SemVer
Write-Host "release-update: ok: 1: installed A ($versionA) with its setup program"
$out = & $exe update
if ($LASTEXITCODE -ne 0) { throw "denice update failed: $out" }
$versionB = ([regex]::Match(($out -join "`n"), 'Denice (\S+) is installed\.')).Groups[1].Value
if (-not $versionB.StartsWith($Tag)) { throw "denice update did not install B ($Tag): $out" }
if (-not (Test-Path (Join-Path $app "versions\$versionB"))) { throw "B is not in $app\versions" }
Write-Host "release-update: ok: 2: denice update downloaded B ($versionB) from GitHub Releases and installed it"
if ((Get-SemVer) -ne $versionB) { throw 'the loader did not start B' }
$carried = Join-Path $app "versions\$versionB\denice.exe"
if ((Get-FileHash $exe).Hash -ne (Get-FileHash $carried).Hash) { throw 'B did not replace denice.exe' }
Write-Host 'release-update: ok: 3: the loader started B and B replaced denice.exe'
if ((Get-SemVer) -ne $versionB) { throw 'B did not run again' }
if (Test-Path (Join-Path $app "versions\$versionA")) { throw 'A was not removed after B started' }
if (Test-Path "$exe.old") { throw 'denice.exe.old was not deleted' }
Write-Host 'release-update: ok: 4: B runs again, A was removed, denice.exe.old is gone'
Uninstall-Denice
Write-Host "release-update: ok: updated $($a.tag_name) -> $Tag through GitHub Releases on win-x64"
