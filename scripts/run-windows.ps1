param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "build/windows/win-x64/app/Fowan.Windows.Dev.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    & (Join-Path $PSScriptRoot "build-windows.ps1") -Configuration $Configuration
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
