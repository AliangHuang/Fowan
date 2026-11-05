param(
    [ValidateSet("chat", "config")]
    [string]$Tool = "chat",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("credentials", "models", "bindings")]
    [string]$Page = "credentials"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build-windows-ai.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$executable = if ($Tool -eq "chat") {
    Join-Path $repoRoot "build/windows/win-x64/app/Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe"
} else {
    Join-Path $repoRoot "build/windows/win-x64/app/Tools/AI/Config/Fowan.Ai.Config.Windows.Dev.exe"
}
$arguments = if ($Tool -eq "config") { @("--page=$Page") } else { @() }
$startProcessParameters = @{
    FilePath = $executable
    WorkingDirectory = Split-Path -Parent $executable
}
if ($arguments.Count -gt 0) {
    $startProcessParameters.ArgumentList = $arguments
}

Start-Process @startProcessParameters
