param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$ReleaseRepository = "AliangHuang/Fowan",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $repoRoot "out/installer/windows/$RuntimeIdentifier"
$appStage = Join-Path $installerRoot "app"
$prereqRoot = Join-Path $installerRoot "prerequisites"
$vcRedistPath = Join-Path $prereqRoot "vc_redist.x64.exe"
$windowsProject = Join-Path $repoRoot "apps/windows/Fowan.Windows.csproj"
$todoProject = Join-Path $repoRoot "apps/windows-todo/Fowan.Todo.Windows.csproj"
$stickyProject = Join-Path $repoRoot "apps/windows-todo-sticky/Fowan.Todo.Sticky.Windows.csproj"
$issPath = Join-Path $repoRoot "installer/windows/Fowan.iss"
$changelogRoot = Join-Path $repoRoot "changelogs"
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$vcRedistUrl = "https://aka.ms/vc14/vc_redist.x64.exe"
$assemblyVersion = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }

function Assert-PathInside {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $expectedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected root. Path: $resolvedPath Root: $resolvedRoot"
    }
}

function Publish-FowanProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    & $dotnet publish $Project `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:FowanVersion=$Version `
        -p:FowanAssemblyVersion=$assemblyVersion `
        -p:PublishSingleFile=false `
        -o $Output `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Get-ChangelogSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Changelog file was not found: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $pattern = "(?ms)^##\s+\[?$escapedVersion\]?(?:\s+-\s+.*)?\r?\n.*?(?=^##\s+|\z)"
    $match = [regex]::Match($content, $pattern)
    if (-not $match.Success) {
        throw "Changelog file does not contain a section for version $Version`: $Path"
    }

    return $match.Value.Trim()
}

function Write-ReleaseNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    $releaseNotesRoot = Join-Path $OutputRoot "ReleaseNotes"
    if (Test-Path -LiteralPath $releaseNotesRoot) {
        Assert-PathInside -Path $releaseNotesRoot -Root $OutputRoot
        Remove-Item -LiteralPath $releaseNotesRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $releaseNotesRoot | Out-Null

    $componentChangelogs = @(
        @{
            Id = "toolbox"
            Title = "Fowan Toolbox"
            Path = Join-Path $changelogRoot "toolbox/CHANGELOG.md"
        },
        @{
            Id = "todo"
            Title = "Todo"
            Path = Join-Path $changelogRoot "tools/todo/CHANGELOG.md"
        }
    )

    $combined = New-Object System.Collections.Generic.List[string]
    $combined.Add("Fowan $Version Release Notes")
    $combined.Add("")

    foreach ($component in $componentChangelogs) {
        $section = Get-ChangelogSection -Path $component.Path -Version $Version
        $componentFile = Join-Path $releaseNotesRoot "$($component.Id).md"
        Set-Content -LiteralPath $componentFile -Value $section -Encoding UTF8

        $combined.Add("[$($component.Title)]")
        $combined.Add($section)
        $combined.Add("")
    }

    $combinedPath = Join-Path $releaseNotesRoot "release-notes.txt"
    Set-Content -LiteralPath $combinedPath -Value $combined -Encoding UTF8

    return $combinedPath
}

function Write-UpdateManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SetupExe,
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRepository,
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    $setupFileName = Split-Path -Leaf $SetupExe
    $releaseTag = "v$Version"
    $releaseBaseUrl = "https://github.com/$ReleaseRepository/releases/download/$releaseTag"
    $setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $SetupExe).Hash.ToLowerInvariant()
    $manifestPath = Join-Path $OutputRoot "fowan-update.json"
    $manifest = [ordered]@{
        version = $Version
        channel = "stable"
        installerUrl = "$releaseBaseUrl/$setupFileName"
        installerSha256 = $setupHash
        releaseNotesUrl = "https://github.com/$ReleaseRepository/releases/tag/$releaseTag"
        notes = "Fowan $Version release."
    }

    $json = $manifest | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $json + [System.Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    return $manifestPath
}

