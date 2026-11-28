# Stage W1 of the Windows browser-audio redirection proof (windows-linux-audio-proof.md). Runs on a free GitHub-hosted
# Windows runner (windows-latest for win-x64, windows-11-arm for win-arm64) from the repository root. Hosted runners have
# no audio endpoint: the proof's mixer runs on the test-only timer-driven FakeMixerOutput and DENICE's redirected
# enumerator supplies one virtual default endpoint, so nothing reaches any device.
#
# Usage: pwsh eng/audio-proof/windows/run-proof.ps1 -Rid win-x64 -Out $env:RUNNER_TEMP/audio-proof
param(
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'win-arm64')][string]$Rid,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
"OS: $([Environment]::OSVersion.VersionString); arch: $([Runtime.InteropServices.RuntimeInformation]::OSArchitecture)" | Tee-Object "$Out/host.txt"
# Measurement context: render endpoints the system has (hosted runners are expected to have none).
Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue | Format-Table -AutoSize | Out-String | Tee-Object -Append "$Out/host.txt"
Get-Service -Name Audiosrv, AudioEndpointBuilder -ErrorAction SilentlyContinue | Format-Table -AutoSize | Out-String | Tee-Object -Append "$Out/host.txt"

$env:DENICE_AUDIO_PROOF_OUT = "$Out/evidence"
dotnet test tests/Denice.CrossPlatform.Tests/Denice.CrossPlatform.Tests.csproj -c Release -r $Rid `
    -p:DeniceAudioRedirectionProof=true --filter Category=AudioRedirectionProof `
    --logger "console;verbosity=detailed" --logger "trx;LogFileName=$Out/audio-proof.trx" --blame `
    --diag "$Out/diag/vstest.log" 2>&1 | Tee-Object "$Out/test.log"
$code = $LASTEXITCODE
"test exit=$code" | Tee-Object "$Out/test-exit.txt"
Copy-Item -ErrorAction SilentlyContinue "$Out/evidence/cache/chromium.log" "$Out/chromium.log"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$Out/evidence/cache"
exit $code
