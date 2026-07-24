param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Debug",
    [string]$CoreArtifactPath = "",
    [switch]$StopRunning
)

if ($StopRunning) {
    & (Join-Path $PSScriptRoot "stop-windows.ps1") -Component Ai
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot "build-windows.ps1") -Configuration $Configuration -CoreArtifactPath $CoreArtifactPath
exit $LASTEXITCODE
