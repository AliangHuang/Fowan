using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public static class TodoRecycleBin
{
    public static int RetentionDays(TodoSettings settings)
    {
        return settings.RecycleBinRetentionPreset switch
        {
            TodoRecycleBinRetentionPresets.SevenDays => 7,
            TodoRecycleBinRetentionPresets.NinetyDays => 90,
            TodoRecycleBinRetentionPresets.Custom => Math.Clamp(settings.RecycleBinCustomRetentionDays, 1, 365),
            _ => 30
        };
    }

    public static IReadOnlyCollection<string> TaskTreeIds(TodoData data, string taskId)
    {
        return TodoQuery.DescendantIds(data, taskId)
            .Append(taskId)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static bool DeleteTaskTree(TodoData data, TodoSettings settings, string taskId, DateTimeOffset? now = null)
    {
        var treeIds = TaskTreeIds(data, taskId);
        if (treeIds.Count == 0 || !data.Tasks.Any(task => treeIds.Contains(task.Id)))
        {
            return false;
        }

        if (!settings.IsRecycleBinEnabled)
        {
            data.Tasks.RemoveAll(task => treeIds.Contains(task.Id));
            return true;
        }

        var deletedAt = now ?? DateTimeOffset.Now;
        foreach (var task in data.Tasks.Where(task => treeIds.Contains(task.Id)))
        {
            task.DeletedAt = deletedAt;
            task.UpdatedAt = deletedAt;
        }

        return true;
    }

    public static bool RestoreTaskTree(TodoData data, string taskId, DateTimeOffset? now = null)
    {
        var rootId = DeletedTreeRootId(data, taskId);
        if (rootId is null)
        {
            return false;
        }

        var treeIds = TaskTreeIds(data, rootId);
        var updatedAt = now ?? DateTimeOffset.Now;
        foreach (var task in data.Tasks.Where(task => treeIds.Contains(task.Id)))
        {
            task.DeletedAt = null;
            task.UpdatedAt = updatedAt;
        }

        return true;
    }

    public static bool PermanentlyDeleteTaskTree(TodoData data, string taskId)
    {
        var rootId = DeletedTreeRootId(data, taskId) ?? taskId;
        var treeIds = TaskTreeIds(data, rootId);
        var removed = data.Tasks.RemoveAll(task => treeIds.Contains(task.Id));
        return removed > 0;
    }

    public static int PurgeExpired(TodoData data, TodoSettings settings, DateTimeOffset? now = null)
    {
        if (!settings.IsRecycleBinEnabled)
        {
            return 0;
        }

        var cutoff = (now ?? DateTimeOffset.Now).AddDays(-RetentionDays(settings));
        return data.Tasks.RemoveAll(task => task.DeletedAt is not null && task.DeletedAt.Value <= cutoff);
    }

    private static string? DeletedTreeRootId(TodoData data, string taskId)
    {
        var byId = data.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        if (!byId.TryGetValue(taskId, out var current) || current.DeletedAt is null)
        {
            return null;
        }

        var root = current;
        var visited = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        while (!string.IsNullOrWhiteSpace(root.ParentTaskId) &&
            byId.TryGetValue(root.ParentTaskId, out var parent) &&
            parent.DeletedAt is not null &&
            visited.Add(parent.Id))
        {
            root = parent;
        }

        return root.Id;
    }
}
