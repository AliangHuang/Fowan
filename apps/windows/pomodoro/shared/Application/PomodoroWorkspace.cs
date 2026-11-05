using Fowan.Pomodoro.Models;
using Fowan.Pomodoro.Shared.Application.Ports;
using Fowan.Pomodoro.Shared.Services;
using Fowan.Pomodoro.Services;
using Fowan.Reminders.Models;
using Fowan.Reminders.Services;
using System.Collections.Immutable;

namespace Fowan.Pomodoro.Shared.Application;

[Flags]
public enum PomodoroChangeSet { None = 0, Phase = 1, Sessions = 2, Settings = 4, Reminders = 8, All = Phase | Sessions | Settings | Reminders }

public interface IPomodoroCommands
{
    void StartFocus(string? todoTaskId = null, string? todoTaskTitle = null, DateTimeOffset? now = null);
    void StartNextPhase(DateTimeOffset? now = null);
    void Pause(DateTimeOffset? now = null);
    void Resume(DateTimeOffset? now = null);
    void Stop(DateTimeOffset? now = null);
    void Skip(DateTimeOffset? now = null);
}

/// <summary>The single owner of persisted Pomodoro phase and session state.</summary>
public sealed class PomodoroWorkspace : IPomodoroCommands
{
    private readonly PomodoroStore _store;
    private readonly ReminderService _reminders;
    private readonly IToolWakeSchedulerClient _wakeScheduler;
    private PomodoroData _data;

    public PomodoroWorkspace(
        PomodoroStore store,
        ReminderService reminders,
        IToolWakeSchedulerClient? wakeScheduler = null)
    {
        _store = store;
        _reminders = reminders;
        _wakeScheduler = wakeScheduler ?? new LocalPomodoroWakeSchedulerClient();
        _data = store.Load();
        AdvanceIfDue(DateTimeOffset.Now, publish: false);
    }

    public static PomodoroWorkspace CreateDefault() => new(new PomodoroStore(), ReminderService.CreateDefault());

    public PomodoroSnapshot State => Snapshot(DateTimeOffset.Now);

    public event EventHandler<PomodoroChangeSet>? Changed;

    public void StartFocus(string? todoTaskId = null, string? todoTaskTitle = null, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        var active = EnsureActive();
        if (active.State == PomodoroRunState.AwaitingNextPhase)
            throw new InvalidOperationException("当前阶段已结束。请使用开始下一阶段，而不是跳过休息或专注。");
        if (active.State == PomodoroRunState.Running || active.State == PomodoroRunState.Paused)
            throw new InvalidOperationException("当前已有进行中的番茄阶段。请先暂停、停止或跳过。");
        active.Phase = PomodoroPhaseIds.Focus;
        active.TodoTaskId = string.IsNullOrWhiteSpace(todoTaskId) ? null : todoTaskId.Trim();
        active.TodoTaskTitle = string.IsNullOrWhiteSpace(todoTaskTitle) ? null : todoTaskTitle.Trim();
        StartPhase(active, timestamp);
        Save(PomodoroChangeSet.Phase);
    }

    public void StartNextPhase(DateTimeOffset? now = null)
    {
        var active = EnsureActive();
        if (active.State != PomodoroRunState.AwaitingNextPhase) return;
        StartPhase(active, now ?? DateTimeOffset.Now);
        Save(PomodoroChangeSet.Phase);
    }

    public void Pause(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        var active = EnsureActive();
        if (active.State != PomodoroRunState.Running || active.EndsAt is null) return;
        active.RemainingWhenPaused = Remaining(active, timestamp);
        active.EndsAt = null;
        active.State = PomodoroRunState.Paused;
        Save(PomodoroChangeSet.Phase);
    }

    public void Resume(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        var active = EnsureActive();
        if (active.State != PomodoroRunState.Paused) return;
        var remaining = active.RemainingWhenPaused ?? PhaseDuration(active.Phase);
        active.EndsAt = timestamp + remaining;
        active.RemainingWhenPaused = null;
        active.State = PomodoroRunState.Running;
        Save(PomodoroChangeSet.Phase);
    }

