using Fowan.Pomodoro.Models;
using Fowan.Pomodoro.Services;
using Fowan.Pomodoro.Shared.Application;
using Fowan.Pomodoro.Shared.Application.Ports;
using Fowan.Pomodoro.Shared.Services;
using Fowan.Reminders.Models;
using Fowan.Reminders.Services;
using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Xunit;

namespace Fowan.Pomodoro.Shared.Tests;

public sealed class PomodoroSharedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Fowan.Pomodoro.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CompletedFocusAdvancesToShortBreakAndRecordsSession()
    {
        var workspace = CreateWorkspace();
        workspace.UpdateSettings(new PomodoroSettings { FocusMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, FocusesPerCycle = 4 });
        var start = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.FromHours(8));

        workspace.StartFocus("task-1", "撰写方案", start);
        Assert.True(workspace.AdvanceIfDue(start.AddMinutes(25)));

        Assert.Equal(PomodoroRunState.AwaitingNextPhase, workspace.State.Phase.State);
        Assert.Equal(PomodoroPhaseIds.ShortBreak, workspace.State.Phase.Phase);
        Assert.Single(workspace.State.Sessions);
        Assert.True(workspace.State.Sessions[0].IsCompleted);
        Assert.Equal(1, workspace.State.Phase.CompletedFocusesInCycle);
    }

    [Fact]
    public void PauseResumeAndStopPreserveRemainingAndDoNotCompleteCycle()
    {
        var workspace = CreateWorkspace();
        var start = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.FromHours(8));
        workspace.StartFocus(now: start);
        workspace.Pause(start.AddMinutes(10));
        Assert.Equal(PomodoroRunState.Paused, workspace.State.Phase.State);
        Assert.Equal(TimeSpan.FromMinutes(15), workspace.State.Phase.Remaining);

        workspace.Resume(start.AddHours(2));
        Assert.Equal(PomodoroRunState.Running, workspace.State.Phase.State);
        workspace.Stop(start.AddHours(2).AddMinutes(5));

        Assert.Equal(PomodoroRunState.Idle, workspace.State.Phase.State);
        Assert.Equal(0, workspace.State.Phase.CompletedFocusesInCycle);
        Assert.False(workspace.State.Sessions.Single().IsCompleted);
    }

    [Fact]
    public void RestartAndSleepRecoverySettleOneOverduePhase()
    {
        var start = DateTimeOffset.Now.AddHours(-1);
        var workspace = CreateWorkspace();
        workspace.StartFocus(now: start);

        var restored = CreateWorkspace();
        Assert.Equal(PomodoroRunState.AwaitingNextPhase, restored.State.Phase.State);
        Assert.Equal(PomodoroPhaseIds.ShortBreak, restored.State.Phase.Phase);
        Assert.Single(restored.State.Sessions);
        Assert.False(restored.AdvanceIfDue(start.AddHours(3)));
    }

    [Fact]
    public void WorkspaceRegistersWakeInItsConfiguredPomodoroRoot()
    {
        var workspace = CreateWorkspace();
        var start = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.FromHours(8));

        workspace.StartFocus(now: start);

        Assert.Equal(start.AddMinutes(25), PomodoroWakeSchedule.Read(PomodoroRoot));
    }

    [Fact]
    public async Task WakeScheduleSerializesConcurrentWrites()
    {
        var root = Path.Combine(_root, "wake-schedule");
        var wakes = Enumerable.Range(1, 12)
            .Select(offset => new DateTimeOffset(2026, 7, 29, 9, offset, 0, TimeSpan.FromHours(8)))
            .ToArray();

        await Task.WhenAll(wakes.Select(wake => Task.Run(() => PomodoroWakeSchedule.Write(wake, root))));

        var actual = PomodoroWakeSchedule.Read(root);
        Assert.NotNull(actual);
        Assert.Contains(actual!.Value, wakes);
    }

    [Fact]
    public void FourCompletedFocusesLeadToLongBreak()
    {
        var workspace = CreateWorkspace();
        workspace.UpdateSettings(new PomodoroSettings { FocusMinutes = 1, ShortBreakMinutes = 1, LongBreakMinutes = 2, FocusesPerCycle = 4 });
        var now = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.FromHours(8));
        workspace.StartFocus(now: now);
        for (var focus = 0; focus < 4; focus++)
        {
            now = now.AddMinutes(1);
            workspace.AdvanceIfDue(now);
            if (focus == 3) break;
            workspace.StartNextPhase(now);
            workspace.Skip(now);
            workspace.StartNextPhase(now);
        }

        Assert.Equal(PomodoroPhaseIds.LongBreak, workspace.State.Phase.Phase);
        Assert.Equal(PomodoroRunState.AwaitingNextPhase, workspace.State.Phase.State);
        Assert.Equal(4, workspace.State.Phase.CompletedFocusesInCycle);
    }

    [Fact]
    public void ReminderRecurrenceSnoozeAndSingleEffectiveTaskRuleAreEnforced()
    {
        var reminders = new ReminderService(new ReminderStore(Path.Combine(_root, "reminders")));
        var now = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.FromHours(8));
        var first = reminders.Create("任务提醒", now.AddMinutes(5), new ReminderRecurrenceRule
        {
            Frequency = ReminderRecurrenceFrequencies.Weekly,
            Weekdays = [DayOfWeek.Friday]
        }, "task-1");
        Assert.Throws<InvalidOperationException>(() => reminders.Create("重复", now.AddMinutes(10), todoTaskId: "task-1"));
        Assert.True(reminders.Snooze(first.Id, now, TimeSpan.FromMinutes(10)));
        Assert.Empty(reminders.DispatchDue(now.AddMinutes(9)));
        var dispatch = reminders.DispatchDue(now.AddMinutes(10));
        Assert.Single(dispatch);
        Assert.Equal(first.Id, dispatch[0].Reminder.Id);
        Assert.Empty(reminders.DispatchDue(now.AddMinutes(11)));
    }

    [Fact]
    public void MonthlyRecurrenceAndStandalonePromptSuppressionFollowLocalRules()
    {
        var reminders = new ReminderService(new ReminderStore(Path.Combine(_root, "reminders")));
        var first = new DateTimeOffset(2026, 7, 10, 9, 30, 0, TimeSpan.FromHours(8));
        var reminder = reminders.Create("月度复盘", first, new ReminderRecurrenceRule
        {
            Frequency = ReminderRecurrenceFrequencies.Monthly,
            MonthDays = [10]
        });
        var next = ReminderRecurrenceRules.NextOccurrence(new ReminderDefinition
        {
            Id = reminder.Id, Title = reminder.Title, FirstOccursAt = reminder.FirstOccursAt,
            Recurrence = reminder.Recurrence, IsEnabled = true
        }, first);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 9, 30, 0, TimeSpan.FromHours(8)), next);

        var today = new DateOnly(2026, 7, 24);
        reminders.SuppressStandaloneLinkPrompt(today, forever: false);
        Assert.False(reminders.ShouldAskToLinkStandaloneReminder(today));
        Assert.True(reminders.ShouldAskToLinkStandaloneReminder(today.AddDays(1)));
        reminders.SuppressStandaloneLinkPrompt(today.AddDays(1), forever: true);
        Assert.False(reminders.ShouldAskToLinkStandaloneReminder(today.AddDays(2)));
        reminders.RestoreStandaloneLinkPrompt();
        Assert.True(reminders.ShouldAskToLinkStandaloneReminder(today.AddDays(2)));
    }

    [Fact]
    public async Task ReminderStoreSerializesConcurrentWrites()
    {
        var root = Path.Combine(_root, "reminders");
        var tasks = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            new ReminderService(new ReminderStore(root)).Create($"提醒 {index}", DateTimeOffset.Now.AddHours(index + 1))));
        await Task.WhenAll(tasks);

        Assert.Equal(12, new ReminderService(new ReminderStore(root)).List().Count);
    }

    [Fact]
    public void TaskCompletionAndRestoreOnlyResumeSystemSuspendedReminders()
    {
        var service = new ReminderService(new ReminderStore(Path.Combine(_root, "reminders")));
        var taskReminder = service.Create("任务", DateTimeOffset.Now.AddHours(1), todoTaskId: "task-1");
        var manualReminder = service.Create("独立", DateTimeOffset.Now.AddHours(1));
        service.SetEnabled(manualReminder.Id, false);
        service.SuspendForTask("task-1", ReminderSuspensionReason.TaskCompleted);
        service.RestoreForTask("task-1");

        var current = service.List();
        Assert.True(current.Single(item => item.Id == taskReminder.Id).IsEnabled);
        Assert.False(current.Single(item => item.Id == manualReminder.Id).IsEnabled);
        Assert.Equal(ReminderSuspensionReason.Manual, current.Single(item => item.Id == manualReminder.Id).SuspensionReason);
    }

    [Fact]
    public void DailyDigestExcludesCompletedDeletedAndDeduplicatesTasks()
    {
        var date = new DateOnly(2026, 7, 24);
        var digest = DailyDigestBuilder.Build(date, new ReminderSettings { IsStartDigestEnabled = true, IsDueDigestEnabled = true },
        [
            new TodoReminderTask("one", "同日任务", date, date, false, false),
            new TodoReminderTask("two", "截止任务", date.AddDays(-1), date, false, false),
            new TodoReminderTask("done", "已完成", date, date, true, false),
            new TodoReminderTask("deleted", "已删除", date, date, false, true)
        ]);

        Assert.Single(digest.StartTasks);
        Assert.Single(digest.DueTasks);
        Assert.Equal("two", digest.DueTasks[0].Id);
    }

    [Fact]
    public void TodoWorkspaceSynchronizesReminderSuspensionWithoutChangingTodoStorage()
    {
        var reminderService = new ReminderService(new ReminderStore(Path.Combine(_root, "reminders")));
        reminderService.Create("任务", DateTimeOffset.Now.AddHours(1), todoTaskId: "task-1");
        var repository = new MemoryTodoRepository();
        repository.Data.Tasks.Add(new TodoTask { Id = "task-1", Title = "任务", ListId = TodoStore.DefaultListId, StartDate = DateTime.Today });
        var workspace = new TodoWorkspace(repository, reminderService);

        workspace.SetTaskCompleted("task-1", true, false);
        Assert.Equal(ReminderSuspensionReason.TaskCompleted, reminderService.List().Single().SuspensionReason);
        workspace.SetTaskCompleted("task-1", false, false);
        Assert.True(reminderService.List().Single().IsEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string PomodoroRoot => Path.Combine(_root, "pomodoro");

    private PomodoroWorkspace CreateWorkspace() => new(
        new PomodoroStore(PomodoroRoot),
        new ReminderService(new ReminderStore(Path.Combine(_root, "reminders"))),
        new LocalPomodoroWakeSchedulerClient(PomodoroRoot));

    private sealed class MemoryTodoRepository : ITodoRepository
    {
        public TodoData Data { get; } = new();
        public TodoSettings Settings { get; } = new();
        public TodoData LoadData() => Data;
        public TodoSettings LoadSettings() => Settings;
        public void SaveData(TodoData data) { }
        public void SaveSettings(TodoSettings settings) { }
        public bool UpdateData(Func<TodoData, TodoSettings, bool> update) => update(Data, Settings);
        public string CreateTaskId() => Guid.NewGuid().ToString("N");
        public string CreateListId() => Guid.NewGuid().ToString("N");
        public string DefaultListId => TodoStore.DefaultListId;
    }
}
