# Copyright (c) Denice.ai. All rights reserved.
#
# Builds the per-user Windows installer denice-setup-v<version>-<rid>.exe from one platform's extracted release zip
# (denice.exe + versions\<version>\**), with Inno Setup 6 and eng\windows\denice.iss.
#
# Usage: eng\windows\build-installer.ps1 -Rid win-x64 -Version <version> -Layout <extracted zip folder> -Out <dir>
#
# Inno Setup 6 is preinstalled on GitHub's Windows runners: the runner image documentation lists "InnoSetup 6.7.1"
# under "Tools" for both windows-latest (= windows-2025, image 20261111.331.1)
#   https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md
# and windows-2022 (image 20261112.366.7)
#   https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md
# (checked 2026-11-16). This script installs nothing: if ISCC.exe is missing it stops and says so.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'win-arm64')][string]$Rid,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Layout,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'

function Find-Iscc {
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $roots = @(
        @{ Root = ${env:ProgramFiles(x86)}; Sub = 'Inno Setup 6\ISCC.exe' },
        @{ Root = $env:ProgramFiles; Sub = 'Inno Setup 6\ISCC.exe' },
        @{ Root = $env:LOCALAPPDATA; Sub = 'Programs\Inno Setup 6\ISCC.exe' }
    )
    $candidates = @($roots | Where-Object { $_.Root } | ForEach-Object { Join-Path $_.Root $_.Sub })
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
    }
    throw "build-installer: Inno Setup 6 (ISCC.exe) was not found on PATH or in: $($candidates -join '; ')"
}

$Layout = (Resolve-Path -LiteralPath $Layout).Path
if (-not (Test-Path -LiteralPath (Join-Path $Layout 'denice.exe') -PathType Leaf)) {
    throw "build-installer: $Layout has no denice.exe; pass the folder the release zip was extracted into"
}
$versionDir = Join-Path $Layout "versions\$Version"
if (-not (Test-Path -LiteralPath $versionDir -PathType Container)) {
    throw "build-installer: $Layout has no versions\$Version folder"
}
$others = @(Get-ChildItem -LiteralPath (Join-Path $Layout 'versions') -Directory | Where-Object { $_.Name -ne $Version })
if ($others.Count -gt 0) {
    throw "build-installer: $Layout\versions holds other versions too ($($others.Name -join ', ')); the setup carries exactly one"
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path -LiteralPath $Out).Path

$iscc = Find-Iscc
$script = Join-Path $PSScriptRoot 'denice.iss'
Write-Host "build-installer: $iscc ($Rid, $Version)"
& $iscc "/DAppVersion=$Version" "/DRid=$Rid" "/DLayout=$Layout" "/DOutDir=$Out" $script
if ($LASTEXITCODE -ne 0) { throw "build-installer: ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $Out "denice-setup-v$Version-$Rid.exe"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw "build-installer: ISCC succeeded but $setup is missing" }
Write-Host "build-installer: wrote $setup"
