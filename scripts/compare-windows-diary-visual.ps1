param(
    [string]$ConceptPath,
    [string]$ActualPath,
    [string]$OutputRoot,
    [switch]$ForceInstall
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ConceptPath)) {
    $ConceptPath = Join-Path $repoRoot "assets/design/windows/fowan-diary-windows-main-concept.png"
}

if ([string]::IsNullOrWhiteSpace($ActualPath)) {
    $latest = Get-ChildItem -Path (Join-Path $repoRoot "out/screenshots") `
        -Filter "fowan-diary-visual-parity-v*.png" `
        -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No Diary visual parity screenshot found under out/screenshots. Pass -ActualPath explicitly."
    }

    $ActualPath = $latest.FullName
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "out/screenshots/visual-qa"
}

$helper = Join-Path $PSScriptRoot "visual-qa/compare_diary_visual.py"
if (-not (Test-Path -LiteralPath $helper)) {
    throw "Visual QA helper not found: $helper"
}

foreach ($path in @($ConceptPath, $ActualPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Image not found: $path"
    }
}

$venvRoot = Join-Path $repoRoot "out/visual-qa/.venv"
$pythonExe = Join-Path $venvRoot "Scripts/python.exe"
$basePython = "python"

if (-not (Test-Path -LiteralPath $pythonExe)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $venvRoot) | Out-Null
    & $basePython -m venv $venvRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create visual QA virtual environment."
    }
}

$needsInstall = $ForceInstall
if (-not $needsInstall) {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $pythonExe -c "import PIL" *> $null
    $importExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    $needsInstall = $importExitCode -ne 0
}

if ($needsInstall) {
    $previousPipVersionCheck = $env:PIP_DISABLE_PIP_VERSION_CHECK
    $env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
    & $pythonExe -m pip install --disable-pip-version-check Pillow
    $env:PIP_DISABLE_PIP_VERSION_CHECK = $previousPipVersionCheck
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install Pillow into $venvRoot."
    }
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

& $pythonExe $helper `
    --concept $ConceptPath `
    --actual $ActualPath `
    --output-root $OutputRoot

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
