using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

/// <summary>
/// Keeps the separate sticky menu surface in the client area so its application-level
/// drag handler is never replaced by Windows caption dragging or snap behavior.
/// </summary>
internal sealed class StickyMenuNativeWindowController : IDisposable
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int SwHide = 0;
    private const int DwmwaTransitionsForceDisabled = 3;
    private readonly Window _window;
    private HwndSource? _source;

    public StickyMenuNativeWindowController(Window window)
    {
        _window = window;
        _window.SourceInitialized += OnSourceInitialized;
    }

    public void Dispose()
    {
        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WndProc);
    }

    public void HideImmediately()
    {
        var wasVisible = _window.IsVisible;
        if (_source is not null)
        {
            ShowWindow(_source.Handle, SwHide);
        }

        if (wasVisible)
        {
            _window.Hide();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        if (_source is not null)
        {
            var disableTransitions = 1;
            DwmSetWindowAttribute(
                _source.Handle,
                DwmwaTransitionsForceDisabled,
                ref disableTransitions,
                Marshal.SizeOf<int>());
        }
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest) return IntPtr.Zero;

        handled = true;
        return new IntPtr(HtClient);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
