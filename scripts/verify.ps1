param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
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

    & dotnet restore .\Fowan.sln --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet build .\Fowan.sln -c $configuration --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet test .\Fowan.sln -c $configuration --no-build --nologo
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

    $layeringRules = @(
        @{ Path = "apps/windows/ai/chat/ChatWindow.cs"; Pattern = '\.InvokeAsync\s*<' ; Message = "AI Chat view must use AiChatController instead of raw RPC." },
        @{ Path = "apps/windows/ai/config/ConfigWindow.cs"; Pattern = '\.InvokeAsync\s*<' ; Message = "AI Config view must use AiConfigController instead of raw RPC." },
        @{ Path = "apps/windows/diary/app/DiaryWindow.cs"; Pattern = '_(?:store|settingsStore)\.' ; Message = "Diary view must use DiaryPersistenceController." },
        @{ Path = "apps/windows/diary/app/DiaryWindow.cs"; Pattern = '\b(?:DiaryStore|DiarySettingsStore)\b' ; Message = "Diary view must not depend on concrete persistence stores." },
        @{ Path = "apps/windows/todo/app/TodoWindow.cs"; Pattern = 'private\s+readonly\s+TodoStore\b' ; Message = "Todo view must use TodoPersistenceController." },
        @{ Path = "apps/windows/todo/app/TodoWindow.cs"; Pattern = '\bTodoStore\b' ; Message = "Todo view must not depend on the concrete persistence store." },
        @{ Path = "apps/windows/todo/sticky/StickyWindow.cs"; Pattern = 'private\s+readonly\s+TodoStore\b' ; Message = "Sticky view must use TodoPersistenceController." },
        @{ Path = "apps/windows/todo/sticky/StickyWindow.cs"; Pattern = '\bTodoStore\b' ; Message = "Sticky view must not depend on the concrete persistence store." },
        @{ Path = "apps/windows/toolbox/MainWindow.cs"; Pattern = '\bSettingsStore\b' ; Message = "Toolbox view must not depend on the concrete settings store." },
        @{ Path = "apps/windows/toolbox/MainWindow.cs"; Pattern = 'Shell_NotifyIcon|NotifyIconData|TrackPopupMenu|SetWindowLongPtr' ; Message = "Toolbox tray integration must stay behind ITrayService." }
    )
    foreach ($rule in $layeringRules) {
        $path = Join-Path $repoRoot $rule.Path
        if (Select-String -LiteralPath $path -Pattern $rule.Pattern -Quiet) {
            throw "$($rule.Message) Path: $($rule.Path)"
        }
    }

    & git diff --check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $after = @(git status --porcelain=v1 --untracked-files=all)
    if (Compare-Object $before $after) {
        throw "Verification changed the source worktree or produced non-ignored files."
    }
}
finally {
    Pop-Location
}
