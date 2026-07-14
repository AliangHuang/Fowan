param()

$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "build-output.ps1")
$root = Join-Path ([IO.Path]::GetTempPath()) "Fowan build output tests $([guid]::NewGuid().ToString('N'))"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

try {
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $repository = Join-Path $root "repo with spaces"
    $outputRoot = Join-Path $repository "out"
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

    $staging = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "sample"
    Assert-True ((ConvertTo-DotnetOutputDirectory $staging).EndsWith([IO.Path]::DirectorySeparatorChar)) "dotnet output must end with a directory separator"
    Set-Content -LiteralPath (Join-Path $staging "new.txt") -Value "new"
    $destination = Join-Path $outputRoot "debug"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Set-Content -LiteralPath (Join-Path $destination "old.txt") -Value "old"
    Install-IsolatedBuildDirectory -StagingDirectory $staging -Destination $destination -AllowedOutputRoot $outputRoot
    Assert-True (Test-Path -LiteralPath (Join-Path $destination "new.txt")) "replacement output was not installed"
    Assert-True (-not (Get-ChildItem -LiteralPath $outputRoot -Directory -Filter '*.backup-*')) "backup directory was not removed"

    $missing = Join-Path $repository "artifacts/staging/missing/value"
    $failed = $false
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $missing -Destination $destination -AllowedOutputRoot $outputRoot
    }
    catch { $failed = $true }
    Assert-True $failed "missing staging must fail"
    Assert-True (Test-Path -LiteralPath (Join-Path $destination "new.txt")) "existing output changed after missing staging failure"

    $outsideStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "outside"
    $outsideFailed = $false
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $outsideStage -Destination (Join-Path $repository "elsewhere") -AllowedOutputRoot $outputRoot
    }
    catch { $outsideFailed = $true }
    finally { Remove-IsolatedBuildDirectory $outsideStage }
    Assert-True $outsideFailed "destination outside output root must be rejected"

    $rollbackStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "rollback"
    Set-Content -LiteralPath (Join-Path $rollbackStage "replacement.txt") -Value "replacement"
    Set-Content -LiteralPath (Join-Path $destination "preserved.txt") -Value "preserved"
    $moveCount = 0
    $failInstallMove = {
        param([string]$Source, [string]$Target)
        $script:moveCount++
        if ($script:moveCount -eq 2) { throw "injected install failure" }
        Move-Item -LiteralPath $Source -Destination $Target -ErrorAction Stop
    }
    $rollbackFailed = $false
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $rollbackStage -Destination $destination -AllowedOutputRoot $outputRoot -MoveDirectory $failInstallMove
    }
    catch {
        $rollbackFailed = $true
        Assert-True ($_.Exception.Message -match 'previous output was restored') "successful rollback diagnostic was missing"
    }
    Assert-True $rollbackFailed "injected install failure must fail"
    Assert-True (Test-Path -LiteralPath (Join-Path $destination "preserved.txt")) "previous output was not restored"
    Assert-True (-not (Get-ChildItem -LiteralPath $outputRoot -Directory -Filter '*.backup-*')) "restored backup was not removed"

    $doubleFailureStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "double-failure"
    Set-Content -LiteralPath (Join-Path $doubleFailureStage "replacement.txt") -Value "replacement"
    $moveCount = 0
    $failInstallAndRestore = {
        param([string]$Source, [string]$Target)
        $script:moveCount++
        if ($script:moveCount -ge 2) { throw "injected move failure $script:moveCount" }
        Move-Item -LiteralPath $Source -Destination $Target -ErrorAction Stop
    }
    $doubleFailureMessage = $null
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $doubleFailureStage -Destination $destination -AllowedOutputRoot $outputRoot -MoveDirectory $failInstallAndRestore
    }
    catch { $doubleFailureMessage = $_.Exception.Message }
    Assert-True ($doubleFailureMessage -match 'Replace error:.*injected move failure 2') "replace failure diagnostic was missing"
    Assert-True ($doubleFailureMessage -match 'Restore error:.*injected move failure 3') "restore failure diagnostic was missing"
    Assert-True (-not (Test-Path -LiteralPath $doubleFailureStage)) "failed staging was not cleaned"
    Get-ChildItem -LiteralPath $outputRoot -Directory -Filter '*.backup-*' | Remove-Item -Recurse -Force

    Assert-StagingDirectoryEmpty -RepositoryRoot $repository
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}
