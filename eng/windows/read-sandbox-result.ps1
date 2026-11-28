# Copyright (c) Denice.ai. All rights reserved.
# Shared validation for the controller and its platform-independent fixture tests.
function Read-DeniceSandboxResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][Guid]$JobId,
        [switch]$HardwareFixtures
    )
    $result = Get-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $result -or $result -is [Array]) { throw 'Guest completion must be one object.' }
    if ($result.jobId -cne $JobId.ToString()) { throw 'Guest completion belongs to a different job.' }
    if ($result.exitCode -isnot [int] -and $result.exitCode -isnot [long]) { throw 'Guest completion has no integer exit code.' }
    if ($result.exitCode -ne 0) { throw "Guest test failed: $($result.message)" }
    $expectedKind = if ($HardwareFixtures) { 'hardware-fixtures' } else { 'application' }
    if ($result.checkKind -cne $expectedKind) { throw 'Guest completed a different check than the controller requested.' }
    if ($HardwareFixtures) {
        Read-DeniceHardwareFixtureResult -OutputDirectory $OutputDirectory | Out-Null
        return $result
    }
    $video = Get-Item -LiteralPath (Join-Path $OutputDirectory 'denice-orb.mp4') -ErrorAction Stop
    if ($video.PSIsContainer -or $video.Length -le 0 -or ($video.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Guest reported success without a nonempty regular video file.'
    }
    return $result
}


# Both direct execution and Sandbox completion use the same required-fixture checks.
function Read-DeniceHardwareFixtureResult {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$OutputDirectory)
    foreach ($relative in @('hardware.trx', 'hardware-fixtures/hardware.mp4')) {
        $file = Get-Item -LiteralPath (Join-Path $OutputDirectory $relative) -ErrorAction Stop
        if ($file.PSIsContainer -or $file.Length -le 0 -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Missing nonempty regular fixture artifact: $relative"
        }
    }
    [xml]$report = Get-Content -LiteralPath (Join-Path $OutputDirectory 'hardware.trx') -Raw
    $results = @($report.TestRun.Results.UnitTestResult)
    $counters = $report.TestRun.ResultSummary.Counters
    if ($report.TestRun.ResultSummary.outcome -ne 'Completed' -or
        $results.Count -lt 4 -or [int]$counters.total -ne $results.Count -or
        [int]$counters.executed -ne $results.Count -or [int]$counters.passed -ne $results.Count -or
        @($results | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
        throw 'Hardware fixture report is incomplete, skipped, or failed.'
    }
    Assert-DeniceHardwareFixtureNames -Names @($results | ForEach-Object { $_.testName })
    $coverage = Join-Path $OutputDirectory 'hardware-coverage/coverage.cobertura.xml'
    if (Test-Path -LiteralPath $coverage) {
        $item = Get-Item -LiteralPath $coverage -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Hardware coverage must be a regular file.' }
        [xml]$cobertura = Get-Content -LiteralPath $coverage -Raw
        $packages = @($cobertura.coverage.packages.package | Where-Object { $_.name -eq 'Denice.CrossPlatform.Windows' })
        if ($packages.Count -ne 1 -or @($packages[0].SelectNodes('.//line')).Count -eq 0) { throw 'Hardware coverage does not instrument Denice.CrossPlatform.Windows.' }
    }
    return $report
}


