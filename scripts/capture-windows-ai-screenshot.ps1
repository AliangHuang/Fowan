param(
    [ValidateSet("chat", "config")]
    [string]$Tool = "chat",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("credentials", "models", "bindings")]
    [string]$Page = "credentials",
    [string]$OutputPath,
    [switch]$VisualFixture,
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$processName = if ($Tool -eq "chat") { "Fowan.Ai.Chat.Windows.Dev" } else { "Fowan.Ai.Config.Windows.Dev" }
$executable = if ($Tool -eq "chat") {
    Join-Path $repoRoot "build/windows/win-x64/app/Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe"
} else {
    Join-Path $repoRoot "build/windows/win-x64/app/Tools/AI/Config/Fowan.Ai.Config.Windows.Dev.exe"
}
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $repoRoot "build/test/screenshots/fowan-ai-$Tool.png"
} else {
    [System.IO.Path]::GetFullPath($OutputPath)
}
$executable = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) { $executable } else { [System.IO.Path]::GetFullPath($ExecutablePath) }

if ($VisualFixture -and $Configuration -ne "Debug") {
    throw "The AI visual fixture is available only in Debug builds."
}

if (-not $VisualFixture) {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
}
if (-not $VisualFixture) {
    Get-Process -Name "fowan-core.Dev" -ErrorAction SilentlyContinue | Stop-Process -Force
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$windowsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
$drawingAssembly = Get-ChildItem (Join-Path $windowsDirectory "Microsoft.NET\assembly\GAC_MSIL\System.Drawing") -Recurse -Filter "System.Drawing.dll" |
    Select-Object -First 1 -ExpandProperty FullName
$qaRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FowanAiVisualQa-" + [Guid]::NewGuid().ToString("N"))
$qaLocalAppData = Join-Path $qaRoot "LocalAppData"
New-Item -ItemType Directory -Force -Path $qaLocalAppData | Out-Null
$previousLocalAppData = $env:LOCALAPPDATA
$env:LOCALAPPDATA = $qaLocalAppData

Add-Type -ReferencedAssemblies $drawingAssembly @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class FowanAiWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    public static void Save(IntPtr handle, string path)
    {
        SetForegroundWindow(handle);
        Rect rect;
        if (!GetWindowRect(handle, out rect)) throw new InvalidOperationException("Unable to read AI window bounds.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
'@

if ($Tool -eq "config") {
    $startedProcess = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -ArgumentList "--page=$Page" -PassThru
} else {
    $arguments = if ($VisualFixture) { "--visual-fixture" } else { $null }
    $startedProcess = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -ArgumentList $arguments -PassThru
}
Start-Sleep -Seconds 4
$process = if ($VisualFixture) { Get-Process -Id $startedProcess.Id -ErrorAction Stop } else { Get-Process -Name $processName -ErrorAction Stop | Select-Object -First 1 }
if ($process.MainWindowHandle -eq 0) { throw "$processName did not create a top-level window." }
[FowanAiWindowCapture]::Save($process.MainWindowHandle, $OutputPath)
if ($VisualFixture) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } else { Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force }
if (-not $VisualFixture) {
    Get-Process -Name "fowan-core.Dev" -ErrorAction SilentlyContinue | Stop-Process -Force
}
$env:LOCALAPPDATA = $previousLocalAppData
Remove-Item -LiteralPath $qaRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "AI screenshot: $OutputPath"
