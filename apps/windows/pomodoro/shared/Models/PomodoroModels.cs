using System.Collections.Immutable;

namespace Fowan.Pomodoro.Models;

public static class PomodoroPhaseIds
{
    public const string Focus = "focus";
    public const string ShortBreak = "short-break";
    public const string LongBreak = "long-break";
}

public enum PomodoroRunState { Idle, Running, Paused, AwaitingNextPhase }

public sealed class PomodoroData
{
    public int SchemaVersion { get; set; } = 1;
    public PomodoroSettings Settings { get; set; } = new();
    public ActivePomodoro? Active { get; set; }
    public List<FocusSessionRecord> Sessions { get; set; } = [];
}

public sealed class PomodoroSettings
{
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int FocusesPerCycle { get; set; } = 4;
    public bool AutoStartNextPhase { get; set; }
}

public sealed class ActivePomodoro
{
    public PomodoroRunState State { get; set; } = PomodoroRunState.Idle;
    public string Phase { get; set; } = PomodoroPhaseIds.Focus;
    public string? TodoTaskId { get; set; }
    public string? TodoTaskTitle { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public TimeSpan? RemainingWhenPaused { get; set; }
    public int CompletedFocusesInCycle { get; set; }
}

public sealed class FocusSessionRecord
{
    public string Id { get; set; } = string.Empty;
    public string Phase { get; set; } = PomodoroPhaseIds.Focus;
    public string? TodoTaskId { get; set; }
    public string? TodoTaskTitle { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int PlannedSeconds { get; set; }
    public int ActualSeconds { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed record PomodoroPhaseState(
    PomodoroRunState State,
    string Phase,
    TimeSpan Remaining,
    int CompletedFocusesInCycle,
    string? TodoTaskId,
    string? TodoTaskTitle);

public sealed record PomodoroSnapshot(
    PomodoroPhaseState Phase,
    ImmutableArray<FocusSessionRecord> Sessions,
    PomodoroSettings Settings,
    int TodayCompletedFocuses,
    int ThisWeekCompletedFocuses,
    int TodayCompletedMinutes,
    int ThisWeekCompletedMinutes);
