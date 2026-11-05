using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal static class ToolCenterWindowActivation
{
    public static void Configure(Window window)
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;
        if (scale <= 0) scale = 1;
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1240 * scale), Math.Max(1, area.Width - (int)(32 * scale)));
        var height = Math.Min((int)(820 * scale), Math.Max(1, area.Height - (int)(32 * scale)));
        window.AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(900 * scale), width);
            presenter.PreferredMinimumHeight = Math.Min((int)(620 * scale), height);
        }
    }

    public static void Restore(Window window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (IsIconic(handle)) ShowWindow(handle, 9);
        window.Activate();
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
