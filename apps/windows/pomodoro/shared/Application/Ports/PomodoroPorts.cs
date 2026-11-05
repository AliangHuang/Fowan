using Fowan.Pomodoro.Shared.Services;

namespace Fowan.Pomodoro.Shared.Application.Ports;

/// <summary>Read/write boundary for the Todo integration; Todo JSON remains outside Pomodoro state.</summary>
public interface ITodoIntegration
{
    IReadOnlyList<TodoReminderTask> ActiveTasks();
    bool IsTaskActive(string taskId);
    TodoReminderTask CreateTaskForReminder(string title);
}

/// <summary>Constrained handoff used by Pomodoro to register only its next due time with Toolbox.</summary>
public interface IToolWakeSchedulerClient
{
    void Register(DateTimeOffset? nextWakeAt);
}

public sealed class LocalPomodoroWakeSchedulerClient(string? rootPath = null) : IToolWakeSchedulerClient
{
    public void Register(DateTimeOffset? nextWakeAt) => PomodoroWakeSchedule.Write(nextWakeAt, rootPath);
}
