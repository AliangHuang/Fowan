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
$manifestPath = Join-Path $releaseRoot "fowan-update.json"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"

$expectedNames = @("FowanSetup-$Version-$RuntimeIdentifier.exe", "fowan-update.json", "SHA256SUMS.txt")
$actualNames = @(Get-ChildItem -LiteralPath $releaseRoot -File -Force | Select-Object -ExpandProperty Name | Sort-Object)
if (Compare-Object ($expectedNames | Sort-Object) $actualNames) { throw "Release directory contains unexpected or missing delivery files." }
foreach ($path in @($installer, $manifestPath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required delivery is missing: $path" }
}

$checksums = @{}
foreach ($line in Get-Content -LiteralPath $checksumPath -Encoding UTF8) {
    if ($line -match '^(?<hash>[a-fA-F0-9]{64}) \*(?<name>.+)$') { $checksums[$matches.name] = $matches.hash.ToLowerInvariant() }
}
foreach ($path in @($installer, $manifestPath)) {
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

Write-Host "Packaged release validation passed for Fowan $Version."
