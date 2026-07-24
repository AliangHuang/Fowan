param(
    [string]$ConceptPath,
    [string]$ActualPath,
    [string]$OutputRoot,
    [int]$SidebarX = 350,
    [switch]$ForceInstall
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$ConceptPath = if ([string]::IsNullOrWhiteSpace($ConceptPath)) { Join-Path $repoRoot "assets/design/windows/fowan-ai-chat-app-concept.png" } else { $ConceptPath }
$ActualPath = if ([string]::IsNullOrWhiteSpace($ActualPath)) { Join-Path $repoRoot "build/test/screenshots/fowan-ai-chat.png" } else { $ActualPath }
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) { Join-Path $repoRoot "build/test/visual-qa/ai" } else { $OutputRoot }
$helper = Join-Path $PSScriptRoot "visual-qa/compare-ai-concept.py"

foreach ($path in @($ConceptPath, $ActualPath, $helper)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Visual QA input not found: $path" }
}

$venvRoot = Join-Path $repoRoot "build/test/visual-qa/.venv"
$pythonExe = Join-Path $venvRoot "Scripts/python.exe"
if (-not (Test-Path -LiteralPath $pythonExe)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $venvRoot) | Out-Null
    & python -m venv $venvRoot
    if ($LASTEXITCODE -ne 0) { throw "Failed to create visual QA virtual environment." }
}

$needsInstall = $ForceInstall
if (-not $needsInstall) {
    & $pythonExe -c "import PIL" *> $null
    $needsInstall = $LASTEXITCODE -ne 0
}
if ($needsInstall) {
    & $pythonExe -m pip install --disable-pip-version-check Pillow
    if ($LASTEXITCODE -ne 0) { throw "Failed to install Pillow into $venvRoot." }
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
& $pythonExe $helper --concept $ConceptPath --actual $ActualPath --output-root $OutputRoot --sidebar-x $SidebarX
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "AI visual QA: $OutputRoot"
