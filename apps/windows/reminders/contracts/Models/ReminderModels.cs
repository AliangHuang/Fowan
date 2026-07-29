namespace Fowan.Reminders.Models;

public sealed class ReminderData
{
    public int SchemaVersion { get; set; } = 1;
    public List<ReminderDefinition> Reminders { get; set; } = [];
    public ReminderSettings Settings { get; set; } = new();
}

public sealed class ReminderDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? TodoTaskId { get; set; }
    public DateTimeOffset FirstOccursAt { get; set; }
    public ReminderRecurrenceRule? Recurrence { get; set; }
    public bool IsEnabled { get; set; } = true;
    public ReminderSuspensionReason SuspensionReason { get; set; }
    public DateTimeOffset? SnoozedUntil { get; set; }
    public DateTimeOffset? LastDeliveredOccurrence { get; set; }
    public int PendingMissedCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ReminderRecurrenceRule
{
    public string Frequency { get; set; } = string.Empty;
    public List<DayOfWeek> Weekdays { get; set; } = [];
    public List<int> MonthDays { get; set; } = [];
}

public sealed class ReminderSettings
{
    public bool IsStartDigestEnabled { get; set; }
    public TimeSpan StartDigestTime { get; set; } = new(9, 30, 0);
    public bool IsDueDigestEnabled { get; set; } = true;
    public TimeSpan DueDigestTime { get; set; } = new(9, 30, 0);
    public DateOnly? SuppressStandaloneLinkPromptUntil { get; set; }
    public bool IsStandaloneLinkPromptSuppressedForever { get; set; }
    public DateOnly? LastStartDigestDate { get; set; }
    public DateOnly? LastDueDigestDate { get; set; }
}

public enum ReminderSuspensionReason
{
    None,
    Manual,
    TaskCompleted,
    TaskDeleted,
    TaskPermanentlyDeleted
}

public static class ReminderRecurrenceFrequencies
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
}

public sealed record ReminderSnapshot(
    string Id,
    string Title,
    string Notes,
    string? TodoTaskId,
    DateTimeOffset FirstOccursAt,
    ReminderRecurrenceRule? Recurrence,
    bool IsEnabled,
    ReminderSuspensionReason SuspensionReason,
    DateTimeOffset? SnoozedUntil,
    DateTimeOffset? LastDeliveredOccurrence,
    int PendingMissedCount);

public sealed record ReminderDispatchItem(
    ReminderSnapshot Reminder,
    DateTimeOffset OccurrenceAt,
    int MissedCount);
