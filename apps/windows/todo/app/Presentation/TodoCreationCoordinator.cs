using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoCreationCoordinator(
    ITodoCommands commands,
    Func<TodoSnapshot> snapshot,
    TodoDialogService dialogs,
    Func<string> currentView,
    Action<string> setView,
    Action<string?> selectTask,
    Func<IEnumerable<TodoList>> orderedLists,
    Func<string> defaultListForNewTask,
    Func<string, bool> isDefaultList,
    Func<string, IEnumerable<TodoTask>> tasksForList,
    Func<string, TodoTask?> firstTaskForSelection,
    Func<Brush> secondaryText,
    Action refreshAfterMutation)
{
    public async Task AddFromInputAsync(TextBox input)
    {
        var title = input.Text.Trim();
        if (title.Length == 0)
        {
            await ShowAddTaskAsync();
            return;
        }
        Add(title, defaultListForNewTask(), currentView() == TodoViewIds.Important, DateTime.Today, null);
        input.Text = string.Empty;
    }

    public async Task ShowAddTaskAsync()
    {
        var draft = await dialogs.ShowAddTaskAsync(
            orderedLists(), defaultListForNewTask(), currentView() == TodoViewIds.Important);
        if (draft is not null) Add(draft);
    }

    public async Task ShowAddSubtaskAsync(TodoTask parent)
    {
        if (!TodoQuery.CanAddChild(snapshot().ToQueryData(), parent)) return;
        var draft = await dialogs.ShowAddSubtaskAsync(parent, secondaryText());
        if (draft is not null) Add(draft);
    }

    public async Task ShowAddListAsync()
    {
        var name = await dialogs.ShowListNameAsync("新建清单", "创建");
        if (name is null) return;
        var list = commands.AddList(name);
        setView(TodoViewIds.List(list.Id));
        selectTask(null);
        refreshAfterMutation();
    }

    public async Task ShowRenameListAsync(TodoList list)
    {
        var name = await dialogs.ShowListNameAsync("重命名清单", "保存", list.Name);
        if (name is not null && commands.RenameList(list.Id, name)) refreshAfterMutation();
    }

    public async Task ShowDeleteListAsync(TodoList list)
    {
        if (isDefaultList(list.Id) || snapshot().Lists.Length <= 1)
        {
            await dialogs.ShowDefaultListDeleteBlockedAsync();
            return;
        }
        if (!await dialogs.ConfirmDeleteListAsync(list, tasksForList(list.Id).Count())) return;
        if (!commands.DeleteList(list.Id)) return;
        if (string.Equals(currentView(), TodoViewIds.List(list.Id), StringComparison.Ordinal))
        {
            setView(TodoViewIds.Today);
            selectTask(firstTaskForSelection(TodoViewIds.Today)?.Id);
        }
        refreshAfterMutation();
    }

    private void Add(TodoTaskDraft draft) => Add(
        draft.Title, draft.ListId, draft.IsImportant, draft.StartDate,
        draft.DueDate, draft.ParentTaskId, draft.Notes);

    private void Add(
        string title,
        string listId,
        bool important,
        DateTime startDate,
        DateTime? dueDate,
        string? parentTaskId = null,
        string? notes = null)
    {
        var task = commands.AddTask(title, listId, important, startDate, dueDate, parentTaskId, notes);
        selectTask(task.Id);
        if (currentView() == TodoViewIds.Completed) setView(TodoViewIds.All);
        refreshAfterMutation();
    }
}
