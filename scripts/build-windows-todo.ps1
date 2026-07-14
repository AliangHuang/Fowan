param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-output.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "apps/windows/todo/app/Fowan.Todo.Windows.csproj"
$stickyProject = Join-Path $repoRoot "apps/windows/todo/sticky/Fowan.Todo.Sticky.Windows.csproj"
$configurationName = $Configuration.ToLowerInvariant()
$windowsOutputRoot = Join-Path $repoRoot "out/windows-todo"
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
        -Description "Todo output"

    foreach ($legacyBin in @(
        (Join-Path $repoRoot "apps/windows/todo/app/bin"),
        (Join-Path $repoRoot "apps/windows/todo/shared/bin"),
        (Join-Path $repoRoot "apps/windows/todo/sticky/bin")
    )) {
        Remove-DirectoryInside `
            -Path $legacyBin `
            -AllowedRoot (Split-Path -Parent $legacyBin) `
            -Description "legacy Todo bin output"
    }
}

$usePublish = $Publish
$command = if ($usePublish) { "publish" } else { "build" }
$candidate = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "windows-todo-candidate"
$staging = $null
try {

$index = 0
foreach ($projectToBuild in @($project, $stickyProject)) {
    $staging = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "windows-todo-$index"
    $dotnetOutput = ConvertTo-DotnetOutputDirectory -Path $staging
    & $dotnet $command $projectToBuild `
        -c $Configuration `
        -o $dotnetOutput `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Remove-IsolatedBuildDirectory -Path $staging
        Remove-IsolatedBuildDirectory -Path $candidate
        exit $LASTEXITCODE
    }
    Copy-BuildDirectoryContent -Source $staging -Destination $candidate
    Remove-IsolatedBuildDirectory -Path $staging
    $index++
}

$exe = Join-Path $candidate "Fowan.Todo.Windows.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    Remove-IsolatedBuildDirectory -Path $candidate
    throw "Build completed, but expected executable was not found: $exe"
}

$stickyExe = Join-Path $candidate "Fowan.Todo.Sticky.Windows.exe"
if (-not (Test-Path -LiteralPath $stickyExe)) {
    Remove-IsolatedBuildDirectory -Path $candidate
    throw "Build completed, but expected sticky executable was not found: $stickyExe"
}

Install-IsolatedBuildDirectory -StagingDirectory $candidate -Destination $output -AllowedOutputRoot $windowsOutputRoot
$exe = Join-Path $output "Fowan.Todo.Windows.exe"
$stickyExe = Join-Path $output "Fowan.Todo.Sticky.Windows.exe"

Write-Host "Windows todo client $command output: $output"
Write-Host "Executable: $exe"
Write-Host "Sticky executable: $stickyExe"
}
finally {
    Remove-IsolatedBuildDirectory -Path $staging
    Remove-IsolatedBuildDirectory -Path $candidate
}
