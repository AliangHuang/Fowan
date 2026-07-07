namespace Fowan.Todo.Core.Models;

public sealed class TodoData
{
    public int SchemaVersion { get; set; } = 1;
    public List<TodoList> Lists { get; set; } = [];
    public List<TodoTask> Tasks { get; set; } = [];
}

public sealed class TodoList
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class TodoTask
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ListId { get; set; } = string.Empty;
    public string? ParentTaskId { get; set; }
    public double SortOrder { get; set; }
    public bool IsImportant { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class TodoTaskNode
{
    public TodoTask Task { get; set; } = new();
    public int Depth { get; set; }
}

public sealed class TodoSettings
{
    public const double MinStickyOpacity = 0.35;
    public const double MaxStickyOpacity = 1.0;
    public const double MinStickyScale = 0.5;
    public const double MaxStickyScale = 2.0;

    public string Theme { get; set; } = TodoThemeIds.System;
    public string CurrentViewId { get; set; } = TodoViewIds.Today;
    public string? SelectedTaskId { get; set; }
    public List<string> CollapsedTaskIds { get; set; } = [];
    public bool IsCompletedExpanded { get; set; } = true;
    public bool IsStickyModeEnabled { get; set; }
    public bool IsStickyTopmost { get; set; } = true;
    public bool IsStickyCompletedExpanded { get; set; } = true;
    public double StickyOpacity { get; set; } = MaxStickyOpacity;
    public double StickyScale { get; set; } = 1.0;
    public double? StickyLeft { get; set; }
    public double? StickyTop { get; set; }
    public double? StickyWidth { get; set; }
    public double? StickyHeight { get; set; }
}

public static class TodoThemeIds
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
}

public static class TodoViewIds
{
    public const string Today = "today";
    public const string Planned = "planned";
    public const string Important = "important";
    public const string All = "all";
    public const string Completed = "completed";

    public static string List(string listId) => $"list:{listId}";

    public static bool IsList(string viewId) => viewId.StartsWith("list:", StringComparison.Ordinal);

    public static string ListId(string viewId) => IsList(viewId) ? viewId[5..] : string.Empty;
}
