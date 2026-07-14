namespace Fowan.Todo.Shared.Models;

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
    public string ColorId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public static class TodoListColorIds
{
    public const string Cyan = "cyan";
    public const string Blue = "blue";
    public const string Indigo = "indigo";
    public const string Purple = "purple";
    public const string Pink = "pink";
    public const string Red = "red";
    public const string Orange = "orange";
    public const string Green = "green";
    public const string Cobalt = "cobalt";
    public const string Mauve = "mauve";
    public const string Olive = "olive";
    public const string Copper = "copper";

    public static IReadOnlyList<string> All { get; } =
    [
        Cyan,
        Blue,
        Indigo,
        Purple,
        Pink,
        Red,
        Orange,
        Green,
        Cobalt,
        Mauve,
        Olive,
        Copper
    ];

    public static string Normalize(string? colorId, string? listId = null)
    {
        if (!string.IsNullOrWhiteSpace(colorId) &&
            All.Contains(colorId.Trim(), StringComparer.Ordinal))
        {
            return colorId.Trim();
        }

        return listId switch
        {
            "default" => Cyan,
            "personal" => Green,
            "study" => Purple,
            _ => Blue
        };
    }
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
    public DateTimeOffset? DeletedAt { get; set; }
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
    public bool IsStickyFloatingModeEnabled { get; set; }
    public string? StickyFloatingEdge { get; set; }
    public double? StickyFloatingTop { get; set; }
    public bool HasCompletedMainOnboarding { get; set; }
    public bool IsRecycleBinEnabled { get; set; } = true;
    public string RecycleBinRetentionPreset { get; set; } = TodoRecycleBinRetentionPresets.ThirtyDays;
    public int RecycleBinCustomRetentionDays { get; set; } = 30;
}

public static class TodoStickyFloatingEdges
{
    public const string Left = "left";
    public const string Right = "right";
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
    public const string RecycleBin = "recycle-bin";

    public static string List(string listId) => $"list:{listId}";

    public static bool IsList(string viewId) => viewId.StartsWith("list:", StringComparison.Ordinal);

    public static string ListId(string viewId) => IsList(viewId) ? viewId[5..] : string.Empty;
}

public static class TodoRecycleBinRetentionPresets
{
    public const string SevenDays = "7";
    public const string ThirtyDays = "30";
    public const string NinetyDays = "90";
    public const string Custom = "custom";
}

public enum TodoDateFilterMode
{
    StartDate,
    ExecutionPeriod
}

public sealed class TodoDateRangeFilter
{
    public TodoDateFilterMode Mode { get; init; } = TodoDateFilterMode.StartDate;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }

    public bool IsValid => StartDate != default && EndDate != default && StartDate.Date <= EndDate.Date;
}
