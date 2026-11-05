using Fowan.Reminders.Models;

namespace Fowan.Reminders.Services;

public static class ReminderRecurrenceRules
{
    public static ReminderRecurrenceRule? Normalize(ReminderRecurrenceRule? value)
    {
        if (value is null) return null;
        var frequency = value.Frequency?.Trim().ToLowerInvariant();
        if (frequency == ReminderRecurrenceFrequencies.Weekly)
        {
            var days = (value.Weekdays ?? []).Distinct().OrderBy(day => day).ToList();
            return days.Count == 0 ? null : new ReminderRecurrenceRule { Frequency = frequency, Weekdays = days };
        }
        if (frequency == ReminderRecurrenceFrequencies.Monthly)
        {
            var days = (value.MonthDays ?? []).Where(day => day is >= 1 and <= 28).Distinct().OrderBy(day => day).ToList();
            return days.Count == 0 ? null : new ReminderRecurrenceRule { Frequency = frequency, MonthDays = days };
        }
        return null;
    }

    public static ReminderRecurrenceRule? Clone(ReminderRecurrenceRule? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : new ReminderRecurrenceRule
        {
            Frequency = normalized.Frequency,
            Weekdays = [.. normalized.Weekdays],
            MonthDays = [.. normalized.MonthDays]
        };
    }

    public static DateTimeOffset? NextOccurrence(ReminderDefinition reminder, DateTimeOffset after)
    {
        if (!reminder.IsEnabled || reminder.SuspensionReason != ReminderSuspensionReason.None) return null;
        if (reminder.SnoozedUntil is { } snoozed && snoozed > after) return snoozed;
        if (reminder.Recurrence is null) return reminder.FirstOccursAt > after ? reminder.FirstOccursAt : null;

        var localStart = reminder.FirstOccursAt.LocalDateTime;
        var startDate = DateOnly.FromDateTime(after.LocalDateTime.Date);
        if (reminder.Recurrence.Frequency == ReminderRecurrenceFrequencies.Weekly)
        {
            var wanted = reminder.Recurrence.Weekdays.ToHashSet();
            for (var offset = 0; offset <= 8; offset++)
            {
                var date = startDate.AddDays(offset).ToDateTime(TimeOnly.FromTimeSpan(localStart.TimeOfDay));
                if (!wanted.Contains(date.DayOfWeek)) continue;
                var candidate = LocalOccurrence(date);
                if (candidate > after && candidate >= reminder.FirstOccursAt) return candidate;
            }
        }
        else if (reminder.Recurrence.Frequency == ReminderRecurrenceFrequencies.Monthly)
        {
            var cursor = new DateTime(startDate.Year, startDate.Month, 1);
            for (var monthOffset = 0; monthOffset <= 14; monthOffset++)
            {
                var month = cursor.AddMonths(monthOffset);
                foreach (var day in reminder.Recurrence.MonthDays)
                {
                    var candidate = LocalOccurrence(new DateTime(month.Year, month.Month, day, localStart.Hour, localStart.Minute, localStart.Second));
                    if (candidate > after && candidate >= reminder.FirstOccursAt) return candidate;
                }
            }
        }
        return null;
    }

    public static DateTimeOffset LocalOccurrence(DateTime local)
    {
        var zone = TimeZoneInfo.Local;
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
