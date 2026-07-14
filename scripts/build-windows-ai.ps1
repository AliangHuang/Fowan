param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$CoreArtifactPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$outputConfiguration = $Configuration.ToLowerInvariant()

Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -in @("Fowan.Ai.Chat.Windows", "Fowan.Ai.Config.Windows", "fowan-core")
} | Stop-Process -Force

foreach ($project in @(
    "apps/windows-ai-chat/Fowan.Ai.Chat.Windows.csproj",
    "apps/windows-ai-config/Fowan.Ai.Config.Windows.csproj"
)) {
    & $dotnet build (Join-Path $repoRoot $project) -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ([string]::IsNullOrWhiteSpace($CoreArtifactPath)) {
    $coreConfiguration = if ($Configuration -eq "Release") { "release" } else { "debug" }
    $CoreArtifactPath = Join-Path (Split-Path -Parent $repoRoot) "FowanCore/target/$coreConfiguration/fowan-core.exe"
}

if (Test-Path -LiteralPath $CoreArtifactPath -PathType Leaf) {
    foreach ($toolOutput in @("windows-ai-chat", "windows-ai-config")) {
        $coreOutput = Join-Path $repoRoot "out/$toolOutput/$outputConfiguration/Core"
        New-Item -ItemType Directory -Path $coreOutput -Force | Out-Null
        Copy-Item -LiteralPath $CoreArtifactPath -Destination (Join-Path $coreOutput "fowan-core.exe") -Force
    }
} elseif ($Configuration -eq "Release") {
    throw "Release AI builds require a fowan-core.exe artifact."
}

Write-Host "AI Chat: $(Join-Path $repoRoot "out/windows-ai-chat/$outputConfiguration/Fowan.Ai.Chat.Windows.exe")"
Write-Host "AI Config: $(Join-Path $repoRoot "out/windows-ai-config/$outputConfiguration/Fowan.Ai.Config.Windows.exe")"
