using Fowan.Reminders.Models;

namespace Fowan.Pomodoro.Shared.Services;

public sealed record TodoReminderTask(string Id, string Title, DateOnly StartDate, DateOnly? DueDate, bool IsCompleted, bool IsDeleted);

public sealed record DailyDigest(string Title, DateOnly Date, IReadOnlyList<TodoReminderTask> StartTasks, IReadOnlyList<TodoReminderTask> DueTasks)
{
    public bool HasTasks => StartTasks.Count > 0 || DueTasks.Count > 0;
}

public static class DailyDigestBuilder
{
    public static DailyDigest Build(DateOnly date, ReminderSettings settings, IEnumerable<TodoReminderTask> tasks)
    {
        var active = tasks.Where(task => !task.IsCompleted && !task.IsDeleted).ToArray();
        var starts = settings.IsStartDigestEnabled
            ? active.Where(task => task.StartDate == date).OrderBy(task => task.Title, StringComparer.CurrentCulture).ToArray()
            : [];
        var startIds = starts.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        var due = settings.IsDueDigestEnabled
            ? active.Where(task => task.DueDate == date && !startIds.Contains(task.Id)).OrderBy(task => task.Title, StringComparer.CurrentCulture).ToArray()
            : [];
        return new DailyDigest("今日待办提醒", date, starts, due);
    }

    public static bool IsDueAt(DateTimeOffset now, ReminderSettings settings)
    {
        var localTime = now.LocalDateTime.TimeOfDay;
        return (settings.IsStartDigestEnabled && localTime >= settings.StartDigestTime && settings.LastStartDigestDate != DateOnly.FromDateTime(now.LocalDateTime)) ||
               (settings.IsDueDigestEnabled && localTime >= settings.DueDigestTime && settings.LastDueDigestDate != DateOnly.FromDateTime(now.LocalDateTime));
    }
}
