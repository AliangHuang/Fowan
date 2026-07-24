param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Publish,
    [string]$CoreArtifactPath = ""
)

& (Join-Path $PSScriptRoot "build-windows.ps1") @PSBoundParameters
exit $LASTEXITCODE
