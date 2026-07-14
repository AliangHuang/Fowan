param(
    [ValidateSet("Toolbox", "Todo", "Diary", "Ai", "All")]
    [string]$Component = "All"
)

$ErrorActionPreference = "Stop"
$processes = switch ($Component) {
    "Toolbox" { @("Fowan.Windows") }
    "Todo" { @("Fowan.Todo.Windows", "Fowan.Todo.Sticky.Windows") }
    "Diary" { @("Fowan.Diary.Windows") }
    "Ai" { @("Fowan.Ai.Chat.Windows", "Fowan.Ai.Config.Windows", "fowan-core") }
    default {
        @(
            "Fowan.Windows",
            "Fowan.Todo.Windows",
            "Fowan.Todo.Sticky.Windows",
            "Fowan.Diary.Windows",
            "Fowan.Ai.Chat.Windows",
            "Fowan.Ai.Config.Windows",
            "fowan-core"
        )
    }
}

foreach ($name in $processes) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
}
