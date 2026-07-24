param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Publish,
    [string]$CoreArtifactPath = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-output.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeIdentifier = "win-x64"
$outputRoot = Join-Path $repoRoot "build/windows/$runtimeIdentifier"
$output = Join-Path $outputRoot "app"
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$command = if ($Publish) { "publish" } else { "build" }
$developmentProperty = "-p:FowanDevelopmentRuntime=true"

function Remove-DevelopmentOutput {
    if (-not (Test-Path -LiteralPath $output)) { return }
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    $resolvedRoot = [IO.Path]::GetFullPath($outputRoot).TrimEnd('\', '/')
    if (-not $resolvedOutput.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside the development output root: $resolvedOutput"
    }
    Remove-Item -LiteralPath $output -Recurse -Force
}

if ($Clean) { Remove-DevelopmentOutput }

$core = $CoreArtifactPath
if ([string]::IsNullOrWhiteSpace($core)) {
    $coreConfiguration = if ($Configuration -eq "Release") { "release" } else { "debug" }
    $core = Join-Path (Split-Path -Parent $repoRoot) "FowanCore/out/core/windows/win-x64/$coreConfiguration/fowan-core.exe"
}
if (-not (Test-Path -LiteralPath $core -PathType Leaf)) {
    throw "A Fowan Core artifact is required for the unified application tree: $core"
}

$staging = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "windows-app"
try {
    $targets = @(
        @{ Project = "apps/windows/toolbox/Fowan.Windows.csproj"; Destination = "."; Exe = "Fowan.Windows.Dev.exe"; LegacyExe = "Fowan.Windows.exe"; Component = "toolbox" },
        @{ Project = "apps/windows/todo/app/Fowan.Todo.Windows.csproj"; Destination = "Tools/Todo"; Exe = "Fowan.Todo.Windows.Dev.exe"; LegacyExe = "Fowan.Todo.Windows.exe"; Component = "todo" },
        @{ Project = "apps/windows/todo/sticky/Fowan.Todo.Sticky.Windows.csproj"; Destination = "Tools/Todo"; Exe = "Fowan.Todo.Sticky.Windows.Dev.exe"; LegacyExe = "Fowan.Todo.Sticky.Windows.exe"; Component = "todo-sticky" },
        @{ Project = "apps/windows/diary/app/Fowan.Diary.Windows.csproj"; Destination = "Tools/Diary"; Exe = "Fowan.Diary.Windows.Dev.exe"; LegacyExe = "Fowan.Diary.Windows.exe"; Component = "diary" },
        @{ Project = "apps/windows/report/app/Fowan.Report.Windows.csproj"; Destination = "Tools/Report"; Exe = "Fowan.Report.Windows.Dev.exe"; LegacyExe = "Fowan.Report.Windows.exe"; Component = "report" },
        @{ Project = "apps/windows/ai/chat/Fowan.Ai.Chat.Windows.csproj"; Destination = "Tools/AI/Chat"; Exe = "Fowan.Ai.Chat.Windows.Dev.exe"; LegacyExe = "Fowan.Ai.Chat.Windows.exe"; Component = "ai-chat" },
        @{ Project = "apps/windows/ai/config/Fowan.Ai.Config.Windows.csproj"; Destination = "Tools/AI/Config"; Exe = "Fowan.Ai.Config.Windows.Dev.exe"; LegacyExe = "Fowan.Ai.Config.Windows.exe"; Component = "ai-config" }
    )
    foreach ($target in $targets) {
        $projectStage = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "windows-$($target.Component)"
        try {
            & $dotnet $command (Join-Path $repoRoot $target.Project) -c $Configuration `
                -o (ConvertTo-DotnetOutputDirectory $projectStage) --nologo `
                $developmentProperty
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            $builtExecutable = Join-Path $projectStage $target.Exe
            if (-not (Test-Path -LiteralPath $builtExecutable -PathType Leaf)) {
                throw "Build completed, but expected executable was not found: $builtExecutable"
            }
            Copy-BuildDirectoryContent -Source $projectStage -Destination (Join-Path $staging $target.Destination)
        }
        finally {
            Remove-IsolatedBuildDirectory -Path $projectStage
        }
    }
    $coreDestination = Join-Path $staging "Core"
    New-Item -ItemType Directory -Force -Path $coreDestination | Out-Null
    Copy-Item -LiteralPath $core -Destination (Join-Path $coreDestination "fowan-core.Dev.exe") -Force

    $runtimeMappings = @()
    foreach ($target in $targets) {
        $restartRelativePath = Join-Path $target.Destination $target.Exe
        $runtimeMappings += [pscustomobject]@{
            CurrentRelativePath = $restartRelativePath
            RestartRelativePath = $restartRelativePath
        }
        $runtimeMappings += [pscustomobject]@{
            CurrentRelativePath = Join-Path $target.Destination $target.LegacyExe
            RestartRelativePath = $restartRelativePath
        }
    }
    $runtimeMappings += [pscustomobject]@{
        CurrentRelativePath = "Core/fowan-core.Dev.exe"
        RestartRelativePath = "Core/fowan-core.Dev.exe"
    }
    $runtimeMappings += [pscustomobject]@{
        CurrentRelativePath = "Core/fowan-core.exe"
        RestartRelativePath = "Core/fowan-core.Dev.exe"
    }

    $runningProcesses = @(Get-DevelopmentRuntimeProcesses -OutputDirectory $output -ExecutableMappings $runtimeMappings)
    $restartPlan = @(Get-DevelopmentRuntimeRestartPlan -Processes $runningProcesses)
    if ($runningProcesses.Count -gt 0) {
        Write-Host "Stopping development runtime processes: $($runningProcesses.ProcessName -join ', ')"
        foreach ($process in $runningProcesses) {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        foreach ($process in $runningProcesses) {
            Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 750
    }

    $installError = $null
    try {
        Install-IsolatedBuildDirectory -StagingDirectory $staging -Destination $output -AllowedOutputRoot $outputRoot
    }
    catch {
        $installError = $_
    }

    $restartErrors = @()
    foreach ($restart in $restartPlan) {
        $executable = if ($installError) {
            $restart.PreviousPath
        } else {
            Join-Path $output $restart.RestartRelativePath
        }
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            $restartErrors += "missing restart executable: $executable"
            continue
        }
        try {
            $startParameters = @{
                FilePath = $executable
                WorkingDirectory = Split-Path -Parent $executable
            }
            if ([IO.Path]::GetFileName($executable) -eq "fowan-core.Dev.exe") {
                $startParameters.WindowStyle = "Hidden"
            }
            Start-Process @startParameters
            Write-Host "Restarted development process: $executable"
        }
        catch {
            $restartErrors += "$executable`: $($_.Exception.Message)"
        }
    }
    if ($installError) {
        if ($restartErrors.Count -gt 0) {
            Write-Warning "The previous development runtime could not be fully restarted: $($restartErrors -join '; ')"
        }
        throw $installError
    }
    if ($restartErrors.Count -gt 0) {
        throw "Development output was installed, but process restart failed: $($restartErrors -join '; ')"
    }
    Write-Host "Unified Windows $command output: $output"
    Write-Host "Toolbox executable: $(Join-Path $output 'Fowan.Windows.Dev.exe')"
}
finally {
    Remove-IsolatedBuildDirectory -Path $staging
}
