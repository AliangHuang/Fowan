using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoTaskCommandCoordinator(
    ITodoCommands commands,
    Func<TodoSnapshot> snapshot,
    Func<XamlRoot?> xamlRoot,
    Func<ElementTheme> theme,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Action<string?> selectTask,
    Action refresh,
    Action refreshAfterMutation,
    Action refreshDetail)
{
    public async Task ToggleCompletedAsync(TodoTask task)
    {
        var completed = !task.IsCompleted;
        if (!completed || !commands.HasIncompleteDescendants(task.Id))
        {
            SetCompleted(task, completed, includeDescendants: false);
            return;
        }
        if (!await ConfirmCompleteWithChildrenAsync()) return;
        SetCompleted(task, true, includeDescendants: true);
    }

    public void ToggleImportant(TodoTask task)
    {
        if (commands.ToggleTaskImportant(task.Id)) refresh();
    }

    public void RestoreCompleted(TodoTask task) => SetCompleted(task, false, includeDescendants: false);

    public void UpdateTitle(TodoTask task, string title)
    {
        var normalized = title.Trim();
        if (normalized.Length == 0 || string.Equals(task.Title, normalized, StringComparison.Ordinal))
        {
            refreshDetail();
            return;
        }
        if (commands.UpdateTaskTitle(task.Id, normalized)) refreshAfterMutation();
    }

    public async Task DeleteAsync(TodoTask task)
    {
        var descendantCount = TodoQuery.DescendantIds(snapshot().ToQueryData(), task.Id).Count();
        if (descendantCount > 0 && !await ConfirmDeleteTreeAsync(descendantCount)) return;
        if (!commands.DeleteTaskTree(task.Id)) return;
        selectTask(null);
        refreshAfterMutation();
    }

    public void RestoreTree(TodoTask task)
    {
        if (!commands.RestoreTaskTree(task.Id)) return;
        selectTask(null);
        refreshAfterMutation();
    }

    public async Task PermanentlyDeleteTreeAsync(TodoTask task)
    {
        var dialog = Dialog(
            "永久删除任务",
            "此操作无法恢复所选任务及其全部后代。",
            "永久删除");
        if (await showDialog(dialog) != ContentDialogResult.Primary) return;
        if (!commands.PermanentlyDeleteTaskTree(task.Id)) return;
        selectTask(null);
        refreshAfterMutation();
    }

    private void SetCompleted(TodoTask task, bool completed, bool includeDescendants)
    {
        if (task.IsCompleted == completed) return;
        commands.SetTaskCompleted(task.Id, completed, includeDescendants);
        selectTask(task.Id);
        refresh();
    }

    private async Task<bool> ConfirmCompleteWithChildrenAsync() =>
        await showDialog(Dialog(
            "完成当前任务？",
            "是否完成当前任务，仍有未完成的子任务",
            "确认完成")) == ContentDialogResult.Primary;

    private async Task<bool> ConfirmDeleteTreeAsync(int descendantCount)
    {
        var target = snapshot().Settings.IsRecycleBinEnabled ? "移动到回收站" : "永久删除";
        return await showDialog(Dialog(
            "删除任务树",
            $"当前任务包含 {descendantCount} 个子任务或孙任务。确认后将{target}当前任务及全部后代。",
            "确认删除")) == ContentDialogResult.Primary;
    }

    private ContentDialog Dialog(string title, string content, string primaryButton) => new()
    {
        XamlRoot = xamlRoot(),
        RequestedTheme = theme(),
        Title = title,
        Content = content,
        PrimaryButtonText = primaryButton,
        CloseButtonText = "取消",
        DefaultButton = ContentDialogButton.Close
    };
}
