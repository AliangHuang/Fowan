using System.Diagnostics;
using Fowan.Reminders.Models;
using Fowan.Reminders.Services;

namespace Fowan.Todo.Windows.Platform.Windows;

internal sealed class WindowsTodoReminderCoordinator
{
    private readonly ReminderService _reminders = ReminderService.CreateDefault();

    public string Summary(string taskId)
    {
        var reminder = _reminders.List().FirstOrDefault(item => string.Equals(item.TodoTaskId, taskId, StringComparison.Ordinal));
        if (reminder is null) return "未设置提醒";
        if (!reminder.IsEnabled || reminder.SuspensionReason != ReminderSuspensionReason.None)
            return $"已暂停：{SuspensionText(reminder.SuspensionReason)}";
        var time = reminder.SnoozedUntil ?? reminder.FirstOccursAt;
        return $"{time.LocalDateTime:g} · {(reminder.Recurrence is null ? "不重复" : reminder.Recurrence.Frequency == "weekly" ? "每周" : "每月")}";
    }

    public bool OpenEditor()
    {
        var executable = ResolvePomodoro();
        if (executable is null) return false;
        Process.Start(new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = true });
        return true;
    }

    private static string? ResolvePomodoro()
    {
#if FOWAN_DEVELOPMENT_RUNTIME
        const string executableName = "Fowan.Pomodoro.Windows.Dev.exe";
#else
        const string executableName = "Fowan.Pomodoro.Windows.exe";
#endif
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, executableName),
            Path.Combine(AppContext.BaseDirectory, "..", "Pomodoro", executableName),
            Path.Combine(AppContext.BaseDirectory, "Tools", "Pomodoro", executableName)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static string SuspensionText(ReminderSuspensionReason reason) => reason switch
    {
        ReminderSuspensionReason.TaskCompleted => "任务已完成",
        ReminderSuspensionReason.TaskDeleted => "任务已删除",
        ReminderSuspensionReason.TaskPermanentlyDeleted => "任务已永久删除",
        ReminderSuspensionReason.Manual => "手动关闭",
        _ => "已暂停"
    };
}
