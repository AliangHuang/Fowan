using Fowan.Todo.Shared.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoTaskDragController
{
    private const int LongPressMilliseconds = 350;
    private const double MoveCancelDistance = 6;
    private const double AutoScrollActivationDistance = 96;
    private const double AutoScrollMinimumSpeed = 120;
    private const double AutoScrollMaximumSpeed = 720;

    private readonly TodoTaskDropEnvironment _environment;
    private readonly TodoTaskDropTargetResolver _resolver;
    private readonly TodoTaskDropPreview _preview;
    private readonly Action<TodoTask, TodoDropTarget> _applyDrop;
    private DispatcherQueueTimer? _longPressTimer;
    private DispatcherQueueTimer? _autoScrollTimer;
    private string? _candidateTaskId;
    private Button? _candidateRow;
    private TodoDropTarget? _currentTarget;
    private Point _startPoint;
    private Point _currentPoint;
    private bool _dragging;
    private string? _suppressedClickId;

    public TodoTaskDragController(
        TodoTaskDropEnvironment environment,
        TodoTaskDropPreviewPalette palette,
        Action<TodoTask, TodoDropTarget> applyDrop)
    {
        _environment = environment;
        _resolver = new TodoTaskDropTargetResolver(environment);
        _preview = new TodoTaskDropPreview(environment, palette);
        _applyDrop = applyDrop;
    }

    public void Attach(Button row, TodoTask task)
    {
        row.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, args) => StartCandidate(row, task, args)), true);
        row.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler((_, args) => Update(args)), true);
        row.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, args) => Finish(args)), true);
        row.PointerCaptureLost += (_, _) =>
        {
            if (!ReferenceEquals(_candidateRow, row)) return;
            if (_dragging) Complete(applyDrop: true); else Cancel();
        };
    }

    public bool TryConsumeSuppressedClick(string taskId)
    {
        if (!string.Equals(_suppressedClickId, taskId, StringComparison.Ordinal)) return false;
        _suppressedClickId = null;
        return true;
    }

    public void Cancel()
    {
        _longPressTimer?.Stop();
        _longPressTimer = null;
        StopAutoScroll();
        _preview.Remove();
        foreach (var row in _environment.Rows()) row.Opacity = 1;
        var captured = _candidateRow;
        _candidateTaskId = null;
        _candidateRow = null;
        _currentTarget = null;
        _dragging = false;
        captured?.ReleasePointerCaptures();
    }

    private void StartCandidate(Button row, TodoTask task, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_environment.Root());
        if (!point.Properties.IsLeftButtonPressed ||
            IsIgnoredSource(args.OriginalSource as DependencyObject, row)) return;
        Cancel();
        _candidateTaskId = task.Id;
        _candidateRow = row;
        _startPoint = point.Position;
        _currentPoint = _startPoint;
        _longPressTimer = _environment.Root().DispatcherQueue.CreateTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressMilliseconds);
        _longPressTimer.Tick += (_, _) => Begin();
        _longPressTimer.Start();
        row.CapturePointer(args.Pointer);
    }

    private void Update(PointerRoutedEventArgs args)
    {
        if (CandidateTask() is null || _candidateRow is null) return;
        var point = args.GetCurrentPoint(_environment.Root()).Position;
        _currentPoint = point;
        if (!_dragging)
        {
            if (Distance(_startPoint, point) > MoveCancelDistance) Cancel();
            return;
        }
        RefreshTarget(point);
        args.Handled = true;
    }

    private void Finish(PointerRoutedEventArgs args)
    {
        if (CandidateTask() is null) return;
        var handled = _dragging;
        Complete(applyDrop: true);
        args.Handled = handled;
    }

    private void Complete(bool applyDrop)
    {
        var task = CandidateTask();
        if (task is null) return;
        var wasDragging = _dragging;
        var target = _currentTarget;
        if (wasDragging) SuppressClick(task.Id);
        Cancel();
        if (applyDrop && wasDragging && target is not null) _applyDrop(task, target);
    }

    private void Begin()
    {
        _longPressTimer?.Stop();
        if (CandidateTask() is null || _candidateRow is null)
        {
            Cancel();
            return;
        }
        _dragging = true;
        _candidateRow.Opacity = 0.58;
        RefreshTarget(_currentPoint);
        StartAutoScroll();
    }

    private void RefreshTarget(Point point)
    {
        var task = CandidateTask();
        if (task is null || _candidateRow is null) return;
        var next = _resolver.Resolve(point, task, _candidateRow, _preview.Element);
        if (next is null && _currentTarget is not null && _preview.Contains(point, _resolver))
        {
            next = _currentTarget;
        }
        if (TargetsEqual(_currentTarget, next)) return;
        _currentTarget = next;
        _preview.Apply(_currentTarget, task);
    }

    private void StartAutoScroll()
    {
        _autoScrollTimer ??= _environment.Root().DispatcherQueue.CreateTimer();
        _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _autoScrollTimer.Tick -= OnAutoScrollTick;
        _autoScrollTimer.Tick += OnAutoScrollTick;
        _autoScrollTimer.Start();
    }

    private void StopAutoScroll() => _autoScrollTimer?.Stop();

    private void OnAutoScrollTick(DispatcherQueueTimer sender, object args)
    {
        var scroll = _environment.Scroll();
        var task = CandidateTask();
        if (!_dragging || task is null || scroll.ActualHeight <= 0)
        {
            StopAutoScroll();
            return;
        }
        var origin = scroll.TransformToVisual(_environment.Root()).TransformPoint(new Point(0, 0));
        var top = origin.Y;
        var bottom = top + scroll.ActualHeight;
        var distance = _currentPoint.Y < top
            ? _currentPoint.Y - top
            : _currentPoint.Y > bottom ? _currentPoint.Y - bottom : 0;
        if (Math.Abs(distance) < double.Epsilon) return;
        var ratio = Math.Clamp(Math.Abs(distance) / AutoScrollActivationDistance, 0, 1);
        var speed = AutoScrollMinimumSpeed + (AutoScrollMaximumSpeed - AutoScrollMinimumSpeed) * ratio;
        var range = _resolver.AutoScrollRange(task);
        var nextOffset = Math.Clamp(
            scroll.VerticalOffset + Math.Sign(distance) * speed * 0.016,
            range.Minimum,
            range.Maximum);
        if (Math.Abs(nextOffset - scroll.VerticalOffset) < 0.01) return;
        scroll.ChangeView(null, nextOffset, null, disableAnimation: true);
        RefreshTarget(_currentPoint);
    }

    private void SuppressClick(string taskId)
    {
        _suppressedClickId = taskId;
        _environment.Root().DispatcherQueue.TryEnqueue(() =>
        {
            if (string.Equals(_suppressedClickId, taskId, StringComparison.Ordinal)) _suppressedClickId = null;
        });
    }

    private TodoTask? CandidateTask() => _candidateTaskId is null ? null : _environment.FindTask(_candidateTaskId);

    private static bool IsIgnoredSource(DependencyObject? source, DependencyObject stopAt)
    {
        while (source is not null && !ReferenceEquals(source, stopAt))
        {
            if (source is ButtonBase or TextBox or Slider or Thumb) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static bool TargetsEqual(TodoDropTarget? first, TodoDropTarget? second) =>
        first is null || second is null
            ? first is null && second is null
            : first.Placement == second.Placement &&
              string.Equals(first.TaskId, second.TaskId, StringComparison.Ordinal);

    private static double Distance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
