using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fowan.Todo.Windows.Presentation;

internal enum TodoDropPlacement { Before, After, Child, TopLevelEnd }

internal sealed record TodoDropTarget(string TaskId, TodoDropPlacement Placement);

internal sealed record TodoTaskDropEnvironment(
    Func<Grid> Root,
    Func<ScrollViewer> Scroll,
    Func<IReadOnlyList<Button>> Rows,
    Func<FrameworkElement?> ActiveSection,
    Func<FrameworkElement?> CompletedSection,
    Func<TodoData> Data,
    Func<string, TodoTask?> FindTask,
    Func<TodoTask, TodoDropTarget, bool> IsValid);

internal sealed class TodoTaskDropTargetResolver(TodoTaskDropEnvironment environment)
{
    private const double EdgeRatio = 0.28;
    private const double AutoScrollSectionOverlap = 50;

    public TodoDropTarget? Resolve(
        Point point,
        TodoTask draggedTask,
        Button draggedRow,
        FrameworkElement? preview)
    {
        foreach (var row in environment.Rows())
        {
            if (ReferenceEquals(row, draggedRow) || row.Tag is not string taskId ||
                !TryGetBounds(row, out var bounds) || point.Y < bounds.Top || point.Y > bounds.Bottom)
            {
                continue;
            }
            var targetTask = environment.FindTask(taskId);
            if (targetTask is null || targetTask.IsCompleted != draggedTask.IsCompleted) continue;
            var ratio = bounds.Height <= 0 ? 0.5 : (point.Y - bounds.Top) / bounds.Height;
            var placement = ratio < EdgeRatio
                ? TodoDropPlacement.Before
                : ratio > 1 - EdgeRatio ? TodoDropPlacement.After : TodoDropPlacement.Child;
            var target = placement == TodoDropPlacement.After
                ? AfterVisibleRow(row, targetTask, draggedTask, preview) ?? new(taskId, placement)
                : new TodoDropTarget(taskId, placement);
            return environment.IsValid(draggedTask, target) ? target : null;
        }
        return BetweenRows(point, draggedTask, draggedRow, preview);
    }

    public (double Minimum, double Maximum) AutoScrollRange(TodoTask draggedTask)
    {
        var scroll = environment.Scroll();
        var maximum = Math.Max(0, scroll.ScrollableHeight);
        var active = environment.ActiveSection();
        var completed = environment.CompletedSection();
        if (active is null || completed is null || !TryGetBounds(completed, out var completedBounds))
        {
            return (0, maximum);
        }
        var scrollOrigin = scroll.TransformToVisual(environment.Root()).TransformPoint(new Point(0, 0));
        var completedTop = Math.Clamp(completedBounds.Top - scrollOrigin.Y + scroll.VerticalOffset, 0, maximum);
        if (draggedTask.IsCompleted)
        {
            return (Math.Clamp(completedTop - AutoScrollSectionOverlap, 0, maximum), maximum);
        }
        var activeMaximum = Math.Clamp(
            completedTop - scroll.ActualHeight + AutoScrollSectionOverlap,
            0,
            maximum);
        return (0, activeMaximum);
    }

    public bool TryGetBounds(FrameworkElement element, out Rect bounds)
    {
        try
        {
            var topLeft = element.TransformToVisual(environment.Root()).TransformPoint(new Point(0, 0));
            bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (Exception)
        {
            bounds = Rect.Empty;
            return false;
        }
    }

    private TodoDropTarget? BetweenRows(
        Point point,
        TodoTask draggedTask,
        Button draggedRow,
        FrameworkElement? preview)
    {
        var hits = new List<(Button Row, Panel Host, TodoTask Task, Rect Bounds)>();
        foreach (var row in environment.Rows())
        {
            if (row.Tag is not string taskId || row.Parent is not Panel host || !TryGetBounds(row, out var bounds))
            {
                continue;
            }
            var task = environment.FindTask(taskId);
            if (task is not null) hits.Add((row, host, task, bounds));
        }
        foreach (var group in hits.GroupBy(hit => hit.Host))
        {
            var ordered = group.OrderBy(hit => hit.Bounds.Top).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                var upper = ordered[index - 1];
                var lower = ordered[index];
                var gapTop = Math.Min(upper.Bounds.Bottom, lower.Bounds.Top);
                var gapBottom = Math.Max(upper.Bounds.Bottom, lower.Bounds.Top);
                if (gapBottom - gapTop < 2 || point.Y < gapTop || point.Y > gapBottom) continue;
                if (ReferenceEquals(lower.Row, draggedRow))
                {
                    if (lower.Task.IsCompleted != draggedTask.IsCompleted) continue;
                    var beforeDragged = new TodoDropTarget(lower.Task.Id, TodoDropPlacement.Before);
                    return environment.IsValid(draggedTask, beforeDragged) ? beforeDragged : null;
                }
                if (lower.Task.IsCompleted != draggedTask.IsCompleted) continue;
                var target = new TodoDropTarget(lower.Task.Id, TodoDropPlacement.Before);
                return environment.IsValid(draggedTask, target) ? target : null;
            }
            var bottom = AtHostBottom(point, ordered, draggedTask, preview);
            if (bottom is not null) return bottom;
        }
        return null;
    }

