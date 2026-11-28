# Copyright (c) Denice.ai. All rights reserved.
# Read-only machine evidence. Device enumeration does not prove accelerated rendering.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This inspection requires Windows.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$os = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem
$facts = [ordered]@{
    operatingSystem = $os.Caption
    version = $os.Version
    build = $os.BuildNumber
    architecture = $os.OSArchitecture
    model = $computer.Model
    memoryBytes = $computer.TotalPhysicalMemory
    hypervisorPresent = $computer.HypervisorPresent
    interactive = [Environment]::UserInteractive
    sessionId = (Get-Process -Id $PID).SessionId
    processors = @(Get-CimInstance Win32_Processor | Select-Object Name, VirtualizationFirmwareEnabled, SecondLevelAddressTranslationExtensions)
    adapters = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, AdapterRAM, VideoProcessor, Status)
    sandboxInstalled = [bool](Get-Command WindowsSandbox.exe -ErrorAction SilentlyContinue)
    graphicsIntegration = 'NOT_TESTED'
    hardwareEncoding = 'NOT_TESTED'
}
$facts | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'windows-host.json')
$nvidia = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
if ($nvidia) {
    & $nvidia.Source --query-gpu=name,driver_version,memory.total,driver_model.current,driver_model.pending --format=csv 2>&1 |
        Out-File -Encoding UTF8 (Join-Path $OutputDirectory 'nvidia-smi.txt')
    if ($LASTEXITCODE -ne 0) { throw "nvidia-smi exited $LASTEXITCODE; inspect preserved evidence." }
    # Retain driver model, vGPU licensing and virtualization details for graphics capability diagnosis.
    & $nvidia.Source -q 2>&1 | Out-File -Encoding UTF8 (Join-Path $OutputDirectory 'nvidia-smi-query.txt')
    if ($LASTEXITCODE -ne 0) { throw "nvidia-smi -q exited $LASTEXITCODE; inspect preserved evidence." }
}
$dxdiag = Get-Command dxdiag.exe -ErrorAction SilentlyContinue
if ($dxdiag) {
    $path = [IO.Path]::GetFullPath((Join-Path $OutputDirectory 'dxdiag.xml'))
    $process = Start-Process -FilePath $dxdiag.Source -ArgumentList @('/whql:off', '/x', ('"' + $path + '"')) -PassThru
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw 'dxdiag exceeded 60 seconds; inventory evidence remains available.'
    }
    if ($process.ExitCode -ne 0) { throw "dxdiag exited $($process.ExitCode)." }
}
Write-Host 'Windows inventory saved. Native graphics, shared-texture import, and hardware encoding are NOT TESTED by this inspection.'