    public void Stop(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        var active = EnsureActive();
        if (active.State is not (PomodoroRunState.Running or PomodoroRunState.Paused)) return;
        AddSession(active, timestamp, completed: false);
        ResetToIdle(active);
        Save(PomodoroChangeSet.Phase | PomodoroChangeSet.Sessions);
    }

    public void Skip(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        var active = EnsureActive();
        if (active.State is PomodoroRunState.Idle or PomodoroRunState.AwaitingNextPhase) return;
        AddSession(active, timestamp, completed: false);
        MoveToNextPhase(active, timestamp, completed: false);
        Save(PomodoroChangeSet.Phase | PomodoroChangeSet.Sessions);
    }

    public bool AdvanceIfDue(DateTimeOffset now, bool publish = true)
    {
        var active = EnsureActive();
        if (active.State != PomodoroRunState.Running || active.EndsAt is null || active.EndsAt > now) return false;
        AddSession(active, active.EndsAt.Value, completed: true);
        MoveToNextPhase(active, now, completed: true);
        _store.Save(_data);
        if (publish) Changed?.Invoke(this, PomodoroChangeSet.Phase | PomodoroChangeSet.Sessions);
        return true;
    }

    public void UpdateSettings(PomodoroSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _data.Settings = CloneSettings(settings);
        NormalizeSettings(_data.Settings);
        Save(PomodoroChangeSet.Settings | PomodoroChangeSet.Phase);
    }

    public DateTimeOffset? NextWake(DateTimeOffset now)
    {
        var active = EnsureActive();
        var phaseWake = active.State == PomodoroRunState.Running ? active.EndsAt : null;
        var reminderWake = _reminders.NextWake(now);
        return phaseWake is null ? reminderWake : reminderWake is null ? phaseWake : phaseWake < reminderWake ? phaseWake : reminderWake;
    }

    public void ReconcileWake(DateTimeOffset now)
    {
        AdvanceIfDue(now);
        RegisterNextWake(now);
    }

    public IReadOnlyList<FocusSessionRecord> SessionsFor(DateOnly day)
    {
        return _data.Sessions.Where(item => DateOnly.FromDateTime(item.EndedAt.LocalDateTime) == day)
            .Select(CloneSession).ToArray();
    }

    public IReadOnlyList<ReminderSnapshot> Reminders => _reminders.List();

    public ReminderSettings ReminderSettings => CloneReminderSettings(_reminders.Load().Settings);

    public ReminderSnapshot CreateReminder(
        string title, DateTimeOffset firstOccursAt, ReminderRecurrenceRule? recurrence = null,
        string? todoTaskId = null, string? notes = null)
    {
        var result = _reminders.Create(title, firstOccursAt, recurrence, todoTaskId, notes);
        RegisterNextWake(DateTimeOffset.Now);
        Changed?.Invoke(this, PomodoroChangeSet.Reminders);
        return result;
    }

    public bool SnoozeReminder(string reminderId, DateTimeOffset now)
    {
        var changed = _reminders.Snooze(reminderId, now);
        if (changed)
        {
            RegisterNextWake(now);
            Changed?.Invoke(this, PomodoroChangeSet.Reminders);
        }
        return changed;
    }

    public bool UpdateReminder(ReminderSnapshot snapshot)
    {
        var changed = _reminders.Update(snapshot);
        if (changed)
        {
            RegisterNextWake(DateTimeOffset.Now);
            Changed?.Invoke(this, PomodoroChangeSet.Reminders);
        }
        return changed;
    }

    public bool SetReminderEnabled(string reminderId, bool enabled)
    {
        var changed = _reminders.SetEnabled(reminderId, enabled);
        if (changed)
        {
            RegisterNextWake(DateTimeOffset.Now);
            Changed?.Invoke(this, PomodoroChangeSet.Reminders);
        }
        return changed;
    }

    public void UpdateReminderSettings(Action<ReminderSettings> update)
    {
        _reminders.UpdateDigestSettings(update);
        RegisterNextWake(DateTimeOffset.Now);
        Changed?.Invoke(this, PomodoroChangeSet.Reminders);
    }