    private TodoDropTarget? AtHostBottom(
        Point point,
        IReadOnlyList<(Button Row, Panel Host, TodoTask Task, Rect Bounds)> rows,
        TodoTask draggedTask,
        FrameworkElement? preview)
    {
        if (rows.Count == 0) return null;
        var last = rows[^1];
        if (last.Task.IsCompleted != draggedTask.IsCompleted ||
            !TryBottomBoundary(last.Host, last.Row, last.Bounds, preview, out var boundary) ||
            boundary - last.Bounds.Bottom < 2 || point.Y < last.Bounds.Bottom || point.Y > boundary)
        {
            return null;
        }
        var target = new TodoDropTarget(last.Task.Id, TodoDropPlacement.TopLevelEnd);
        return environment.IsValid(draggedTask, target) ? target : null;
    }

    private bool TryBottomBoundary(
        Panel host,
        UIElement lastRow,
        Rect lastBounds,
        FrameworkElement? preview,
        out double boundary)
    {
        boundary = lastBounds.Bottom;
        var lastIndex = host.Children.IndexOf(lastRow);
        if (TryNextSiblingTop(host, lastIndex, lastBounds.Bottom, preview, out boundary)) return true;
        if (host.Parent is Panel parent)
        {
            var hostIndex = parent.Children.IndexOf(host);
            if (TryNextSiblingTop(parent, hostIndex, lastBounds.Bottom, preview, out boundary)) return true;
        }
        boundary = lastBounds.Bottom + Math.Max(16, lastBounds.Height * EdgeRatio);
        return true;
    }

    private bool TryNextSiblingTop(
        Panel host,
        int startIndex,
        double afterY,
        FrameworkElement? preview,
        out double top)
    {
        top = afterY;
        if (startIndex < 0) return false;
        for (var index = startIndex + 1; index < host.Children.Count; index++)
        {
            if (ReferenceEquals(host.Children[index], preview) ||
                host.Children[index] is not FrameworkElement next || next.Visibility != Visibility.Visible)
            {
                continue;
            }
            if (TryGetBounds(next, out var bounds) && bounds.Top > afterY)
            {
                top = bounds.Top;
                return true;
            }
        }
        return false;
    }

    private TodoDropTarget? AfterVisibleRow(
        Button upperRow,
        TodoTask upperTask,
        TodoTask draggedTask,
        FrameworkElement? preview)
    {
        if (upperRow.Parent is not Panel host) return null;
        var upperIndex = host.Children.IndexOf(upperRow);
        if (upperIndex < 0) return null;
        var upperDepth = TodoQuery.TaskIndentDepth(environment.Data(), upperTask);
        for (var index = upperIndex + 1; index < host.Children.Count; index++)
        {
            if (ReferenceEquals(host.Children[index], preview)) continue;
            if (host.Children[index] is not Button lowerRow || lowerRow.Tag is not string lowerTaskId) continue;
            var lowerTask = environment.FindTask(lowerTaskId);
            if (lowerTask is null) continue;
            if (lowerTask.IsCompleted != draggedTask.IsCompleted) return null;
            return TodoQuery.TaskIndentDepth(environment.Data(), lowerTask) > upperDepth
                ? new TodoDropTarget(lowerTask.Id, TodoDropPlacement.Before)
                : null;
        }
        return null;
    }
}
