using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public static class TodoDateRangePresets
{
    public static (DateTime Start, DateTime End) ThisWeek(DateTime? today = null)
    {
        var date = (today ?? DateTime.Today).Date;
        var mondayOffset = ((int)date.DayOfWeek + 6) % 7;
        return (date.AddDays(-mondayOffset), date.AddDays(6 - mondayOffset));
    }

    public static (DateTime Start, DateTime End) PreviousWeek(DateTime? today = null)
    {
        var current = ThisWeek(today);
        return (current.Start.AddDays(-7), current.Start.AddDays(-1));
    }

    public static (DateTime Start, DateTime End) ThisMonth(DateTime? today = null)
    {
        var date = (today ?? DateTime.Today).Date;
        var first = new DateTime(date.Year, date.Month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// Quick ranges default to execution-period semantics. Once a user has
    /// selected start-date semantics the shortcut preserves that explicit mode.
    /// </summary>
    public static TodoDateRangeFilter Apply(
        TodoDateRangeFilter? current,
        DateTime start,
        DateTime end) => new()
    {
        Mode = current?.Mode ?? TodoDateFilterMode.ExecutionPeriod,
        StartDate = start.Date,
        EndDate = end.Date
    };
}
