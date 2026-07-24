param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "build/windows/win-x64/app/Tools/Todo/Fowan.Todo.Windows.Dev.exe"
$stickyExe = Join-Path $repoRoot "build/windows/win-x64/app/Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe"

if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $stickyExe)) {
    & (Join-Path $PSScriptRoot "build-windows-todo.ps1") -Configuration $Configuration
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
