param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "protocol/ai/v0.1"
$temporaryRoot = Join-Path $repoRoot "artifacts/verification/protocol-$([guid]::NewGuid().ToString('N'))"

function Get-ProtocolGeneratedFiles([string]$Root) {
    $paths = @(
        (Join-Path $Root "schema/generated"),
        (Join-Path $Root "examples/generated")
    )
    $result = @{}
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Container)) {
            throw "Generated protocol directory was not found: $path"
        }
        Get-ChildItem -LiteralPath $path -Recurse -File | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
            $result[$relative] = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        }
    }
    $manifest = Join-Path $Root "examples/manifest.json"
    $result["examples/manifest.json"] = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifest).Hash.ToLowerInvariant()
    return $result
}

try {
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $temporaryRoot "v0.1") -Recurse -Force
    $copy = Join-Path $temporaryRoot "v0.1"
    & (Join-Path $PSScriptRoot "generate-ai-protocol-fixtures.ps1") -ProtocolRoot $copy
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $expected = Get-ProtocolGeneratedFiles $source
    $actual = Get-ProtocolGeneratedFiles $copy
    $allNames = @($expected.Keys + $actual.Keys | Sort-Object -Unique)
    $differences = @($allNames | Where-Object {
        -not $expected.ContainsKey($_) -or -not $actual.ContainsKey($_) -or $expected[$_] -ne $actual[$_]
    })
    if ($differences.Count -ne 0) {
        throw "Generated AI protocol content has drifted:`n$($differences -join [Environment]::NewLine)"
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    $verificationRoot = Split-Path -Parent $temporaryRoot
    if ((Test-Path -LiteralPath $verificationRoot) -and
        -not (Get-ChildItem -LiteralPath $verificationRoot -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $verificationRoot -Force -ErrorAction SilentlyContinue
    }
}
