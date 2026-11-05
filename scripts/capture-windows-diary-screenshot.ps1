param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("today", "timeline", "tags")]
    [string]$ViewId = "today",
    [string]$TimelineNotebookId = "all",
    [ValidateSet("all", "today", "week", "month", "year")]
    [string]$TimelineRangeId = "all",
    [string]$TimelineAnchorDate = "2026-07-07",
    [string]$TimelineDate,
    [string]$TimelineNavigatorMonth,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $repoRoot "build"
$fixtureRoot = Join-Path $repoRoot "tests/fixtures/diary-visual"
$runtimeRoot = Join-Path $buildRoot "test/visual-qa/diary-runtime"
$defaultOutput = Join-Path $buildRoot ("test/screenshots/fowan-diary-functional-" + $ViewId + ".png")
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) { $defaultOutput } else { [System.IO.Path]::GetFullPath($OutputPath) }

if (-not (Test-Path -LiteralPath $fixtureRoot)) {
    throw "Diary visual fixture was not found: $fixtureRoot"
}

$resolvedBuildRoot = [System.IO.Path]::GetFullPath($buildRoot).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
$resolvedRuntimeRoot = [System.IO.Path]::GetFullPath($runtimeRoot)
if (-not $resolvedRuntimeRoot.StartsWith($resolvedBuildRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare visual runtime outside the build root: $resolvedRuntimeRoot"
}

Get-Process -Name "Fowan.Diary.Windows.Dev" -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path -LiteralPath $runtimeRoot) {
    Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $fixtureRoot "diary-data.json") -Destination (Join-Path $runtimeRoot "diary-data.json")
Copy-Item -LiteralPath (Join-Path $fixtureRoot "diary-settings.json") -Destination (Join-Path $runtimeRoot "diary-settings.json")
$settingsPath = Join-Path $runtimeRoot "diary-settings.json"
$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$settings.currentViewId = $ViewId
$settings.timelineNotebookId = $TimelineNotebookId
[System.IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null

$drawingAssembly = Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Drawing" -Recurse -Filter "System.Drawing.dll" |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($drawingAssembly)) {
    throw "System.Drawing was not found for diary screenshot capture."
}

Add-Type -ReferencedAssemblies $drawingAssembly @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class FowanDiaryWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    public static bool Save(IntPtr handle, string path)
    {
        Rect rect;
        if (!GetWindowRect(handle, out rect)) throw new InvalidOperationException("Unable to read Diary window bounds.");
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            bitmap.Save(path, ImageFormat.Png);
            var samples = 0;
            var blackSamples = 0;
            for (var y = 16; y < height; y += 32)
            for (var x = 16; x < width; x += 32)
            {
                samples++;
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 4 && pixel.G < 4 && pixel.B < 4) blackSamples++;
            }
            var center = bitmap.GetPixel(Math.Max(0, width / 2), Math.Max(0, height / 2));
            var detail = bitmap.GetPixel(Math.Max(0, width * 3 / 4), Math.Max(0, height / 2));
            var centerOrDetailIsBlack = (center.R < 4 && center.G < 4 && center.B < 4) ||
                (detail.R < 4 && detail.G < 4 && detail.B < 4);
            return samples > 0 && blackSamples * 10 < samples && !centerOrDetailIsBlack;
        }
    }
}
'@

$previousRoot = $env:FOWAN_DIARY_DATA_ROOT
$previousToday = $env:FOWAN_DIARY_TODAY
$previousTimelineRange = $env:FOWAN_DIARY_TIMELINE_RANGE
$previousTimelineAnchor = $env:FOWAN_DIARY_TIMELINE_ANCHOR
$previousTimelineDate = $env:FOWAN_DIARY_TIMELINE_DATE
$previousTimelineNavigatorMonth = $env:FOWAN_DIARY_TIMELINE_NAVIGATOR_MONTH
try {
    $env:FOWAN_DIARY_DATA_ROOT = $runtimeRoot
    $env:FOWAN_DIARY_TODAY = "2026-07-07"
    $env:FOWAN_DIARY_TIMELINE_RANGE = $TimelineRangeId
    $env:FOWAN_DIARY_TIMELINE_ANCHOR = $TimelineAnchorDate
    $env:FOWAN_DIARY_TIMELINE_DATE = $TimelineDate
    $env:FOWAN_DIARY_TIMELINE_NAVIGATOR_MONTH = $TimelineNavigatorMonth
    & (Join-Path $PSScriptRoot "run-windows-diary.ps1") -Configuration $Configuration
    Start-Sleep -Seconds 4
    $process = @(Get-Process -Name "Fowan.Diary.Windows.Dev" -ErrorAction Stop | Select-Object -First 1)[0]
    if ($process.MainWindowHandle -eq 0) {
        throw "Diary did not create a top-level window."
    }
    $captured = $false
    foreach ($attempt in 1..3) {
        $captured = [FowanDiaryWindowCapture]::Save($process.MainWindowHandle, $OutputPath)
        if ($captured) { break }
        Start-Sleep -Seconds 2
    }
    if (-not $captured) {
        throw "Diary did not finish rendering a usable screenshot after three capture attempts."
    }
    Write-Host "Diary screenshot: $OutputPath"
}
finally {
    Get-Process -Name "Fowan.Diary.Windows.Dev" -ErrorAction SilentlyContinue | Stop-Process -Force
    $env:FOWAN_DIARY_DATA_ROOT = $previousRoot
    $env:FOWAN_DIARY_TODAY = $previousToday
    $env:FOWAN_DIARY_TIMELINE_RANGE = $previousTimelineRange
    $env:FOWAN_DIARY_TIMELINE_ANCHOR = $previousTimelineAnchor
    $env:FOWAN_DIARY_TIMELINE_DATE = $previousTimelineDate
    $env:FOWAN_DIARY_TIMELINE_NAVIGATOR_MONTH = $previousTimelineNavigatorMonth
}
