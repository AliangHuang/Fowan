param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../../.."))
$releaseRoot = Join-Path $repoRoot "publish/windows/$RuntimeIdentifier/$Version"
$installer = Join-Path $releaseRoot "FowanSetup-$Version-$RuntimeIdentifier.exe"
$portable = Join-Path $releaseRoot "Fowan-$Version-portable.zip"
$manifestPath = Join-Path $releaseRoot "fowan-update.json"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "Fowan-package-$([guid]::NewGuid().ToString('N'))"

function Get-VersionSection([string]$Path) {
    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match "^##\s+\[?$escapedVersion\]?(?:\s+-\s+.*)?$") { $start = $index; break }
    }
    if ($start -lt 0) { throw "$Path does not contain a section for version $Version." }
    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match "^##\s+") { $end = $index; break }
    }
    return ($lines[$start..($end - 1)] -join [Environment]::NewLine).Trim()
}

function Normalize([string]$Text) { (($Text -replace "`r`n", "`n") -replace "`r", "`n").Trim() }

try {
    $expectedNames = @("FowanSetup-$Version-$RuntimeIdentifier.exe", "Fowan-$Version-portable.zip", "fowan-update.json", "SHA256SUMS.txt")
    $actualNames = @(Get-ChildItem -LiteralPath $releaseRoot -File -Force | Select-Object -ExpandProperty Name | Sort-Object)
    if (Compare-Object ($expectedNames | Sort-Object) $actualNames) { throw "Release directory contains unexpected or missing delivery files." }
    foreach ($path in @($installer, $portable, $manifestPath, $checksumPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required delivery is missing: $path" }
    }

    $checksums = @{}
    foreach ($line in Get-Content -LiteralPath $checksumPath -Encoding UTF8) {
        if ($line -match '^(?<hash>[a-fA-F0-9]{64}) \*(?<name>.+)$') { $checksums[$matches.name] = $matches.hash.ToLowerInvariant() }
    }
    foreach ($path in @($installer, $portable, $manifestPath)) {
        $name = Split-Path -Leaf $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($checksums[$name] -ne $hash) { throw "SHA256SUMS does not match $name." }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedUrl = "https://github.com/AliangHuang/Fowan/releases/download/v$Version/FowanSetup-$Version-$RuntimeIdentifier.exe"
    if ($manifest.version -ne $Version -or $manifest.channel -ne "stable" -or
        $manifest.installerUrl -ne $expectedUrl -or $manifest.installerSha256 -ne $installerHash) {
        throw "Update manifest does not match the packaged installer."
    }

    Expand-Archive -LiteralPath $portable -DestinationPath $temporaryRoot -Force
    $portableRoot = Join-Path $temporaryRoot "Fowan-$Version-portable"
    $requiredPortablePaths = @(
        "README.txt", "prerequisites/vc_redist.x64.exe", "app/Fowan.Windows.exe", "app/Core/fowan-core.exe",
        "app/Tools/Todo/Fowan.Todo.Windows.exe", "app/Tools/Todo/Fowan.Todo.Sticky.Windows.exe",
        "app/Tools/Diary/Fowan.Diary.Windows.exe", "app/Tools/AI/Chat/Fowan.Ai.Chat.Windows.exe",
        "app/Tools/AI/Config/Fowan.Ai.Config.Windows.exe", "app/ReleaseNotes/release-notes.txt"
    )
    foreach ($relativePath in $requiredPortablePaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $portableRoot $relativePath) -PathType Leaf)) {
            throw "Portable archive is missing: $relativePath"
        }
    }
    foreach ($component in @(@{ Id = "toolbox"; Path = "changelogs/toolbox/CHANGELOG.md" }, @{ Id = "todo"; Path = "changelogs/tools/todo/CHANGELOG.md" }, @{ Id = "diary"; Path = "changelogs/tools/diary/CHANGELOG.md" })) {
        $source = Get-VersionSection (Join-Path $repoRoot $component.Path)
        $packaged = Get-Content -LiteralPath (Join-Path $portableRoot "app/ReleaseNotes/$($component.Id).md") -Raw -Encoding UTF8
        if ((Normalize $source) -ne (Normalize $packaged)) { throw "Portable $($component.Id) release notes do not match source changelog." }
    }
    Write-Host "Packaged release validation passed for Fowan $Version."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
