param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "build-output.ps1")
Push-Location $repoRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Fowan verification requires a stable .NET SDK 8.0.422 or newer. dotnet was not found."
    }
    $sdkOutput = @(& dotnet --version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Fowan verification requires a stable .NET SDK 8.0.422 or newer. dotnet could not select it:`n$($sdkOutput -join [Environment]::NewLine)"
    }
    $sdk = ($sdkOutput -join [Environment]::NewLine).Trim()
    if ($sdk -notmatch '^\d+\.\d+\.\d+$' -or [version]$sdk -lt [version]"8.0.422") {
        throw "Fowan verification requires a stable .NET SDK 8.0.422 or newer. Found: $sdk"
    }
    $before = @(git status --porcelain=v1 --untracked-files=all)
    Assert-StagingDirectoryEmpty -RepositoryRoot $repoRoot
    $backupBefore = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Filter '*.backup-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/](?:artifacts|out)[\\/]' })
    if ($backupBefore) {
        throw "Backup residue was found before verification:`n$($backupBefore.FullName -join [Environment]::NewLine)"
    }

    & (Join-Path $PSScriptRoot "tests/build-output.tests.ps1")
    & (Join-Path $PSScriptRoot "verify-ai-protocol-generation.ps1")
    & (Join-Path $PSScriptRoot "verify-development-policy.ps1")
    & (Join-Path $PSScriptRoot "tests/development-policy-fixtures.tests.ps1")
    & (Join-Path $PSScriptRoot "verify-architecture.ps1")

    & dotnet restore .\Fowan.sln --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet restore .\tests\Fowan.Architecture.Tests\Fowan.Architecture.Tests.csproj --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet build .\Fowan.sln -c $configuration --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet test .\Fowan.sln -c $configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & dotnet test .\tests\Fowan.Architecture.Tests\Fowan.Architecture.Tests.csproj -c $configuration --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    Get-ChildItem -Recurse -Filter *.json | Where-Object {
        $_.FullName -notmatch '[\\/](?:\.git|artifacts|out)[\\/]'
    } | ForEach-Object {
        Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName | ConvertFrom-Json | Out-Null
    }

    $linkPattern = [regex]'\[[^\]]+\]\((?<target>[^)]+)\)'
    Get-ChildItem -Recurse -Filter *.md | Where-Object {
        $_.FullName -notmatch '[\\/](artifacts|out)[\\/]'
    } | ForEach-Object {
        $document = $_
        foreach ($match in $linkPattern.Matches((Get-Content -Raw -Encoding UTF8 -LiteralPath $document.FullName))) {
            $target = $match.Groups['target'].Value.Trim().Trim('<', '>')
            if ($target -match '^(?:https?://|mailto:|#)') { continue }
            $path = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($path)) { continue }
            $resolved = Join-Path $document.DirectoryName ([Uri]::UnescapeDataString($path))
            if (-not (Test-Path -LiteralPath $resolved)) {
                throw "Broken documentation link in $($document.FullName): $target"
            }
        }
    }

    $trackedArtifacts = git ls-files | Select-String -Pattern '(^|/)(bin|obj|out|artifacts|target)/|\.(exe|dll|pdb|msi|nupkg|log)$'
    if ($trackedArtifacts) {
        throw "Tracked build artifacts were found:`n$($trackedArtifacts -join [Environment]::NewLine)"
    }

    $oldPathPattern = 'apps/(windows-(?:todo|diary|ai)(?:-[^/]+)?|windows-todo-shared|windows-diary-shared)(?:/|$)'
    $textExtensions = @('.cs', '.csproj', '.iss', '.json', '.md', '.props', '.ps1', '.sln', '.targets', '.toml', '.xaml', '.yaml', '.yml')
    $currentSources = Get-ChildItem -LiteralPath $repoRoot -Recurse -File | Where-Object {
        $textExtensions -contains $_.Extension -and
        $_.FullName -notmatch '[\\/](?:\.git|artifacts|out)[\\/]' -and
        $_.FullName -notmatch '[\\/]docs[\\/]history[\\/]'
    }
    foreach ($source in $currentSources) {
        $matches = Select-String -LiteralPath $source.FullName -Pattern $oldPathPattern
        if ($matches) {
            throw "Current source contains retired project paths: $($source.FullName)"
        }
    }

    & git diff --check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Assert-StagingDirectoryEmpty -RepositoryRoot $repoRoot
    $backupAfter = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Filter '*.backup-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/](?:artifacts|out)[\\/]' })
    if ($backupAfter) {
        throw "Backup residue was found after verification:`n$($backupAfter.FullName -join [Environment]::NewLine)"
    }

    $after = @(git status --porcelain=v1 --untracked-files=all)
    if (Compare-Object $before $after) {
        throw "Verification changed the source worktree or produced non-ignored files."
    }
}
finally {
    Pop-Location
}
