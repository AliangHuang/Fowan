using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Sticky.Windows.Coordination;
using Fowan.Todo.Sticky.Windows.Presentation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Fowan.Todo.Sticky.Windows.Coordination;

internal sealed class StickyTaskInteractionController(
    Window window,
    TodoWorkspace workspace,
    StickyTaskCommandCoordinator commands,
    Func<TodoData> data,
    Func<TextBox> addBox,
    Func<TextBlock> addPlaceholder,
    Action refreshAll)
{
    public bool ApplyDrop(TodoTask draggedTask, StickyDropTarget target)
    {
        if (!workspace.TryApplyTaskDrop(draggedTask.Id, target.TaskId, SharedPlacement(target.Placement))) return false;
        refreshAll();
        return true;
    }

    public bool IsValidDrop(TodoTask draggedTask, StickyDropTarget target) =>
        workspace.CanApplyTaskDrop(draggedTask.Id, target.TaskId, SharedPlacement(target.Placement));

    public TodoTask? FindTask(string taskId) =>
        data().Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

    public bool TryGetElementBounds(FrameworkElement element, out Rect bounds)
    {
        try
        {
            var topLeft = element.TransformToAncestor(window).Transform(new Point(0, 0));
            bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            bounds = Rect.Empty;
            return false;
        }
    }

    public bool AddFromInput()
    {
        var input = addBox();
        var title = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(title)) return false;
        Add(title);
        input.Text = string.Empty;
        return true;
    }

    public bool Add(string title, TodoTask? parent = null, string? notes = null) =>
        commands.Add(title, parent, notes);

    public void FocusInlineAdd()
    {
        var input = addBox();
        input.Focus();
        Keyboard.Focus(input);
        input.CaretIndex = input.Text.Length;
    }

    public void UpdateAddPlaceholder()
    {
        addPlaceholder().Visibility = string.IsNullOrWhiteSpace(addBox().Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static TodoTaskDropPlacement SharedPlacement(StickyDropPlacement placement) => placement switch
    {
        StickyDropPlacement.Before => TodoTaskDropPlacement.Before,
        StickyDropPlacement.Child => TodoTaskDropPlacement.Child,
        StickyDropPlacement.After => TodoTaskDropPlacement.After,
        _ => TodoTaskDropPlacement.TopLevelEnd
    };
}
