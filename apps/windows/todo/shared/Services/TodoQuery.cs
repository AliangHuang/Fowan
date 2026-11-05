using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public static class TodoQuery
{
    public const int MaxTaskTreeDepth = 3;
    public const int MaxChildTasksPerTask = 100;

    public static bool HasListName(TodoData data, string name, string? excludedListId = null)
    {
        var normalized = name.Trim();
        return normalized.Length > 0 && data.Lists.Any(list =>
            !string.Equals(list.Id, excludedListId, StringComparison.Ordinal) &&
            string.Equals(list.Name.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<TodoTask> ActiveTasksForView(
        TodoData data,
        string viewId,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        string? listIdFilter = null)
    {
        return ActiveTaskNodesForView(data, viewId, today, dateFilter, listIdFilter: listIdFilter).Select(node => node.Task);
    }

    /// <summary>
    /// The canonical cross-tool filtered task query. It reuses the exact Todo
    /// active/completed, hierarchy, date-range, list, and recycle-bin rules.
    /// </summary>
    public static IEnumerable<TodoTaskNode> TaskNodesForCriteria(
        TodoData data,
        bool completed,
        TodoFilterCriteria? criteria,
        DateTime? today = null)
    {
        var normalized = (criteria ?? TodoFilterCriteria.Default).Normalize();
        return completed
            ? CompletedTaskNodesForView(
                data,
                TodoViewIds.All,
                today,
                normalized.DateRange,
                normalized.MaximumDepth,
                normalized.ListId)
            : ActiveTaskNodesForView(
                data,
                TodoViewIds.All,
                today,
                normalized.DateRange,
                normalized.MaximumDepth,
                normalized.ListId);
    }

    public static IEnumerable<TodoTask> CompletedTasksForView(
        TodoData data,
        string viewId,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        string? listIdFilter = null)
    {
        return CompletedTaskNodesForView(data, viewId, today, dateFilter, listIdFilter: listIdFilter).Select(node => node.Task);
    }

    public static IEnumerable<TodoTaskNode> ActiveTaskNodesForView(
        TodoData data,
        string viewId,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null)
    {
        return TaskNodesForView(data, viewId, completed: false, collapsedTaskIds: null, today, dateFilter, maximumDepth, listIdFilter);
    }

    public static IEnumerable<TodoTaskNode> ActiveTaskNodesForView(
        TodoData data,
        string viewId,
        ISet<string>? collapsedTaskIds,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null)
    {
        return TaskNodesForView(data, viewId, completed: false, collapsedTaskIds, today, dateFilter, maximumDepth, listIdFilter);
    }

    public static IEnumerable<TodoTaskNode> CompletedTaskNodesForView(
        TodoData data,
        string viewId,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null)
    {
        return TaskNodesForView(
            data,
            viewId == TodoViewIds.Completed ? TodoViewIds.All : viewId,
            completed: true,
            collapsedTaskIds: null,
            today,
            dateFilter,
            maximumDepth,
            listIdFilter);
    }

    public static IEnumerable<TodoTaskNode> CompletedTaskNodesForView(
        TodoData data,
        string viewId,
        ISet<string>? collapsedTaskIds,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null)
    {
        return TaskNodesForView(
            data,
            viewId == TodoViewIds.Completed ? TodoViewIds.All : viewId,
            completed: true,
            collapsedTaskIds,
            today,
            dateFilter,
            maximumDepth,
            listIdFilter);
    }

    public static IEnumerable<TodoTaskNode> TaskNodesForView(
        TodoData data,
        string viewId,
        bool completed,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null)
    {
        return TaskNodesForView(data, viewId, completed, collapsedTaskIds: null, today, dateFilter, maximumDepth, listIdFilter);
    }

    public static IEnumerable<TodoTaskNode> TaskNodesForView(
        TodoData data,
        string viewId,
        bool completed,
        ISet<string>? collapsedTaskIds,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null)
    {
        var visible = FilterTasks(data, viewId, completed, today, dateFilter, listIdFilter).ToList();
        return BuildTaskNodes(data, visible, collapsedTaskIds, completed, maximumDepth);
    }

    /// <summary>
    /// Builds one filter result tree that can contain both completed and incomplete tasks.
    /// When parent filtering is disabled, every matching descendant brings its non-deleted
    /// ancestors into the tree so that the match keeps its original context.
    /// </summary>
    public static IEnumerable<TodoTaskNode> FilteredTaskNodesForView(
        TodoData data,
        string viewId,
        TodoCompletionFilter completionFilter,
        ISet<string>? collapsedTaskIds = null,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        int? maximumDepth = null,
        string? listIdFilter = null,
        bool filterParentTasks = false)
    {
        var effectiveCompletion = EffectiveCompletionFilter(viewId, completionFilter);
        if (effectiveCompletion is null)
        {
            return [];
        }

        var visible = new List<TodoTask>();
        if (effectiveCompletion is TodoCompletionFilter.All or TodoCompletionFilter.Incomplete)
        {
            visible.AddRange(FilterTasks(
                data,
                viewId,
                completed: false,
                today: today,
                dateFilter: dateFilter,
                listIdFilter: listIdFilter));
        }

        if (effectiveCompletion is TodoCompletionFilter.All or TodoCompletionFilter.Completed)
        {
            var completedViewId = viewId == TodoViewIds.Completed ? TodoViewIds.All : viewId;
            visible.AddRange(FilterTasks(
                data,
                completedViewId,
                completed: true,
                today: today,
                dateFilter: dateFilter,
                listIdFilter: listIdFilter));
        }

        var matching = visible
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var treeTasks = filterParentTasks ? matching : IncludeUnmatchedParents(data, matching);
        return BuildTaskNodes(
            data,
            treeTasks,
            filterParentTasks ? collapsedTaskIds : null,
            effectiveCompletion == TodoCompletionFilter.Completed,
            maximumDepth);
    }

    public static IEnumerable<TodoTask> RecycleBinTasks(TodoData data)
    {
        return data.Tasks.Where(task => task.DeletedAt is not null);
    }

    public static IEnumerable<TodoTaskNode> RecycleBinTaskNodes(
        TodoData data,
        ISet<string>? collapsedTaskIds = null,
        int? maximumDepth = null)
    {
        var visible = RecycleBinTasks(data).ToList();
        var visibleIds = visible.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        var childrenByParent = visible
            .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId) && visibleIds.Contains(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(task => task.SortOrder).ToList(), StringComparer.Ordinal);
        var roots = visible
            .Where(task => string.IsNullOrWhiteSpace(task.ParentTaskId) || !visibleIds.Contains(task.ParentTaskId))
            .OrderByDescending(task => task.DeletedAt)
            .ThenBy(task => task.SortOrder);

        foreach (var root in roots)
        {
            foreach (var node in FlattenTaskNode(data, root, childrenByParent, collapsedTaskIds))
            {
                if (!maximumDepth.HasValue || node.Depth < maximumDepth.Value)
                {
                    yield return node;
                }
            }
        }
    }

    public static IEnumerable<TodoTask> FilterTasks(
        TodoData data,
        string viewId,
        bool completed,
        DateTime? today = null,
        TodoDateRangeFilter? dateFilter = null,
        string? listIdFilter = null)
    {
        var currentDate = (today ?? DateTime.Today).Date;
        return data.Tasks.Where(task =>
        {
            if (task.DeletedAt is not null || task.IsCompleted != completed)
            {
                return false;
            }

            if (!MatchesDateFilter(task, completed, currentDate, dateFilter))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(listIdFilter) &&
                !string.Equals(task.ListId, listIdFilter, StringComparison.Ordinal))
            {
                return false;
            }

            if (TodoViewIds.IsList(viewId))
            {
                return string.Equals(task.ListId, TodoViewIds.ListId(viewId), StringComparison.Ordinal);
            }

            return viewId switch
            {
                TodoViewIds.Today when completed => CompletedDate(task) == currentDate,
                TodoViewIds.Today => task.StartDate.Date <= currentDate &&
                    (!task.DueDate.HasValue || task.DueDate.Value.Date <= currentDate),
                TodoViewIds.Planned => task.StartDate.Date > currentDate ||
                    (task.DueDate.HasValue && task.DueDate.Value.Date > currentDate),
                TodoViewIds.Important => task.IsImportant,
                TodoViewIds.Recurring => task.Recurrence is not null,
                TodoViewIds.Uncompleted when completed => false,
                TodoViewIds.Uncompleted => true,
                TodoViewIds.All or TodoViewIds.Completed => true,
                _ => false
            };
        });
    }

    public static bool IsKnownView(TodoData data, string viewId)
    {
        if (viewId is TodoViewIds.Today or TodoViewIds.Planned or TodoViewIds.Important or TodoViewIds.Recurring or TodoViewIds.All or TodoViewIds.Uncompleted or TodoViewIds.Completed or TodoViewIds.RecycleBin)
        {
            return true;
        }

        return TodoViewIds.IsList(viewId) &&
            data.Lists.Any(list => string.Equals(list.Id, TodoViewIds.ListId(viewId), StringComparison.Ordinal));
    }

    public static string ViewTitle(TodoData data, string viewId)
    {
        if (TodoViewIds.IsList(viewId))
        {
            return data.Lists.FirstOrDefault(list => string.Equals(list.Id, TodoViewIds.ListId(viewId), StringComparison.Ordinal))?.Name
                ?? "任务清单";
        }

        return viewId switch
        {
            TodoViewIds.Planned => "计划任务",
            TodoViewIds.Important => "重要任务",
            TodoViewIds.Recurring => "循环任务",
            TodoViewIds.All => "全部任务",
            TodoViewIds.Uncompleted => "未完成",
            TodoViewIds.Completed => "已完成",
            TodoViewIds.RecycleBin => "回收站",
            _ => "今日任务"
        };
    }

    public static string DefaultListId(TodoData data)
    {
        return data.Lists.FirstOrDefault(list => string.Equals(list.Id, TodoStore.DefaultListId, StringComparison.Ordinal))?.Id
            ?? data.Lists.First().Id;
    }

    public static string DefaultListIdForNewTask(TodoData data, string viewId)
    {
        if (TodoViewIds.IsList(viewId))
        {
            var listId = TodoViewIds.ListId(viewId);
            if (data.Lists.Any(list => string.Equals(list.Id, listId, StringComparison.Ordinal)))
            {
                return listId;
            }
        }

        return DefaultListId(data);
    }

    public static int TaskDepth(TodoData data, TodoTask task)
    {
        var byId = data.Tasks.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        var depth = 1;
        var seen = new HashSet<string>(StringComparer.Ordinal) { task.Id };
        var current = task;

        while (!string.IsNullOrWhiteSpace(current.ParentTaskId) &&
            byId.TryGetValue(current.ParentTaskId, out var parent) &&
            seen.Add(parent.Id))
        {
            depth++;
            current = parent;
        }

        return Math.Min(depth, MaxTaskTreeDepth);
    }

    public static int TaskIndentDepth(TodoData data, TodoTask task)
    {
        return Math.Clamp(TaskDepth(data, task) - 1, 0, MaxTaskTreeDepth - 1);
    }

    public static int DirectChildCount(TodoData data, string taskId)
    {
        return data.Tasks.Count(task => task.DeletedAt is null && string.Equals(task.ParentTaskId, taskId, StringComparison.Ordinal));
    }

    public static bool CanAddChild(TodoData data, TodoTask parent)
    {
        return parent.DeletedAt is null &&
            TaskDepth(data, parent) < MaxTaskTreeDepth &&
            DirectChildCount(data, parent.Id) < MaxChildTasksPerTask;
    }

    public static string AddChildBlockedReason(TodoData data, TodoTask parent)
    {
        if (TaskDepth(data, parent) >= MaxTaskTreeDepth)
        {
            return $"任务树最多 {MaxTaskTreeDepth} 层";
        }

        if (DirectChildCount(data, parent.Id) >= MaxChildTasksPerTask)
        {
            return $"每个任务最多 {MaxChildTasksPerTask} 个子任务";
        }

        return string.Empty;
    }

    public static IEnumerable<string> DescendantIds(TodoData data, string taskId)
    {
        var childrenByParent = data.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var stack = new Stack<TodoTask>(childrenByParent.TryGetValue(taskId, out var children) ? children : []);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (stack.Count > 0)
        {
            var task = stack.Pop();
            if (!seen.Add(task.Id))
            {
                continue;
            }

            yield return task.Id;
            if (childrenByParent.TryGetValue(task.Id, out var nestedChildren))
            {
                foreach (var child in nestedChildren)
                {
                    stack.Push(child);
                }
            }
        }
    }

    private static IEnumerable<TodoTaskNode> BuildTaskNodes(
        TodoData data,
        IReadOnlyCollection<TodoTask> visible,
        ISet<string>? collapsedTaskIds,
        bool completed,
        int? maximumDepth)
    {
        var visibleIds = visible.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        var childrenByParent = visible
            .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId) && visibleIds.Contains(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => OrderSiblings(group, completed).ToList(), StringComparer.Ordinal);
        var roots = visible
            .Where(task => string.IsNullOrWhiteSpace(task.ParentTaskId) || !visibleIds.Contains(task.ParentTaskId))
            .ToList();

        foreach (var root in OrderSiblings(roots, completed))
        {
            foreach (var node in FlattenTaskNode(data, root, childrenByParent, collapsedTaskIds))
            {
                if (!maximumDepth.HasValue || node.Depth < maximumDepth.Value)
                {
                    yield return node;
                }
            }
        }
    }

    private static TodoCompletionFilter? EffectiveCompletionFilter(
        string viewId,
        TodoCompletionFilter completionFilter)
    {
        return viewId switch
        {
            TodoViewIds.Completed when completionFilter == TodoCompletionFilter.Incomplete => null,
            TodoViewIds.Completed => TodoCompletionFilter.Completed,
            TodoViewIds.Uncompleted when completionFilter == TodoCompletionFilter.Completed => null,
            TodoViewIds.Uncompleted => TodoCompletionFilter.Incomplete,
            _ => completionFilter
        };
    }

    private static List<TodoTask> IncludeUnmatchedParents(
        TodoData data,
        IReadOnlyCollection<TodoTask> matching)
    {
        var activeTasksById = data.Tasks
            .Where(task => task.DeletedAt is null)
            .ToDictionary(task => task.Id, StringComparer.Ordinal);
        var includedIds = matching.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var task in matching)
        {
            var current = task;
            var seen = new HashSet<string>(StringComparer.Ordinal) { task.Id };
            while (!string.IsNullOrWhiteSpace(current.ParentTaskId) &&
                   activeTasksById.TryGetValue(current.ParentTaskId, out var parent) &&
                   seen.Add(parent.Id))
            {
                includedIds.Add(parent.Id);
                current = parent;
            }
        }

        return data.Tasks.Where(task => task.DeletedAt is null && includedIds.Contains(task.Id)).ToList();
    }

    private static IEnumerable<TodoTaskNode> FlattenTaskNode(
        TodoData data,
        TodoTask task,
        IReadOnlyDictionary<string, List<TodoTask>> childrenByParent,
        ISet<string>? collapsedTaskIds)
    {
        yield return new TodoTaskNode
        {
            Task = task,
            Depth = TaskIndentDepth(data, task)
        };

        if (collapsedTaskIds?.Contains(task.Id) == true || !childrenByParent.TryGetValue(task.Id, out var children))
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var node in FlattenTaskNode(data, child, childrenByParent, collapsedTaskIds))
            {
                yield return node;
            }
        }
    }

    private static IOrderedEnumerable<TodoTask> OrderSiblings(IEnumerable<TodoTask> tasks, bool completed)
    {
        return completed
            ? tasks.OrderBy(SortOrderKey)
                .ThenByDescending(task => task.CompletedAt ?? task.UpdatedAt)
                .ThenBy(task => task.CreatedAt)
            : tasks.OrderBy(SortOrderKey)
                .ThenBy(task => task.StartDate.Date)
                .ThenBy(task => task.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(task => task.IsImportant)
                .ThenBy(task => task.CreatedAt);
    }

    private static double SortOrderKey(TodoTask task)
    {
        return task.SortOrder <= 0 ? double.MaxValue : task.SortOrder;
    }

    private static DateTime CompletedDate(TodoTask task)
    {
        return (task.CompletedAt ?? task.UpdatedAt).ToLocalTime().Date;
    }

    private static bool MatchesDateFilter(
        TodoTask task,
        bool completed,
        DateTime currentDate,
        TodoDateRangeFilter? dateFilter)
    {
        if (dateFilter is not { IsValid: true })
        {
            return true;
        }

        var rangeStart = dateFilter.StartDate.Date;
        var rangeEnd = dateFilter.EndDate.Date;
        if (dateFilter.Mode == TodoDateFilterMode.StartDate)
        {
            return task.StartDate.Date >= rangeStart && task.StartDate.Date <= rangeEnd;
        }

        var executionEnd = task.DueDate?.Date ?? (completed ? CompletedDate(task) : currentDate);
        executionEnd = executionEnd < task.StartDate.Date ? task.StartDate.Date : executionEnd;
        return task.StartDate.Date <= rangeEnd && executionEnd >= rangeStart;
    }
}
