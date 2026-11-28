# Copyright (c) Denice.ai. All rights reserved.
# Runs with PowerShell on any host. Does not launch Windows Sandbox.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'read-sandbox-result.ps1')
$directory = Join-Path ([IO.Path]::GetTempPath()) ('denice-result-' + [Guid]::NewGuid())
$jobId = [Guid]::NewGuid()
$passed = 0
function Assert-Rejected([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Incorrectly accepted: $Name" }
    $script:passed++
}
function Write-Result($Value) {
    if (-not $Value.ContainsKey('checkKind')) { $Value.checkKind = 'application' }
    $Value | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'result.json')
}
try {
    New-Item -ItemType Directory $directory | Out-Null
    $read = { Read-DeniceSandboxResult -OutputDirectory $directory -JobId $jobId }
    Assert-Rejected 'missing completion' $read
    'invalid JSON' | Set-Content (Join-Path $directory 'result.json')
    Assert-Rejected 'malformed completion' $read
    Write-Result @{ jobId = [Guid]::NewGuid().ToString(); exitCode = 0 }
    Assert-Rejected 'different job' $read
    Write-Result @{ jobId = $jobId.ToString(); exitCode = '0' }
    Assert-Rejected 'string exit code' $read
    Write-Result @{ jobId = $jobId.ToString() }
    Assert-Rejected 'missing exit code' $read
    Write-Result @{ jobId = $jobId.ToString(); exitCode = 1; message = 'application failed' }
    Assert-Rejected 'failed application' $read
    Write-Result @{ jobId = $jobId.ToString(); exitCode = 0 }
    Assert-Rejected 'missing footage' $read
    $video = Join-Path $directory 'denice-orb.mp4'
    [IO.File]::WriteAllBytes($video, [byte[]]@())
    Assert-Rejected 'empty footage' $read
    Remove-Item $video
    New-Item -ItemType Directory $video | Out-Null
    Assert-Rejected 'directory posing as footage' $read
    Remove-Item $video
    [IO.File]::WriteAllBytes($video, [byte[]]@(1, 2, 3))
    $result = & $read
    if ($result.exitCode -ne 0) { throw 'Valid execution result was not accepted.' }
    $passed++
    Write-Result @{ jobId = $jobId.ToString(); exitCode = 0; checkKind = 'hardware-fixtures' }
    Assert-Rejected 'different requested check' $read
    $readHardware = { Read-DeniceSandboxResult -OutputDirectory $directory -JobId $jobId -HardwareFixtures }
    Assert-Rejected 'hardware report missing' $readHardware
    $media = Join-Path $directory 'hardware-fixtures'
    New-Item -ItemType Directory $media | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $media 'hardware.mp4'), [byte[]]@(1, 2, 3))
    $trx = @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results>
