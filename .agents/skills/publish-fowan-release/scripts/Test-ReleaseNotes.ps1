param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [ValidateSet("Toolbox", "Todo", "Diary")]
    [string[]]$AllowNoUserVisibleChanges = @()
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../../.."))
$currentTag = "v$Version"
$previousTag = @(& git -C $repoRoot tag --list "v*" --sort=-v:refname) |
    Where-Object { $_ -and $_ -ne $currentTag } |
    Select-Object -First 1
if (-not $previousTag) { throw "Unable to resolve a release tag before $currentTag." }

function Get-VersionSection([string]$Path) {
    $fullPath = Join-Path $repoRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Changelog was not found: $Path" }
    $lines = Get-Content -LiteralPath $fullPath -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $start = @($lines | ForEach-Object -Begin { $index = 0 } -Process {
        $match = $_ -match "^##\s+\[?$escapedVersion\]?(?:\s+-\s+.*)?$"
        $result = if ($match) { $index } else { $null }
        $index++
        $result
    } | Select-Object -First 1)[0]
    if ($null -eq $start) { throw "$Path does not contain a section for version $Version." }
    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match "^##\s+") { $end = $index; break }
    }
    return ($lines[$start..($end - 1)] -join [Environment]::NewLine).Trim()
}

$changedFiles = @(& git -c core.autocrlf=false -C $repoRoot diff --name-only $previousTag --)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$components = @(
    @{ Name = "Toolbox"; Changelog = "changelogs/toolbox/CHANGELOG.md"; Pattern = '^(apps/windows/(?:toolbox|ai)/|installer/windows/|scripts/(?:package-windows|build-windows)\.ps1|docs/windows_installer_spec\.md)' },
    @{ Name = "Todo"; Changelog = "changelogs/tools/todo/CHANGELOG.md"; Pattern = '^(apps/windows/todo/|tests/Fowan\.Todo\.|docs/windows_todo_)' },
    @{ Name = "Diary"; Changelog = "changelogs/tools/diary/CHANGELOG.md"; Pattern = '^(apps/windows/diary/|tests/Fowan\.Diary\.|docs/windows_diary_)' }
)
$placeholder = '(?i)no[ -]user[ -](?:visible|facing) changes|maintenance release|没有用户可见变化|无用户可见变化|随.{0,30}发布包更新'
foreach ($component in $components) {
    $section = Get-VersionSection $component.Changelog
    $changes = @($changedFiles | Where-Object { $_ -match $component.Pattern })
    if (([regex]::Matches($section, '(?m)^-\s+')).Count -lt 1) {
        throw "$($component.Changelog) version $Version must contain at least one release-note bullet."
    }
    if ($changes.Count -gt 0 -and $section -match $placeholder -and $component.Name -notin $AllowNoUserVisibleChanges) {
        throw "$($component.Name) changed, but its $Version changelog uses no-change placeholder wording."
    }
    Write-Host "$($component.Name): $($changes.Count) changed file(s)."
}
Write-Host "Release-note accuracy check passed for Fowan $Version against $previousTag."
