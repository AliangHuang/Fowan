param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$CoreArtifactPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-output.ps1")
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$outputConfiguration = $Configuration.ToLowerInvariant()

$projects = @{
    "windows-ai-chat" = "apps/windows/ai/chat/Fowan.Ai.Chat.Windows.csproj"
    "windows-ai-config" = "apps/windows/ai/config/Fowan.Ai.Config.Windows.csproj"
}
$builds = @{}
foreach ($toolOutput in $projects.Keys) {
    $output = Join-Path $repoRoot "out/$toolOutput/$outputConfiguration"
    $staging = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component $toolOutput
    & $dotnet build (Join-Path $repoRoot $projects[$toolOutput]) -c $Configuration -o $staging --nologo
    if ($LASTEXITCODE -ne 0) {
        Remove-IsolatedBuildDirectory -Path $staging
        foreach ($completed in $builds.Values) { Remove-IsolatedBuildDirectory -Path $completed.Staging }
        exit $LASTEXITCODE
    }
    $executableName = if ($toolOutput -eq "windows-ai-chat") { "Fowan.Ai.Chat.Windows.exe" } else { "Fowan.Ai.Config.Windows.exe" }
    $expected = Join-Path $staging $executableName
    if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) {
        Remove-IsolatedBuildDirectory -Path $staging
        foreach ($completed in $builds.Values) { Remove-IsolatedBuildDirectory -Path $completed.Staging }
        throw "Build completed, but the expected executable was not found: $expected"
    }
    $builds[$toolOutput] = @{ Output = $output; Staging = $staging }
}

if ([string]::IsNullOrWhiteSpace($CoreArtifactPath)) {
    $coreConfiguration = if ($Configuration -eq "Release") { "release" } else { "debug" }
    $CoreArtifactPath = Join-Path (Split-Path -Parent $repoRoot) "FowanCore/out/core/windows/win-x64/$coreConfiguration/fowan-core.exe"
}

if (Test-Path -LiteralPath $CoreArtifactPath -PathType Leaf) {
    foreach ($toolOutput in @("windows-ai-chat", "windows-ai-config")) {
        $coreOutput = Join-Path $builds[$toolOutput].Staging "Core"
        New-Item -ItemType Directory -Path $coreOutput -Force | Out-Null
        Copy-Item -LiteralPath $CoreArtifactPath -Destination (Join-Path $coreOutput "fowan-core.exe") -Force
    }
} elseif ($Configuration -eq "Release") {
    foreach ($completed in $builds.Values) { Remove-IsolatedBuildDirectory -Path $completed.Staging }
    throw "Release AI builds require a fowan-core.exe artifact."
}

foreach ($toolOutput in @("windows-ai-chat", "windows-ai-config")) {
    Install-IsolatedBuildDirectory -StagingDirectory $builds[$toolOutput].Staging `
        -Destination $builds[$toolOutput].Output `
        -AllowedOutputRoot (Join-Path $repoRoot "out/$toolOutput")
}

Write-Host "AI Chat: $(Join-Path $repoRoot "out/windows-ai-chat/$outputConfiguration/Fowan.Ai.Chat.Windows.exe")"
Write-Host "AI Config: $(Join-Path $repoRoot "out/windows-ai-config/$outputConfiguration/Fowan.Ai.Config.Windows.exe")"
