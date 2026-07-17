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
    $outputRoot = Join-Path $repository "build"
    $publishRoot = Join-Path $repository "publish/windows/win-x64"
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

    $developmentOutput = Join-Path $outputRoot "windows/win-x64/app"
    $developmentToolbox = Join-Path $developmentOutput "Fowan.Windows.Dev.exe"
    $installedToolbox = Join-Path $repository "installed/Fowan.Windows.Dev.exe"
    $getProcesses = {
        param([string]$Name)
        if ($Name -ne "Fowan.Windows.Dev") { return @() }
        return @(
            [pscustomobject]@{ Id = 101; Path = $developmentToolbox },
            [pscustomobject]@{ Id = 202; Path = $installedToolbox }
        )
    }.GetNewClosure()
    $processMappings = @(
        [pscustomobject]@{
            CurrentRelativePath = "Fowan.Windows.Dev.exe"
            RestartRelativePath = "Fowan.Windows.Dev.exe"
        }
    )
    $developmentProcesses = @(Get-DevelopmentRuntimeProcesses `
        -OutputDirectory $developmentOutput `
        -ExecutableMappings $processMappings `
        -GetProcessesByName $getProcesses)
    Assert-True ($developmentProcesses.Count -eq 1) "only a process from the development output tree may be selected"
    Assert-True ($developmentProcesses[0].Id -eq 101) "the installed process must not be selected for automatic shutdown"

    $restartProcesses = @(
        [pscustomobject]@{ RestartRelativePath = "Tools/Todo/Fowan.Todo.Windows.Dev.exe" },
        [pscustomobject]@{ RestartRelativePath = "Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe" },
        [pscustomobject]@{ RestartRelativePath = "Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe" },
        [pscustomobject]@{ RestartRelativePath = "Core/fowan-core.Dev.exe" }
    )
    $restartPaths = @((Get-DevelopmentRuntimeRestartPlan -Processes $restartProcesses).RestartRelativePath)
    Assert-True ($restartPaths -contains "Tools/Todo/Fowan.Todo.Windows.Dev.exe") "Todo must be restarted"
    Assert-True ($restartPaths -notcontains "Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe") "Todo owns sticky restart"
    Assert-True ($restartPaths -contains "Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe") "AI Chat must be restarted"
    Assert-True ($restartPaths -notcontains "Core/fowan-core.Dev.exe") "AI applications own Core restart"

    $staging = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "sample"
    Assert-True ((ConvertTo-DotnetOutputDirectory $staging).EndsWith([IO.Path]::DirectorySeparatorChar)) "dotnet output must end with a directory separator"
    Set-Content -LiteralPath (Join-Path $staging "new.txt") -Value "new"
    $destination = Join-Path $outputRoot "debug"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Set-Content -LiteralPath (Join-Path $destination "old.txt") -Value "old"
    Install-IsolatedBuildDirectory -StagingDirectory $staging -Destination $destination -AllowedOutputRoot $outputRoot
    Assert-True (Test-Path -LiteralPath (Join-Path $destination "new.txt")) "replacement output was not installed"
    Assert-True (-not (Get-ChildItem -LiteralPath $outputRoot -Directory -Filter '*.backup-*')) "backup directory was not removed"

    $missing = Join-Path $repository "build/staging/missing/value"
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

    $publishStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "publish"
    Set-Content -LiteralPath (Join-Path $publishStage "FowanSetup-1.2.3-win-x64.exe") -Value "setup"
    Set-Content -LiteralPath (Join-Path $publishStage "Fowan-1.2.3-portable.zip") -Value "portable"
    $publishDestination = Join-Path $publishRoot "1.2.3"
    Install-IsolatedBuildDirectory -StagingDirectory $publishStage -Destination $publishDestination -AllowedOutputRoot $publishRoot
    Assert-True (Test-Path -LiteralPath (Join-Path $publishDestination "FowanSetup-1.2.3-win-x64.exe")) "publish delivery was not installed"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $publishDestination "app"))) "publish delivery must not contain an application staging directory"
    Assert-True (-not (Get-ChildItem -LiteralPath $publishRoot -Directory -Filter '*.backup-*')) "publish backup directory was not removed"

    Set-Content -LiteralPath (Join-Path $publishDestination "previous.txt") -Value "previous"
    $publishRollbackStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "publish-rollback"
    Set-Content -LiteralPath (Join-Path $publishRollbackStage "replacement.txt") -Value "replacement"
    $moveCount = 0
    $failPublishMove = {
        param([string]$Source, [string]$Target)
        $script:moveCount++
        if ($script:moveCount -eq 2) { throw "injected publish install failure" }
        Move-Item -LiteralPath $Source -Destination $Target -ErrorAction Stop
    }
    $publishRollbackFailed = $false
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $publishRollbackStage -Destination $publishDestination -AllowedOutputRoot $publishRoot -MoveDirectory $failPublishMove
    }
    catch { $publishRollbackFailed = $true }
    Assert-True $publishRollbackFailed "injected publish install failure must fail"
    Assert-True (Test-Path -LiteralPath (Join-Path $publishDestination "previous.txt")) "previous publish delivery was not restored"
    Assert-True (-not (Test-Path -LiteralPath $publishRollbackStage)) "failed publish staging was not cleaned"
    Assert-True (-not (Get-ChildItem -LiteralPath $publishRoot -Directory -Filter '*.backup-*')) "restored publish backup was not removed"

    $wrongRootStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "wrong-root"
    $wrongRootFailed = $false
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $wrongRootStage -Destination (Join-Path $publishRoot "bad") -AllowedOutputRoot $outputRoot
    }
    catch { $wrongRootFailed = $true }
    finally { Remove-IsolatedBuildDirectory $wrongRootStage }
    Assert-True $wrongRootFailed "publish destination must not be installable through the build root"

    $retentionPublishRoot = Join-Path $repository "publish/windows/win-x64-retention"
    foreach ($version in @("1.0.0", "1.1.0", "1.2.0", "1.3.0", "1.4.0")) {
        New-Item -ItemType Directory -Force -Path (Join-Path $retentionPublishRoot $version) | Out-Null
    }
    $retentionStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "retention"
    $expired = @(Move-ExpiredPublishDirectories `
        -PublishRoot $retentionPublishRoot `
        -ReleaseVersion "1.5.0" `
        -RetentionRoot $retentionStage `
        -MaximumVersionCount 4)
    Assert-True ($expired.Name -join ',' -eq '1.0.0,1.1.0') "publish retention did not select the two oldest versions"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $retentionPublishRoot "1.0.0"))) "expired version remained in publish root"
    Assert-True (Test-Path -LiteralPath (Join-Path $retentionStage "1.0.0")) "expired version was not moved into retention staging"
    Restore-ExpiredPublishDirectories -PublishRoot $retentionPublishRoot -RetentionRoot $retentionStage
    Assert-True (Test-Path -LiteralPath (Join-Path $retentionPublishRoot "1.0.0")) "expired version was not restored after publish failure"
    Assert-True (-not (Get-ChildItem -LiteralPath $retentionStage -Force | Select-Object -First 1)) "retention staging was not emptied after restoration"
    Remove-IsolatedBuildDirectory $retentionStage

    $replacementRetentionStage = New-IsolatedBuildDirectory -RepositoryRoot $repository -Component "retention-replace"
    $replacementExpired = @(Move-ExpiredPublishDirectories `
        -PublishRoot $retentionPublishRoot `
        -ReleaseVersion "1.4.0" `
        -RetentionRoot $replacementRetentionStage `
        -MaximumVersionCount 4)
    Assert-True ($replacementExpired.Count -eq 1 -and $replacementExpired[0].Name -eq '1.0.0') "replacing a retained version did not preserve four-version retention"
    Restore-ExpiredPublishDirectories -PublishRoot $retentionPublishRoot -RetentionRoot $replacementRetentionStage
    Remove-IsolatedBuildDirectory $replacementRetentionStage

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