function Resolve-Iscc {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6/ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6/ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    return $null
}

function Save-Download {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    try {
        Invoke-WebRequest -Uri $Url -OutFile $Output -UseBasicParsing
        return
    }
    catch {
        $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
        if (-not $curl) {
            throw
        }

        & $curl.Source -L --retry 5 --retry-delay 2 --fail --output $Output $Url
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

function Ensure-VcRedist {
    New-Item -ItemType Directory -Force -Path $prereqRoot | Out-Null

    if (-not (Test-Path -LiteralPath $vcRedistPath -PathType Leaf)) {
        Write-Host "Downloading Microsoft Visual C++ Redistributable x64..."
        Save-Download -Url $vcRedistUrl -Output $vcRedistPath
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $vcRedistPath
    if ($signature.Status -ne "Valid" -or
        $signature.SignerCertificate.Subject -notlike "*Microsoft Corporation*") {
        throw "Visual C++ Redistributable signature validation failed: $vcRedistPath"
    }
}

$outRoot = Join-Path $repoRoot "out"
Assert-PathInside -Path $installerRoot -Root $outRoot
New-Item -ItemType Directory -Force -Path $installerRoot | Out-Null
Ensure-VcRedist

if (Test-Path -LiteralPath $appStage) {
    Assert-PathInside -Path $appStage -Root $installerRoot
    Remove-Item -LiteralPath $appStage -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $appStage | Out-Null

$todoStage = Join-Path $appStage "Tools/Todo"
New-Item -ItemType Directory -Force -Path $todoStage | Out-Null

Publish-FowanProject -Project $windowsProject -Output $appStage
Publish-FowanProject -Project $todoProject -Output $todoStage
Publish-FowanProject -Project $stickyProject -Output $todoStage

$requiredExecutables = @(
    (Join-Path $appStage "Fowan.Windows.exe"),
    (Join-Path $todoStage "Fowan.Todo.Windows.exe"),
    (Join-Path $todoStage "Fowan.Todo.Sticky.Windows.exe")
)

foreach ($exe in $requiredExecutables) {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Package staging is missing required executable: $exe"
    }
}

$releaseNotesPath = Write-ReleaseNotes -OutputRoot $appStage

$fileCount = (Get-ChildItem -LiteralPath $appStage -Recurse -File | Measure-Object).Count
$sizeBytes = (Get-ChildItem -LiteralPath $appStage -Recurse -File | Measure-Object Length -Sum).Sum
$sizeMb = [Math]::Round($sizeBytes / 1MB, 1)

Write-Host "Fowan Windows app staging: $appStage"
Write-Host "Release notes: $releaseNotesPath"
Write-Host "Files: $fileCount"
Write-Host "Size: $sizeMb MB"

if ($SkipInstaller) {
    Write-Host "Skipping installer compilation because -SkipInstaller was specified."
    exit 0
}

if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw "Inno Setup script was not found: $issPath"
}

$iscc = Resolve-Iscc
if (-not $iscc) {
    Write-Warning "Inno Setup compiler ISCC.exe was not found. Install Inno Setup 6, then rerun this script to build the setup .exe."
    Write-Host "Staging output is ready: $appStage"
    exit 0
}

& $iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$appStage" `
    "/DReleaseNotesPath=$releaseNotesPath" `
    "/DVcRedistPath=$vcRedistPath" `
    "/DOutputDir=$installerRoot" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$setupExe = Join-Path $installerRoot "FowanSetup-$Version-$RuntimeIdentifier.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer compiler completed, but expected setup executable was not found: $setupExe"
}

$updateManifestPath = Write-UpdateManifest `
    -SetupExe $setupExe `
    -Version $Version `
    -ReleaseRepository $ReleaseRepository `
    -OutputRoot $installerRoot

Write-Host "Fowan Windows installer: $setupExe"
Write-Host "Update manifest: $updateManifestPath"
