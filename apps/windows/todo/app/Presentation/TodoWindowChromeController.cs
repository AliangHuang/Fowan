using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoWindowChromeController(Window window)
{
    private readonly TodoWindowPresentationController _state = new();
    private readonly List<FrameworkElement> _interactiveElements = [];
    private Grid? _root;
    private FrameworkElement? _brandArea;

    public void SetLayout(Grid root, FrameworkElement brandArea, params FrameworkElement[] interactiveElements)
    {
        _root = root;
        _brandArea = brandArea;
        _interactiveElements.Clear();
        _interactiveElements.AddRange(interactiveElements);
        root.Loaded += (_, _) => QueueRegionUpdate();
        root.SizeChanged += (_, _) => QueueRegionUpdate();
        QueueRegionUpdate();
    }

    public void QueueRegionUpdate()
    {
        var generation = _state.NextTitleBarGeneration();
        window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_state.IsCurrentTitleBarGeneration(generation)) UpdateRegions();
        });
    }

    public void EnterModalSurface()
    {
        _state.EnterModalSurface();
        ClearRegions();
    }

    public void ExitModalSurface()
    {
        if (_state.ExitModalSurface()) QueueRegionUpdate();
    }

    public async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        EnterModalSurface();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            ExitModalSurface();
        }
    }

    public Task WaitForLayoutAsync()
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => completion.TrySetResult(null))))
        {
            completion.TrySetResult(null);
        }
        return completion.Task;
    }

    private void ClearRegions()
    {
        _state.InvalidateTitleBarGeneration();
        try
        {
            window.SetTitleBar(null);
            var inputSource = InputNonClientPointerSource.GetForWindowId(WindowId());
            inputSource.ClearRegionRects(NonClientRegionKind.Caption);
            inputSource.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clear Todo title-bar regions: {exception}");
        }
    }

    private void UpdateRegions()
    {
        var root = _root;
        var brandArea = _brandArea;
        if (root is null || brandArea is null || root.ActualWidth <= 0 || root.XamlRoot is null) return;
        try
        {
            if (_state.HasModalSurface || !brandArea.IsLoaded)
            {
                ClearRegions();
                return;
            }
            var windowId = WindowId();
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var scale = Math.Clamp(root.XamlRoot.RasterizationScale, 1.0, 3.0);
            var clientWidth = Math.Max(0, (int)Math.Ceiling(root.ActualWidth * scale));
            var clientHeight = Math.Max(0, (int)Math.Ceiling(root.ActualHeight * scale));
            var titleBarHeight = Math.Max(appWindow.TitleBar.Height / scale, brandArea.ActualHeight);
            var captionHeight = (int)Math.Ceiling(titleBarHeight * scale);
            var captionWidth = Math.Max(0, clientWidth - appWindow.TitleBar.RightInset);
            var inputSource = InputNonClientPointerSource.GetForWindowId(windowId);
            window.SetTitleBar(brandArea);
            inputSource.SetRegionRects(
                NonClientRegionKind.Caption,
                captionWidth > 0 && captionHeight > 0
                    ? [new RectInt32(0, 0, captionWidth, captionHeight)]
                    : []);

            var passThrough = _interactiveElements
                .Where(element => element.IsHitTestVisible)
                .Select(element => Bounds(root, element))
                .Where(bounds => bounds.HasValue && bounds.Value.Bottom > 0 && bounds.Value.Top < titleBarHeight)
                .Select(bounds => Scale(bounds!.Value, scale, clientWidth, clientHeight))
                .Where(rect => rect.Width > 0 && rect.Height > 0)
                .ToArray();
            inputSource.SetRegionRects(NonClientRegionKind.Passthrough, passThrough);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update Todo title-bar regions: {exception}");
        }
    }

    private static Rect? Bounds(Grid root, FrameworkElement element)
    {
        try
        {
            var topLeft = element.TransformToVisual(root).TransformPoint(new Point(0, 0));
            return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RectInt32 Scale(Rect bounds, double scale, int clientWidth, int clientHeight)
    {
        var left = Math.Max(0, (int)Math.Floor(bounds.Left * scale));
        var top = Math.Max(0, (int)Math.Floor(bounds.Top * scale));
        var right = Math.Min(clientWidth, (int)Math.Ceiling(bounds.Right * scale));
        var bottom = Math.Min(clientHeight, (int)Math.Ceiling(bounds.Bottom * scale));
        return new RectInt32(left, top, right - left, bottom - top);
    }

    private WindowId WindowId() => Win32Interop.GetWindowIdFromWindow(
        WinRT.Interop.WindowNative.GetWindowHandle(window));
}
