using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyDropPreviewPresenter(
    Window window,
    Func<TodoData> data,
    Func<TodoSettings> settings,
    StickyThemePalette palette,
    Func<TodoTask?> draggedTask,
    Func<Border?> hoveredRow,
    Func<IReadOnlyList<Border>> rows,
    Func<string, TodoTask?> findTask)
{
    private FrameworkElement? _preview;
    private Panel? _previewHost;

    public void Apply(StickyDropTarget? target)
    {
        Remove();
        if (target is null) return;
        var row = RowForTask(target.TaskId);
        if (row is not null) Insert(target, row);
    }

    public bool Contains(Point point)
    {
        if (_preview is null || !_preview.IsVisible) return false;
        try
        {
            var topLeft = _preview.TransformToAncestor(window).Transform(new Point(0, 0));
            return new Rect(topLeft, new Size(_preview.ActualWidth, _preview.ActualHeight)).Contains(point);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Remove()
    {
        if (_previewHost is not null && _preview is not null) _previewHost.Children.Remove(_preview);
        _preview = null;
        _previewHost = null;
    }

    public void ResetRow(Border row)
    {
        row.Opacity = 1;
        if (row.Tag is not string taskId) return;
        var task = findTask(taskId);
        if (task is not null) RefreshRow(row, task, ReferenceEquals(row, hoveredRow()));
    }

    public void RefreshRow(Border row, TodoTask task, bool pointerOver)
    {
        row.Opacity = 1;
        row.Background = pointerOver
            ? palette.TaskHoverBackground(task.IsCompleted)
            : task.IsCompleted ? palette.Panel(0xF5F8F9) : palette.Panel(0xFFFFFF);
        row.BorderBrush = pointerOver
            ? palette.TaskHoverBorder(task.IsCompleted)
            : task.IsCompleted ? Brushes.Transparent : palette.Brush(0xDCE7EA);
        row.BorderThickness = pointerOver || !task.IsCompleted ? new Thickness(1) : new Thickness(0);
    }

    public static bool TargetsEqual(StickyDropTarget? first, StickyDropTarget? second) =>
        first is null || second is null
            ? first is null && second is null
            : first.Placement == second.Placement &&
              string.Equals(first.TaskId, second.TaskId, StringComparison.Ordinal);

    private void Insert(StickyDropTarget target, Border targetRow)
    {
        if (targetRow.Parent is not Panel host || targetRow.Tag is not string taskId) return;
        var targetTask = findTask(taskId);
        if (targetTask is null) return;
        var targetDepth = TodoQuery.TaskIndentDepth(data(), targetTask);
        var previewDepth = target.Placement == StickyDropPlacement.TopLevelEnd
            ? 0
            : target.Placement == StickyDropPlacement.Child
                ? Math.Clamp(targetDepth + 1, 0, TodoQuery.MaxTaskTreeDepth - 1)
                : targetDepth;
        var preview = Create(target, targetTask, previewDepth);
        _preview = preview;
        _previewHost = host;
        host.Children.Insert(InsertIndex(host, targetRow, target, targetDepth), preview);
    }

    private int InsertIndex(Panel host, Border targetRow, StickyDropTarget target, int targetDepth)
    {
        var index = host.Children.IndexOf(targetRow);
        if (index < 0) return host.Children.Count;
        return target.Placement switch
        {
            StickyDropPlacement.TopLevelEnd or StickyDropPlacement.Child => Math.Min(index + 1, host.Children.Count),
            StickyDropPlacement.Before => index,
            _ => IndexAfterSubtree(host, index, targetDepth)
        };
    }

    private int IndexAfterSubtree(Panel host, int startIndex, int targetDepth)
    {
        var insertIndex = startIndex + 1;
        for (var index = startIndex + 1; index < host.Children.Count; index++)
        {
            if (host.Children[index] is not Border row || row.Tag is not string taskId) break;
            var task = findTask(taskId);
            if (task is null || TodoQuery.TaskIndentDepth(data(), task) <= targetDepth) break;
            if (IsDraggedOrDescendant(task)) continue;
            insertIndex = index + 1;
        }
        return insertIndex;
    }

    private bool IsDraggedOrDescendant(TodoTask task)
    {
        var dragged = draggedTask();
        if (dragged is null) return false;
        var current = task;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(current.Id))
        {
            if (string.Equals(current.Id, dragged.Id, StringComparison.Ordinal)) return true;
            if (string.IsNullOrWhiteSpace(current.ParentTaskId)) return false;
            var parent = findTask(current.ParentTaskId);
            if (parent is null) return false;
            current = parent;
        }
        return false;
    }

    private FrameworkElement Create(StickyDropTarget target, TodoTask targetTask, int depth)
    {
        var completed = draggedTask()?.IsCompleted == true;
        var preview = new Grid
        {
            Height = completed ? 34 : 44,
            Margin = new Thickness(depth * 18, 0, 0, completed ? 8 : 10),
            IsHitTestVisible = false
        };
        preview.Children.Add(new Rectangle
        {
            RadiusX = 8, RadiusY = 8, Stroke = palette.Accent, StrokeThickness = 1.6,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = palette.Brush(0xEEF9FA, Math.Clamp(
                settings().StickyOpacity + 0.02, TodoSettings.MinStickyOpacity, 0.86))
        });
        preview.Children.Add(new TextBlock
        {
            Text = PreviewText(target, targetTask), Margin = new Thickness(12, 0, 12, 0),
            FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = palette.AccentDark,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        });
        return preview;
    }

    private string PreviewText(StickyDropTarget target, TodoTask targetTask)
    {
        if (target.Placement == StickyDropPlacement.Child)
            return $"松手后作为“{targetTask.Title}”的子任务";
        if (target.Placement == StickyDropPlacement.TopLevelEnd)
            return "松手后放到顶层任务末尾";
        var parent = string.IsNullOrWhiteSpace(targetTask.ParentTaskId)
            ? null
            : findTask(targetTask.ParentTaskId);
        var parentName = parent?.Title ?? "顶层任务";
        return target.Placement == StickyDropPlacement.Before
            ? $"松手后放在“{parentName}”下，位于目标之前"
            : $"松手后放在“{parentName}”下，位于目标之后";
    }

    private Border? RowForTask(string taskId) => rows().FirstOrDefault(row =>
        row.Tag is string id && string.Equals(id, taskId, StringComparison.Ordinal));
}
