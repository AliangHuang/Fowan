function New-IsolatedBuildDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Component
    )

    $root = Join-Path $RepositoryRoot "artifacts/staging/$Component"
    $path = Join-Path $root ([guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    return $path
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
        [Parameter(Mandatory = $true)][string]$AllowedOutputRoot
    )

    $resolvedDestination = [IO.Path]::GetFullPath($Destination)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedOutputRoot).TrimEnd('\', '/')
    $prefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedDestination.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace output outside $resolvedRoot`: $resolvedDestination"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    $backup = "$resolvedDestination.backup-$([guid]::NewGuid().ToString('N'))"
    $hadDestination = Test-Path -LiteralPath $Destination
    $installed = $false
    try {
        if ($hadDestination) {
            Move-Item -LiteralPath $Destination -Destination $backup -ErrorAction Stop
        }
        Move-Item -LiteralPath $StagingDirectory -Destination $Destination -ErrorAction Stop
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
                Move-Item -LiteralPath $backup -Destination $Destination -ErrorAction Stop
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
        if (-not $installed -and (Test-Path -LiteralPath $StagingDirectory)) {
            Remove-Item -LiteralPath $StagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Remove-IsolatedBuildDirectory {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}
