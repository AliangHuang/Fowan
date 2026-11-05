using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public enum TodoTaskDropPlacement { Before, Child, After, TopLevelEnd }

public static class TodoTaskDropController
{
    public static bool CanApply(TodoData data, string draggedId, string targetId, TodoTaskDropPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(data);
        var dragged = Find(data, draggedId);
        var target = Find(data, targetId);
        if (dragged is null || target is null || dragged.IsCompleted != target.IsCompleted ||
            placement != TodoTaskDropPlacement.TopLevelEnd && dragged.Id == target.Id)
        {
            return false;
        }
        var descendants = TodoQuery.DescendantIds(data, dragged.Id).ToHashSet(StringComparer.Ordinal);
        if (placement != TodoTaskDropPlacement.TopLevelEnd && descendants.Contains(target.Id))
        {
            return false;
        }
        var parentId = ParentId(target, placement);
        return parentId != dragged.Id && (parentId is null || !descendants.Contains(parentId)) &&
               !IsOriginalPosition(data, dragged, target, placement, parentId) &&
               CanMoveToParent(data, dragged, parentId);
    }

    public static bool TryApply(
        TodoData data,
        TodoSettings settings,
        string draggedId,
        string targetId,
        TodoTaskDropPlacement placement,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!CanApply(data, draggedId, targetId, placement)) return false;
        var dragged = Find(data, draggedId)!;
        var target = Find(data, targetId)!;
        var oldParentId = dragged.ParentTaskId;
        var parentId = ParentId(target, placement);
        var parentChanged = !ParentIdsEqual(oldParentId, parentId);
        dragged.ParentTaskId = string.IsNullOrWhiteSpace(parentId) ? null : parentId;
        if (parentChanged)
        {
            MoveSubtreeToList(data, dragged.Id, ResolveListId(data, target, parentId), now);
            ReassignOrders(OrderedSiblings(data, oldParentId, dragged.IsCompleted));
        }

        var siblings = OrderedSiblings(data, parentId, dragged.IsCompleted)
            .Where(task => task.Id != dragged.Id).ToList();
        if (placement == TodoTaskDropPlacement.TopLevelEnd)
        {
            siblings.Add(dragged);
        }
        else if (placement == TodoTaskDropPlacement.Child)
        {
            settings.CollapsedTaskIds.RemoveAll(id => id == target.Id);
            siblings.Insert(0, dragged);
        }
        else
        {
            var targetIndex = siblings.FindIndex(task => task.Id == target.Id);
            siblings.Insert(
                targetIndex < 0 ? siblings.Count : placement == TodoTaskDropPlacement.After ? targetIndex + 1 : targetIndex,
                dragged);
        }
        ReassignOrders(siblings);
        dragged.UpdatedAt = now;
        return true;
    }

    private static bool IsOriginalPosition(
        TodoData data, TodoTask dragged, TodoTask target, TodoTaskDropPlacement placement, string? parentId)
    {
        if (placement == TodoTaskDropPlacement.TopLevelEnd)
        {
            var roots = OrderedSiblings(data, null, dragged.IsCompleted);
            return ParentIdsEqual(dragged.ParentTaskId, null) && roots.Count > 0 && roots[^1].Id == dragged.Id;
        }
        if (placement == TodoTaskDropPlacement.Child)
        {
            var children = OrderedSiblings(data, target.Id, dragged.IsCompleted);
            return ParentIdsEqual(dragged.ParentTaskId, target.Id) && children.Count > 0 && children[0].Id == dragged.Id;
        }
        if (!ParentIdsEqual(dragged.ParentTaskId, parentId)) return false;
        var siblings = OrderedSiblings(data, parentId, dragged.IsCompleted);
        var draggedIndex = siblings.FindIndex(task => task.Id == dragged.Id);
        var targetIndex = siblings.FindIndex(task => task.Id == target.Id);
        return draggedIndex >= 0 && targetIndex >= 0 &&
               (placement == TodoTaskDropPlacement.Before ? draggedIndex + 1 == targetIndex : draggedIndex - 1 == targetIndex);
    }

    private static bool CanMoveToParent(TodoData data, TodoTask dragged, string? parentId)
    {
        var depth = 1;
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            var parent = Find(data, parentId);
            if (parent is null) return false;
            depth = TodoQuery.TaskDepth(data, parent) + 1;
            if (data.Tasks.Count(task => task.Id != dragged.Id && task.ParentTaskId == parentId) >= TodoQuery.MaxChildTasksPerTask)
            {
                return false;
            }
        }
        return depth + MaxDescendantOffset(data, dragged.Id) <= TodoQuery.MaxTaskTreeDepth;
    }

    private static int MaxDescendantOffset(TodoData data, string id)
    {
        var children = data.Tasks.Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        return MaxDescendantOffset(id, children, new HashSet<string>(StringComparer.Ordinal));
    }

    private static int MaxDescendantOffset(
        string id, IReadOnlyDictionary<string, List<TodoTask>> children, HashSet<string> seen)
    {
        if (!seen.Add(id) || !children.TryGetValue(id, out var direct) || direct.Count == 0) return 0;
        return direct.Max(child => 1 + MaxDescendantOffset(child.Id, children, seen));
    }

    private static string? ParentId(TodoTask target, TodoTaskDropPlacement placement) => placement switch
    {
        TodoTaskDropPlacement.Child => target.Id,
        TodoTaskDropPlacement.TopLevelEnd => null,
        _ => target.ParentTaskId
    };

    private static List<TodoTask> OrderedSiblings(TodoData data, string? parentId, bool completed) =>
        data.Tasks.Where(task => task.IsCompleted == completed && ParentIdsEqual(task.ParentTaskId, parentId))
            .OrderBy(task => task.SortOrder <= 0 ? double.MaxValue : task.SortOrder)
            .ThenBy(task => task.CreatedAt).ToList();

    private static void ReassignOrders(IReadOnlyList<TodoTask> siblings)
    {
        var order = 1000.0;
        foreach (var task in siblings) { task.SortOrder = order; order += 1000.0; }
    }

    private static void MoveSubtreeToList(TodoData data, string id, string listId, DateTimeOffset now)
    {
        var ids = TodoQuery.DescendantIds(data, id).Append(id).ToHashSet(StringComparer.Ordinal);
        foreach (var task in data.Tasks.Where(task => ids.Contains(task.Id)))
        {
            task.ListId = listId;
            task.UpdatedAt = now;
        }
    }

    private static string ResolveListId(TodoData data, TodoTask target, string? parentId) =>
        !string.IsNullOrWhiteSpace(parentId) ? Find(data, parentId)?.ListId ?? target.ListId : target.ListId;
    private static TodoTask? Find(TodoData data, string id) => data.Tasks.FirstOrDefault(task => task.Id == id);
    private static bool ParentIdsEqual(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
}
