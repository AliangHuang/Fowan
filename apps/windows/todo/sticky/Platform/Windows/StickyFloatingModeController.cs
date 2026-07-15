using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Windows;
using System.Windows.Interop;
using static Fowan.Todo.Sticky.Windows.Platform.Windows.StickyNativeMethods;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal sealed class StickyFloatingModeController(
    Window window,
    TodoWorkspace workspace,
    Func<FrameworkElement> brandIcon,
    Func<(double X, double Y)> deviceScale,
    Func<(double Width, double Height)> monitorSize,
    Action closeChildren,
    Action buildUi,
    Action applyStoredSettings,
    Action refresh,
    Action<bool> updateMinimumSize,
    Action<IntPtr, bool> setNativeResize,
    Action<bool> enforceDisplayConstraints)
{
    private const double BaseWidth = 408;
    private const double BaseHeight = 568;
    private const double FloatingSize = 52;
    private const double EdgeThreshold = 16;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public bool IsApplying { get; private set; }

    public void ApplyCurrent(bool save)
    {
        if (workspace.Settings.IsStickyFloatingModeEnabled)
        {
            ApplyFloatingGeometry(save);
            return;
        }
        window.ResizeMode = ResizeMode.CanResize;
        window.Topmost = workspace.Settings.IsStickyTopmost;
        var hwnd = Handle();
        if (hwnd != IntPtr.Zero) setNativeResize(hwnd, true);
        enforceDisplayConstraints(save);
    }

    public bool TryEnter(NativePoint? releasePoint = null, StickyWindowGeometry? startGeometry = null)
    {
        var settings = workspace.Settings;
        if (settings.IsStickyFloatingModeEnabled || window.WindowState != WindowState.Normal) return false;
        var hwnd = Handle();
        if (hwnd == IntPtr.Zero) return false;
        var monitor = releasePoint.HasValue
            ? MonitorFromPoint(releasePoint.Value, MonitorDefaultToNearest)
            : MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (!TryMonitorInfo(monitor, out var info)) return false;
        var scale = deviceScale();
        var workLeft = info.WorkArea.Left / scale.X;
        var workRight = info.WorkArea.Right / scale.X;
        var center = window.Left + (window.ActualWidth > 0 ? window.ActualWidth : window.Width) / 2;
        var edge = TodoStickyPlacement.FindDockEdgeByCenter(workLeft, workRight, center, EdgeThreshold);
        if (edge is null) return false;
        var restore = startGeometry ?? new StickyWindowGeometry(window.Left, window.Top, window.Width, window.Height);
        settings.StickyLeft = restore.Left;
        settings.StickyTop = restore.Top;
        settings.StickyWidth = restore.Width;
        settings.StickyHeight = restore.Height;
        settings.StickyFloatingEdge = edge;
        settings.StickyFloatingTop = AlignedTop();
        settings.IsStickyFloatingModeEnabled = true;
        workspace.SaveSettings();
        closeChildren();
        buildUi();
        applyStoredSettings();
        ApplyFloatingGeometry(save: true);
        return true;
    }

    public void Snap(NativePoint? releasePoint = null)
    {
        var settings = workspace.Settings;
        if (!settings.IsStickyFloatingModeEnabled) return;
        var hwnd = Handle();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        var monitor = releasePoint.HasValue
            ? MonitorFromPoint(releasePoint.Value, MonitorDefaultToNearest)
            : MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (!TryMonitorInfo(monitor, out var info)) return;
        var center = releasePoint?.X ?? rect.Left + (rect.Right - rect.Left) / 2;
        settings.StickyFloatingEdge = TodoStickyPlacement.NearestEdge(
            info.WorkArea.Left, info.WorkArea.Right, center);
        settings.StickyFloatingTop = window.Top;
        ApplyFloatingGeometry(save: true);
    }

    public void Exit()
    {
        var settings = workspace.Settings;
        if (!settings.IsStickyFloatingModeEnabled) return;
        settings.IsStickyFloatingModeEnabled = false;
        settings.StickyFloatingEdge = null;
        settings.StickyFloatingTop = null;
        workspace.SaveSettings();
        IsApplying = true;
        try
        {
            window.ResizeMode = ResizeMode.CanResize;
            updateMinimumSize(false);
            var max = monitorSize();
            window.Width = Math.Clamp(settings.StickyWidth ?? BaseWidth * settings.StickyScale, window.MinWidth, max.Width);
            window.Height = Math.Clamp(settings.StickyHeight ?? BaseHeight * settings.StickyScale, window.MinHeight, max.Height);
            if (settings.StickyLeft.HasValue && settings.StickyTop.HasValue)
            {
                window.Left = settings.StickyLeft.Value;
                window.Top = settings.StickyTop.Value;
            }
            window.Topmost = settings.IsStickyTopmost;
            buildUi();
            applyStoredSettings();
            refresh();
            var hwnd = Handle();
            if (hwnd != IntPtr.Zero) setNativeResize(hwnd, true);
        }
        finally
        {
            IsApplying = false;
        }
        enforceDisplayConstraints(true);
        window.Activate();
    }

    public void ApplyFloatingGeometry(bool save)
    {
        var hwnd = Handle();
        if (hwnd == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (!TryMonitorInfo(monitor, out var info)) return;
        var scale = deviceScale();
        var left = info.WorkArea.Left / scale.X;
        var right = info.WorkArea.Right / scale.X;
        var top = info.WorkArea.Top / scale.Y;
        var bottom = info.WorkArea.Bottom / scale.Y;
        var settings = workspace.Settings;
        var desiredTop = settings.StickyFloatingTop ?? (window.Top + Math.Max(0, window.Height - FloatingSize) / 2);
        IsApplying = true;
        try
        {
            closeChildren();
            window.ResizeMode = ResizeMode.NoResize;
            window.MinWidth = FloatingSize;
            window.MinHeight = FloatingSize;
            window.Width = FloatingSize;
            window.Height = FloatingSize;
            window.Left = string.Equals(settings.StickyFloatingEdge, TodoStickyFloatingEdges.Right, StringComparison.Ordinal)
                ? right - FloatingSize
                : left;
            window.Top = Math.Clamp(desiredTop, top, Math.Max(top, bottom - FloatingSize));
            window.Topmost = true;
            settings.StickyFloatingTop = window.Top;
            setNativeResize(hwnd, false);
        }
        finally
        {
            IsApplying = false;
        }
        if (save) workspace.SaveSettings();
    }

    private double AlignedTop()
    {
        var icon = brandIcon();
        if (!icon.IsLoaded || icon.ActualHeight <= 0) return window.Top;
        var iconTop = icon.TranslatePoint(new Point(0, 0), window).Y;
        return TodoStickyPlacement.AlignCenters(window.Top + iconTop, icon.ActualHeight, FloatingSize);
    }

    private IntPtr Handle() => new WindowInteropHelper(window).Handle;

    private static bool TryMonitorInfo(IntPtr monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info);
    }
}
