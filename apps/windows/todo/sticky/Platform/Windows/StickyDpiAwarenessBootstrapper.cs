using System.Runtime.InteropServices;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

/// <summary>
/// Establishes the process DPI context before WPF creates its first window.
/// The manifest remains the primary declaration; this protects it from WPF's
/// System-DPI default when the runtime has not already applied that manifest.
/// </summary>
internal static class StickyDpiAwarenessBootstrapper
{
    private const int ProcessPerMonitorDpiAware = 2;
    private const int ErrorAccessDenied = 5;
    private static readonly IntPtr PerMonitorV2Context = new(-4);

    public static void EnsurePerMonitorAwareness()
    {
        if (TrySetPerMonitorV2()) return;
        TrySetPerMonitorV1();
    }

    private static bool TrySetPerMonitorV2()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorV2Context)) return true;

            // The manifest may already have selected the process context. Do not
            // replace that declared context with an older fallback.
            return Marshal.GetLastWin32Error() == ErrorAccessDenied;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void TrySetPerMonitorV1()
    {
        try
        {
            if (SetProcessDpiAwareness(ProcessPerMonitorDpiAware) == 0) return;
        }
        catch (DllNotFoundException)
        {
            TrySetSystemDpiAwareness();
            return;
        }
        catch (EntryPointNotFoundException)
        {
            TrySetSystemDpiAwareness();
        }
    }

    private static void TrySetSystemDpiAwareness()
    {
        try
        {
            SetProcessDPIAware();
        }
        catch (EntryPointNotFoundException)
        {
            // Windows versions without this API use the manifest's legacy fallback.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();
}
