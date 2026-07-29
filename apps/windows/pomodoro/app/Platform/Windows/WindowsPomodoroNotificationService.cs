using Fowan.Reminders.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Fowan.Pomodoro.Windows.Platform.Windows;

internal sealed class WindowsPomodoroNotificationService
{
    public bool TryShowReminder(ReminderDispatchItem item)
    {
        var suffix = item.MissedCount > 0 ? $"（错过 {item.MissedCount} 次）" : string.Empty;
        return TryShow(item.Reminder.TodoTaskId is null ? "Fowan 提醒" : "待办提醒", $"{item.Reminder.Title}{suffix}");
    }

    public bool TryShowDigest(string body) => TryShow("今日待办摘要", body);

    public bool TryShowPhaseCompleted(string phase) => TryShow("番茄钟", phase == "focus" ? "专注阶段已结束，准备休息。" : "休息阶段已结束，准备下一轮专注。" );

    private static bool TryShow(string title, string body)
    {
        try
        {
            AppNotificationManager.Default.Register();
            var notification = new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification();
            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
