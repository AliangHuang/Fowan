using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Fowan.Todo.Windows.Platform.Windows;

internal sealed class TodoNativeWindowController(Window window, Func<bool> isDarkTheme)
{
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const int ShowWindowRestore = 9;

    public void Configure(string title, string iconPath)
    {
        window.Title = title;
        try
        {
            var hwnd = Handle();
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var scale = Math.Clamp(NativeWindowMethods.GetDpiForWindow(hwnd) / 96.0, 1.0, 3.0);
            var width = (int)Math.Round(1280 * scale);
            var height = (int)Math.Round(820 * scale);
            appWindow.Resize(new SizeInt32(width, height));
            var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
            window.ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            ApplyCaptionColors(appWindow);
            if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
        }
        catch
        {
            // Window decoration APIs can fail in restricted hosts; the UI still renders.
        }
    }

    public void ApplyCaptionColors()
    {
        try
        {
            ApplyCaptionColors(AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Handle())));
        }
        catch
        {
            // Theme switching still rebuilds the app surface when caption APIs are unavailable.
        }
    }

    public void Hide() => NativeWindowMethods.ShowWindow(Handle(), ShowWindowHide);

    public void Show() => NativeWindowMethods.ShowWindow(Handle(), ShowWindowShow);

    public void RestoreAndForeground()
    {
        var hwnd = Handle();
        NativeWindowMethods.ShowWindow(hwnd, ShowWindowRestore);
        window.Activate();
        NativeWindowMethods.SetForegroundWindow(hwnd);
    }

    private void ApplyCaptionColors(AppWindow appWindow)
    {
        var dark = isDarkTheme();
        var foreground = dark
            ? ColorHelper.FromArgb(255, 215, 224, 234)
            : ColorHelper.FromArgb(255, 23, 36, 42);
        appWindow.TitleBar.ButtonForegroundColor = foreground;
        appWindow.TitleBar.ButtonInactiveForegroundColor = dark
            ? ColorHelper.FromArgb(180, 215, 224, 234)
            : ColorHelper.FromArgb(180, 23, 36, 42);
        appWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        appWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        appWindow.TitleBar.ButtonHoverBackgroundColor = dark
            ? ColorHelper.FromArgb(28, 215, 224, 234)
            : ColorHelper.FromArgb(18, 23, 36, 42);
        appWindow.TitleBar.ButtonPressedBackgroundColor = dark
            ? ColorHelper.FromArgb(42, 215, 224, 234)
            : ColorHelper.FromArgb(32, 23, 36, 42);
    }

    private IntPtr Handle() => WinRT.Interop.WindowNative.GetWindowHandle(window);
}
