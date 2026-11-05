using System.Windows;
using System.Windows.Threading;
using static Fowan.Todo.Sticky.Windows.Platform.Windows.StickyNativeMethods;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal sealed class StickyNativeWindowController(
    Window window,
    Func<bool> isFloating,
    Func<bool> isWindowDragCandidate,
    Action completeWindowDrag,
    Func<bool> tryEnterFloating,
    Action enforceDisplayConstraints,
    Action synchronizeChildWindows,
    Action synchronizeForDpiChange)
{
    private const double ResizeBorderDip = 10;
    private const int MinResizeBorderPixels = 8;
    private const int MaxResizeBorderPixels = 12;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int WmKeyFirst = 0x0100;
    private const int WmKeyLast = 0x0109;
    private const int WmImeStartComposition = 0x010D;
    private const int WmImeEndComposition = 0x010E;
    private const int WmImeComposition = 0x010F;
    private const int WmImeChar = 0x0286;
    private const int WmSysCommand = 0x0112;
    private const int WmNonClientLeftButtonUp = 0x00A2;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmDpiChanged = 0x02E0;
    private const long SysCommandMask = 0xFFF0;
    private const long ScMinimize = 0xF020;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int MaNoActivate = 3;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private bool _wasMoving;

    public bool IsInMoveSizeLoop { get; private set; }

    public static void SetResizeEnabled(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        if (enabled)
        {
            style |= WsThickFrame;
        }
        else
        {
            style &= ~WsThickFrame;
        }
        style &= ~(WsMinimizeBox | WsMaximizeBox);
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static void SetFloatingMouseInput(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style = enabled ? style | WsExNoActivate : style & ~WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (isFloating() && message == WmMouseActivate)
        {
            // Keep delivering the click to the floating icon without allowing it to take keyboard focus.
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        if (isFloating() && IsKeyboardOrImeMessage(message))
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (message == WmDpiChanged)
        {
            // Leave WPF's default handler in control of the suggested bounds. Synchronize
            // owned windows after WPF has adopted the new device transform.
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, synchronizeForDpiChange);
            return IntPtr.Zero;
        }
        if (message == WmSysCommand && (wParam.ToInt64() & SysCommandMask) == ScMinimize)
        {
            handled = true;
            return IntPtr.Zero;
        }
        if ((message == WmLeftButtonUp || message == WmNonClientLeftButtonUp) && isWindowDragCandidate())
            window.Dispatcher.BeginInvoke(completeWindowDrag);
        if (message == WmEnterSizeMove)
        {
            IsInMoveSizeLoop = true;
            _wasMoving = false;
            return IntPtr.Zero;
        }
        if (message == WmMoving)
        {
            _wasMoving = true;
            return IntPtr.Zero;
        }
        if (message == WmExitSizeMove)
        {
            IsInMoveSizeLoop = false;
            var shouldTryFloating = _wasMoving;
            _wasMoving = false;
            window.Dispatcher.BeginInvoke(() =>
            {
                if (isFloating()) return;
                if (!shouldTryFloating || !tryEnterFloating())
                {
                    enforceDisplayConstraints();
                    synchronizeChildWindows();
                }
            });
            return IntPtr.Zero;
        }
        if (message != WmNcHitTest) return IntPtr.Zero;
        handled = true;
        return new IntPtr(isFloating() ? HtClient : HitTestResizeBorder(hwnd, lParam));
    }

    private int HitTestResizeBorder(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var rect)) return HtClient;
        var x = GetX(lParam);
        var y = GetY(lParam);
        var border = ResizeBorderPixels();
        var left = x >= rect.Left && x < rect.Left + border;
        var right = x <= rect.Right && x > rect.Right - border;
        var top = y >= rect.Top && y < rect.Top + border;
        var bottom = y <= rect.Bottom && y > rect.Bottom - border;
        if (left && top) return HtTopLeft;
        if (right && top) return HtTopRight;
        if (left && bottom) return HtBottomLeft;
        if (right && bottom) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        return bottom ? HtBottom : HtClient;
    }

    private int ResizeBorderPixels()
    {
        var source = PresentationSource.FromVisual(window);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return Math.Clamp((int)Math.Round(ResizeBorderDip * scale), MinResizeBorderPixels, MaxResizeBorderPixels);
    }

    private static int GetX(IntPtr value) => unchecked((short)((long)value & 0xFFFF));
    private static int GetY(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));
    private static bool IsKeyboardOrImeMessage(int message) =>
        (message >= WmKeyFirst && message <= WmKeyLast) ||
        message is WmImeStartComposition or WmImeEndComposition or WmImeComposition or WmImeChar;
}
