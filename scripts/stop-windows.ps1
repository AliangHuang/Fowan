param(
    [ValidateSet("Toolbox", "ToolCenter", "Todo", "Diary", "Report", "Pomodoro", "Ai", "All")]
    [string]$Component = "All"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot "build/windows/win-x64/app"
$relativePaths = switch ($Component) {
    "Toolbox" { @("Fowan.Windows.Dev.exe", "Fowan.Windows.exe") }
    "ToolCenter" { @("Tools/ToolCenter/Fowan.ToolCenter.Windows.Dev.exe", "Tools/ToolCenter/Fowan.ToolCenter.Windows.exe") }
    "Todo" {
        @(
            "Tools/Todo/Fowan.Todo.Windows.Dev.exe",
            "Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe",
            "Tools/Todo/Fowan.Todo.Windows.exe",
            "Tools/Todo/Fowan.Todo.Sticky.Windows.exe"
        )
    }
    "Diary" { @("Tools/Diary/Fowan.Diary.Windows.Dev.exe", "Tools/Diary/Fowan.Diary.Windows.exe") }
    "Report" { @("Tools/Report/Fowan.Report.Windows.Dev.exe", "Tools/Report/Fowan.Report.Windows.exe") }
    "Pomodoro" { @("Tools/Pomodoro/Fowan.Pomodoro.Windows.Dev.exe", "Tools/Pomodoro/Fowan.Pomodoro.Windows.exe") }
    "Ai" {
        @(
            "Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe",
            "Tools/AI/Config/Fowan.Ai.Config.Windows.Dev.exe",
            "Core/fowan-core.Dev.exe",
            "Tools/AI/Chat/Fowan.Ai.Chat.Windows.exe",
            "Tools/AI/Config/Fowan.Ai.Config.Windows.exe",
            "Core/fowan-core.exe"
        )
    }
    default {
        @(
            "Fowan.Windows.Dev.exe",
            "Tools/ToolCenter/Fowan.ToolCenter.Windows.Dev.exe",
            "Tools/Todo/Fowan.Todo.Windows.Dev.exe",
            "Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe",
            "Tools/Diary/Fowan.Diary.Windows.Dev.exe",
            "Tools/Report/Fowan.Report.Windows.Dev.exe",
            "Tools/Pomodoro/Fowan.Pomodoro.Windows.Dev.exe",
            "Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe",
            "Tools/AI/Config/Fowan.Ai.Config.Windows.Dev.exe",
            "Core/fowan-core.Dev.exe",
            "Fowan.Windows.exe",
            "Tools/ToolCenter/Fowan.ToolCenter.Windows.exe",
            "Tools/Todo/Fowan.Todo.Windows.exe",
            "Tools/Todo/Fowan.Todo.Sticky.Windows.exe",
            "Tools/Diary/Fowan.Diary.Windows.exe",
            "Tools/Report/Fowan.Report.Windows.exe",
            "Tools/Pomodoro/Fowan.Pomodoro.Windows.exe",
            "Tools/AI/Chat/Fowan.Ai.Chat.Windows.exe",
            "Tools/AI/Config/Fowan.Ai.Config.Windows.exe",
            "Core/fowan-core.exe"
        )
    }
}

foreach ($relativePath in $relativePaths) {
    $expectedPath = [IO.Path]::GetFullPath((Join-Path $output $relativePath))
    $name = [IO.Path]::GetFileNameWithoutExtension($expectedPath)
    foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
        $processPath = $null
        try { $processPath = $process.Path } catch { }
        if ($processPath -and
            [IO.Path]::GetFullPath($processPath).Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $process.Id -Force
        }
    }
}
