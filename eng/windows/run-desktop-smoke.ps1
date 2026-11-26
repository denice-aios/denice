# Copyright (c) Denice.ai. All rights reserved.
# Executes the real packaged Rust entry. Success here is application execution, not GPU certification.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PayloadDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][double]$DurationSeconds = 2.5,
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 120,
    [switch]$HardwareFixtures
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($HardwareFixtures) {
    . (Join-Path $PSScriptRoot 'read-sandbox-result.ps1')
    $payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
    $dotnet = Join-Path $payload 'dotnet/dotnet.exe'
    $tests = Join-Path $payload 'tests'
    $assembly = Join-Path $tests 'Denice.CrossPlatform.Tests.dll'
    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf) -or -not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw 'Hardware fixture payload must contain its pinned dotnet SDK and published win-x64 tests.'
    }
    if (Test-Path (Join-Path $output 'hardware.trx')) { throw 'Hardware fixture results already exist.' }
    Copy-Item -LiteralPath (Join-Path $payload 'fixture-source.txt'), (Join-Path $payload 'payload-sha256.json') -Destination $output
    $process = Start-Process -FilePath $dotnet -WorkingDirectory $tests -ArgumentList @(
        'vstest', ('"' + $assembly + '"'), '--TestCaseFilter:Category=GpuHardware',
        ('--TestAdapterPath:"' + $tests + '"'), ('--ResultsDirectory:"' + $output + '"'),
        '--Logger:"trx;LogFileName=hardware.trx"'
    ) -RedirectStandardOutput (Join-Path $output 'hardware-fixtures.stdout.txt') `
      -RedirectStandardError (Join-Path $output 'hardware-fixtures.stderr.txt') -PassThru
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Hardware fixtures exceeded $TimeoutSeconds seconds." }
        if ($process.ExitCode -ne 0) { throw "Hardware fixtures exited $($process.ExitCode)." }
    } finally {
        if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
        $process.Dispose()
        $media = Join-Path $tests 'hardware-fixture-output'
        if (Test-Path -LiteralPath $media -PathType Container) {
            Copy-Item -LiteralPath $media -Destination (Join-Path $output 'hardware-fixtures') -Recurse
        }
    }
    Read-DeniceHardwareFixtureResult -OutputDirectory $output | Out-Null
    Write-Host 'Isolated win-x64 graphics and hardware-encoder fixtures passed, including decoded video checks. CEF callback safety, product audio, and ARM64 execution remain untested.'
    return
}
$video = Join-Path $output 'denice-orb.mp4'
if (Test-Path $video) { throw 'The recording destination already exists.' }
$entries = @(Get-ChildItem -LiteralPath (Join-Path $PayloadDirectory 'versions') -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Filter 'denice-v*-win-x64.exe' -File
})
if ($entries.Count -ne 1) { throw 'Expected exactly one win-x64 versioned entry in the install layout.' }
$entry = $entries[0]
if (-not (Test-Path (Join-Path $entry.DirectoryName 'Denice.Runtime.dll'))) { throw 'The packaged Runtime is missing beside the Rust entry.' }
$duration = $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
$process = Start-Process -FilePath $entry.FullName -WorkingDirectory $entry.DirectoryName `
    -ArgumentList @('gui', '--duration', $duration, '--record', ('"' + $video + '"')) `
    -RedirectStandardOutput (Join-Path $output 'application.stdout.txt') `
    -RedirectStandardError (Join-Path $output 'application.stderr.txt') -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "DENICE exceeded $TimeoutSeconds seconds; partial evidence is preserved."
    }
    if ($process.ExitCode -ne 0) { throw "DENICE exited $($process.ExitCode)." }
    if (-not (Test-Path $video) -or (Get-Item $video).Length -eq 0) { throw 'DENICE did not produce a nonempty MP4.' }
    Write-Host 'DENICE exited zero and produced an MP4. Playback, GPU use, texture import, frame rate, and audio synchronization still require validation.'
} finally {
    if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
    $process.Dispose()
}
