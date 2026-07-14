param(
    [ValidateSet("chat", "config")]
    [string]$Tool = "chat",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("credentials", "models", "bindings")]
    [string]$Page = "credentials",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputConfiguration = $Configuration.ToLowerInvariant()
$processName = if ($Tool -eq "chat") { "Fowan.Ai.Chat.Windows" } else { "Fowan.Ai.Config.Windows" }
$executable = if ($Tool -eq "chat") {
    Join-Path $repoRoot "out/windows-ai-chat/$outputConfiguration/Fowan.Ai.Chat.Windows.exe"
} else {
    Join-Path $repoRoot "out/windows-ai-config/$outputConfiguration/Fowan.Ai.Config.Windows.exe"
}
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $repoRoot "out/screenshots/fowan-ai-$Tool.png"
} else {
    [System.IO.Path]::GetFullPath($OutputPath)
}

Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "fowan-core" -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$qaRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FowanAiVisualQa-" + [Guid]::NewGuid().ToString("N"))
$qaLocalAppData = Join-Path $qaRoot "LocalAppData"
New-Item -ItemType Directory -Force -Path $qaLocalAppData | Out-Null
$previousLocalAppData = $env:LOCALAPPDATA
$env:LOCALAPPDATA = $qaLocalAppData

$drawingAssembly = Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Drawing" -Recurse -Filter "System.Drawing.dll" |
    Select-Object -First 1 -ExpandProperty FullName
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
        System.Threading.Thread.Sleep(400);
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
    Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -ArgumentList "--page=$Page"
} else {
    Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable)
}
Start-Sleep -Seconds 4
$process = Get-Process -Name $processName -ErrorAction Stop | Select-Object -First 1
if ($process.MainWindowHandle -eq 0) { throw "$processName did not create a top-level window." }
[FowanAiWindowCapture]::Save($process.MainWindowHandle, $OutputPath)
Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "fowan-core" -ErrorAction SilentlyContinue | Stop-Process -Force
$env:LOCALAPPDATA = $previousLocalAppData
Remove-Item -LiteralPath $qaRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "AI screenshot: $OutputPath"
