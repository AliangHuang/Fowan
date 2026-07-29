using Fowan.Reminders.Models;

namespace Fowan.Reminders.Services;

/// <summary>Owns Reminder V1 rules while keeping task data in Todo unchanged.</summary>
public sealed class ReminderService(IReminderRepository repository)
{
    public static ReminderService CreateDefault() => new(new ReminderStore());

    public ReminderData Load() => repository.Load();

    public IReadOnlyList<ReminderSnapshot> List()
    {
        return repository.Load().Reminders
            .OrderBy(item => item.FirstOccursAt)
            .Select(ToSnapshot)
            .ToArray();
    }

    public ReminderSnapshot Create(
        string title,
        DateTimeOffset firstOccursAt,
        ReminderRecurrenceRule? recurrence = null,
        string? todoTaskId = null,
        string? notes = null)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length == 0) throw new ArgumentException("提醒标题不能为空。", nameof(title));
        var created = new ReminderDefinition
        {
            Id = ReminderStore.NewId("reminder"),
            Title = normalizedTitle,
            Notes = notes?.Trim() ?? string.Empty,
            TodoTaskId = NormalizeTaskId(todoTaskId),
            FirstOccursAt = firstOccursAt,
            Recurrence = ReminderRecurrenceRules.Clone(recurrence),
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
        repository.Update(data =>
        {
            EnsureSingleEffectiveTaskReminder(data, created.TodoTaskId, exceptId: null);
            data.Reminders.Add(created);
            return true;
        });
        return ToSnapshot(created);
    }

    public bool Update(ReminderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return repository.Update(data =>
        {
            var current = data.Reminders.FirstOrDefault(item => item.Id == snapshot.Id);
            if (current is null) return false;
            var normalizedTitle = snapshot.Title?.Trim() ?? string.Empty;
            if (normalizedTitle.Length == 0) throw new ArgumentException("提醒标题不能为空。", nameof(snapshot));
            var taskId = NormalizeTaskId(snapshot.TodoTaskId);
            EnsureSingleEffectiveTaskReminder(data, taskId, current.Id);
            current.Title = normalizedTitle;
            current.Notes = snapshot.Notes?.Trim() ?? string.Empty;
            current.TodoTaskId = taskId;
            current.FirstOccursAt = snapshot.FirstOccursAt;
            current.Recurrence = ReminderRecurrenceRules.Clone(snapshot.Recurrence);
            current.IsEnabled = snapshot.IsEnabled;
            current.SuspensionReason = snapshot.SuspensionReason;
            current.SnoozedUntil = snapshot.SnoozedUntil;
            current.UpdatedAt = DateTimeOffset.Now;
            return true;
        });
    }

    public bool SetEnabled(string reminderId, bool enabled, bool manual = true)
    {
        return repository.Update(data =>
        {
            var reminder = data.Reminders.FirstOrDefault(item => item.Id == reminderId);
            if (reminder is null) return false;
            reminder.IsEnabled = enabled;
            reminder.SuspensionReason = enabled
                ? ReminderSuspensionReason.None
                : manual ? ReminderSuspensionReason.Manual : reminder.SuspensionReason;
            reminder.UpdatedAt = DateTimeOffset.Now;
            return true;
        });
    }

    public bool Snooze(string reminderId, DateTimeOffset now, TimeSpan? duration = null)
    {
        var until = now + (duration ?? TimeSpan.FromMinutes(10));
        return repository.Update(data =>
        {
            var reminder = data.Reminders.FirstOrDefault(item => item.Id == reminderId);
            if (reminder is null || !reminder.IsEnabled || reminder.SuspensionReason != ReminderSuspensionReason.None) return false;
            reminder.SnoozedUntil = until;
            reminder.UpdatedAt = now;
            return true;
        });
    }

    public bool Delete(string reminderId) => repository.Update(data =>
    {
        var reminder = data.Reminders.FirstOrDefault(item => item.Id == reminderId);
        if (reminder is null) return false;
        data.Reminders.Remove(reminder);
        return true;
    });

    public bool SuspendForTask(string taskId, ReminderSuspensionReason reason)
    {
        if (reason is not (ReminderSuspensionReason.TaskCompleted or ReminderSuspensionReason.TaskDeleted or ReminderSuspensionReason.TaskPermanentlyDeleted))
            throw new ArgumentOutOfRangeException(nameof(reason));
        return repository.Update(data =>
        {
            var changed = false;
            foreach (var reminder in data.Reminders.Where(item => string.Equals(item.TodoTaskId, taskId, StringComparison.Ordinal)))
            {
                reminder.IsEnabled = false;
                reminder.SuspensionReason = reason;
                reminder.UpdatedAt = DateTimeOffset.Now;
                changed = true;
            }
            return changed;
        });
    }

    public bool RestoreForTask(string taskId)
    {
        return repository.Update(data =>
        {
            var changed = false;
            foreach (var reminder in data.Reminders.Where(item =>
                         string.Equals(item.TodoTaskId, taskId, StringComparison.Ordinal) &&
                         item.SuspensionReason is ReminderSuspensionReason.TaskCompleted or ReminderSuspensionReason.TaskDeleted))
            {
                reminder.IsEnabled = true;
                reminder.SuspensionReason = ReminderSuspensionReason.None;
                reminder.UpdatedAt = DateTimeOffset.Now;
                changed = true;
            }
            return changed;
        });
    }

    public DateTimeOffset? NextWake(DateTimeOffset now)
    {
        var data = repository.Load();
        var candidates = data.Reminders
            .Select(item => ReminderRecurrenceRules.NextOccurrence(item, now))
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToList();
        AddDigestWake(candidates, data.Settings, now, data.Settings.IsStartDigestEnabled, data.Settings.StartDigestTime);
        AddDigestWake(candidates, data.Settings, now, data.Settings.IsDueDigestEnabled, data.Settings.DueDigestTime);
        return candidates.Count == 0 ? null : candidates.Min();
    }

    public IReadOnlyList<ReminderDispatchItem> DispatchDue(DateTimeOffset now, Func<string, bool>? isLinkedTaskActive = null)
    {
        var dispatched = new List<ReminderDispatchItem>();
        repository.Update(data =>
        {
            foreach (var reminder in data.Reminders)
            {
                if (!reminder.IsEnabled || reminder.SuspensionReason != ReminderSuspensionReason.None) continue;
                if (reminder.TodoTaskId is { Length: > 0 } taskId && isLinkedTaskActive is not null && !isLinkedTaskActive(taskId))
                {
                    reminder.IsEnabled = false;
                    reminder.SuspensionReason = ReminderSuspensionReason.TaskCompleted;
                    reminder.UpdatedAt = now;
                    continue;
                }
                var due = DueOccurrence(reminder, now);
                if (due is null) continue;
                reminder.LastDeliveredOccurrence = due.Occurrence;
                reminder.SnoozedUntil = null;
                reminder.PendingMissedCount = 0;
                reminder.UpdatedAt = now;
                dispatched.Add(new ReminderDispatchItem(ToSnapshot(reminder), due.Occurrence, due.MissedCount));
            }
            return dispatched.Count > 0;
        });
        return dispatched;
    }

    public bool UpdateDigestSettings(Action<ReminderSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return repository.Update(data => { update(data.Settings); return true; });
    }

    public bool ShouldAskToLinkStandaloneReminder(DateOnly today)
    {
        var settings = repository.Load().Settings;
        return !settings.IsStandaloneLinkPromptSuppressedForever && settings.SuppressStandaloneLinkPromptUntil != today;
    }

    public bool SuppressStandaloneLinkPrompt(DateOnly today, bool forever)
    {
        return repository.Update(data =>
        {
            data.Settings.IsStandaloneLinkPromptSuppressedForever = forever;
            data.Settings.SuppressStandaloneLinkPromptUntil = forever ? null : today;
            return true;
        });
    }

    public bool RestoreStandaloneLinkPrompt() => repository.Update(data =>
    {
        data.Settings.IsStandaloneLinkPromptSuppressedForever = false;
        data.Settings.SuppressStandaloneLinkPromptUntil = null;
        return true;
    });

    private static void EnsureSingleEffectiveTaskReminder(ReminderData data, string? taskId, string? exceptId)
    {
        if (taskId is null) return;
        if (data.Reminders.Any(item => item.Id != exceptId && item.IsEnabled && item.SuspensionReason == ReminderSuspensionReason.None &&
                                       string.Equals(item.TodoTaskId, taskId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("每个待办任务最多只能有一条有效提醒。请先更新现有提醒或取消关联。");
        }
    }

    private static DueReminderOccurrence? DueOccurrence(ReminderDefinition reminder, DateTimeOffset now)
    {
        if (reminder.SnoozedUntil is { } snoozed && snoozed <= now) return new DueReminderOccurrence(snoozed, 0);
        if (reminder.SnoozedUntil is { }) return null;
        if (reminder.Recurrence is null)
            return reminder.LastDeliveredOccurrence is null && reminder.FirstOccursAt <= now
                ? new DueReminderOccurrence(reminder.FirstOccursAt, 0) : null;

        if (reminder.FirstOccursAt > now) return null;
        var lastDelivered = reminder.LastDeliveredOccurrence;
        var cursor = reminder.FirstOccursAt;
        DateTimeOffset? latest = null;
        var occurredSinceDelivery = 0;
        for (var safety = 0; safety < 500 && cursor <= now; safety++)
        {
            if (lastDelivered is null || cursor > lastDelivered)
            {
                latest = cursor;
                occurredSinceDelivery++;
            }
            var next = ReminderRecurrenceRules.NextOccurrence(reminder, cursor);
            if (next is null) break;
            cursor = next.Value;
        }
        return latest is null ? null : new DueReminderOccurrence(latest.Value, Math.Max(0, occurredSinceDelivery - 1));
    }

    private static void AddDigestWake(List<DateTimeOffset> candidates, ReminderSettings settings, DateTimeOffset now, bool enabled, TimeSpan time)
    {
        _ = settings;
        if (!enabled) return;
        var local = now.LocalDateTime.Date + time;
        var candidate = ReminderRecurrenceRules.LocalOccurrence(local);
        if (candidate <= now) candidate = ReminderRecurrenceRules.LocalOccurrence(local.AddDays(1));
        candidates.Add(candidate);
    }

    public static ReminderSnapshot ToSnapshot(ReminderDefinition reminder) => new(
        reminder.Id, reminder.Title, reminder.Notes, reminder.TodoTaskId, reminder.FirstOccursAt,
        ReminderRecurrenceRules.Clone(reminder.Recurrence), reminder.IsEnabled, reminder.SuspensionReason,
        reminder.SnoozedUntil, reminder.LastDeliveredOccurrence, reminder.PendingMissedCount);

    private static string? NormalizeTaskId(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record DueReminderOccurrence(DateTimeOffset Occurrence, int MissedCount);
}
