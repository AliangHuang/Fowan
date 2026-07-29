using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using static Fowan.Todo.Sticky.Windows.Platform.Windows.StickyNativeMethods;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal sealed class StickyFloatingModeController(
    Window window,
    TodoWorkspace workspace,
    Func<(double Top, double Height)> titleBarGeometry,
    Func<(double X, double Y)> deviceScale,
    Func<(double Width, double Height)> monitorSize,
    Func<double> menuHeight,
    Action closeChildren,
    Action hideMenuWindow,
    Action buildUi,
    Action applyStoredSettings,
    Action refresh,
    Action<bool> updateMinimumSize,
    Action<IntPtr, bool> setNativeResize,
    Action<IntPtr, bool> setFloatingMouseInput,
    Action<bool> enforceDisplayConstraints)
{
    private const double BaseWidth = 408;
    private const double BaseHeight = 568;
    private const double FloatingSize = 52;
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
        if (hwnd != IntPtr.Zero)
        {
            setNativeResize(hwnd, true);
            setFloatingMouseInput(hwnd, false);
        }
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
        var windowWidth = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var edge = TodoStickyPlacement.FindDockEdgeByWindowBounds(
            workLeft,
            workRight,
            window.Left,
            window.Left + windowWidth);
        if (edge is null) return false;
        var titleBar = titleBarGeometry();
        hideMenuWindow();
        var restore = startGeometry ?? new StickyWindowGeometry(window.Left, window.Top, window.Width, window.Height);
        var expanded = TodoStickyPlacement.ExpandedGeometryFromBody(restore.Top, restore.Height, menuHeight());
        settings.StickyLeft = restore.Left;
        settings.StickyTop = expanded.Top;
        settings.StickyWidth = restore.Width;
        settings.StickyHeight = expanded.Height;
        settings.StickyFloatingEdge = edge;
        settings.StickyFloatingTop = AlignedTop(titleBar);
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
            var expandedHeight = settings.StickyHeight ?? BaseHeight * settings.StickyScale;
            var body = TodoStickyPlacement.BodyGeometryFromExpanded(0, expandedHeight, menuHeight());
            window.Height = Math.Clamp(
                body.Height,
                window.MinHeight,
                Math.Max(window.MinHeight, max.Height - menuHeight()));
            if (TodoStickyPlacement.IsUsableStoredOrigin(settings.StickyLeft, settings.StickyTop))
            {
                window.Left = settings.StickyLeft!.Value;
                window.Top = TodoStickyPlacement.BodyGeometryFromExpanded(
                    settings.StickyTop!.Value,
                    0,
                    menuHeight()).Top;
            }
            window.Topmost = settings.IsStickyTopmost;
            buildUi();
            applyStoredSettings();
            refresh();
            var hwnd = Handle();
            if (hwnd != IntPtr.Zero)
            {
                setNativeResize(hwnd, true);
                setFloatingMouseInput(hwnd, false);
            }
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
        hideMenuWindow();
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
            setFloatingMouseInput(hwnd, true);
            Keyboard.ClearFocus();
        }
        finally
        {
            IsApplying = false;
        }
        if (save) workspace.SaveSettings();
    }

    private static double AlignedTop((double Top, double Height) titleBar) =>
        TodoStickyPlacement.AlignCenters(titleBar.Top, titleBar.Height, FloatingSize);

    private IntPtr Handle() => new WindowInteropHelper(window).Handle;

    private static bool TryMonitorInfo(IntPtr monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info);
    }
}
