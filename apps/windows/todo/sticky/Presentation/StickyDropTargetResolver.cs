using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Windows;
using System.Windows.Controls;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal enum StickyDropPlacement { Before, After, Child, TopLevelEnd }

internal sealed record StickyDropTarget(string TaskId, StickyDropPlacement Placement);

internal delegate bool TryGetStickyBounds(FrameworkElement element, out Rect bounds);

internal sealed class StickyDropTargetResolver(
    Func<TodoTask?> draggedTask,
    Func<Border?> draggedRow,
    Func<IReadOnlyList<Border>> rows,
    Func<Panel> activeTasks,
    Func<FrameworkElement> taskDivider,
    Func<TodoData> data,
    Func<string, TodoTask?> findTask,
    Func<TodoTask, StickyDropTarget, bool> isValidDrop,
    TryGetStickyBounds tryGetBounds,
    double edgeRatio)
{
    public StickyDropTarget? FromPoint(Point point)
    {
        var dragged = draggedTask();
        if (dragged is null) return null;
        foreach (var row in rows())
        {
            if (ReferenceEquals(row, draggedRow()) || row.Tag is not string taskId ||
                !tryGetBounds(row, out var bounds) || point.Y < bounds.Top || point.Y > bounds.Bottom)
                continue;
            var targetTask = findTask(taskId);
            if (targetTask is null || targetTask.IsCompleted != dragged.IsCompleted) continue;
            var ratio = bounds.Height <= 0 ? 0.5 : (point.Y - bounds.Top) / bounds.Height;
            var placement = ratio < edgeRatio
                ? StickyDropPlacement.Before
                : ratio > 1 - edgeRatio ? StickyDropPlacement.After : StickyDropPlacement.Child;
            var target = placement == StickyDropPlacement.After
                ? AfterVisibleRow(row, targetTask) ?? new StickyDropTarget(taskId, placement)
                : new StickyDropTarget(taskId, placement);
            return isValidDrop(dragged, target) ? target : null;
        }
        return BetweenRows(point);
    }

    private StickyDropTarget? BetweenRows(Point point)
    {
        var dragged = draggedTask();
        if (dragged is null) return null;
        var hits = new List<(Border Row, Panel Host, TodoTask Task, Rect Bounds)>();
        foreach (var row in rows())
        {
            if (row.Tag is not string taskId || row.Parent is not Panel host ||
                !tryGetBounds(row, out var bounds)) continue;
            var task = findTask(taskId);
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
                if (ReferenceEquals(lower.Row, draggedRow()))
                {
                    if (lower.Task.IsCompleted != dragged.IsCompleted) continue;
                    return Valid(dragged, new StickyDropTarget(lower.Task.Id, StickyDropPlacement.Before));
                }
                if (lower.Task.IsCompleted != dragged.IsCompleted) continue;
                return Valid(dragged, new StickyDropTarget(lower.Task.Id, StickyDropPlacement.Before));
            }
            var bottom = AtHostBottom(point, ordered);
            if (bottom is not null) return bottom;
        }
        return null;
    }

    private StickyDropTarget? AtHostBottom(
        Point point,
        IReadOnlyList<(Border Row, Panel Host, TodoTask Task, Rect Bounds)> ordered)
    {
        var dragged = draggedTask();
        if (dragged is null || ordered.Count == 0) return null;
        var last = ordered[^1];
        if (last.Task.IsCompleted != dragged.IsCompleted ||
            !TryBottomBoundary(last.Host, last.Bounds, out var boundary) ||
            boundary - last.Bounds.Bottom < 2 || point.Y < last.Bounds.Bottom || point.Y > boundary)
            return null;
        return Valid(dragged, new StickyDropTarget(last.Task.Id, StickyDropPlacement.TopLevelEnd));
    }

    private bool TryBottomBoundary(Panel host, Rect lastBounds, out double boundary)
    {
        boundary = lastBounds.Bottom;
        if (ReferenceEquals(host, activeTasks()) &&
            tryGetBounds(taskDivider(), out var divider) && divider.Top > lastBounds.Bottom)
        {
            boundary = divider.Top;
            return true;
        }
        if (host.Parent is Panel parent)
        {
            var hostIndex = parent.Children.IndexOf(host);
            for (var index = hostIndex + 1; index < parent.Children.Count; index++)
            {
                if (parent.Children[index] is not FrameworkElement next || !next.IsVisible) continue;
                if (tryGetBounds(next, out var nextBounds) && nextBounds.Top > lastBounds.Bottom)
                {
                    boundary = nextBounds.Top;
                    return true;
                }
            }
        }
        boundary = lastBounds.Bottom + Math.Max(16, lastBounds.Height * edgeRatio);
        return true;
    }

    private StickyDropTarget? AfterVisibleRow(Border upperRow, TodoTask upperTask)
    {
        var dragged = draggedTask();
        if (upperRow.Parent is not Panel host || dragged is null) return null;
        var upperIndex = host.Children.IndexOf(upperRow);
        if (upperIndex < 0) return null;
        var upperDepth = TodoQuery.TaskIndentDepth(data(), upperTask);
        for (var index = upperIndex + 1; index < host.Children.Count; index++)
        {
            if (host.Children[index] is not Border lowerRow || lowerRow.Tag is not string id) continue;
            var lowerTask = findTask(id);
            if (lowerTask is null) continue;
            if (lowerTask.IsCompleted != dragged.IsCompleted) return null;
            return TodoQuery.TaskIndentDepth(data(), lowerTask) > upperDepth
                ? new StickyDropTarget(lowerTask.Id, StickyDropPlacement.Before)
                : null;
        }
        return null;
    }

    private StickyDropTarget? Valid(TodoTask dragged, StickyDropTarget target) =>
        isValidDrop(dragged, target) ? target : null;
}
