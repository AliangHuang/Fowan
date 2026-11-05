using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;

namespace Fowan.Todo.Sticky.Windows.Coordination;

internal sealed class StickyTaskCommandCoordinator(
    TodoWorkspace workspace,
    Action refresh,
    Action<string, string, string, Action> confirm)
{
    public bool Add(string title, TodoTask? parent = null, string? notes = null) =>
        Add(
            title,
            TodoQuery.DefaultListId(workspace.Data),
            isImportant: false,
            DateTime.Today,
            dueDate: null,
            parent,
            notes);

    public bool Add(
        string title,
        string listId,
        bool isImportant,
        DateTime startDate,
        DateTime? dueDate,
        TodoTask? parent = null,
        string? notes = null,
        TodoRecurrenceRule? recurrence = null)
    {
        var normalized = title.Trim();
        if (normalized.Length == 0 || (parent is not null && !TodoQuery.CanAddChild(workspace.Data, parent)))
            return false;
        workspace.AddTask(
            normalized,
            parent?.ListId ?? listId,
            parent?.IsImportant ?? isImportant,
            parent?.StartDate.Date ?? startDate,
            parent?.DueDate ?? dueDate,
            parent?.Id,
            notes,
            recurrence);
        refresh();
        return true;
    }

    public bool SaveDetails(string taskId, string title, string notes)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var task = Find(taskId);
        if (task is null || task.DeletedAt is not null) return false;
        if (workspace.UpdateTaskDetails(taskId, title, notes)) refresh();
        return true;
    }

    public void ToggleCompleted(TodoTask task)
    {
        var completed = !task.IsCompleted;
        if (!completed || !workspace.HasIncompleteDescendants(task.Id))
        {
            SetCompleted(task.Id, completed, includeDescendants: false);
            return;
        }
        confirm(
            "完成当前任务",
            "是否完成当前任务？仍有未完成的子任务。",
            "确认完成",
            () => SetCompleted(task.Id, true, includeDescendants: true));
    }

    public void ToggleImportant(string taskId)
    {
        if (workspace.ToggleTaskImportant(taskId)) refresh();
    }

    public void Delete(TodoTask task)
    {
        var descendants = TodoQuery.DescendantIds(workspace.Data, task.Id).Count();
        if (descendants == 0)
        {
            DeleteTree(task.Id);
            return;
        }
        var target = workspace.Settings.IsRecycleBinEnabled ? "移动到回收站" : "永久删除";
        confirm(
            "删除任务树",
            $"当前任务包含 {descendants} 个子任务或孙任务。确认后将{target}当前任务及全部后代。",
            "确认删除",
            () => DeleteTree(task.Id));
    }

    private void SetCompleted(string taskId, bool completed, bool includeDescendants)
    {
        if (workspace.SetTaskCompleted(taskId, completed, includeDescendants) > 0) refresh();
    }

    private void DeleteTree(string taskId)
    {
        if (workspace.DeleteTaskTree(taskId)) refresh();
    }

    private TodoTask? Find(string taskId) => workspace.Data.Tasks.FirstOrDefault(task =>
        string.Equals(task.Id, taskId, StringComparison.Ordinal));
}
