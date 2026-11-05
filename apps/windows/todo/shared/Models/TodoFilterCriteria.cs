using Fowan.Todo.Shared.Services;

namespace Fowan.Todo.Shared.Models;

/// <summary>
/// Shared task-filter input for Windows tools. Consumers must route it through
/// <see cref="TodoQuery"/> instead of reimplementing date, list, or
/// recycle-bin rules.
/// </summary>
public sealed record TodoFilterCriteria(
    string? ListId,
    int MaximumDepth,
    TodoDateRangeFilter? DateRange)
{
    public static TodoFilterCriteria Default { get; } = new(null, TodoQuery.MaxTaskTreeDepth, null);

    public TodoFilterCriteria Normalize() => this with
    {
        ListId = string.IsNullOrWhiteSpace(ListId) ? null : ListId.Trim(),
        MaximumDepth = Math.Clamp(MaximumDepth, 1, TodoQuery.MaxTaskTreeDepth),
        DateRange = DateRange is { IsValid: true } range
            ? new TodoDateRangeFilter
            {
                Mode = range.Mode,
                StartDate = range.StartDate.Date,
                EndDate = range.EndDate.Date
            }
            : null
    };
}
