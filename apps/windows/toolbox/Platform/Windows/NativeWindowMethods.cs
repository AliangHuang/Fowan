using System.Runtime.InteropServices;

namespace Fowan.Windows.Platform.Windows;

internal static class NativeWindowMethods
{
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint windowHandle);
}
