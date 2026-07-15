using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static Fowan.Todo.Sticky.Windows.Platform.Windows.StickyNativeMethods;

namespace Fowan.Todo.Sticky.Windows.Platform.Windows;

internal readonly record struct StickyWindowGeometry(double Left, double Top, double Width, double Height);

internal sealed class StickyWindowDragController(
    Window window,
    Func<bool> isFloating,
    Func<(double X, double Y)> deviceScale,
    Action<NativePoint> snapFloating,
    Action exitFloating,
    Action enforceVerticalConstraints,
    Func<NativePoint?, StickyWindowGeometry?, bool> tryEnterFloating,
    Action saveGeometry)
{
    private const double ActivationDistance = 6;
    private bool _candidate;
    private bool _moved;
    private bool _startedFloating;
    private double _distanceX;
    private double _distanceY;
    private NativePoint _lastCursor;
    private UIElement? _captureSource;
    private DispatcherTimer? _releaseTimer;
    private StickyWindowGeometry? _startGeometry;

    public bool IsDragging { get; private set; }
    public bool IsCandidate => _candidate;
    public bool StartedInFloatingMode => _startedFloating;

    public void CompleteFromExternal(bool allowFloatingClickRestore)
    {
        if (_candidate) Complete(allowFloatingClickRestore);
    }

    public void OnHeaderDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ButtonState != MouseButtonState.Pressed || IsHeaderButtonHit(args.OriginalSource as DependencyObject))
            return;
        OnMouseDown(sender, args);
    }

    public void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || args.ButtonState != MouseButtonState.Pressed ||
            sender is not UIElement || !GetCursorPos(out _lastCursor)) return;
        _candidate = true;
        _moved = false;
        _startedFloating = isFloating();
        _startGeometry = !_startedFloating && window.WindowState == WindowState.Normal
            ? new StickyWindowGeometry(window.Left, window.Top, window.Width, window.Height)
            : null;
        _distanceX = 0;
        _distanceY = 0;
        _captureSource = window;
        if (!window.CaptureMouse())
        {
            _candidate = false;
            _captureSource = null;
            _startGeometry = null;
            return;
        }
        _releaseTimer ??= CreateReleaseTimer();
        _releaseTimer.Start();
        args.Handled = true;
    }

    public void OnMouseMove(object sender, MouseEventArgs args)
    {
        if (!_candidate || args.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var cursor)) return;
        var scale = deviceScale();
        var dx = (cursor.X - _lastCursor.X) / scale.X;
        var dy = (cursor.Y - _lastCursor.Y) / scale.Y;
        _lastCursor = cursor;
        _distanceX += dx;
        _distanceY += dy;
        if (!_moved)
        {
            if (Math.Sqrt(_distanceX * _distanceX + _distanceY * _distanceY) < ActivationDistance) return;
            dx = _distanceX;
            dy = _distanceY;
        }
        _moved = true;
        IsDragging = true;
        window.Left += dx;
        window.Top += dy;
        args.Handled = true;
    }

    public void OnMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (!_candidate || args.ChangedButton != MouseButton.Left) return;
        Complete(allowFloatingClickRestore: true);
        args.Handled = true;
    }

    public void OnLostCapture(object sender, MouseEventArgs args)
    {
        if (!_candidate) return;
        if (IsLeftPressed())
        {
            window.Dispatcher.BeginInvoke(() =>
            {
                if (_candidate && !window.IsMouseCaptured) window.CaptureMouse();
            });
            return;
        }
        Complete(allowFloatingClickRestore: false);
    }

    private void Complete(bool allowFloatingClickRestore)
    {
        if (GetCursorPos(out var release)) _lastCursor = release;
        var moved = _moved;
        var startedFloating = _startedFloating;
        var startGeometry = _startGeometry;
        var captureSource = _captureSource;
        _candidate = false;
        _moved = false;
        IsDragging = false;
        _releaseTimer?.Stop();
        _captureSource = null;
        _startGeometry = null;
        if (captureSource?.IsMouseCaptured == true) captureSource.ReleaseMouseCapture();
        if (startedFloating)
        {
            if (moved || !allowFloatingClickRestore) snapFloating(_lastCursor);
            else exitFloating();
            return;
        }
        if (!moved) return;
        enforceVerticalConstraints();
        if (!tryEnterFloating(_lastCursor, startGeometry)) saveGeometry();
    }

    private DispatcherTimer CreateReleaseTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (_candidate && !IsLeftPressed()) Complete(allowFloatingClickRestore: true);
        };
        return timer;
    }

    private static bool IsLeftPressed() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    private static bool IsHeaderButtonHit(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