<UnitTestResult testName="HardwareWindowSubmitsDistinctGpuFramesAndCloses" outcome="Passed" />
<UnitTestResult testName="HardwareEncoderDecodesHeldFramesWithCorrectOrientationAndTimes" outcome="Passed" />
<UnitTestResult testName="ImportedTexturePreservesColorAndOrientation(format: R8G8B8A8_UNorm)" outcome="Passed" />
<UnitTestResult testName="ImportedTexturePreservesColorAndOrientation(format: B8G8R8A8_UNorm)" outcome="Passed" />
</Results>
<ResultSummary outcome="Completed"><Counters total="4" executed="4" passed="4" /></ResultSummary>
</TestRun>
'@
    $trxPath = Join-Path $directory 'hardware.trx'
    $trx.Replace('outcome="Passed"', 'outcome="NotExecuted"') | Set-Content $trxPath
    Assert-Rejected 'skipped hardware fixtures' $readHardware
    $trx.Replace('HardwareEncoderDecodesHeldFramesWithCorrectOrientationAndTimes', 'SomeOtherTest') | Set-Content $trxPath
    Assert-Rejected 'encoder fixture absent' $readHardware
    $trx.Replace('B8G8R8A8_UNorm', 'R8G8B8A8_UNorm') | Set-Content $trxPath
    Assert-Rejected 'BGRA fixture absent and RGBA duplicated' $readHardware
    $trx.Replace('executed="4"', 'executed="3"') | Set-Content $trxPath
    Assert-Rejected 'incomplete execution counters' $readHardware
    $trx | Set-Content $trxPath
    $hardwareResult = & $readHardware
    if ($hardwareResult.checkKind -ne 'hardware-fixtures') { throw 'Hardware result was not accepted.' }
    $passed++
    [xml]$validTrx = $trx
    $discoveryPath = Join-Path $directory 'discovery.txt'
    $discovery = @($validTrx.TestRun.Results.UnitTestResult | ForEach-Object { '    Denice.CrossPlatform.Tests.DesktopGpuTests.' + $_.testName })
    $discovery | Set-Content $discoveryPath
    Assert-DeniceHardwareFixtureDiscovery -ReportPath $discoveryPath
    $passed++
    @($discovery + 'Test Run Aborted') | Set-Content $discoveryPath
    Assert-Rejected 'discovery aborted despite listing cases' { Assert-DeniceHardwareFixtureDiscovery -ReportPath $discoveryPath }
    @($discovery[0], $discovery[1], $discovery[2], $discovery[2]) | Set-Content $discoveryPath
    Assert-Rejected 'discovery duplicates RGBA and omits BGRA' { Assert-DeniceHardwareFixtureDiscovery -ReportPath $discoveryPath }
    $payload = Join-Path $directory 'fixture-payload'
    New-Item -ItemType Directory (Join-Path $payload 'dotnet/sdk/10.0.400'), (Join-Path $payload 'tests') | Out-Null
    $commit = 'a' * 40
    ('fixture_commit=' + $commit) | Set-Content (Join-Path $payload 'fixture-source.txt')
    'sdk fixture' | Set-Content (Join-Path $payload 'dotnet/dotnet.exe')
    'sdk vstest fixture' | Set-Content (Join-Path $payload 'dotnet/sdk/10.0.400/vstest.console.dll')
    'sdk datacollector fixture' | Set-Content (Join-Path $payload 'dotnet/sdk/10.0.400/datacollector.dll')
    '{"sdk":{"version":"10.0.400"}}' | Set-Content (Join-Path $payload 'global.json')
    foreach ($name in @('Denice.CrossPlatform.Tests.dll', 'Denice.CrossPlatform.Tests.runtimeconfig.json', 'testhost.dll',
        'xunit.runner.visualstudio.testadapter.dll', 'av_libglesv2.dll', 'libSkiaSharp.dll', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll',
        'coverage.runsettings', 'coverlet.collector.dll', 'coverlet.core.dll')) {
        'test fixture' | Set-Content (Join-Path $payload ('tests/' + $name))
    }
    $entries = @(Get-ChildItem $payload -Recurse -File | ForEach-Object {
        [PSCustomObject]@{ path = $_.FullName.Substring($payload.Length + 1); sha256 = (Get-FileHash -Algorithm SHA256 $_.FullName).Hash }
    })
    $manifestPath = Join-Path $payload 'payload-sha256.json'
    $entries | ConvertTo-Json | Set-Content $manifestPath
    $verifyPayload = { Assert-DeniceFixturePayload -PayloadDirectory $payload -ExpectedCommit $commit }
    & $verifyPayload
    $passed++
    Assert-Rejected 'different fixture source commit' { Assert-DeniceFixturePayload -PayloadDirectory $payload -ExpectedCommit ('b' * 40) }
    'unlisted' | Set-Content (Join-Path $payload 'extra.txt')
    Assert-Rejected 'unlisted fixture file' $verifyPayload
    Remove-Item (Join-Path $payload 'extra.txt')
    @($entries + $entries[0]) | ConvertTo-Json | Set-Content $manifestPath
    Assert-Rejected 'duplicate fixture path' $verifyPayload
    '[]' | Set-Content $manifestPath
    Assert-Rejected 'empty fixture manifest' $verifyPayload
    $entries | ConvertTo-Json | Set-Content $manifestPath
    'modified sdk' | Set-Content (Join-Path $payload 'dotnet/dotnet.exe')
    Assert-Rejected 'changed fixture bytes' $verifyPayload
    'sdk fixture' | Set-Content (Join-Path $payload 'dotnet/dotnet.exe')
    $entries[0].path = '../outside'
    $entries | ConvertTo-Json | Set-Content $manifestPath
    Assert-Rejected 'escaping fixture path' $verifyPayload
    $entries[0].path = ''
    $entries | ConvertTo-Json | Set-Content $manifestPath
    Assert-Rejected 'empty fixture path' $verifyPayload
    Write-Host "$passed completion checks passed. Fixture bytes deliberately do not prove MP4 playback or GPU rendering."
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
