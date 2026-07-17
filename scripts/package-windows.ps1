param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$ReleaseRepository = "AliangHuang/Fowan",
    [Parameter(Mandatory = $true)]
    [string]$CoreArtifactPath,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-output.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRuntimeRoot = Join-Path $repoRoot "publish/windows/$RuntimeIdentifier"
$publishVersionRoot = Join-Path $publishRuntimeRoot $Version
$installerRoot = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "package-delivery"
$retentionRoot = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component "package-retention"
$retentionPending = $false
$appStage = Join-Path $installerRoot "app"
$prereqRoot = Join-Path $installerRoot "prerequisites"
$vcRedistPath = Join-Path $prereqRoot "vc_redist.x64.exe"
$windowsProject = Join-Path $repoRoot "apps/windows/toolbox/Fowan.Windows.csproj"
$todoProject = Join-Path $repoRoot "apps/windows/todo/app/Fowan.Todo.Windows.csproj"
$stickyProject = Join-Path $repoRoot "apps/windows/todo/sticky/Fowan.Todo.Sticky.Windows.csproj"
$diaryProject = Join-Path $repoRoot "apps/windows/diary/app/Fowan.Diary.Windows.csproj"
$aiChatProject = Join-Path $repoRoot "apps/windows/ai/chat/Fowan.Ai.Chat.Windows.csproj"
$aiConfigProject = Join-Path $repoRoot "apps/windows/ai/config/Fowan.Ai.Config.Windows.csproj"
$issPath = Join-Path $repoRoot "installer/windows/Fowan.iss"
$changelogRoot = Join-Path $repoRoot "changelogs"
$localDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$vcRedistUrl = "https://aka.ms/vc14/vc_redist.x64.exe"
$assemblyVersion = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }

function Assert-PathInside {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $expectedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected root. Path: $resolvedPath Root: $resolvedRoot"
    }
}

function Publish-FowanProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $dotnetOutput = ConvertTo-DotnetOutputDirectory -Path $Output
    & $dotnet publish $Project `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:FowanVersion=$Version `
        -p:FowanAssemblyVersion=$assemblyVersion `
        -p:PublishSingleFile=false `
        -o $dotnetOutput `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Get-ChangelogSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Changelog file was not found: $Path"
    }

    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    $escapedVersion = [regex]::Escape($Version)
    $headerPattern = "^##\s+\[?$escapedVersion\]?(?:\s+-\s+.*)?$"
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match $headerPattern) {
            $start = $index
            break
        }
    }
    if ($start -lt 0) {
        throw "Changelog file does not contain a section for version $Version`: $Path"
    }

    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match "^##\s+") {
            $end = $index
            break
        }
    }
    return ($lines[$start..($end - 1)] -join [Environment]::NewLine).Trim()
}

