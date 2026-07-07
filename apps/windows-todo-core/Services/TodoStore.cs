using Fowan.Todo.Core.Models;
using System.Text.Json;

namespace Fowan.Todo.Core.Services;

public sealed class TodoStore
{
    public const string DefaultListId = "default";
    public const string DefaultListName = "默认清单";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string RootPath { get; }
    public string DataPath { get; }
    public string SettingsPath { get; }
    public TodoStoragePaths Paths { get; }

    public TodoStore()
    {
        Paths = TodoStoragePaths.Resolve();
        Paths.EnsureReady();
        RootPath = Paths.TodoRoot;
        DataPath = Paths.DataPath;
        SettingsPath = Paths.SettingsPath;
    }

    public TodoData LoadData()
    {
        Directory.CreateDirectory(RootPath);

        var data = ReadJson<TodoData>(DataPath) ?? new TodoData();
        NormalizeData(data);
        SaveData(data);
        return data;
    }

    public TodoSettings LoadSettings()
    {
        Directory.CreateDirectory(RootPath);

        var settings = ReadJson<TodoSettings>(SettingsPath) ?? new TodoSettings();
        NormalizeSettings(settings);
        SaveSettings(settings);
        return settings;
    }

    public void SaveData(TodoData data)
    {
        Directory.CreateDirectory(RootPath);
        NormalizeData(data);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(data, JsonOptions));
    }

    public void SaveSettings(TodoSettings settings)
    {
        Directory.CreateDirectory(RootPath);
        NormalizeSettings(settings);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void NormalizeData(TodoData data)
    {
        data.SchemaVersion = 3;
        data.Lists ??= [];
        data.Tasks ??= [];

        EnsureList(data, DefaultListId, DefaultListName);

        foreach (var list in data.Lists)
        {
            if (string.IsNullOrWhiteSpace(list.Id))
            {
                list.Id = NewId("list");
            }

            if (string.IsNullOrWhiteSpace(list.Name))
            {
                list.Name = string.Equals(list.Id, DefaultListId, StringComparison.Ordinal)
                    ? DefaultListName
                    : "未命名清单";
            }
        }

        var knownListIds = data.Lists.Select(list => list.Id).ToHashSet(StringComparer.Ordinal);
        var fallbackListId = data.Lists.FirstOrDefault(list => string.Equals(list.Id, DefaultListId, StringComparison.Ordinal))?.Id
            ?? data.Lists.First().Id;
        foreach (var task in data.Tasks.ToList())
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                task.Id = NewId("task");
            }

            task.Title = task.Title.Trim();
            if (string.IsNullOrWhiteSpace(task.Title))
            {
                data.Tasks.Remove(task);
                continue;
            }

            if (string.IsNullOrWhiteSpace(task.ListId) || !knownListIds.Contains(task.ListId))
            {
                task.ListId = fallbackListId;
            }

            task.ParentTaskId = string.IsNullOrWhiteSpace(task.ParentTaskId)
                ? null
                : task.ParentTaskId.Trim();

            if (task.CreatedAt == default)
            {
                task.CreatedAt = DateTimeOffset.Now;
            }

            if (task.UpdatedAt == default)
            {
                task.UpdatedAt = task.CreatedAt;
            }

            task.StartDate = task.StartDate == default ? DateTime.Today : task.StartDate.Date;
            task.DueDate = task.DueDate?.Date;
            if (task.IsCompleted && task.CompletedAt is null)
            {
                task.CompletedAt = task.UpdatedAt;
            }

            if (!task.IsCompleted)
            {
                task.CompletedAt = null;
            }
        }

        NormalizeTaskTree(data);
        NormalizeTaskOrder(data);
    }

    private static void NormalizeTaskTree(TodoData data)
    {
        var knownTaskIds = data.Tasks.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var task in data.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.ParentTaskId))
            {
                task.ParentTaskId = null;
                continue;
            }

            if (string.Equals(task.Id, task.ParentTaskId, StringComparison.Ordinal) ||
                !knownTaskIds.Contains(task.ParentTaskId))
            {
                task.ParentTaskId = null;
            }
        }

        foreach (var task in data.Tasks)
        {
            if (HasParentCycle(data, task))
            {
                task.ParentTaskId = null;
            }
        }

        foreach (var task in data.Tasks.OrderBy(task => task.CreatedAt).ToList())
        {
            while (!string.IsNullOrWhiteSpace(task.ParentTaskId) &&
                TodoQuery.TaskDepth(data, task) > TodoQuery.MaxTaskTreeDepth)
            {
                task.ParentTaskId = null;
            }
        }

        foreach (var group in data.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal))
        {
            foreach (var overflow in group
                .OrderBy(task => task.CreatedAt)
                .Skip(TodoQuery.MaxChildTasksPerTask))
            {
                overflow.ParentTaskId = null;
            }
        }
    }

    private static bool HasParentCycle(TodoData data, TodoTask task)
    {
        var byId = data.Tasks.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { task.Id };
        var current = task;

        while (!string.IsNullOrWhiteSpace(current.ParentTaskId) &&
            byId.TryGetValue(current.ParentTaskId, out var parent))
        {
            if (!seen.Add(parent.Id))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static void NormalizeTaskOrder(TodoData data)
    {
        foreach (var group in data.Tasks.GroupBy(task => task.ParentTaskId ?? string.Empty, StringComparer.Ordinal))
        {
            var order = 1000.0;
            foreach (var task in group
                .OrderBy(task => task.SortOrder <= 0 ? double.MaxValue : task.SortOrder)
                .ThenBy(task => task.CreatedAt))
            {
                task.SortOrder = order;
                order += 1000.0;
            }
        }
    }

    private static void NormalizeSettings(TodoSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Theme) ||
            settings.Theme is not (TodoThemeIds.System or TodoThemeIds.Light or TodoThemeIds.Dark))
        {
            settings.Theme = TodoThemeIds.System;
        }

        if (string.IsNullOrWhiteSpace(settings.CurrentViewId))
        {
            settings.CurrentViewId = TodoViewIds.Today;
        }

        settings.CollapsedTaskIds = settings.CollapsedTaskIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (double.IsNaN(settings.StickyOpacity) || double.IsInfinity(settings.StickyOpacity))
        {
            settings.StickyOpacity = TodoSettings.MaxStickyOpacity;
        }

        settings.StickyOpacity = Math.Clamp(
            settings.StickyOpacity,
            TodoSettings.MinStickyOpacity,
            TodoSettings.MaxStickyOpacity);

        if (double.IsNaN(settings.StickyScale) || double.IsInfinity(settings.StickyScale))
        {
            settings.StickyScale = 1.0;
        }

        settings.StickyScale = Math.Clamp(
            settings.StickyScale,
            TodoSettings.MinStickyScale,
            TodoSettings.MaxStickyScale);

        settings.StickyWidth = NormalizeFinite(settings.StickyWidth);
        settings.StickyHeight = NormalizeFinite(settings.StickyHeight);
        settings.StickyLeft = NormalizeFinite(settings.StickyLeft);
        settings.StickyTop = NormalizeFinite(settings.StickyTop);
    }

    private static double? NormalizeFinite(double? value)
    {
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? null
            : value;
    }

    private static void EnsureList(TodoData data, string id, string name)
    {
        if (data.Lists.Any(list => string.Equals(list.Id, id, StringComparison.Ordinal)))
        {
            return;
        }

        data.Lists.Add(new TodoList
        {
            Id = id,
            Name = name,
            CreatedAt = DateTimeOffset.Now
        });
    }

    public static string NewId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 17, prefix.Length + 1 + 32)];
    }
}
