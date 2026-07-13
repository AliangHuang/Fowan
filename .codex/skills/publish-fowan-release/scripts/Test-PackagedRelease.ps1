param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../../.."))
$packageRoot = Join-Path $repoRoot "out/installer/windows/$RuntimeIdentifier"
$releaseNotesRoot = Join-Path $packageRoot "app/ReleaseNotes"
$installerPath = Join-Path $packageRoot "FowanSetup-$Version-$RuntimeIdentifier.exe"
$manifestPath = Join-Path $packageRoot "fowan-update.json"

function Get-VersionSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Changelog was not found: $Path"
    }

    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match "^##\s+\[?$escapedVersion\]?(?:\s+-\s+.*)?$") {
            $start = $index
            break
        }
    }

    if ($start -lt 0) {
        throw "$Path does not contain a section for version $Version."
    }

    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match "^##\s+") {
            $end = $index
            break
        }
    }

    return ($lines[$start..($end - 1)] -join [Environment]::NewLine).Trim()
}

function Normalize-Text {
    param(
        [AllowEmptyString()]
        [string]$Text
    )

    return (($Text -replace "`r`n", "`n") -replace "`r", "`n").Trim()
}

if (-not (Test-Path -LiteralPath $releaseNotesRoot -PathType Container)) {
    throw "Packaged release-notes directory was not found: $releaseNotesRoot"
}

$components = @(
    @{
        Id = "toolbox"
        Title = "Fowan Toolbox"
        Changelog = Join-Path $repoRoot "changelogs/toolbox/CHANGELOG.md"
    },
    @{
        Id = "todo"
        Title = "Todo"
        Changelog = Join-Path $repoRoot "changelogs/tools/todo/CHANGELOG.md"
    },
    @{
        Id = "diary"
        Title = "Diary"
        Changelog = Join-Path $repoRoot "changelogs/tools/diary/CHANGELOG.md"
    }
)

$combined = New-Object System.Collections.Generic.List[string]
$combined.Add("Fowan $Version Release Notes")
$combined.Add("")
$packagedNoteFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]

foreach ($component in $components) {
    $sourceSection = Get-VersionSection -Path $component.Changelog
    $packagedPath = Join-Path $releaseNotesRoot "$($component.Id).md"
    if (-not (Test-Path -LiteralPath $packagedPath -PathType Leaf)) {
        throw "Packaged component release notes were not found: $packagedPath"
    }

    $packagedSection = [System.IO.File]::ReadAllText($packagedPath, [System.Text.Encoding]::UTF8)
    if ((Normalize-Text $packagedSection) -ne (Normalize-Text $sourceSection)) {
        throw "$($component.Title) packaged release notes do not match the $Version changelog. Re-run package-windows.ps1 before publishing."
    }

    $packagedNoteFiles.Add((Get-Item -LiteralPath $packagedPath))
    $combined.Add("[$($component.Title)]")
    $combined.Add($sourceSection)
    $combined.Add("")
    Write-Host "$($component.Title): packaged notes match the source changelog."
}

$combinedPath = Join-Path $releaseNotesRoot "release-notes.txt"
if (-not (Test-Path -LiteralPath $combinedPath -PathType Leaf)) {
    throw "Combined packaged release notes were not found: $combinedPath"
}

$combinedFile = Get-Item -LiteralPath $combinedPath
$packagedNoteFiles.Add($combinedFile)
$packagedCombined = [System.IO.File]::ReadAllText($combinedPath, [System.Text.Encoding]::UTF8)
$expectedCombined = $combined -join [Environment]::NewLine
if ((Normalize-Text $packagedCombined) -ne (Normalize-Text $expectedCombined)) {
    throw "Combined packaged release notes do not match the current component changelogs. Re-run package-windows.ps1 before publishing."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer was not found: $installerPath"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Update manifest was not found: $manifestPath"
}

$installer = Get-Item -LiteralPath $installerPath
$newestPackagedNote = $packagedNoteFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($installer.LastWriteTimeUtc -lt $newestPackagedNote.LastWriteTimeUtc) {
    throw "Installer is older than its packaged release notes. Rebuild the installer before publishing."
}

$manifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedInstallerUrl = "https://github.com/AliangHuang/Fowan/releases/download/v$Version/FowanSetup-$Version-$RuntimeIdentifier.exe"

if ($manifest.version -ne $Version) {
    throw "Manifest version '$($manifest.version)' does not match $Version."
}
if ($manifest.channel -ne "stable") {
    throw "Manifest channel must be stable."
}
if ($manifest.installerUrl -ne $expectedInstallerUrl) {
    throw "Manifest installer URL does not match the expected release asset URL."
}
if ($manifest.installerSha256 -ne $installerHash) {
    throw "Manifest SHA-256 does not match the packaged installer."
}

Write-Host "Combined release notes match all current changelog sections."
Write-Host "Installer is newer than its packaged release notes."
Write-Host "Manifest metadata and SHA-256 match the installer."
Write-Host "Packaged release validation passed for Fowan $Version."
