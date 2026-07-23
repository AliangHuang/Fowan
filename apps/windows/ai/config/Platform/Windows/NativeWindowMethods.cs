using System.Runtime.InteropServices;

namespace Fowan.Ai.Config.Windows.Platform.Windows;

internal static class NativeWindowMethods
{
    private const uint WmNcLeftButtonDown = 0x00A1;
    private const nint HtCaption = 2;

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    internal static void BeginWindowDrag(nint windowHandle)
    {
        _ = ReleaseCapture();
        _ = SendMessage(windowHandle, WmNcLeftButtonDown, HtCaption, 0);
    }
}
