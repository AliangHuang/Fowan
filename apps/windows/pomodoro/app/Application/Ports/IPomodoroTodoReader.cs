using Fowan.Pomodoro.Shared.Services;
using Fowan.Pomodoro.Shared.Application.Ports;

namespace Fowan.Pomodoro.Windows.Application.Ports;

internal interface IPomodoroTodoReader : ITodoIntegration
{
    bool IsActive(string taskId);
    TodoReminderTask CreateForReminder(string title);
}
