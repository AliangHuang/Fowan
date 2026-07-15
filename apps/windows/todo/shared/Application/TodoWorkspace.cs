using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;

namespace Fowan.Todo.Shared.Application;

[Flags]
public enum TodoChangeSet
{
    None = 0,
    Tasks = 1,
    Lists = 2,
    Settings = 4,
    Selection = 8,
    All = Tasks | Lists | Settings | Selection
}

public sealed class TodoWorkspace : ITodoCommands
{
    private readonly TodoPersistenceController _persistence;
    private TodoData _committedData;
    private TodoSettings _committedSettings;

    public TodoWorkspace(ITodoRepository repository)
    {
        _persistence = new TodoPersistenceController(repository);
        Data = _persistence.LoadData();
        Settings = _persistence.LoadSettings();
        _committedData = Clone(Data);
        _committedSettings = Clone(Settings);
    }

    public static TodoWorkspace CreateDefault() => new(new TodoStore());

    public TodoData Data { get; private set; }

    public TodoSettings Settings { get; private set; }

    public TodoSnapshot State => TodoSnapshot.From(Data, Settings);

    public string DefaultListId => _persistence.DefaultListId;

    public event EventHandler<TodoChangeSet>? Changed;

    public TodoData LoadData()
    {
        Data = _persistence.LoadData();
        return Data;
    }

    public TodoSettings LoadSettings()
    {
        Settings = _persistence.LoadSettings();
        return Settings;
    }

    public void Reload()
    {
        Data = _persistence.LoadData();
        Settings = _persistence.LoadSettings();
        _committedData = Clone(Data);
        _committedSettings = Clone(Settings);
        Publish(TodoChangeSet.All);
    }

    public void SaveData()
    {
        try
        {
            _persistence.SaveData(Data);
            _committedData = Clone(Data);
        }
        catch
        {
            Data = Clone(_committedData);
            throw;
        }
        Publish(TodoChangeSet.Tasks | TodoChangeSet.Lists);
    }

    public void SaveData(TodoData data)
    {
        Data = data;
        SaveData();
    }

    public void SaveSettings()
    {
        try
        {
            _persistence.SaveSettings(Settings);
            _committedSettings = Clone(Settings);
        }
        catch
        {
            Settings = Clone(_committedSettings);
            throw;
        }
        Publish(TodoChangeSet.Settings | TodoChangeSet.Selection);
    }

    public void SaveSettings(TodoSettings settings)
    {
        Settings = settings;
        SaveSettings();
    }

    public void SaveAll()
    {
        var priorData = Clone(_committedData);
        var priorSettings = Clone(_committedSettings);
        try
        {
            _persistence.SaveData(Data);
            _persistence.SaveSettings(Settings);
            _committedData = Clone(Data);
            _committedSettings = Clone(Settings);
        }
        catch (Exception error)
        {
            Data = priorData;
            Settings = priorSettings;
            try
            {
                _persistence.SaveData(priorData);
                _persistence.SaveSettings(priorSettings);
            }
            catch (Exception compensation)
            {
                throw new InvalidOperationException(
                    "Todo persistence failed and the previous durable state could not be restored.",
                    new AggregateException(error, compensation));
            }
            throw;
        }
        Publish(TodoChangeSet.All);
    }

    public bool UpdateData(Func<TodoData, TodoSettings, bool> update)
    {
        var changed = _persistence.UpdateData(update);
        if (changed)
        {
            Reload();
        }
        return changed;
    }

    public string CreateTaskId() => _persistence.CreateTaskId();

    public string CreateListId() => _persistence.CreateListId();

    public bool HasIncompleteDescendants(string taskId) =>
        TodoTaskCommands.HasIncompleteDescendants(Data, taskId);

    public int SetTaskCompleted(string taskId, bool completed, bool includeDescendants)
    {
        var changed = TodoTaskCommands.SetCompleted(
            Data,
            taskId,
            completed,
            includeDescendants,
            DateTimeOffset.Now);
        if (changed > 0)
        {
            SaveData();
        }
        return changed;
    }

    public bool ToggleTaskImportant(string taskId)
    {
        if (!TodoTaskCommands.ToggleImportant(Data, taskId, DateTimeOffset.Now))
        {
            return false;
        }
        SaveData();
        return true;
    }