function Write-ReleaseNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    $releaseNotesRoot = Join-Path $OutputRoot "ReleaseNotes"
    if (Test-Path -LiteralPath $releaseNotesRoot) {
        Assert-PathInside -Path $releaseNotesRoot -Root $OutputRoot
        Remove-Item -LiteralPath $releaseNotesRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $releaseNotesRoot | Out-Null

    $componentChangelogs = @(
        @{
            Id = "toolbox"
            Title = "Fowan Toolbox"
            Path = Join-Path $changelogRoot "toolbox/CHANGELOG.md"
        },
        @{
            Id = "todo"
            Title = "Todo"
            Path = Join-Path $changelogRoot "tools/todo/CHANGELOG.md"
        },
        @{
            Id = "diary"
            Title = "Diary"
            Path = Join-Path $changelogRoot "tools/diary/CHANGELOG.md"
        }
    )

    $combined = New-Object System.Collections.Generic.List[string]
    $combined.Add("Fowan $Version Release Notes")
    $combined.Add("")

    foreach ($component in $componentChangelogs) {
        $section = Get-ChangelogSection -Path $component.Path -Version $Version
        $componentFile = Join-Path $releaseNotesRoot "$($component.Id).md"
        [System.IO.File]::WriteAllText(
            $componentFile,
            $section + [System.Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))

        $combined.Add("[$($component.Title)]")
        $combined.Add($section)
        $combined.Add("")
    }

    $combinedPath = Join-Path $releaseNotesRoot "release-notes.txt"
    [System.IO.File]::WriteAllText(
        $combinedPath,
        ($combined -join [System.Environment]::NewLine) + [System.Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    return $combinedPath
}

function Write-UpdateManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SetupExe,
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRepository,
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    $setupFileName = Split-Path -Leaf $SetupExe
    $releaseTag = "v$Version"
    $releaseBaseUrl = "https://github.com/$ReleaseRepository/releases/download/$releaseTag"
    $setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $SetupExe).Hash.ToLowerInvariant()
    $manifestPath = Join-Path $OutputRoot "fowan-update.json"
    $manifest = [ordered]@{
        version = $Version
        channel = "stable"
        installerUrl = "$releaseBaseUrl/$setupFileName"
        installerSha256 = $setupHash
        releaseNotesUrl = "https://github.com/$ReleaseRepository/releases/tag/$releaseTag"
        notes = "Fowan $Version release."
    }

    $json = $manifest | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $json + [System.Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    return $manifestPath
}

function Resolve-Iscc {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6/ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6/ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    return $null
}

function Save-Download {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    try {
        Invoke-WebRequest -Uri $Url -OutFile $Output -UseBasicParsing
        return
    }
    catch {
        $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
        if (-not $curl) {
            throw
        }

        & $curl.Source -L --retry 5 --retry-delay 2 --fail --output $Output $Url
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

function Ensure-VcRedist {
    New-Item -ItemType Directory -Force -Path $prereqRoot | Out-Null

    if (-not (Test-Path -LiteralPath $vcRedistPath -PathType Leaf)) {
        Write-Host "Downloading Microsoft Visual C++ Redistributable x64..."
        Save-Download -Url $vcRedistUrl -Output $vcRedistPath
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $vcRedistPath
    if ($signature.Status -ne "Valid" -or
        $signature.SignerCertificate.Subject -notlike "*Microsoft Corporation*") {
        throw "Visual C++ Redistributable signature validation failed: $vcRedistPath"
    }
}

function Write-ChecksumManifest {
    param([Parameter(Mandatory = $true)][string]$OutputRoot)

    $files = @(
        "FowanSetup-$Version-$RuntimeIdentifier.exe",
        "Fowan-$Version-portable.zip",
        "fowan-update.json"
    )
    $lines = foreach ($file in $files) {
        $path = Join-Path $OutputRoot $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Release delivery is missing required file: $path"
        }
        "{0} *{1}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant(), $file
    }
    $path = Join-Path $OutputRoot "SHA256SUMS.txt"
    [IO.File]::WriteAllLines($path, $lines, [Text.UTF8Encoding]::new($false))
    return $path
}

try {
$deliveryStage = $installerRoot
Ensure-VcRedist

if (-not (Test-Path -LiteralPath $CoreArtifactPath -PathType Leaf)) {
    throw "Fowan Core artifact was not found: $CoreArtifactPath"
}

$todoStage = Join-Path $appStage "Tools/Todo"
New-Item -ItemType Directory -Force -Path $todoStage | Out-Null
$diaryStage = Join-Path $appStage "Tools/Diary"
New-Item -ItemType Directory -Force -Path $diaryStage | Out-Null
$aiChatStage = Join-Path $appStage "Tools/AI/Chat"
New-Item -ItemType Directory -Force -Path $aiChatStage | Out-Null
$aiConfigStage = Join-Path $appStage "Tools/AI/Config"
New-Item -ItemType Directory -Force -Path $aiConfigStage | Out-Null

$publishTargets = @(
    @{ Project = $windowsProject; Destination = $appStage; Component = "publish-toolbox" },
    @{ Project = $todoProject; Destination = $todoStage; Component = "publish-todo" },
    @{ Project = $stickyProject; Destination = $todoStage; Component = "publish-sticky" },
    @{ Project = $diaryProject; Destination = $diaryStage; Component = "publish-diary" },
    @{ Project = $aiChatProject; Destination = $aiChatStage; Component = "publish-ai-chat" },
    @{ Project = $aiConfigProject; Destination = $aiConfigStage; Component = "publish-ai-config" }
)
foreach ($target in $publishTargets) {
    $projectStage = New-IsolatedBuildDirectory -RepositoryRoot $repoRoot -Component $target.Component
    try {
        Publish-FowanProject -Project $target.Project -Output $projectStage
        Copy-BuildDirectoryContent -Source $projectStage -Destination $target.Destination
    }
    finally {
        Remove-IsolatedBuildDirectory -Path $projectStage
    }
}
$coreStage = Join-Path $appStage "Core"
New-Item -ItemType Directory -Force -Path $coreStage | Out-Null
Copy-Item -LiteralPath $CoreArtifactPath -Destination (Join-Path $coreStage "fowan-core.exe") -Force

$requiredExecutables = @(
    (Join-Path $appStage "Fowan.Windows.exe"),
    (Join-Path $todoStage "Fowan.Todo.Windows.exe"),
    (Join-Path $todoStage "Fowan.Todo.Sticky.Windows.exe"),
    (Join-Path $diaryStage "Fowan.Diary.Windows.exe"),
    (Join-Path $aiChatStage "Fowan.Ai.Chat.Windows.exe"),
    (Join-Path $aiConfigStage "Fowan.Ai.Config.Windows.exe"),
    (Join-Path $coreStage "fowan-core.exe")
)

foreach ($exe in $requiredExecutables) {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Package staging is missing required executable: $exe"
    }
}

$releaseNotesPath = Write-ReleaseNotes -OutputRoot $appStage

$fileCount = (Get-ChildItem -LiteralPath $appStage -Recurse -File | Measure-Object).Count
$sizeBytes = (Get-ChildItem -LiteralPath $appStage -Recurse -File | Measure-Object Length -Sum).Sum
$sizeMb = [Math]::Round($sizeBytes / 1MB, 1)

Write-Host "Fowan Windows app staging: $appStage"
Write-Host "Release notes: $releaseNotesPath"
Write-Host "Files: $fileCount"
Write-Host "Size: $sizeMb MB"

if ($SkipInstaller) {
    Write-Host "Package preflight completed; -SkipInstaller does not create an incomplete publish directory."
    exit 0
}

if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw "Inno Setup script was not found: $issPath"
}

$iscc = Resolve-Iscc
if (-not $iscc) {
    throw "Inno Setup compiler ISCC.exe was not found. A publish directory is created only when both installer and portable zip succeed."
}

& $iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$appStage" `
    "/DReleaseNotesPath=$releaseNotesPath" `
    "/DVcRedistPath=$vcRedistPath" `
    "/DOutputDir=$installerRoot" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$setupExe = Join-Path $installerRoot "FowanSetup-$Version-$RuntimeIdentifier.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer compiler completed, but expected setup executable was not found: $setupExe"
}

$updateManifestPath = Write-UpdateManifest `
    -SetupExe $setupExe `
    -Version $Version `
    -ReleaseRepository $ReleaseRepository `
    -OutputRoot $installerRoot

$portableRoot = Join-Path $installerRoot "Fowan-$Version-portable"
Copy-BuildDirectoryContent -Source $appStage -Destination (Join-Path $portableRoot "app")
Copy-BuildDirectoryContent -Source $prereqRoot -Destination (Join-Path $portableRoot "prerequisites")
[IO.File]::WriteAllText(
    (Join-Path $portableRoot "README.txt"),
    "解压后运行 app\\Fowan.Windows.exe。若系统提示缺少 VC++ 运行库，请运行 prerequisites\\vc_redist.x64.exe。`r`n",
    [Text.UTF8Encoding]::new($false))
$portableZip = Join-Path $installerRoot "Fowan-$Version-portable.zip"
Compress-Archive -Path $portableRoot -DestinationPath $portableZip -CompressionLevel Optimal -Force
if (-not (Test-Path -LiteralPath $portableZip -PathType Leaf)) {
    throw "Portable archive was not created: $portableZip"
}

Remove-Item -LiteralPath $appStage -Recurse -Force
Remove-Item -LiteralPath $prereqRoot -Recurse -Force
Remove-Item -LiteralPath $portableRoot -Recurse -Force
$checksumPath = Write-ChecksumManifest -OutputRoot $installerRoot
$retentionPending = $true
$expiredVersions = @(Move-ExpiredPublishDirectories `
    -PublishRoot $publishRuntimeRoot `
    -ReleaseVersion $Version `
    -RetentionRoot $retentionRoot `
    -MaximumVersionCount 4)
if ($expiredVersions.Count -gt 0) {
    Write-Host "Publish retention removed before release: $($expiredVersions.Name -join ', ')"
}
Install-IsolatedBuildDirectory -StagingDirectory $deliveryStage -Destination $publishVersionRoot -AllowedOutputRoot $publishRuntimeRoot
$retentionPending = $false
Remove-IsolatedBuildDirectory -Path $retentionRoot

Write-Host "Fowan Windows installer: $(Join-Path $publishVersionRoot (Split-Path -Leaf $setupExe))"
Write-Host "Portable archive: $(Join-Path $publishVersionRoot (Split-Path -Leaf $portableZip))"
Write-Host "Update manifest: $(Join-Path $publishVersionRoot (Split-Path -Leaf $updateManifestPath))"
Write-Host "Checksums: $(Join-Path $publishVersionRoot (Split-Path -Leaf $checksumPath))"
}
catch {
    $publishError = $_
    if ($retentionPending) {
        try {
            Restore-ExpiredPublishDirectories -PublishRoot $publishRuntimeRoot -RetentionRoot $retentionRoot
            $retentionPending = $false
        }
        catch {
            throw "Publishing failed and retained versions could not be restored. Publish error: $($publishError.Exception.Message) Restore error: $($_.Exception.Message)"
        }
    }
    throw $publishError
}
finally {
    Remove-IsolatedBuildDirectory -Path $retentionRoot
    Remove-IsolatedBuildDirectory -Path $installerRoot
}
