param(
    [string]$BackupRoot = (Join-Path $env:PUBLIC "Desktop")
)

$ErrorActionPreference = "Stop"

$excludedProfileNames = @(
    "All Users",
    "Default",
    "Default User",
    "Public"
)

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-SafeName {
    param([Parameter(Mandatory = $true)][string]$Name)
    return ($Name -replace '[<>:"/\\|?*]', "_")
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "FowanUserDataBackup_$timestamp"

try {
    $profilesRoot = Join-Path $env:SystemDrive "Users"
    if (-not (Test-Path -LiteralPath $profilesRoot -PathType Container)) {
        exit 2
    }

    $dataRoots = @()
    foreach ($profile in Get-ChildItem -LiteralPath $profilesRoot -Directory -Force) {
        if ($excludedProfileNames -contains $profile.Name) {
            continue
        }

        if (($profile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            continue
        }

        $candidate = Join-Path $profile.FullName "AppData\Local\Fowan"
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $resolvedCandidate = Resolve-FullPath $candidate
            $expectedSuffix = "\AppData\Local\Fowan"
            if ($resolvedCandidate.EndsWith($expectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $dataRoots += [pscustomobject]@{
                    UserName = $profile.Name
                    Path = $resolvedCandidate
                }
            }
        }
    }

    if ($dataRoots.Count -eq 0) {
        exit 2
    }

    $backupRootPath = Resolve-FullPath $BackupRoot
    New-Item -ItemType Directory -Force -Path $backupRootPath | Out-Null

    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }

    $payloadRoot = Join-Path $tempRoot "Fowan_UserData"
    New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null

    $manifest = @(
        "Fowan user data backup",
        "CreatedAt=$((Get-Date).ToString('o'))",
        "SourceMachine=$env:COMPUTERNAME",
        ""
    )

    foreach ($root in $dataRoots) {
        $safeUserName = Get-SafeName $root.UserName
        $destination = Join-Path $payloadRoot $safeUserName
        Copy-Item -LiteralPath $root.Path -Destination $destination -Recurse -Force
        $manifest += "$($root.UserName)=$($root.Path)"
    }

    $manifestPath = Join-Path $payloadRoot "backup-manifest.txt"
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

    $zipPath = Join-Path $backupRootPath "Fowan_UserData_Backup_$timestamp.zip"
    $suffix = 1
    while (Test-Path -LiteralPath $zipPath) {
        $zipPath = Join-Path $backupRootPath "Fowan_UserData_Backup_${timestamp}_$suffix.zip"
        $suffix++
    }

    Compress-Archive -Path (Join-Path $payloadRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Backup archive was not created: $zipPath"
    }

    foreach ($root in $dataRoots) {
        Remove-Item -LiteralPath $root.Path -Recurse -Force
    }

    Remove-Item -LiteralPath $tempRoot -Recurse -Force
    Write-Output "Backup created: $zipPath"
    exit 0
}
catch {
    try {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
    catch {
    }

    Write-Error $_
    exit 1
}
