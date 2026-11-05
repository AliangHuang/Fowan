function New-IsolatedBuildDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Component
    )

    if ($Component.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $Component.Contains([IO.Path]::DirectorySeparatorChar) -or
        $Component.Contains([IO.Path]::AltDirectorySeparatorChar)) {
        throw "Build component must be a single safe directory name: $Component"
    }

    $root = Join-Path ([IO.Path]::GetFullPath($RepositoryRoot)) "build/staging/$Component"
    $path = Join-Path $root ([guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    return $path
}

function ConvertTo-DotnetOutputDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    return $resolved + [IO.Path]::DirectorySeparatorChar
}

function Get-DevelopmentRuntimeProcesses {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][array]$ExecutableMappings,
        [scriptblock]$GetProcessesByName = {
            param([string]$Name)
            @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
        }
    )

    $resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
    $matches = @()
    foreach ($mapping in $ExecutableMappings) {
        $expectedPath = [IO.Path]::GetFullPath((Join-Path $resolvedOutput $mapping.CurrentRelativePath))
        $processName = [IO.Path]::GetFileNameWithoutExtension($expectedPath)
        foreach ($process in @(& $GetProcessesByName $processName)) {
            $processPath = $null
            try { $processPath = $process.Path } catch { }
            if ([string]::IsNullOrWhiteSpace($processPath)) {
                try { $processPath = $process.MainModule.FileName } catch { }
            }
            if ([string]::IsNullOrWhiteSpace($processPath) -or
                -not [IO.Path]::GetFullPath($processPath).Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $matches += [pscustomobject]@{
                Id = [int]$process.Id
                ProcessName = $processName
                CurrentPath = $expectedPath
                RestartRelativePath = [string]$mapping.RestartRelativePath
            }
        }
    }
    return @($matches | Sort-Object Id -Unique)
}

function Get-DevelopmentRuntimeRestartPlan {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][array]$Processes)

    $plan = @($Processes |
        Where-Object { $_.RestartRelativePath } |
        Group-Object RestartRelativePath |
        ForEach-Object {
            $relativePath = $_.Name.Replace('\', '/')
            while ($relativePath.StartsWith("./", [StringComparison]::Ordinal)) {
                $relativePath = $relativePath.Substring(2)
            }
            [pscustomobject]@{
                RestartRelativePath = $relativePath
                PreviousPath = $_.Group[0].CurrentPath
            }
        })
    $paths = @($plan.RestartRelativePath)
    $todoMain = "Tools/Todo/Fowan.Todo.Windows.Dev.exe"
    $todoSticky = "Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe"
    if ($paths -contains $todoMain) {
        $plan = @($plan | Where-Object { $_.RestartRelativePath -ne $todoSticky })
    }
    $aiChat = "Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe"
    $aiConfig = "Tools/AI/Config/Fowan.Ai.Config.Windows.Dev.exe"
    $core = "Core/fowan-core.Dev.exe"
    if ($paths -contains $aiChat -or $paths -contains $aiConfig) {
        $plan = @($plan | Where-Object { $_.RestartRelativePath -ne $core })
    }
    return @($plan | Sort-Object RestartRelativePath)
}

