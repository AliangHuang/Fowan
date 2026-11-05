using Fowan.Report.Shared;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;

namespace Fowan.Report.Windows.Platform.Windows;

internal sealed class WindowsReportTodoReader : IReportTodoReader
{
    public Task<ReportTaskPreview> ReadAsync(TodoFilterCriteria filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = new TodoStore();
        var data = store.LoadData();
        var listNames = data.Lists.ToDictionary(list => list.Id, list => list.Name, StringComparer.Ordinal);
        ReportTaskSnapshot Snapshot(TodoTaskNode node, string status) => new(
            node.Task.Title,
            node.Task.Notes,
            listNames.GetValueOrDefault(node.Task.ListId, node.Task.ListId),
            node.Depth + 1,
            node.Task.IsImportant,
            node.Task.StartDate.Date,
            node.Task.DueDate?.Date,
            node.Task.CompletedAt,
            status);

        // TodoQuery owns all date/list/depth/recycle-bin semantics. The report
        // tool intentionally does not inspect TodoTask fields for filtering.
        var completed = TodoQuery.TaskNodesForCriteria(data, completed: true, filter)
            .Select(node => Snapshot(node, "completed"))
            .ToArray();
        var unfinished = TodoQuery.TaskNodesForCriteria(data, completed: false, filter)
            .Select(node => Snapshot(node, "unfinished"))
            .ToArray();
        return Task.FromResult<ReportTaskPreview>(new(completed, unfinished));
    }
}
