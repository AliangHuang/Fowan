using Fowan.Pomodoro.Shared.Services;
using Fowan.Pomodoro.Windows.Application.Ports;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Shared.Application;

namespace Fowan.Pomodoro.Windows.Platform.Windows;

internal sealed class WindowsPomodoroTodoReader : IPomodoroTodoReader
{
    public IReadOnlyList<TodoReminderTask> ActiveTasks() => new TodoStore().LoadData().Tasks
        .Where(task => !task.IsCompleted && task.DeletedAt is null)
        .OrderBy(task => task.StartDate)
        .ThenBy(task => task.Title, StringComparer.CurrentCulture)
        .Select(task => new TodoReminderTask(task.Id, task.Title, DateOnly.FromDateTime(task.StartDate),
            task.DueDate is null ? null : DateOnly.FromDateTime(task.DueDate.Value), task.IsCompleted, task.DeletedAt is not null))
        .ToArray();

    public bool IsActive(string taskId)
    {
        var task = new TodoStore().LoadData().Tasks.FirstOrDefault(item => string.Equals(item.Id, taskId, StringComparison.Ordinal));
        return task is not null && !task.IsCompleted && task.DeletedAt is null;
    }

    public bool IsTaskActive(string taskId) => IsActive(taskId);

    public TodoReminderTask CreateForReminder(string title)
    {
        var workspace = TodoWorkspace.CreateDefault();
        var created = workspace.AddTask(title, workspace.DefaultListId, false, DateTime.Today, null);
        return new TodoReminderTask(created.Id, created.Title, DateOnly.FromDateTime(created.StartDate),
            created.DueDate is null ? null : DateOnly.FromDateTime(created.DueDate.Value), false, false);
    }

    public TodoReminderTask CreateTaskForReminder(string title) => CreateForReminder(title);
}
