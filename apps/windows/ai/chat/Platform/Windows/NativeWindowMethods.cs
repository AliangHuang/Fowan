using System.Runtime.InteropServices;

namespace Fowan.Ai.Chat.Windows.Platform.Windows;

internal static class NativeWindowMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint windowHandle);
}
