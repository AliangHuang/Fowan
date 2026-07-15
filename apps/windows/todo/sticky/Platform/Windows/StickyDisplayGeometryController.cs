using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Fowan.Todo.Sticky.Windows.Platform.Windows.StickyNativeMethods;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal sealed class StickyDisplayGeometryController(
    Window window,
    TodoWorkspace workspace,
    Func<TodoSettings> settings,
    Func<FrameworkElement> root,
    Func<FrameworkElement> dragHandle)
{
    private const double BaseWidth = 408;
    private const double BaseHeight = 568;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private bool _isEnforcing;
    public bool IsEnforcing => _isEnforcing;

    public void UpdateMinimumWindowSize(bool clampCurrentSize)
    {
        var maxSize = CurrentMonitorSizeDip();
        var current = settings();
        window.MinWidth = Math.Min(BaseWidth * TodoSettings.MinStickyScale * current.StickyScale, maxSize.Width);
        window.MinHeight = Math.Min(BaseHeight * TodoSettings.MinStickyScale * current.StickyScale, maxSize.Height);
        if (!clampCurrentSize) return;
        window.Width = Math.Max(window.Width, window.MinWidth);
        window.Height = Math.Max(window.Height, window.MinHeight);
    }

    public void Enforce(bool save)
    {
        if (_isEnforcing) return;
        _isEnforcing = true;
        try
        {
            UpdateMinimumWindowSize(clampCurrentSize: false);
            ClampWindowSizeToCurrentMonitor();
            ClampDragHandleToVisibleDisplay();
        }
        finally
        {
            _isEnforcing = false;
        }
        if (save) SaveGeometry();
    }

    public (double Width, double Height) CurrentMonitorSizeDip()
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (TryGetMonitorInfo(monitor, out var monitorInfo))
            {
                var scale = DeviceScale();
                return (
                    Math.Max(1, (monitorInfo.Monitor.Right - monitorInfo.Monitor.Left) / scale.X),
                    Math.Max(1, (monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top) / scale.Y));
            }
        }
        return (SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    public (double X, double Y) DeviceScale()
    {
        var target = PresentationSource.FromVisual(window)?.CompositionTarget;
        return target is null ? (1.0, 1.0) : (target.TransformToDevice.M11, target.TransformToDevice.M22);
    }

    public void SaveGeometry()
    {
        if (window.WindowState != WindowState.Normal ||
            (!window.IsLoaded && PresentationSource.FromVisual(window) is null) ||
            double.IsNaN(window.Left) || double.IsNaN(window.Top) ||
            double.IsNaN(window.Width) || double.IsNaN(window.Height)) return;
        var current = settings();
        if (current.IsStickyFloatingModeEnabled)
        {
            current.StickyFloatingTop = window.Top;
        }
        else
        {
            current.StickyLeft = window.Left;
            current.StickyTop = window.Top;
            current.StickyWidth = window.Width;
            current.StickyHeight = window.Height;
        }
        workspace.SaveSettings(current);
    }

    public void EnforceVerticalConstraints()
    {
        if (!TryGetDragHandleScreenRect(out var dragRect)) return;
        var monitor = MonitorFromRect(ref dragRect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo)) return;
        var dy = dragRect.Top < monitorInfo.WorkArea.Top
            ? monitorInfo.WorkArea.Top - dragRect.Top
            : dragRect.Bottom > monitorInfo.WorkArea.Bottom
                ? monitorInfo.WorkArea.Bottom - dragRect.Bottom
                : 0;
        if (dy != 0) window.Top += dy / DeviceScale().Y;
    }

    private void ClampWindowSizeToCurrentMonitor()
    {
        var maxSize = CurrentMonitorSizeDip();
        var nextWidth = Math.Clamp(window.Width, window.MinWidth, Math.Max(window.MinWidth, maxSize.Width));
        var nextHeight = Math.Clamp(window.Height, window.MinHeight, Math.Max(window.MinHeight, maxSize.Height));
        if (Math.Abs(nextWidth - window.Width) > 0.5) window.Width = nextWidth;
        if (Math.Abs(nextHeight - window.Height) > 0.5) window.Height = nextHeight;
    }

    private void ClampDragHandleToVisibleDisplay()
    {
        if (!TryGetDragHandleScreenRect(out var dragRect)) return;
        var monitor = MonitorFromRect(ref dragRect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo)) return;
        var bounds = monitorInfo.Monitor;
        var dx = dragRect.Left < bounds.Left ? bounds.Left - dragRect.Left : dragRect.Right > bounds.Right ? bounds.Right - dragRect.Right : 0;
        var dy = dragRect.Top < bounds.Top ? bounds.Top - dragRect.Top : dragRect.Bottom > bounds.Bottom ? bounds.Bottom - dragRect.Bottom : 0;
        if (dx == 0 && dy == 0) return;
        var scale = DeviceScale();
        window.Left += dx / scale.X;
        window.Top += dy / scale.Y;
    }

    private bool TryGetDragHandleScreenRect(out NativeRect rect)
    {
        var handle = dragHandle();
        if (handle.IsLoaded && handle.ActualWidth > 0 && handle.ActualHeight > 0)
        {
            rect = ScreenRectForElement(handle, handle.ActualWidth, handle.ActualHeight);
            return true;
        }
        var contentRoot = root();
        if (contentRoot.IsLoaded)
        {
            var scale = DeviceScale();
            var topLeft = contentRoot.PointToScreen(new Point(18, 0));
            rect = new NativeRect
            {
                Left = (int)Math.Floor(topLeft.X), Top = (int)Math.Floor(topLeft.Y),
                Right = (int)Math.Ceiling(topLeft.X + 150 * scale.X),
                Bottom = (int)Math.Ceiling(topLeft.Y + 54 * scale.Y)
            };
            return true;
        }
        rect = default;
        return false;
    }

    private static NativeRect ScreenRectForElement(FrameworkElement element, double width, double height)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(width, height));
        return new NativeRect
        {
            Left = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X)),
            Top = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y)),
            Right = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X)),
            Bottom = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y))
        };
    }

    private static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo monitorInfo)
    {
        monitorInfo = CreateMonitorInfo();
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo);
    }
}
