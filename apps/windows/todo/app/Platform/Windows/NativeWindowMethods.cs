using System.Runtime.InteropServices;

namespace Fowan.Todo.Windows.Platform.Windows;

internal static class NativeWindowMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint windowHandle);
}
