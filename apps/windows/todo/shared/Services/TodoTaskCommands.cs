using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public static class TodoTaskCommands
{
    public static bool HasIncompleteDescendants(TodoData data, string taskId)
    {
        ArgumentNullException.ThrowIfNull(data);
        var descendants = TodoQuery.DescendantIds(data, taskId).ToHashSet(StringComparer.Ordinal);
        return data.Tasks.Any(task => descendants.Contains(task.Id) && !task.IsCompleted);
    }

    public static int SetCompleted(
        TodoData data,
        string taskId,
        bool completed,
        bool includeDescendants,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var ids = includeDescendants
            ? TodoQuery.DescendantIds(data, taskId).Append(taskId).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>([taskId], StringComparer.Ordinal);
        var changed = 0;
        foreach (var task in data.Tasks.Where(task => ids.Contains(task.Id)))
        {
            if (task.IsCompleted == completed)
            {
                continue;
            }

            task.IsCompleted = completed;
            task.CompletedAt = completed ? now : null;
            task.UpdatedAt = now;
            changed++;
        }
        return changed;
    }

    public static bool ToggleImportant(TodoData data, string taskId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var task = data.Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
        if (task is null)
        {
            return false;
        }

        task.IsImportant = !task.IsImportant;
        task.UpdatedAt = now;
        return true;
    }
}