    public bool ShouldAskToLinkStandaloneReminder(DateOnly today) => _reminders.ShouldAskToLinkStandaloneReminder(today);

    public void SuppressStandaloneLinkPrompt(DateOnly today, bool forever) => _reminders.SuppressStandaloneLinkPrompt(today, forever);

    public void RestoreStandaloneLinkPrompt() => _reminders.RestoreStandaloneLinkPrompt();

    public IReadOnlyList<ReminderDispatchItem> DispatchDue(DateTimeOffset now, Func<string, bool>? isLinkedTaskActive = null)
    {
        AdvanceIfDue(now);
        var dispatched = _reminders.DispatchDue(now, isLinkedTaskActive);
        RegisterNextWake(now);
        if (dispatched.Count > 0) Changed?.Invoke(this, PomodoroChangeSet.Reminders);
        return dispatched;
    }

    private void MoveToNextPhase(ActivePomodoro active, DateTimeOffset now, bool completed)
    {
        if (completed && active.Phase == PomodoroPhaseIds.Focus) active.CompletedFocusesInCycle++;
        var next = active.Phase == PomodoroPhaseIds.Focus
            ? (active.CompletedFocusesInCycle > 0 && active.CompletedFocusesInCycle % _data.Settings.FocusesPerCycle == 0
                ? PomodoroPhaseIds.LongBreak : PomodoroPhaseIds.ShortBreak)
            : PomodoroPhaseIds.Focus;
        if (active.Phase != PomodoroPhaseIds.Focus && next == PomodoroPhaseIds.Focus)
        {
            active.TodoTaskId = null;
            active.TodoTaskTitle = null;
        }
        active.Phase = next;
        active.StartedAt = null;
        active.EndsAt = null;
        active.RemainingWhenPaused = null;
        active.State = PomodoroRunState.AwaitingNextPhase;
        if (_data.Settings.AutoStartNextPhase) StartPhase(active, now);
    }

    private void StartPhase(ActivePomodoro active, DateTimeOffset now)
    {
        active.StartedAt = now;
        active.EndsAt = now + (active.RemainingWhenPaused ?? PhaseDuration(active.Phase));
        active.RemainingWhenPaused = null;
        active.State = PomodoroRunState.Running;
    }

    private void AddSession(ActivePomodoro active, DateTimeOffset endedAt, bool completed)
    {
        if (active.StartedAt is null) return;
        var planned = (int)Math.Round(PhaseDuration(active.Phase).TotalSeconds);
        var actual = Math.Clamp((int)Math.Round((endedAt - active.StartedAt.Value).TotalSeconds), 0, planned);
        _data.Sessions.Add(new FocusSessionRecord
        {
            Id = $"session-{Guid.NewGuid():N}", Phase = active.Phase, TodoTaskId = active.TodoTaskId,
            TodoTaskTitle = active.TodoTaskTitle, StartedAt = active.StartedAt.Value, EndedAt = endedAt,
            PlannedSeconds = planned, ActualSeconds = actual, IsCompleted = completed
        });
        if (_data.Sessions.Count > 2000) _data.Sessions.RemoveRange(0, _data.Sessions.Count - 2000);
    }

    private PomodoroSnapshot Snapshot(DateTimeOffset now)
    {
        var active = EnsureActive();
        var remaining = active.State switch
        {
            PomodoroRunState.Running => Remaining(active, now),
            PomodoroRunState.Paused => active.RemainingWhenPaused ?? PhaseDuration(active.Phase),
            _ => PhaseDuration(active.Phase)
        };
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var weekStart = today.AddDays(-((int)now.LocalDateTime.DayOfWeek + 6) % 7);
        var completed = _data.Sessions.Where(item => item.IsCompleted && item.Phase == PomodoroPhaseIds.Focus).ToArray();
        var todaySessions = completed.Where(item => DateOnly.FromDateTime(item.EndedAt.LocalDateTime) == today).ToArray();
        var weekSessions = completed.Where(item => DateOnly.FromDateTime(item.EndedAt.LocalDateTime) >= weekStart).ToArray();
        return new PomodoroSnapshot(
            new PomodoroPhaseState(active.State, active.Phase, remaining, active.CompletedFocusesInCycle, active.TodoTaskId, active.TodoTaskTitle),
            _data.Sessions.Select(CloneSession).ToImmutableArray(), CloneSettings(_data.Settings),
            todaySessions.Length, weekSessions.Length,
            (int)Math.Round(todaySessions.Sum(item => item.ActualSeconds) / 60d),
            (int)Math.Round(weekSessions.Sum(item => item.ActualSeconds) / 60d));
    }

