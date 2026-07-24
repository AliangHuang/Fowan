using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Collections.Immutable;

namespace Fowan.Todo.Shared.Application;

public sealed record TodoListSnapshot(
    string Id,
    string Name,
    string ColorId,
    DateTimeOffset CreatedAt);

public sealed record TodoTaskSnapshot(
    string Id,
    string Title,
    string Notes,
    string ListId,
    string? ParentTaskId,
    double SortOrder,
    bool IsImportant,
    DateTime StartDate,
    DateTime? DueDate,
    TodoRecurrenceRule? Recurrence,
    string? RecurrenceSourceTaskId,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TodoSettingsSnapshot(
    string Theme,
    string CurrentViewId,
    string? SelectedTaskId,
    ImmutableHashSet<string> CollapsedTaskIds,
    bool IsCompletedExpanded,
    bool IsStickyModeEnabled,
    bool IsStickyTopmost,
    bool IsStickyCompletedExpanded,
    bool IsStickyTitleHidden,
    bool IsStickyAddTaskMinimized,
    bool IsStickyMenuAutoHideEnabled,
    double StickyTitleFontSize,
    double StickyOpacity,
    double StickyScale,
    double? StickyLeft,
    double? StickyTop,
    double? StickyWidth,
    double? StickyHeight,
    bool IsStickyFloatingModeEnabled,
    string? StickyFloatingEdge,
    double? StickyFloatingTop,
    bool HasCompletedMainOnboarding,
    bool IsRecycleBinEnabled,
    string RecycleBinRetentionPreset,
    int RecycleBinCustomRetentionDays);

public sealed record TodoSnapshot(
    int SchemaVersion,
    ImmutableArray<TodoListSnapshot> Lists,
    ImmutableArray<TodoTaskSnapshot> Tasks,
    TodoSettingsSnapshot Settings)
{
    internal static TodoSnapshot From(TodoData data, TodoSettings settings) => new(
        data.SchemaVersion,
        data.Lists.Select(item => new TodoListSnapshot(item.Id, item.Name, item.ColorId, item.CreatedAt)).ToImmutableArray(),
        data.Tasks.Select(item => new TodoTaskSnapshot(
            item.Id, item.Title, item.Notes, item.ListId, item.ParentTaskId, item.SortOrder,
            item.IsImportant, item.StartDate, item.DueDate,
            TodoRecurrenceRules.Clone(item.Recurrence), item.RecurrenceSourceTaskId,
            item.IsCompleted, item.CompletedAt,
            item.DeletedAt, item.CreatedAt, item.UpdatedAt)).ToImmutableArray(),
        new TodoSettingsSnapshot(
            settings.Theme, settings.CurrentViewId, settings.SelectedTaskId,
            settings.CollapsedTaskIds.ToImmutableHashSet(StringComparer.Ordinal),
            settings.IsCompletedExpanded, settings.IsStickyModeEnabled, settings.IsStickyTopmost,
            settings.IsStickyCompletedExpanded, settings.IsStickyTitleHidden,
            settings.IsStickyAddTaskMinimized, settings.IsStickyMenuAutoHideEnabled,
            settings.StickyTitleFontSize,
            settings.StickyOpacity, settings.StickyScale,
            settings.StickyLeft, settings.StickyTop, settings.StickyWidth, settings.StickyHeight,
            settings.IsStickyFloatingModeEnabled, settings.StickyFloatingEdge,
            settings.StickyFloatingTop, settings.HasCompletedMainOnboarding,
            settings.IsRecycleBinEnabled, settings.RecycleBinRetentionPreset,
            settings.RecycleBinCustomRetentionDays));

    public TodoData ToQueryData() => new()
    {
        SchemaVersion = SchemaVersion,
        Lists = Lists.Select(item => new TodoList
        {
            Id = item.Id,
            Name = item.Name,
            ColorId = item.ColorId,
            CreatedAt = item.CreatedAt
        }).ToList(),
        Tasks = Tasks.Select(item => new TodoTask
        {
            Id = item.Id,
            Title = item.Title,
            Notes = item.Notes,
            ListId = item.ListId,
            ParentTaskId = item.ParentTaskId,
            SortOrder = item.SortOrder,
            IsImportant = item.IsImportant,
            StartDate = item.StartDate,
            DueDate = item.DueDate,
            Recurrence = TodoRecurrenceRules.Clone(item.Recurrence),
            RecurrenceSourceTaskId = item.RecurrenceSourceTaskId,
            IsCompleted = item.IsCompleted,
            CompletedAt = item.CompletedAt,
            DeletedAt = item.DeletedAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        }).ToList()
    };

    public TodoSettings ToQuerySettings() => new()
    {
        Theme = Settings.Theme,
        CurrentViewId = Settings.CurrentViewId,
        SelectedTaskId = Settings.SelectedTaskId,
        CollapsedTaskIds = [.. Settings.CollapsedTaskIds],
        IsCompletedExpanded = Settings.IsCompletedExpanded,
        IsStickyModeEnabled = Settings.IsStickyModeEnabled,
        IsStickyTopmost = Settings.IsStickyTopmost,
        IsStickyCompletedExpanded = Settings.IsStickyCompletedExpanded,
        IsStickyTitleHidden = Settings.IsStickyTitleHidden,
        IsStickyAddTaskMinimized = Settings.IsStickyAddTaskMinimized,
        IsStickyMenuAutoHideEnabled = Settings.IsStickyMenuAutoHideEnabled,
        StickyTitleFontSize = Settings.StickyTitleFontSize,
        StickyOpacity = Settings.StickyOpacity,
        StickyScale = Settings.StickyScale,
        StickyLeft = Settings.StickyLeft,
        StickyTop = Settings.StickyTop,
        StickyWidth = Settings.StickyWidth,
        StickyHeight = Settings.StickyHeight,
        IsStickyFloatingModeEnabled = Settings.IsStickyFloatingModeEnabled,
        StickyFloatingEdge = Settings.StickyFloatingEdge,
        StickyFloatingTop = Settings.StickyFloatingTop,
        HasCompletedMainOnboarding = Settings.HasCompletedMainOnboarding,
        IsRecycleBinEnabled = Settings.IsRecycleBinEnabled,
        RecycleBinRetentionPreset = Settings.RecycleBinRetentionPreset,
        RecycleBinCustomRetentionDays = Settings.RecycleBinCustomRetentionDays
    };
}

public interface ITodoCommands
{
    bool HasIncompleteDescendants(string taskId);
    int SetTaskCompleted(string taskId, bool completed, bool includeDescendants);
    bool ToggleTaskImportant(string taskId);
    TodoTaskSnapshot AddTask(string title, string listId, bool isImportant, DateTime startDate,
        DateTime? dueDate, string? parentTaskId = null, string? notes = null, TodoRecurrenceRule? recurrence = null);
    TodoListSnapshot AddList(string name);
    bool RenameList(string listId, string name);
    bool DeleteList(string listId);
    bool SetListColor(string listId, string colorId);
    bool UpdateTaskTitle(string taskId, string title);
    bool UpdateTaskCompletionTime(string taskId, DateTimeOffset completedAt);
    bool MoveTaskToList(string taskId, string listId);
    bool UpdateTaskStartDate(string taskId, DateTime startDate);
    bool UpdateTaskDueDate(string taskId, DateTime? dueDate);
    bool UpdateTaskNotes(string taskId, string notes);
    bool UpdateTaskDetails(string taskId, string title, string notes);
    bool DeleteTaskTree(string taskId);
    bool RestoreTaskTree(string taskId);
    bool PermanentlyDeleteTaskTree(string taskId);
    int RestoreCompleted(IEnumerable<string> taskIds);
    int DeleteTaskTrees(IEnumerable<string> taskIds);
    bool CanApplyTaskDrop(string draggedId, string targetId, TodoTaskDropPlacement placement);
    bool TryApplyTaskDrop(string draggedId, string targetId, TodoTaskDropPlacement placement);
    void SetPresentationPreferences(string currentViewId, string? selectedTaskId);
    void SetTheme(string theme);
    void SetStickyModeEnabled(bool enabled);
    void ToggleTaskCollapsed(string taskId);
    void ToggleCompletedExpanded();
    void SetOnboardingCompleted(bool completed);
    void UpdateRecycleBinSettings(bool enabled, string retentionPreset, int customRetentionDays);
    int PurgeExpiredRecycleBin();
}