function Copy-BuildDirectoryContent {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Install-IsolatedBuildDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$AllowedOutputRoot,
        [scriptblock]$MoveDirectory = {
            param([string]$Source, [string]$Target)
            Move-Item -LiteralPath $Source -Destination $Target -ErrorAction Stop
        }
    )

    $resolvedDestination = [IO.Path]::GetFullPath($Destination)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedOutputRoot).TrimEnd('\', '/')
    $prefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedDestination.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace output outside $resolvedRoot`: $resolvedDestination"
    }

    $resolvedStaging = [IO.Path]::GetFullPath($StagingDirectory)
    if (-not (Test-Path -LiteralPath $resolvedStaging -PathType Container)) {
        throw "The isolated staging directory does not exist: $resolvedStaging"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    $backup = "$resolvedDestination.backup-$([guid]::NewGuid().ToString('N'))"
    $hadDestination = Test-Path -LiteralPath $Destination
    $installed = $false
    try {
        if ($hadDestination) {
            & $MoveDirectory $resolvedDestination $backup
        }
        & $MoveDirectory $resolvedStaging $resolvedDestination
        $installed = $true
        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop
        }
    }
    catch {
        $replaceError = $_
        try {
            if ($hadDestination -and (Test-Path -LiteralPath $backup)) {
                if (Test-Path -LiteralPath $Destination) {
                    Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction Stop
                }
                & $MoveDirectory $backup $resolvedDestination
            }
            elseif (-not $hadDestination -and (Test-Path -LiteralPath $Destination)) {
                Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction Stop
            }
        }
        catch {
            throw "Could not replace or restore runnable output '$Destination'. Stop the application if files are locked. Replace error: $($replaceError.Exception.Message) Restore error: $($_.Exception.Message)"
        }
        throw "Could not replace runnable output '$Destination'. The previous output was restored. Stop the application if files are locked, then retry. $($replaceError.Exception.Message)"
    }
    finally {
        Remove-IsolatedBuildDirectory -Path $StagingDirectory
    }
}

function Remove-IsolatedBuildDirectory {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($Path) {
        $componentRoot = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
        if ((Test-Path -LiteralPath $componentRoot -PathType Container) -and
            -not (Get-ChildItem -LiteralPath $componentRoot -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $componentRoot -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-StagingDirectoryEmpty {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $stagingRoot = Join-Path $RepositoryRoot "build/staging"
    if ((Test-Path -LiteralPath $stagingRoot) -and
        (Get-ChildItem -LiteralPath $stagingRoot -Force -Recurse | Select-Object -First 1)) {
        throw "Build staging is not empty: $stagingRoot"
    }
}

function Get-VersionedPublishDirectories {
    param([Parameter(Mandatory = $true)][string]$PublishRoot)

    if (-not (Test-Path -LiteralPath $PublishRoot)) { return @() }

    $directories = @(Get-ChildItem -LiteralPath $PublishRoot -Directory -Force)
    $invalid = @($directories | Where-Object { $_.Name -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' })
    if ($invalid) {
        throw "Publish root contains a non-version directory: $($invalid.FullName -join [Environment]::NewLine)"
    }

    return @($directories | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Path = $_.FullName
            Version = [version]$_.Name
        }
    })
}

function Move-ExpiredPublishDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$PublishRoot,
        [Parameter(Mandatory = $true)][string]$ReleaseVersion,
        [Parameter(Mandatory = $true)][string]$RetentionRoot,
        [ValidateRange(1, 100)][int]$MaximumVersionCount = 4
    )

    if (-not (Test-Path -LiteralPath $RetentionRoot -PathType Container)) {
        throw "Publish retention staging directory does not exist: $RetentionRoot"
    }

    $existing = @(Get-VersionedPublishDirectories -PublishRoot $PublishRoot)
    $otherVersions = @($existing | Where-Object { $_.Name -ne $ReleaseVersion } | Sort-Object Version, Name)
    $expiredCount = [Math]::Max(0, $otherVersions.Count + 1 - $MaximumVersionCount)
    $expired = @($otherVersions | Select-Object -First $expiredCount)

    foreach ($version in $expired) {
        $retainedPath = Join-Path $RetentionRoot $version.Name
        if (Test-Path -LiteralPath $retainedPath) {
            throw "Publish retention staging already contains version $($version.Name): $retainedPath"
        }
        Move-Item -LiteralPath $version.Path -Destination $retainedPath -ErrorAction Stop
    }

    return $expired
}

function Restore-ExpiredPublishDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$PublishRoot,
        [Parameter(Mandatory = $true)][string]$RetentionRoot
    )

    if (-not (Test-Path -LiteralPath $RetentionRoot -PathType Container)) { return }
    $expired = @(Get-ChildItem -LiteralPath $RetentionRoot -Directory -Force)
    if ($expired.Count -eq 0) { return }

    $invalid = @($expired | Where-Object { $_.Name -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' })
    if ($invalid) {
        throw "Publish retention staging contains a non-version directory: $($invalid.FullName -join [Environment]::NewLine)"
    }

    New-Item -ItemType Directory -Force -Path $PublishRoot | Out-Null
    foreach ($version in $expired) {
        $destination = Join-Path $PublishRoot $version.Name
        if (Test-Path -LiteralPath $destination) {
            throw "Cannot restore retained publish version because its destination exists: $destination"
        }
        Move-Item -LiteralPath $version.FullName -Destination $destination -ErrorAction Stop
    }
}
