param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "apps/windows-diary/Fowan.Diary.Windows.csproj"
$configurationName = $Configuration.ToLowerInvariant()
$windowsOutputRoot = Join-Path $repoRoot "out/windows-diary"
$output = Join-Path $windowsOutputRoot $configurationName
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }

function Remove-DirectoryInside {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$AllowedRoot,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\', '/')
    $expectedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean $Description outside expected root: $resolvedPath"
    }

    Remove-Item -LiteralPath $Path -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $windowsOutputRoot | Out-Null

if ($Clean) {
    Remove-DirectoryInside `
        -Path $output `
        -AllowedRoot $windowsOutputRoot `
        -Description "Diary output"

    foreach ($legacyBin in @(
        (Join-Path $repoRoot "apps/windows-diary/bin"),
        (Join-Path $repoRoot "apps/windows-diary-shared/bin")
    )) {
        Remove-DirectoryInside `
            -Path $legacyBin `
            -AllowedRoot (Split-Path -Parent $legacyBin) `
            -Description "legacy Diary bin output"
    }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

$command = if ($Publish -or $Configuration -eq "Release") { "publish" } else { "build" }

& $dotnet $command $project `
    -c $Configuration `
    --nologo

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Join-Path $output "Fowan.Diary.Windows.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build completed, but expected executable was not found: $exe"
}

Write-Host "Windows diary client $command output: $output"
Write-Host "Executable: $exe"
