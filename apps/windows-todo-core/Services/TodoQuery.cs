using Fowan.Todo.Core.Models;

namespace Fowan.Todo.Core.Services;

public static class TodoQuery
{
    public const int MaxTaskTreeDepth = 3;
    public const int MaxChildTasksPerTask = 100;

    public static IEnumerable<TodoTask> ActiveTasksForView(TodoData data, string viewId, DateTime? today = null)
    {
        return ActiveTaskNodesForView(data, viewId, today).Select(node => node.Task);
    }

    public static IEnumerable<TodoTask> CompletedTasksForView(TodoData data, string viewId, DateTime? today = null)
    {
        return CompletedTaskNodesForView(data, viewId, today).Select(node => node.Task);
    }

    public static IEnumerable<TodoTaskNode> ActiveTaskNodesForView(TodoData data, string viewId, DateTime? today = null)
    {
        return TaskNodesForView(data, viewId, completed: false, today);
    }

    public static IEnumerable<TodoTaskNode> ActiveTaskNodesForView(
        TodoData data,
        string viewId,
        ISet<string>? collapsedTaskIds,
        DateTime? today = null)
    {
        return TaskNodesForView(data, viewId, completed: false, collapsedTaskIds: collapsedTaskIds, today: today);
    }

    public static IEnumerable<TodoTaskNode> CompletedTaskNodesForView(TodoData data, string viewId, DateTime? today = null)
    {
        return TaskNodesForView(data, viewId == TodoViewIds.Completed ? TodoViewIds.All : viewId, completed: true, today);
    }

    public static IEnumerable<TodoTaskNode> CompletedTaskNodesForView(
        TodoData data,
        string viewId,
        ISet<string>? collapsedTaskIds,
        DateTime? today = null)
    {
        return TaskNodesForView(
            data,
            viewId == TodoViewIds.Completed ? TodoViewIds.All : viewId,
            completed: true,
            collapsedTaskIds: collapsedTaskIds,
            today: today);
    }

    public static IEnumerable<TodoTaskNode> TaskNodesForView(TodoData data, string viewId, bool completed, DateTime? today = null)
    {
        return TaskNodesForView(data, viewId, completed, collapsedTaskIds: null, today);
    }

    public static IEnumerable<TodoTaskNode> TaskNodesForView(
        TodoData data,
        string viewId,
        bool completed,
        ISet<string>? collapsedTaskIds,
        DateTime? today = null)
    {
        var visible = FilterTasks(data, viewId, completed, today).ToList();
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
                yield return node;
            }
        }
    }

    public static IEnumerable<TodoTask> FilterTasks(TodoData data, string viewId, bool completed, DateTime? today = null)
    {
        var currentDate = (today ?? DateTime.Today).Date;
        return data.Tasks.Where(task =>
        {
            if (task.IsCompleted != completed)
            {
                return false;
            }

            if (TodoViewIds.IsList(viewId))
            {
                return string.Equals(task.ListId, TodoViewIds.ListId(viewId), StringComparison.Ordinal);
            }

            return viewId switch
            {
                TodoViewIds.Today => task.StartDate.Date <= currentDate &&
                    (!task.DueDate.HasValue || task.DueDate.Value.Date <= currentDate),
                TodoViewIds.Planned => task.StartDate.Date > currentDate ||
                    (task.DueDate.HasValue && task.DueDate.Value.Date > currentDate),
                TodoViewIds.Important => task.IsImportant,
                TodoViewIds.All or TodoViewIds.Completed => true,
                _ => true
            };
        });
    }

    public static bool IsKnownView(TodoData data, string viewId)
    {
        if (viewId is TodoViewIds.Today or TodoViewIds.Planned or TodoViewIds.Important or TodoViewIds.All or TodoViewIds.Completed)
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
            TodoViewIds.All => "全部任务",
            TodoViewIds.Completed => "已完成",
            _ => "今日任务"
        };
    }

    public static string DefaultListId(TodoData data)
    {
        return data.Lists.FirstOrDefault(list => string.Equals(list.Id, TodoStore.DefaultListId, StringComparison.Ordinal))?.Id
            ?? data.Lists.First().Id;
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
        return data.Tasks.Count(task => string.Equals(task.ParentTaskId, taskId, StringComparison.Ordinal));
    }

    public static bool CanAddChild(TodoData data, TodoTask parent)
    {
        return TaskDepth(data, parent) < MaxTaskTreeDepth &&
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

        if (collapsedTaskIds?.Contains(task.Id) == true)
        {
            yield break;
        }

        if (!childrenByParent.TryGetValue(task.Id, out var children))
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
}