    public TodoTaskSnapshot AddTask(
        string title,
        string listId,
        bool isImportant,
        DateTime startDate,
        DateTime? dueDate,
        string? parentTaskId = null,
        string? notes = null)
    {
        var now = DateTimeOffset.Now;
        parentTaskId = NormalizeParentTaskId(parentTaskId);
        var task = new TodoTask
        {
            Id = CreateTaskId(),
            Title = title.Trim(),
            ListId = ListExists(listId) ? listId : DefaultList(),
            ParentTaskId = parentTaskId,
            IsImportant = isImportant,
            StartDate = startDate.Date,
            DueDate = dueDate?.Date,
            Notes = notes?.Trim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        Data.Tasks.Add(task);
        InsertAsFirstChild(task);
        SaveAll();
        return State.Tasks.First(item => item.Id == task.Id);
    }

    public TodoListSnapshot AddList(string name)
    {
        var list = new TodoList
        {
            Id = CreateListId(),
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.Now
        };
        Data.Lists.Add(list);
        SaveData();
        return State.Lists.First(item => item.Id == list.Id);
    }

    public bool RenameList(string listId, string name)
    {
        var list = FindList(listId);
        var normalized = name.Trim();
        if (list is null || normalized.Length == 0 || string.Equals(list.Name, normalized, StringComparison.Ordinal))
        {
            return false;
        }
        list.Name = normalized;
        SaveData();
        return true;
    }

    public bool DeleteList(string listId)
    {
        var list = FindList(listId);
        if (list is null || IsDefaultList(listId) || Data.Lists.Count <= 1)
        {
            return false;
        }
        var now = DateTimeOffset.Now;
        var fallbackListId = DefaultList();
        foreach (var task in Data.Tasks.Where(task =>
                     task.DeletedAt is null && string.Equals(task.ListId, listId, StringComparison.Ordinal)))
        {
            task.ListId = fallbackListId;
            task.UpdatedAt = now;
        }
        Data.Lists.Remove(list);
        SaveData();
        return true;
    }

    public bool SetListColor(string listId, string colorId)
    {
        var list = FindList(listId);
        if (list is null) return false;
        var normalized = TodoListColorIds.Normalize(colorId, list.Id);
        if (string.Equals(list.ColorId, normalized, StringComparison.Ordinal)) return false;
        list.ColorId = normalized;
        SaveData();
        return true;
    }

    public bool UpdateTaskTitle(string taskId, string title) => UpdateTask(taskId, task =>
    {
        var normalized = title.Trim();
        if (normalized.Length == 0 || string.Equals(task.Title, normalized, StringComparison.Ordinal)) return false;
        task.Title = normalized;
        return true;
    });

    public bool UpdateTaskCompletionTime(string taskId, DateTimeOffset completedAt) =>
        UpdateTask(taskId, task =>
        {
            if (!task.IsCompleted || task.CompletedAt == completedAt) return false;
            task.CompletedAt = completedAt;
            return true;
        });

    public bool MoveTaskToList(string taskId, string listId) => UpdateTask(taskId, task =>
    {
        if (!ListExists(listId) || string.Equals(task.ListId, listId, StringComparison.Ordinal)) return false;
        task.ListId = listId;
        return true;
    });

    public bool UpdateTaskStartDate(string taskId, DateTime startDate) => UpdateTask(taskId, task =>
    {
        var normalized = startDate.Date;
        if (task.StartDate == normalized) return false;
        task.StartDate = normalized;
        return true;
    });

    public bool UpdateTaskDueDate(string taskId, DateTime? dueDate) => UpdateTask(taskId, task =>
    {
        var normalized = dueDate?.Date;
        if (task.DueDate == normalized) return false;
        task.DueDate = normalized;
        return true;
    });

    public bool UpdateTaskNotes(string taskId, string notes) => UpdateTask(taskId, task =>
    {
        var normalized = notes.Trim();
        if (string.Equals(task.Notes, normalized, StringComparison.Ordinal)) return false;
        task.Notes = normalized;
        return true;
    });

    public bool UpdateTaskDetails(string taskId, string title, string notes) => UpdateTask(taskId, task =>
    {
        var normalizedTitle = title.Trim();
        var normalizedNotes = notes.Trim();
        if (normalizedTitle.Length == 0 ||
            (string.Equals(task.Title, normalizedTitle, StringComparison.Ordinal) &&
             string.Equals(task.Notes, normalizedNotes, StringComparison.Ordinal)))
        {
            return false;
        }
        task.Title = normalizedTitle;
        task.Notes = normalizedNotes;
        return true;
    });

    public bool DeleteTaskTree(string taskId)
    {
        var treeIds = TodoRecycleBin.TaskTreeIds(Data, taskId);
        if (!TodoRecycleBin.DeleteTaskTree(Data, Settings, taskId)) return false;
        if (!Settings.IsRecycleBinEnabled) Settings.CollapsedTaskIds.RemoveAll(treeIds.Contains);
        SaveAll();
        return true;
    }

    public bool RestoreTaskTree(string taskId)
    {
        if (!TodoRecycleBin.RestoreTaskTree(Data, taskId)) return false;
        SaveData();
        return true;
    }

    public bool PermanentlyDeleteTaskTree(string taskId)
    {
        if (!TodoRecycleBin.PermanentlyDeleteTaskTree(Data, taskId)) return false;
        SaveData();
        return true;
    }

    public int RestoreCompleted(IEnumerable<string> taskIds)
    {
        var changed = 0;
        foreach (var taskId in taskIds.Distinct(StringComparer.Ordinal))
        {
            changed += TodoTaskCommands.SetCompleted(Data, taskId, false, false, DateTimeOffset.Now);
        }
        if (changed > 0) SaveData();
        return changed;
    }

    public int DeleteTaskTrees(IEnumerable<string> taskIds)
    {
        var changed = 0;
        foreach (var taskId in taskIds.Distinct(StringComparer.Ordinal).ToList())
        {
            if (DeleteTaskTreeWithoutSaving(taskId)) changed++;
        }
        if (changed > 0) SaveAll();
        return changed;
    }

    public bool CanApplyTaskDrop(
        string draggedId,
        string targetId,
        TodoTaskDropPlacement placement) =>
        TodoTaskDropController.CanApply(Data, draggedId, targetId, placement);

    public bool TryApplyTaskDrop(
        string draggedId,
        string targetId,
        TodoTaskDropPlacement placement)
    {
        if (!TodoTaskDropController.TryApply(
                Data,
                Settings,
                draggedId,
                targetId,
                placement,
                DateTimeOffset.Now))
        {
            return false;
        }
        SaveAll();
        return true;
    }

    public void SetPresentationPreferences(string currentViewId, string? selectedTaskId)
    {
        if (string.Equals(Settings.CurrentViewId, currentViewId, StringComparison.Ordinal) &&
            string.Equals(Settings.SelectedTaskId, selectedTaskId, StringComparison.Ordinal)) return;
        Settings.CurrentViewId = currentViewId;
        Settings.SelectedTaskId = selectedTaskId;
        SaveSettings();
    }

    public void SetTheme(string theme)
    {
        if (string.Equals(Settings.Theme, theme, StringComparison.Ordinal)) return;
        Settings.Theme = theme;
        SaveSettings();
    }

    public void SetStickyModeEnabled(bool enabled)
    {
        if (Settings.IsStickyModeEnabled == enabled) return;
        Settings.IsStickyModeEnabled = enabled;
        SaveSettings();
    }

    public void ToggleTaskCollapsed(string taskId)
    {
        if (Settings.CollapsedTaskIds.RemoveAll(id => string.Equals(id, taskId, StringComparison.Ordinal)) == 0)
        {
            Settings.CollapsedTaskIds.Add(taskId);
        }
        SaveSettings();
    }

    public void ToggleCompletedExpanded()
    {
        Settings.IsCompletedExpanded = !Settings.IsCompletedExpanded;
        SaveSettings();
    }

    public void SetOnboardingCompleted(bool completed)
    {
        if (Settings.HasCompletedMainOnboarding == completed) return;
        Settings.HasCompletedMainOnboarding = completed;
        SaveSettings();
    }

    public void UpdateRecycleBinSettings(bool enabled, string retentionPreset, int customRetentionDays)
    {
        Settings.IsRecycleBinEnabled = enabled;
        Settings.RecycleBinRetentionPreset = retentionPreset;
        Settings.RecycleBinCustomRetentionDays = customRetentionDays;
        SaveSettings();
    }

    public int PurgeExpiredRecycleBin()
    {
        var purged = TodoRecycleBin.PurgeExpired(Data, Settings);
        if (purged > 0) SaveData();
        return purged;
    }

    private bool UpdateTask(string taskId, Func<TodoTask, bool> update)
    {
        var task = Data.Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
        if (task is null || !update(task)) return false;
        task.UpdatedAt = DateTimeOffset.Now;
        SaveData();
        return true;
    }

    private string? NormalizeParentTaskId(string? parentTaskId)
    {
        if (string.IsNullOrWhiteSpace(parentTaskId)) return null;
        var parent = Data.Tasks.FirstOrDefault(task => string.Equals(task.Id, parentTaskId, StringComparison.Ordinal));
        return parent is not null && TodoQuery.CanAddChild(Data, parent) ? parent.Id : null;
    }

    private void InsertAsFirstChild(TodoTask task)
    {
        if (string.IsNullOrWhiteSpace(task.ParentTaskId)) return;
        var siblingIndex = 2;
        foreach (var sibling in Data.Tasks
                     .Where(candidate => candidate.Id != task.Id &&
                         string.Equals(candidate.ParentTaskId, task.ParentTaskId, StringComparison.Ordinal))
                     .OrderBy(candidate => candidate.SortOrder <= 0 ? double.MaxValue : candidate.SortOrder)
                     .ThenBy(candidate => candidate.CreatedAt))
        {
            sibling.SortOrder = siblingIndex++ * 1000;
        }
        task.SortOrder = 1000;
        Settings.CollapsedTaskIds.RemoveAll(id => string.Equals(id, task.ParentTaskId, StringComparison.Ordinal));
    }

    private bool DeleteTaskTreeWithoutSaving(string taskId)
    {
        var treeIds = TodoRecycleBin.TaskTreeIds(Data, taskId);
        if (!TodoRecycleBin.DeleteTaskTree(Data, Settings, taskId)) return false;
        if (!Settings.IsRecycleBinEnabled) Settings.CollapsedTaskIds.RemoveAll(treeIds.Contains);
        return true;
    }

    private TodoList? FindList(string listId) => Data.Lists.FirstOrDefault(list =>
        string.Equals(list.Id, listId, StringComparison.Ordinal));

    private bool ListExists(string listId) => FindList(listId) is not null;

    private bool IsDefaultList(string listId) => string.Equals(listId, DefaultListId, StringComparison.Ordinal);

    private string DefaultList() => Data.Lists.FirstOrDefault(list => IsDefaultList(list.Id))?.Id
        ?? Data.Lists.First().Id;

    private void Publish(TodoChangeSet changes) => Changed?.Invoke(this, changes);

    private static TodoData Clone(TodoData value) => new()
    {
        SchemaVersion = value.SchemaVersion,
        Lists = value.Lists.Select(list => new TodoList
        {
            Id = list.Id,
            Name = list.Name,
            ColorId = list.ColorId,
            CreatedAt = list.CreatedAt
        }).ToList(),
        Tasks = value.Tasks.Select(task => new TodoTask
        {
            Id = task.Id,
            Title = task.Title,
            Notes = task.Notes,
            ListId = task.ListId,
            ParentTaskId = task.ParentTaskId,
            SortOrder = task.SortOrder,
            IsImportant = task.IsImportant,
            StartDate = task.StartDate,
            DueDate = task.DueDate,
            IsCompleted = task.IsCompleted,
            CompletedAt = task.CompletedAt,
            DeletedAt = task.DeletedAt,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        }).ToList()
    };

    private static TodoSettings Clone(TodoSettings value) => new()
    {
        Theme = value.Theme,
        CurrentViewId = value.CurrentViewId,
        SelectedTaskId = value.SelectedTaskId,
        CollapsedTaskIds = [.. value.CollapsedTaskIds],
        IsCompletedExpanded = value.IsCompletedExpanded,
        IsStickyModeEnabled = value.IsStickyModeEnabled,
        IsStickyTopmost = value.IsStickyTopmost,
        IsStickyCompletedExpanded = value.IsStickyCompletedExpanded,
        StickyOpacity = value.StickyOpacity,
        StickyScale = value.StickyScale,
        StickyLeft = value.StickyLeft,
        StickyTop = value.StickyTop,
        StickyWidth = value.StickyWidth,
        StickyHeight = value.StickyHeight,
        IsStickyFloatingModeEnabled = value.IsStickyFloatingModeEnabled,
        StickyFloatingEdge = value.StickyFloatingEdge,
        StickyFloatingTop = value.StickyFloatingTop,
        HasCompletedMainOnboarding = value.HasCompletedMainOnboarding,
        IsRecycleBinEnabled = value.IsRecycleBinEnabled,
        RecycleBinRetentionPreset = value.RecycleBinRetentionPreset,
        RecycleBinCustomRetentionDays = value.RecycleBinCustomRetentionDays
    };
}
