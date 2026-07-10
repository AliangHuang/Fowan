using Fowan.Diary.Core.Models;

namespace Fowan.Diary.Core.Services;

public static class DiaryTimeline
{
    public const string AllNotebooksId = "all";

    public static string ResolveNotebookId(DiaryData data, string? notebookId)
    {
        ArgumentNullException.ThrowIfNull(data);
        var selectedNotebookId = string.IsNullOrWhiteSpace(notebookId) ? AllNotebooksId : notebookId.Trim();
        return string.Equals(selectedNotebookId, AllNotebooksId, StringComparison.Ordinal) ||
               data.Notebooks.Any(notebook => string.Equals(notebook.Id, selectedNotebookId, StringComparison.Ordinal))
            ? selectedNotebookId
            : AllNotebooksId;
    }

    public static IReadOnlyList<DiaryEntry> Query(DiaryData data, string? notebookId)
        => Query(data, notebookId, null, null);

    /// <summary>Returns a stable newest-first timeline, optionally limited to inclusive local calendar dates.</summary>
    public static IReadOnlyList<DiaryEntry> Query(DiaryData data, string? notebookId, DateTime? startDate, DateTime? endDate)
    {
        ArgumentNullException.ThrowIfNull(data);
        var selectedNotebookId = string.IsNullOrWhiteSpace(notebookId) ? AllNotebooksId : notebookId.Trim();
        var entries = string.Equals(selectedNotebookId, AllNotebooksId, StringComparison.Ordinal)
            ? data.Entries
            : data.Entries.Where(entry => string.Equals(entry.NotebookId, selectedNotebookId, StringComparison.Ordinal));

        var start = startDate?.Date;
        var end = endDate?.Date;
        if (start is not null && end is not null && start > end)
        {
            return [];
        }
        if (start is not null || end is not null)
        {
            entries = entries.Where(entry =>
            {
                var entryDate = entry.CreatedAt.LocalDateTime.Date;
                return (start is null || entryDate >= start.Value) && (end is null || entryDate <= end.Value);
            });
        }

        return entries
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToList();
    }
}
