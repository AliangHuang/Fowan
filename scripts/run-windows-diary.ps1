param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$configurationName = $Configuration.ToLowerInvariant()
$exe = Join-Path $repoRoot "out/windows-diary/$configurationName/Fowan.Diary.Windows.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    & (Join-Path $PSScriptRoot "build-windows-diary.ps1") -Configuration $Configuration
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
