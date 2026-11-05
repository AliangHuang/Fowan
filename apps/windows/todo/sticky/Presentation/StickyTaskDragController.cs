using Fowan.Todo.Shared.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyDragState
{
    public TodoTask? CandidateTask { get; set; }
    public Border? CandidateRow { get; set; }
    public Border? HoveredRow { get; set; }
    public StickyDropTarget? CurrentTarget { get; set; }
    public Point StartPoint { get; set; }
    public Point CurrentPoint { get; set; }
    public bool IsDragging { get; set; }
}

internal sealed class StickyTaskDragController(
    Window window,
    StickyDragState state,
    Func<bool> hasOpenChildWindow,
    Func<IReadOnlyList<Border>> rows,
    Func<ScrollViewer> scroll,
    Func<FrameworkElement> completedSection,
    StickyDropTargetResolver targets,
    StickyDropPreviewPresenter preview,
    Action<TodoTask, StickyDropTarget> applyDrop)
{
    private const int LongPressMilliseconds = 350;
    private const double MoveCancelDistance = 6;
    private const double AutoScrollActivationDistance = 96;
    private const double AutoScrollMinimumSpeed = 120;
    private const double AutoScrollMaximumSpeed = 720;
    private const double AutoScrollSectionOverlap = 50;
    private DispatcherTimer? _dragTimer;
    private DispatcherTimer? _autoScrollTimer;

    public bool IsDragging => state.IsDragging;

    public void Attach(Border row, TodoTask task)
    {
        row.PreviewMouseLeftButtonDown += (_, args) => StartCandidate(row, task, args);
        row.PreviewMouseMove += (_, args) => Update(args);
        row.PreviewMouseLeftButtonUp += (_, args) => Finish(args);
        row.LostMouseCapture += (_, _) =>
        {
            if (ReferenceEquals(state.CandidateRow, row)) Cancel();
        };
    }

    public void SetPointerOver(Border row, TodoTask task, bool pointerOver)
    {
        if (pointerOver) state.HoveredRow = row;
        else if (ReferenceEquals(state.HoveredRow, row)) state.HoveredRow = null;
        preview.RefreshRow(row, task, pointerOver);
    }

    public void Cancel()
    {
        _dragTimer?.Stop();
        _dragTimer = null;
        _autoScrollTimer?.Stop();
        Mouse.OverrideCursor = null;
        preview.Remove();
        foreach (var row in rows()) preview.ResetRow(row);
        if (state.CandidateRow?.IsMouseCaptured == true) state.CandidateRow.ReleaseMouseCapture();
        state.CandidateTask = null;
        state.CandidateRow = null;
        state.CurrentTarget = null;
        state.IsDragging = false;
    }

    public static bool IsIgnoredSource(DependencyObject? source, DependencyObject stopAt)
    {
        while (source is not null && !ReferenceEquals(source, stopAt))
        {
            if (source is ButtonBase or TextBoxBase or Slider or Thumb) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void StartCandidate(Border row, TodoTask task, MouseButtonEventArgs args)
    {
        if (args.ButtonState != MouseButtonState.Pressed || hasOpenChildWindow() ||
            IsIgnoredSource(args.OriginalSource as DependencyObject, row)) return;
        Cancel();
        state.CandidateTask = task;
        state.CandidateRow = row;
        state.StartPoint = args.GetPosition(window);
        state.CurrentPoint = state.StartPoint;
        _dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressMilliseconds) };
        _dragTimer.Tick += (_, _) => Begin();
        _dragTimer.Start();
        row.CaptureMouse();
    }

    private void Update(MouseEventArgs args)
    {
        if (state.CandidateTask is null) return;
        var point = args.GetPosition(window);
        state.CurrentPoint = point;
        if (!state.IsDragging)
        {
            if (Distance(state.StartPoint, point) > MoveCancelDistance) Cancel();
            return;
        }
        UpdateTarget(point);
        args.Handled = true;
    }

    private void Finish(MouseButtonEventArgs args)
    {
        if (state.CandidateTask is null) return;
        var handled = state.IsDragging;
        if (state.IsDragging && state.CurrentTarget is not null)
            applyDrop(state.CandidateTask, state.CurrentTarget);
        Cancel();
        args.Handled = handled;
    }

    private void Begin()
    {
        _dragTimer?.Stop();
        if (state.CandidateTask is null || state.CandidateRow is null ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            Cancel();
            return;
        }
        state.IsDragging = true;
        state.CandidateRow.Opacity = 0.58;
        Mouse.OverrideCursor = Cursors.SizeAll;
        state.CurrentTarget = targets.FromPoint(state.CurrentPoint);
        preview.Apply(state.CurrentTarget);
        StartAutoScroll();
    }

    private void StartAutoScroll()
    {
        _autoScrollTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _autoScrollTimer.Tick -= OnAutoScroll;
        _autoScrollTimer.Tick += OnAutoScroll;
        _autoScrollTimer.Start();
    }

    private void OnAutoScroll(object? sender, EventArgs args)
    {
        var taskScroll = scroll();
        if (!state.IsDragging || taskScroll.ActualHeight <= 0)
        {
            _autoScrollTimer?.Stop();
            return;
        }
        var origin = taskScroll.TranslatePoint(new Point(0, 0), window);
        var top = origin.Y;
        var bottom = top + taskScroll.ActualHeight;
        var distance = state.CurrentPoint.Y < top
            ? state.CurrentPoint.Y - top
            : state.CurrentPoint.Y > bottom ? state.CurrentPoint.Y - bottom : 0;
        if (Math.Abs(distance) < double.Epsilon) return;
        var ratio = Math.Clamp(Math.Abs(distance) / AutoScrollActivationDistance, 0, 1);
        var speed = AutoScrollMinimumSpeed + (AutoScrollMaximumSpeed - AutoScrollMinimumSpeed) * ratio;
        var range = AutoScrollRange(taskScroll);
        var next = Math.Clamp(
            taskScroll.VerticalOffset + Math.Sign(distance) * speed * 0.016,
            range.Minimum,
            range.Maximum);
        if (Math.Abs(next - taskScroll.VerticalOffset) < 0.01) return;
        taskScroll.ScrollToVerticalOffset(next);
        UpdateTarget(state.CurrentPoint);
    }

    private (double Minimum, double Maximum) AutoScrollRange(ScrollViewer taskScroll)
    {
        var maximum = Math.Max(0, taskScroll.ScrollableHeight);
        var completed = completedSection();
        if (state.CandidateTask is null || completed.ActualHeight <= 0) return (0, maximum);
        var scrollOrigin = taskScroll.TranslatePoint(new Point(0, 0), window);
        var completedOrigin = completed.TranslatePoint(new Point(0, 0), window);
        var completedTop = Math.Clamp(
            completedOrigin.Y - scrollOrigin.Y + taskScroll.VerticalOffset,
            0,
            maximum);
        if (state.CandidateTask.IsCompleted)
            return (Math.Clamp(completedTop - AutoScrollSectionOverlap, 0, maximum), maximum);
        var activeMaximum = Math.Clamp(
            completedTop - taskScroll.ActualHeight + AutoScrollSectionOverlap,
            0,
            maximum);
        return (0, activeMaximum);
    }

    private void UpdateTarget(Point point)
    {
        var next = targets.FromPoint(point);
        if (next is null && state.CurrentTarget is not null && preview.Contains(point))
            next = state.CurrentTarget;
        if (StickyDropPreviewPresenter.TargetsEqual(state.CurrentTarget, next)) return;
        state.CurrentTarget = next;
        preview.Apply(next);
    }

    private static double Distance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