# Verifies the intentionally constructed SDK/test payload before any code is executed.
function Assert-DeniceFixturePayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$ExpectedCommit
    )
    $root = (Get-Item -LiteralPath $PayloadDirectory -Force -ErrorAction Stop)
    if (-not $root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Fixture payload root must be a regular directory.' }
    $rootPath = $root.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    $files = New-Object 'Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    $pending = New-Object 'Collections.Generic.Queue[string]'
    $pending.Enqueue($rootPath)
    while ($pending.Count -gt 0) {
        foreach ($child in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Fixture payload contains a reparse path: $($child.Name)" }
            if ($child.PSIsContainer) { $pending.Enqueue($child.FullName) } else {
                $relative = $child.FullName.Substring($prefix.Length).Replace('\', '/')
                if ($files.ContainsKey($relative)) { throw 'Fixture payload has duplicate case-insensitive file paths.' }
                $files.Add($relative, $child.FullName)
            }
        }
    }
    $manifestPath = Join-Path $rootPath 'payload-sha256.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest -isnot [Array] -or $manifest.Count -eq 0) { throw 'Fixture manifest must be a nonempty array.' }
    $listed = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $manifest) {
        if ($item.path -isnot [string] -or [string]::IsNullOrWhiteSpace($item.path) -or
            $item.sha256 -isnot [string] -or $item.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Fixture manifest entry must contain a relative path and SHA256.' }
        $relative = $item.path.Replace('\', '/')
        if ($relative -eq 'payload-sha256.json' -or $relative.StartsWith('/') -or $relative.Contains(':') -or
            @($relative.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -ne 0 -or
            -not $listed.Add($relative)) { throw 'Fixture manifest has an invalid or duplicate path.' }
        if (-not $files.ContainsKey($relative)) { throw "Fixture manifest file is absent: $relative" }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $files[$relative]).Hash -ine $item.sha256) { throw "Fixture SHA256 mismatch: $relative" }
    }
    if ($files.Count -ne ($listed.Count + 1)) { throw 'Fixture manifest does not cover the exact payload file set.' }
    $source = @(Get-Content -LiteralPath (Join-Path $rootPath 'fixture-source.txt') | Where-Object { $_ -match '^fixture_commit=' })
    if ($source.Count -ne 1 -or $source[0] -ine ('fixture_commit=' + $ExpectedCommit)) { throw 'Fixture source commit does not match the requested build.' }
    $sdkVersion = (Get-Content -LiteralPath (Join-Path $rootPath 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($sdkVersion -isnot [string] -or $sdkVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Fixture SDK pin is invalid.' }
    foreach ($required in @('fixture-source.txt', 'global.json', 'dotnet/dotnet.exe', "dotnet/sdk/$sdkVersion/vstest.console.dll",
        'tests/Denice.CrossPlatform.Tests.dll', 'tests/Denice.CrossPlatform.Tests.runtimeconfig.json',
        'tests/testhost.dll', 'tests/xunit.runner.visualstudio.testadapter.dll',
        'tests/av_libglesv2.dll', 'tests/libSkiaSharp.dll', 'tests/hostfxr.dll', 'tests/hostpolicy.dll', 'tests/coreclr.dll',
        'tests/coverage.runsettings', 'tests/coverlet.collector.dll', 'tests/coverlet.core.dll', "dotnet/sdk/$sdkVersion/datacollector.dll")) {
        if (-not $listed.Contains($required)) { throw "Fixture payload is missing a required runtime/test file: $required" }
    }
}


# One required-case definition serves discovery and completed-result validation.
function Assert-DeniceHardwareFixtureNames {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Names)
    if ($Names.Count -ne 4) { throw 'Expected exactly the four isolated Windows GPU cases.' }
    foreach ($name in @('HardwareWindowSubmitsDistinctGpuFramesAndCloses', 'HardwareEncoderDecodesHeldFramesWithCorrectOrientationAndTimes')) {
        if (@($Names | Where-Object { $_ -match ('(^|\.)' + $name + '$') }).Count -ne 1) {
            throw "Required hardware fixture is absent or duplicated: $name"
        }
    }
    $imports = @($Names | Where-Object { $_ -match '(^|\.)ImportedTexturePreservesColorAndOrientation\(' })
    foreach ($format in @('R8G8B8A8_UNorm', 'B8G8R8A8_UNorm')) {
        if (@($imports | Where-Object { $_ -match $format }).Count -ne 1) {
            throw "Required texture import fixture is absent or duplicated: $format"
        }
    }
}

function Assert-DeniceHardwareFixtureDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ReportPath)
    $lines = @(Get-Content -LiteralPath $ReportPath)
    if (@($lines | Where-Object { $_ -match '(?i)(Test Run Aborted|TestPlatformException|fatal error|No test matches|^\s*Error\b|exited with error)' }).Count -ne 0) {
        throw 'Published fixture discovery reported an error, regardless of its process exit code.'
    }
    $names = @($lines | Where-Object { $_ -match '^\s+Denice\.CrossPlatform\.Tests\.DesktopGpuTests\.' } | ForEach-Object { $_.Trim() })
    Assert-DeniceHardwareFixtureNames -Names $names
}