    private TimeSpan PhaseDuration(string phase) => TimeSpan.FromMinutes(phase switch
    {
        PomodoroPhaseIds.Focus => _data.Settings.FocusMinutes,
        PomodoroPhaseIds.LongBreak => _data.Settings.LongBreakMinutes,
        _ => _data.Settings.ShortBreakMinutes
    });

    private static TimeSpan Remaining(ActivePomodoro active, DateTimeOffset now) => active.EndsAt is null
        ? TimeSpan.Zero : active.EndsAt.Value > now ? active.EndsAt.Value - now : TimeSpan.Zero;

    private ActivePomodoro EnsureActive() => _data.Active ??= new ActivePomodoro();

    private void ResetToIdle(ActivePomodoro active)
    {
        active.State = PomodoroRunState.Idle;
        active.Phase = PomodoroPhaseIds.Focus;
        active.TodoTaskId = null;
        active.TodoTaskTitle = null;
        active.StartedAt = null;
        active.EndsAt = null;
        active.RemainingWhenPaused = null;
    }

    private void Save(PomodoroChangeSet changes)
    {
        _store.Save(_data);
        RegisterNextWake(DateTimeOffset.Now);
        Changed?.Invoke(this, changes);
    }

    private void RegisterNextWake(DateTimeOffset now) => _wakeScheduler.Register(NextWake(now));

    private static FocusSessionRecord CloneSession(FocusSessionRecord value) => new()
    {
        Id = value.Id, Phase = value.Phase, TodoTaskId = value.TodoTaskId, TodoTaskTitle = value.TodoTaskTitle,
        StartedAt = value.StartedAt, EndedAt = value.EndedAt, PlannedSeconds = value.PlannedSeconds,
        ActualSeconds = value.ActualSeconds, IsCompleted = value.IsCompleted
    };

    private static PomodoroSettings CloneSettings(PomodoroSettings value) => new()
    {
        FocusMinutes = value.FocusMinutes, ShortBreakMinutes = value.ShortBreakMinutes,
        LongBreakMinutes = value.LongBreakMinutes, FocusesPerCycle = value.FocusesPerCycle,
        AutoStartNextPhase = value.AutoStartNextPhase
    };

    private static ReminderSettings CloneReminderSettings(ReminderSettings value) => new()
    {
        IsStartDigestEnabled = value.IsStartDigestEnabled, StartDigestTime = value.StartDigestTime,
        IsDueDigestEnabled = value.IsDueDigestEnabled, DueDigestTime = value.DueDigestTime,
        SuppressStandaloneLinkPromptUntil = value.SuppressStandaloneLinkPromptUntil,
        IsStandaloneLinkPromptSuppressedForever = value.IsStandaloneLinkPromptSuppressedForever,
        LastStartDigestDate = value.LastStartDigestDate, LastDueDigestDate = value.LastDueDigestDate
    };

    private static void NormalizeSettings(PomodoroSettings value)
    {
        value.FocusMinutes = Math.Clamp(value.FocusMinutes, 1, 180);
        value.ShortBreakMinutes = Math.Clamp(value.ShortBreakMinutes, 1, 60);
        value.LongBreakMinutes = Math.Clamp(value.LongBreakMinutes, 1, 120);
        value.FocusesPerCycle = Math.Clamp(value.FocusesPerCycle, 2, 12);
    }
}

public sealed class PomodoroWorkspaceOwner
{
    public PomodoroWorkspaceOwner(PomodoroWorkspace? workspace = null) => Workspace = workspace ?? PomodoroWorkspace.CreateDefault();
    public PomodoroWorkspace Workspace { get; }
}
