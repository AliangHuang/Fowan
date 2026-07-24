using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoTaskDropPreviewPalette(Brush Accent, Brush AccentDark, Brush Fill);

internal sealed class TodoTaskDropPreview(
    TodoTaskDropEnvironment environment,
    TodoTaskDropPreviewPalette palette)
{
    private FrameworkElement? _preview;
    private Panel? _host;

    public FrameworkElement? Element => _preview;

    public void Apply(TodoDropTarget? target, TodoTask draggedTask)
    {
        Remove();
        if (target is null) return;
        var row = environment.Rows().FirstOrDefault(candidate =>
            candidate.Tag is string taskId && string.Equals(taskId, target.TaskId, StringComparison.Ordinal));
        if (row is null || row.Parent is not Panel host || row.Tag is not string targetTaskId) return;
        var targetTask = environment.FindTask(targetTaskId);
        if (targetTask is null) return;
        var targetDepth = TodoQuery.TaskIndentDepth(environment.Data(), targetTask);
        var previewDepth = target.Placement switch
        {
            TodoDropPlacement.TopLevelEnd => 0,
            TodoDropPlacement.Child => Math.Clamp(targetDepth + 1, 0, TodoQuery.MaxTaskTreeDepth - 1),
            _ => targetDepth
        };
        var insertIndex = InsertIndex(host, row, target, targetDepth, draggedTask);
        _preview = Create(target, targetTask, previewDepth, draggedTask.IsCompleted);
        _host = host;
        host.Children.Insert(insertIndex, _preview);
    }

    public bool Contains(Point point, TodoTaskDropTargetResolver resolver) =>
        _preview is not null && _preview.Visibility == Visibility.Visible &&
        resolver.TryGetBounds(_preview, out var bounds) && bounds.Contains(point);

    public void Remove()
    {
        if (_host is not null && _preview is not null) _host.Children.Remove(_preview);
        _preview = null;
        _host = null;
    }

    private int InsertIndex(
        Panel host,
        Button targetRow,
        TodoDropTarget target,
        int targetDepth,
        TodoTask draggedTask)
    {
        var index = host.Children.IndexOf(targetRow);
        if (index < 0) return host.Children.Count;
        if (target.Placement is TodoDropPlacement.TopLevelEnd or TodoDropPlacement.Child)
        {
            return Math.Min(index + 1, host.Children.Count);
        }
        if (target.Placement == TodoDropPlacement.Before) return index;
        var insertIndex = index + 1;
        for (var childIndex = index + 1; childIndex < host.Children.Count; childIndex++)
        {
            if (host.Children[childIndex] is not Button row || row.Tag is not string taskId) break;
            var task = environment.FindTask(taskId);
            if (task is null || TodoQuery.TaskIndentDepth(environment.Data(), task) <= targetDepth) break;
            if (IsDraggedTaskOrDescendant(task, draggedTask)) continue;
            insertIndex = childIndex + 1;
        }
        return insertIndex;
    }

    private bool IsDraggedTaskOrDescendant(TodoTask task, TodoTask draggedTask)
    {
        var current = task;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(current.Id))
        {
            if (string.Equals(current.Id, draggedTask.Id, StringComparison.Ordinal)) return true;
            if (string.IsNullOrWhiteSpace(current.ParentTaskId)) return false;
            var parent = environment.FindTask(current.ParentTaskId);
            if (parent is null) return false;
            current = parent;
        }
        return false;
    }

    private FrameworkElement Create(
        TodoDropTarget target,
        TodoTask targetTask,
        int previewDepth,
        bool completed)
    {
        var preview = new Grid
        {
            Height = completed ? 34 : 44,
            Margin = new Thickness(previewDepth * 22, 0, 0, completed ? 8 : 10),
            IsHitTestVisible = false
        };
        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            RadiusX = 8, RadiusY = 8, Stroke = palette.Accent,
            StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = palette.Fill
        });
        preview.Children.Add(new TextBlock
        {
            Text = PreviewText(target, targetTask), Margin = new Thickness(12, 0, 12, 0),
            FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = palette.AccentDark,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        return preview;
    }

    private string PreviewText(TodoDropTarget target, TodoTask targetTask)
    {
        if (target.Placement == TodoDropPlacement.Child)
        {
            return $"松手后作为“{targetTask.Title}”的子任务";
        }
        if (target.Placement == TodoDropPlacement.TopLevelEnd) return "松手后放到顶层任务末尾";
        var parent = string.IsNullOrWhiteSpace(targetTask.ParentTaskId)
            ? null
            : environment.FindTask(targetTask.ParentTaskId);
        var parentName = parent?.Title ?? "顶层任务";
        return target.Placement == TodoDropPlacement.Before
            ? $"松手后放在“{parentName}”下，位于目标之前"
            : $"松手后放在“{parentName}”下，位于目标之后";
    }
}
