# Copyright (c) Denice.ai. All rights reserved.
# Runs only inside the disposable guest. No GitHub credentials are supplied.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][Guid]$JobId, [switch]$HardwareFixtures)
$ErrorActionPreference = 'Stop'
$output = 'C:\DeniceOutput'
$inputDirectory = 'C:\DeniceInput'
$exitCode = 1
$message = 'Guest did not complete.'
$heartbeat = $null
try {
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Start-Transcript -Path (Join-Path $output 'guest.txt') | Out-Null
    $heartbeat = Start-Job -ArgumentList $output -ScriptBlock {
        param($directory)
        while ($true) {
            [DateTime]::UtcNow.ToString('O') | Set-Content (Join-Path $directory 'heartbeat.txt')
            Start-Sleep -Seconds 5
        }
    }
    & (Join-Path $inputDirectory 'inspect-gpu-host.ps1') -OutputDirectory $output
    Copy-Item -LiteralPath (Join-Path $inputDirectory 'payload') -Destination 'C:\DenicePayload' -Recurse
    & (Join-Path $inputDirectory 'run-desktop-smoke.ps1') -PayloadDirectory 'C:\DenicePayload' -OutputDirectory $output -HardwareFixtures:$HardwareFixtures
    $exitCode = 0
    $message = if ($HardwareFixtures) { 'Isolated win-x64 hardware fixtures and decoded-output checks passed. Product CEF/audio readiness remains gated.' } else { 'Application execution completed. Hardware graphics and encoding remain subject to evidence review.' }
} catch {
    $message = $_.Exception.Message
    Write-Output $message
} finally {
    if ($heartbeat) { Stop-Job $heartbeat; Remove-Job $heartbeat }
    try { Stop-Transcript | Out-Null } catch { }
    $result = [ordered]@{
        jobId = $JobId.ToString()
        exitCode = $exitCode
        checkKind = $(if ($HardwareFixtures) { 'hardware-fixtures' } else { 'application' })
        message = $message
        completedUtc = [DateTime]::UtcNow.ToString('O')
        hardwareGraphics = $(if ($HardwareFixtures -and $exitCode -eq 0) { 'ISOLATED_FIXTURES_PASSED' } else { 'NOT_VERIFIED' })
        hardwareEncoding = $(if ($HardwareFixtures -and $exitCode -eq 0) { 'H264_DECODE_FIXTURE_PASSED' } else { 'NOT_VERIFIED' })
        productIntegration = 'NOT_ESTABLISHED_BY_FIXTURE_EXECUTION'
        testedRid = 'win-x64'
    }
    $temporary = Join-Path $output 'result.pending.json'
    $result | ConvertTo-Json | Set-Content -Encoding UTF8 $temporary
    Move-Item -LiteralPath $temporary -Destination (Join-Path $output 'result.json')
    # Closing the disposable guest is intentional only after the recorder and output files are closed.
    & shutdown.exe /s /t 0
}
