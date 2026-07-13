param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [ValidateSet("Toolbox", "Todo", "Diary")]
    [string[]]$AllowNoUserVisibleChanges = @()
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../../.."))
$currentTag = "v$Version"
$previousTag = @(& git -C $repoRoot tag --list "v*" --sort=-v:refname) |
    Where-Object { $_ -and $_ -ne $currentTag } |
    Select-Object -First 1

if (-not $previousTag) {
    throw "Unable to resolve a release tag before $currentTag."
}

$changedFiles = @(& git -c core.autocrlf=false -C $repoRoot diff --name-only $previousTag --)
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

function Get-VersionSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = Join-Path $repoRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Changelog was not found: $Path"
    }

    $lines = Get-Content -LiteralPath $fullPath -Encoding UTF8
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

$components = @(
    @{
        Name = "Toolbox"
        Changelog = "changelogs/toolbox/CHANGELOG.md"
        PathPattern = '^(apps/windows/|installer/windows/|scripts/(?:package-windows|build-windows)\.ps1|docs/windows_installer_spec\.md)'
    },
    @{
        Name = "Todo"
        Changelog = "changelogs/tools/todo/CHANGELOG.md"
        PathPattern = '^(apps/windows-todo(?:-sticky|-shared)?/|tests/Fowan\.Todo\.|docs/windows_todo_)'
    },
    @{
        Name = "Diary"
        Changelog = "changelogs/tools/diary/CHANGELOG.md"
        PathPattern = '^(apps/windows-diary(?:-shared)?/|tests/Fowan\.Diary\.|docs/windows_diary_)'
    }
)

$placeholderPattern = '(?i)\u6ca1\u6709\u7528\u6237\u53ef\u89c1\u53d8\u5316|\u65e0\u7528\u6237\u53ef\u89c1\u53d8\u5316|\u968f.{0,30}\u53d1\u5e03\u5305\u66f4\u65b0|no[ -]user[ -](?:visible|facing) changes|maintenance release'

foreach ($component in $components) {
    $componentChanges = @($changedFiles | Where-Object { $_ -match $component.PathPattern })
    $section = Get-VersionSection -Path $component.Changelog
    $bulletCount = ([regex]::Matches($section, '(?m)^-\s+')).Count

    if ($bulletCount -lt 1) {
        throw "$($component.Changelog) version $Version must contain at least one release-note bullet."
    }

    if ($componentChanges.Count -gt 0 -and
        $section -match $placeholderPattern -and
        $component.Name -notin $AllowNoUserVisibleChanges) {
        $sample = ($componentChanges | Select-Object -First 8) -join [Environment]::NewLine
        throw @"
$($component.Name) changed in $($componentChanges.Count) file(s), but its $Version changelog uses no-change placeholder wording.
Review the component diff and write concrete user-visible notes. If inspection proves the changes are non-observable, rerun with:
  -AllowNoUserVisibleChanges $($component.Name)
Changed files include:
$sample
"@
    }

    $override = $component.Name -in $AllowNoUserVisibleChanges
    Write-Host "$($component.Name): $($componentChanges.Count) changed file(s), $bulletCount note bullet(s), no-change override=$override"
}

Write-Host "Release-note accuracy check passed for Fowan $Version against $previousTag."
