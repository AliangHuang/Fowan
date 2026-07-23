using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public static class TodoRecurrenceRules
{
    public static TodoRecurrenceRule? Normalize(TodoRecurrenceRule? value)
    {
        if (value is null) return null;

        var frequency = value.Frequency?.Trim().ToLowerInvariant();
        if (string.Equals(frequency, TodoRecurrenceFrequencies.Weekly, StringComparison.Ordinal))
        {
            var weekdays = (value.Weekdays ?? [])
                .Where(day => Enum.IsDefined(typeof(DayOfWeek), day))
                .Distinct()
                .OrderBy(day => day)
                .ToList();
            return weekdays.Count == 0
                ? null
                : new TodoRecurrenceRule
                {
                    Frequency = TodoRecurrenceFrequencies.Weekly,
                    Weekdays = weekdays,
                    WeeklyDueDay = value.WeeklyDueDay is { } dueDay &&
                        Enum.IsDefined(typeof(DayOfWeek), dueDay)
                        ? dueDay
                        : null
                };
        }

        if (string.Equals(frequency, TodoRecurrenceFrequencies.Monthly, StringComparison.Ordinal))
        {
            var monthDays = (value.MonthDays ?? [])
                .Where(day => day is >= 1 and <= 28)
                .Distinct()
                .OrderBy(day => day)
                .ToList();
            return monthDays.Count == 0
                ? null
                : new TodoRecurrenceRule
                {
                    Frequency = TodoRecurrenceFrequencies.Monthly,
                    MonthDays = monthDays,
                    MonthlyDueDay = value.MonthlyDueDay is >= 1 and <= 28
                        ? value.MonthlyDueDay
                        : null
                };
        }

        return null;
    }

    public static TodoRecurrenceRule? Clone(TodoRecurrenceRule? value)
    {
        var normalized = Normalize(value);
        return normalized is null
            ? null
            : new TodoRecurrenceRule
            {
                Frequency = normalized.Frequency,
                Weekdays = [.. normalized.Weekdays],
                MonthDays = [.. normalized.MonthDays],
                WeeklyDueDay = normalized.WeeklyDueDay,
                MonthlyDueDay = normalized.MonthlyDueDay
            };
    }

    public static DateTime FirstOccurrenceOnOrAfter(TodoRecurrenceRule rule, DateTime startDate)
    {
        var normalized = Normalize(rule) ?? throw new ArgumentException("A recurrence rule must contain at least one valid date.", nameof(rule));
        var first = startDate.Date;
        if (string.Equals(normalized.Frequency, TodoRecurrenceFrequencies.Weekly, StringComparison.Ordinal))
        {
            var weekdays = normalized.Weekdays.ToHashSet();
            for (var offset = 0; offset < 7; offset++)
            {
                var candidate = first.AddDays(offset);
                if (weekdays.Contains(candidate.DayOfWeek)) return candidate;
            }
        }

        var month = new DateTime(first.Year, first.Month, 1);
        for (var offset = 0; offset < 2; offset++)
        {
            var currentMonth = month.AddMonths(offset);
            foreach (var day in normalized.MonthDays)
            {
                var candidate = new DateTime(currentMonth.Year, currentMonth.Month, day);
                if (candidate >= first) return candidate;
            }
        }

        throw new InvalidOperationException("Unable to find the next recurrence date.");
    }

    public static DateTime? DueDateForOccurrence(
        TodoRecurrenceRule rule,
        DateTime occurrence,
        DateTime templateStartDate,
        DateTime? templateDueDate)
    {
        var normalized = Normalize(rule) ?? throw new ArgumentException(
            "A recurrence rule must contain at least one valid date.",
            nameof(rule));
        var start = occurrence.Date;

        if (string.Equals(normalized.Frequency, TodoRecurrenceFrequencies.Weekly, StringComparison.Ordinal) &&
            normalized.WeeklyDueDay is { } weeklyDueDay)
        {
            var offset = ((int)weeklyDueDay - (int)start.DayOfWeek + 7) % 7;
            return start.AddDays(offset);
        }

        if (string.Equals(normalized.Frequency, TodoRecurrenceFrequencies.Monthly, StringComparison.Ordinal) &&
            normalized.MonthlyDueDay is { } monthlyDueDay)
        {
            var due = new DateTime(start.Year, start.Month, monthlyDueDay);
            return due >= start ? due : due.AddMonths(1);
        }

        return templateDueDate is { } dueDate
            ? start.AddDays((dueDate.Date - templateStartDate.Date).Days)
            : null;
    }
}

public static class TodoRecurringTaskScheduler
{
    public static int CreateDueTasks(
        TodoData data,
        DateTime throughDate,
        Func<string> createTaskId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(createTaskId);

        var created = 0;
        foreach (var template in data.Tasks
                     .Where(task => task.DeletedAt is null && task.Recurrence is not null)
                     .OrderBy(task => task.StartDate)
                     .ThenBy(task => task.CreatedAt)
                     .ToList())
        {
            var recurrence = TodoRecurrenceRules.Normalize(template.Recurrence);
            if (recurrence is null) continue;

            var existingDates = data.Tasks
                .Where(task => string.Equals(task.Id, template.Id, StringComparison.Ordinal) ||
                    string.Equals(task.RecurrenceSourceTaskId, template.Id, StringComparison.Ordinal))
                .Select(task => task.StartDate.Date)
                .ToHashSet();
            foreach (var occurrence in OccurrencesAfter(template.StartDate, throughDate, recurrence))
            {
                if (!existingDates.Add(occurrence)) continue;

                var dueDate = TodoRecurrenceRules.DueDateForOccurrence(
                    recurrence,
                    occurrence,
                    template.StartDate,
                    template.DueDate);
                data.Tasks.Add(new TodoTask
                {
                    Id = createTaskId(),
                    Title = template.Title,
                    Notes = template.Notes,
                    ListId = template.ListId,
                    IsImportant = template.IsImportant,
                    StartDate = occurrence,
                    DueDate = dueDate,
                    RecurrenceSourceTaskId = template.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                created++;
            }
        }

        return created;
    }

    private static IEnumerable<DateTime> OccurrencesAfter(
        DateTime startDate,
        DateTime throughDate,
        TodoRecurrenceRule recurrence)
    {
        var first = startDate.Date.AddDays(1);
        var last = throughDate.Date;
        if (first > last) yield break;

        if (string.Equals(recurrence.Frequency, TodoRecurrenceFrequencies.Weekly, StringComparison.Ordinal))
        {
            var weekdays = recurrence.Weekdays.ToHashSet();
            for (var date = first; date <= last; date = date.AddDays(1))
            {
                if (weekdays.Contains(date.DayOfWeek)) yield return date;
            }
            yield break;
        }

        var month = new DateTime(first.Year, first.Month, 1);
        while (month <= last)
        {
            foreach (var day in recurrence.MonthDays)
            {
                var date = new DateTime(month.Year, month.Month, day);
                if (date >= first && date <= last) yield return date;
            }
            month = month.AddMonths(1);
        }
    }
}
