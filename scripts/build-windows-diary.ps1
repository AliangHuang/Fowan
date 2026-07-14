param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-output.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "apps/windows/diary/app/Fowan.Diary.Windows.csproj"
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
        (Join-Path $repoRoot "apps/windows/diary/app/bin"),
        (Join-Path $repoRoot "apps/windows/diary/shared/bin")
    )) {
        Remove-DirectoryInside `
            -Path $legacyBin `
            -AllowedRoot (Split-Path -Parent $legacyBin) `
            -Description "legacy Diary bin output"
    }
}

$command = if ($Publish) { "publish" } else { "build" }
$staging = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "windows-diary"
$dotnetOutput = ConvertTo-DotnetOutputDirectory -Path $staging
try {

& $dotnet $command $project `
    -c $Configuration `
    -o $dotnetOutput `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Remove-IsolatedBuildDirectory -Path $staging
    exit $LASTEXITCODE
}

$exe = Join-Path $staging "Fowan.Diary.Windows.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    Remove-IsolatedBuildDirectory -Path $staging
    throw "Build completed, but expected executable was not found: $exe"
}

Install-IsolatedBuildDirectory -StagingDirectory $staging -Destination $output -AllowedOutputRoot $windowsOutputRoot
$exe = Join-Path $output "Fowan.Diary.Windows.exe"

Write-Host "Windows diary client $command output: $output"
Write-Host "Executable: $exe"
}
finally {
    Remove-IsolatedBuildDirectory -Path $staging
}
