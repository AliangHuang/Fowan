param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Publish,
    [string]$CoreArtifactPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "apps/windows/Fowan.Windows.csproj"
$configurationName = $Configuration.ToLowerInvariant()
$windowsOutputRoot = Join-Path $repoRoot "out/windows"
$output = Join-Path $windowsOutputRoot $configurationName
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }

New-Item -ItemType Directory -Force -Path $windowsOutputRoot | Out-Null

if ($Clean -and (Test-Path -LiteralPath $output)) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($output)
    $resolvedRoot = [System.IO.Path]::GetFullPath($windowsOutputRoot).TrimEnd('\', '/')
    $expectedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedOutput.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output outside repository out/windows directory: $resolvedOutput"
    }

    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

$usePublish = $Publish -or $Configuration -eq "Release"
$command = if ($usePublish) { "publish" } else { "build" }

& $dotnet $command $project `
    -c $Configuration `
    -o $output `
    --nologo

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Join-Path $output "Fowan.Windows.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build completed, but expected executable was not found: $exe"
}

$resolvedCoreArtifact = $CoreArtifactPath
if ([string]::IsNullOrWhiteSpace($resolvedCoreArtifact)) {
    $workspaceRoot = Split-Path -Parent $repoRoot
    $coreConfiguration = if ($Configuration -eq "Release") { "release" } else { "debug" }
    $candidate = Join-Path $workspaceRoot "FowanCore/target/$coreConfiguration/fowan-core.exe"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $resolvedCoreArtifact = $candidate
    }
}

if (-not [string]::IsNullOrWhiteSpace($resolvedCoreArtifact)) {
    if (-not (Test-Path -LiteralPath $resolvedCoreArtifact -PathType Leaf)) {
        throw "Fowan Core artifact was not found: $resolvedCoreArtifact"
    }
    $coreOutput = Join-Path $output "Core"
    New-Item -ItemType Directory -Force -Path $coreOutput | Out-Null
    Copy-Item -LiteralPath $resolvedCoreArtifact -Destination (Join-Path $coreOutput "fowan-core.exe") -Force
}
elseif ($Configuration -eq "Release" -or $Publish) {
    throw "Release builds require -CoreArtifactPath pointing to fowan-core.exe."
}

Write-Host "Windows client $command output: $output"
Write-Host "Executable: $exe"
if (-not [string]::IsNullOrWhiteSpace($resolvedCoreArtifact)) {
    Write-Host "Fowan Core: $(Join-Path $output 'Core/fowan-core.exe')"
}
