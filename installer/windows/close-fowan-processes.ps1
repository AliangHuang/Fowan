param(
    [Parameter(Mandatory = $true)]
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

$allowedNames = @(
    "Fowan.Windows.exe",
    "Fowan.Todo.Windows.exe",
    "Fowan.Todo.Sticky.Windows.exe",
    "Fowan.Diary.Windows.exe"
)

$normalizedRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar

function Get-FowanInstallProcesses {
    @(Get-CimInstance Win32_Process | Where-Object {
        $process = $_
        $path = $process.ExecutablePath
        $allowedNames -contains $process.Name -and
            -not [string]::IsNullOrWhiteSpace($path) -and
            [System.IO.Path]::GetFullPath($path).StartsWith(
                $normalizedRoot,
                [System.StringComparison]::OrdinalIgnoreCase)
    })
}

$matches = Get-FowanInstallProcesses
foreach ($process in $matches) {
    & "$env:SystemRoot\System32\taskkill.exe" /PID $process.ProcessId /T /F | Out-Null
}

if ($matches.Count -gt 0) {
    Start-Sleep -Milliseconds 750
}

$remaining = Get-FowanInstallProcesses
if ($remaining.Count -gt 0) {
    foreach ($process in $remaining) {
        [Console]::Error.WriteLine("$($process.Name) (PID $($process.ProcessId)): $($process.ExecutablePath)")
    }

    exit 1
}

exit 0
