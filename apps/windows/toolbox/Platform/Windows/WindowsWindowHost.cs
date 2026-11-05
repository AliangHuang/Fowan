using System.Runtime.InteropServices;
using Fowan.Windows.AppPorts;

namespace Fowan.Windows.Platform.Windows;

internal sealed class WindowsWindowHost(IntPtr windowHandle, Action activate) : IWindowHost
{
    private const int HideCommand = 0;
    private const int RestoreCommand = 9;

    public void Hide() => ShowWindow(windowHandle, HideCommand);

    public void RestoreAndActivate()
    {
        ShowWindow(windowHandle, RestoreCommand);
        activate();
        SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
