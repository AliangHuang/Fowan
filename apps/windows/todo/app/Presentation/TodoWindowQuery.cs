using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoWindowQuery(
    TodoData data,
    TodoSettings settings,
    TodoDateRangeFilter? dateRange,
    string? listIdFilter,
    int maximumDepth,
    TodoCompletionFilter completionFilter,
    bool filterParentTasks,
    string defaultListId)
{
    public IEnumerable<TodoTask> ActiveTasks(string viewId) => ActiveNodes(viewId).Select(node => node.Task);

    public IEnumerable<TodoTask> CompletedTasks(string viewId) => CompletedNodes(viewId).Select(node => node.Task);

    public bool IsFilteringActive => DateRange is { IsValid: true } ||
        !string.IsNullOrWhiteSpace(ListIdFilter) ||
        MaximumDepth < TodoQuery.MaxTaskTreeDepth ||
        CompletionFilter != TodoCompletionFilter.All ||
        FilterParentTasks;

    public IEnumerable<TodoTaskNode> ActiveNodes(string viewId)
    {
        if (CompletionFilter == TodoCompletionFilter.Completed) return [];
        return TodoQuery.ActiveTaskNodesForView(
            data,
            viewId,
            CollapsedIds(),
            dateFilter: DateRange,
            maximumDepth: MaximumDepth,
            listIdFilter: ListIdFilter);
    }

    public IEnumerable<TodoTaskNode> CompletedNodes(string viewId)
    {
        if (CompletionFilter == TodoCompletionFilter.Incomplete) return [];
        return TodoQuery.CompletedTaskNodesForView(
            data,
            viewId,
            CollapsedIds(),
            dateFilter: DateRange,
            maximumDepth: MaximumDepth,
            listIdFilter: ListIdFilter);
    }

    public IEnumerable<TodoTaskNode> FilteredNodes(string viewId) => TodoQuery.FilteredTaskNodesForView(
        data,
        viewId,
        CompletionFilter,
        CollapsedIds(),
        dateFilter: DateRange,
        maximumDepth: MaximumDepth,
        listIdFilter: ListIdFilter,
        filterParentTasks: FilterParentTasks);

    public IEnumerable<TodoTask> TasksForList(string listId) => data.Tasks.Where(task =>
        task.DeletedAt is null && string.Equals(task.ListId, listId, StringComparison.Ordinal));

    public TodoTask? SelectedTask(string? selectedTaskId, string viewId)
    {
        var task = data.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, selectedTaskId, StringComparison.Ordinal));
        return task is not null && BelongsToView(task, viewId) ? task : FirstForSelection(viewId);
    }

    public TodoTask? FirstForSelection(string viewId)
    {
        if (viewId == TodoViewIds.RecycleBin) return null;
        if (IsFilteringActive) return FilteredNodes(viewId).Select(node => node.Task).FirstOrDefault();
        return viewId == TodoViewIds.Completed
            ? CompletedTasks(viewId).FirstOrDefault()
            : viewId == TodoViewIds.Uncompleted
                ? ActiveTasks(viewId).FirstOrDefault()
            : ActiveTasks(viewId).Concat(CompletedTasks(viewId)).FirstOrDefault();
    }

    public bool BelongsToView(TodoTask task, string viewId)
    {
        if (task.DeletedAt is not null || viewId == TodoViewIds.RecycleBin) return false;
        if (IsFilteringActive) return FilteredNodes(viewId).Any(candidate => candidate.Task.Id == task.Id);
        if (viewId == TodoViewIds.Completed) return task.IsCompleted;
        if (viewId == TodoViewIds.Uncompleted) return !task.IsCompleted;
        return task.IsCompleted
            ? CompletedTasks(viewId).Any(candidate => candidate.Id == task.Id)
            : ActiveTasks(viewId).Any(candidate => candidate.Id == task.Id);
    }

    public string ViewTitle(string viewId) => TodoQuery.ViewTitle(data, viewId);

    public string DefaultListForNewTask(string viewId) => TodoQuery.DefaultListIdForNewTask(data, viewId);

    public string DefaultList() => data.Lists.FirstOrDefault(list => IsDefaultList(list.Id))?.Id
        ?? data.Lists.First().Id;

    public bool ListExists(string listId) => data.Lists.Any(list =>
        string.Equals(list.Id, listId, StringComparison.Ordinal));

    public bool IsDefaultList(string listId) => string.Equals(listId, defaultListId, StringComparison.Ordinal);

    public IEnumerable<TodoList> OrderedLists() => data.Lists
        .OrderByDescending(list => IsDefaultList(list.Id))
        .ThenBy(list => list.CreatedAt);

    public bool IsKnownView(string viewId) => TodoQuery.IsKnownView(data, viewId) &&
        (viewId != TodoViewIds.RecycleBin || settings.IsRecycleBinEnabled);

    public static string TaskTimeText(TodoTask task)
    {
        if (task.IsCompleted)
        {
            if (!task.CompletedAt.HasValue) return "已完成";
            var local = task.CompletedAt.Value.ToLocalTime();
            return local.Date == DateTimeOffset.Now.Date
                ? $"今天 {local:HH:mm} 完成"
                : $"{local:MM-dd HH:mm} 完成";
        }
        if (!task.DueDate.HasValue)
        {
            var startDate = task.StartDate.Date;
            return startDate == DateTime.Today ? "今天开始" : $"{startDate:MM-dd} 开始";
        }
        var date = task.DueDate.Value.Date;
        if (date == DateTime.Today) return "今天";
        if (date == DateTime.Today.AddDays(1)) return "明天";
        if (date == DateTime.Today.AddDays(2)) return "后天";
        return date.ToString("MM-dd");
    }

    private HashSet<string> CollapsedIds() => new(settings.CollapsedTaskIds, StringComparer.Ordinal);

    private TodoDateRangeFilter? DateRange => dateRange;
    private string? ListIdFilter => listIdFilter;
    private int MaximumDepth => maximumDepth;
    private TodoCompletionFilter CompletionFilter => completionFilter;
    private bool FilterParentTasks => filterParentTasks;
}
