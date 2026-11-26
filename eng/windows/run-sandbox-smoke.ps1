# Copyright (c) Denice.ai. All rights reserved.
# Host-side controller. Run in an interactive Windows account, never Session 0.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PayloadDirectory,
    [Parameter(Mandatory = $true)][string]$WorkDirectory,
    [ValidateRange(60, 900)][int]$TimeoutSeconds = 300,
    [ValidateRange(4096, 32768)][int]$MemoryInMB = 8192,
    [switch]$HardwareFixtures
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows Sandbox requires Windows.' }
if (-not [Environment]::UserInteractive -or (Get-Process -Id $PID).SessionId -eq 0) {
    throw 'Run the controller in the dedicated account desktop session, not as a Windows service.'
}
$sandbox = (Get-Command WindowsSandbox.exe -ErrorAction Stop).Source
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'PayloadDirectory must be an unpacked install layout.' }
if ((Get-Item -LiteralPath $payload).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'PayloadDirectory cannot be a reparse point.' }
if (Get-ChildItem -LiteralPath $payload -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
    throw 'Payload contains a reparse point; refusing to map or copy paths outside the staged payload.'
}
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'WorkDirectory must be a new directory; previous evidence is never overwritten.' }
if ($work.StartsWith($payload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'WorkDirectory cannot be inside PayloadDirectory.'
}
. (Join-Path $PSScriptRoot 'read-sandbox-result.ps1')
$mutex = New-Object Threading.Mutex($false, 'Global\Denice.WindowsSandbox.Controller')
$ownsLock = $false
$process = $null
$completed = $false
try {
    try { $ownsLock = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsLock = $true }
    if (-not $ownsLock) { throw 'Another DENICE Sandbox controller is running.' }
    if (Get-Process -Name WindowsSandbox, WindowsSandboxClient -ErrorAction SilentlyContinue) {
        throw 'A Sandbox is already running. It was not started by this invocation and will not be stopped.'
    }
    New-Item -ItemType Directory -Path $work | Out-Null
    $inputDirectory = Join-Path $work 'input'
    $outputDirectory = Join-Path $work 'output'
    New-Item -ItemType Directory -Path $inputDirectory, $outputDirectory | Out-Null
    Copy-Item -LiteralPath $payload -Destination (Join-Path $inputDirectory 'payload') -Recurse
    foreach ($script in @('sandbox-bootstrap.ps1', 'inspect-gpu-host.ps1', 'run-desktop-smoke.ps1', 'read-sandbox-result.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $inputDirectory
    }
    $jobId = [Guid]::NewGuid()
    $configPath = Join-Path $work 'denice.wsb'
    $settings = New-Object Xml.XmlWriterSettings
    $settings.Indent = $true
    $writer = [Xml.XmlWriter]::Create($configPath, $settings)
    try {
        $writer.WriteStartElement('Configuration')
        $writer.WriteElementString('vGPU', 'Enable')
        $writer.WriteElementString('Networking', 'Disable')
        $writer.WriteElementString('AudioInput', 'Disable')
        $writer.WriteElementString('VideoInput', 'Disable')
        $writer.WriteElementString('ClipboardRedirection', 'Disable')
        $writer.WriteElementString('PrinterRedirection', 'Disable')
        $writer.WriteElementString('MemoryInMB', $MemoryInMB.ToString([Globalization.CultureInfo]::InvariantCulture))
        $writer.WriteStartElement('MappedFolders')
        foreach ($mapping in @(
            @{ Host = $inputDirectory; Guest = 'C:\DeniceInput'; ReadOnly = 'true' },
            @{ Host = $outputDirectory; Guest = 'C:\DeniceOutput'; ReadOnly = 'false' }
        )) {
            $writer.WriteStartElement('MappedFolder')
            $writer.WriteElementString('HostFolder', $mapping.Host)
            $writer.WriteElementString('SandboxFolder', $mapping.Guest)
            $writer.WriteElementString('ReadOnly', $mapping.ReadOnly)
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
        $writer.WriteStartElement('LogonCommand')
        $fixtureArgument = if ($HardwareFixtures) { ' -HardwareFixtures' } else { '' }
        $writer.WriteElementString('Command', ('powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\DeniceInput\sandbox-bootstrap.ps1 -JobId ' + $jobId + $fixtureArgument))
        $writer.WriteEndElement()
        $writer.WriteEndElement()
    } finally { $writer.Dispose() }
    $process = Start-Process -FilePath $sandbox -ArgumentList ('"' + $configPath + '"') -PassThru
    [ordered]@{ jobId = $jobId.ToString(); processId = $process.Id; startedUtc = $process.StartTime.ToUniversalTime().ToString('O') } |
        ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $work 'controller.json')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $resultPath = Join-Path $outputDirectory 'result.json'
    while (-not (Test-Path -LiteralPath $resultPath)) {
        if ($process.HasExited) { throw 'Sandbox launcher exited before guest completion. Inspect logs; Windows process handoff is not assumed successful.' }
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Sandbox exceeded $TimeoutSeconds seconds; partial output is preserved." }
        Start-Sleep -Seconds 1
        $process.Refresh()
    }
    $result = Read-DeniceSandboxResult -OutputDirectory $outputDirectory -JobId $jobId -HardwareFixtures:$HardwareFixtures
    $completed = $true
    Write-Host "Guest selected test completed. Evidence: $outputDirectory"
    Write-Host 'Isolated hardware fixtures do not establish product CEF/audio readiness or ARM64 hardware execution.'
} finally {
    if ($process) {
        if (-not $process.HasExited) {
            # Give the guest time to finish its own shutdown after finalizing all output.
            if (-not $process.WaitForExit(15000)) {
                & taskkill.exe /PID $process.Id /T /F | Out-Null
                if ($LASTEXITCODE -ne 0) { Write-Warning 'Owned Sandbox process did not terminate; inspect controller.json before the next run.' }
            }
        }
        $process.Dispose()
    }
    if ($ownsLock) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
    if ($completed -and (Get-Process -Name WindowsSandbox, WindowsSandboxClient -ErrorAction SilentlyContinue)) {
        throw 'Application completed, but Sandbox disposal is not established. Evidence remains available.'
    }
}
