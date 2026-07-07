param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$configurationName = $Configuration.ToLowerInvariant()
$exe = Join-Path $repoRoot "out/windows-todo/$configurationName/Fowan.Todo.Windows.exe"
$stickyExe = Join-Path $repoRoot "out/windows-todo/$configurationName/Fowan.Todo.Sticky.Windows.exe"

if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $stickyExe)) {
    & (Join-Path $PSScriptRoot "build-windows-todo.ps1") -Configuration $Configuration
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
