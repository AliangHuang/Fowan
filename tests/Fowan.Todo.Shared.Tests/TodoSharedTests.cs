using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Text.Json;
using Xunit;

namespace Fowan.Todo.Shared.Tests;

public sealed class TodoSharedTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Fowan.Todo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PersistenceControllerUsesInjectedRepositoryForBothWindows()
    {
        var repository = new MemoryTodoRepository();
        var controller = new TodoPersistenceController(repository);
        var data = controller.LoadData();
        var settings = controller.LoadSettings();

        controller.SaveData(data);
        controller.SaveSettings(settings);
        var updated = controller.UpdateData((_, _) => true);

        Assert.Same(data, repository.SavedData);
        Assert.Same(settings, repository.SavedSettings);
        Assert.True(updated);
    }

    [Fact]
    public void VersionFiveGoldenFixturePreservesEveryExistingFieldAndValue()
    {
        Directory.CreateDirectory(_rootPath);
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "todo-data.json");
        var dataPath = Path.Combine(_rootPath, TodoStoragePaths.DataFileName);
        File.Copy(fixturePath, dataPath);
        var original = File.ReadAllText(dataPath);
        var store = new TodoStore(_rootPath);

        store.SaveData(store.LoadData());

        AssertJsonSubset(original, File.ReadAllText(dataPath));
    }

    [Fact]
    public void OldJsonDataIsMigratedAndRecycleBinSettingsDefaultOn()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, TodoStoragePaths.DataFileName), """
        {
          "schemaVersion": 1,
          "lists": [],
          "tasks": [
            { "id": "task-1", "title": "  Legacy task  ", "startDate": "2026-07-11" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(_rootPath, TodoStoragePaths.SettingsFileName), """
        {
          "theme": "dark"
        }
        """);

        var store = new TodoStore(_rootPath);
        var data = store.LoadData();
        var settings = store.LoadSettings();

        Assert.Equal(5, data.SchemaVersion);
        Assert.Contains(data.Lists, list => list.Id == TodoStore.DefaultListId && list.ColorId == TodoListColorIds.Cyan);
        var task = Assert.Single(data.Tasks);
        Assert.Equal("Legacy task", task.Title);
        Assert.Equal(TodoStore.DefaultListId, task.ListId);
        Assert.Null(task.DeletedAt);
        Assert.True(settings.IsRecycleBinEnabled);
        Assert.Equal(TodoRecycleBinRetentionPresets.ThirtyDays, settings.RecycleBinRetentionPreset);
        Assert.Equal(30, settings.RecycleBinCustomRetentionDays);
        Assert.False(settings.IsStickyFloatingModeEnabled);
        Assert.Null(settings.StickyFloatingEdge);
        Assert.Null(settings.StickyFloatingTop);
        Assert.False(settings.HasCompletedMainOnboarding);
    }

    [Fact]
    public void FloatingStickySettingsRoundTripAndRejectInvalidEdges()
    {
        var store = new TodoStore(_rootPath);
        store.SaveSettings(new TodoSettings
        {
            IsStickyFloatingModeEnabled = true,
            StickyFloatingEdge = TodoStickyFloatingEdges.Right,
            StickyFloatingTop = 144.5,
            HasCompletedMainOnboarding = true,
            StickyOpacity = 0.72
        });

        var persisted = store.LoadSettings();
        Assert.True(persisted.IsStickyFloatingModeEnabled);
        Assert.Equal(TodoStickyFloatingEdges.Right, persisted.StickyFloatingEdge);
        Assert.Equal(144.5, persisted.StickyFloatingTop);
        Assert.True(persisted.HasCompletedMainOnboarding);
        Assert.Equal(0.72, persisted.StickyOpacity);

        File.WriteAllText(Path.Combine(_rootPath, TodoStoragePaths.SettingsFileName), """
        {
          "isStickyFloatingModeEnabled": true,
          "stickyFloatingEdge": "center",
          "stickyFloatingTop": 120
        }
        """);

        var normalized = store.LoadSettings();
        Assert.False(normalized.IsStickyFloatingModeEnabled);
        Assert.Null(normalized.StickyFloatingEdge);
        Assert.Equal(120, normalized.StickyFloatingTop);
    }

    [Fact]
    public void NewTasksDefaultToTheCurrentListOnlyForValidListViews()
    {
        var data = Data();
        var customList = new TodoList { Id = "list-custom", Name = "Custom" };
        data.Lists.Add(customList);

        Assert.Equal(
            customList.Id,
            TodoQuery.DefaultListIdForNewTask(data, TodoViewIds.List(customList.Id)));
        Assert.Equal(
            TodoStore.DefaultListId,
            TodoQuery.DefaultListIdForNewTask(data, TodoViewIds.List(TodoStore.DefaultListId)));

        foreach (var aggregateViewId in new[]
                 {
                     TodoViewIds.Today,
                     TodoViewIds.Planned,
                     TodoViewIds.Important,
                     TodoViewIds.All,
                     TodoViewIds.Completed,
                     TodoViewIds.RecycleBin
                 })
        {
            Assert.Equal(
                TodoStore.DefaultListId,
                TodoQuery.DefaultListIdForNewTask(data, aggregateViewId));
        }

        Assert.Equal(
            TodoStore.DefaultListId,
            TodoQuery.DefaultListIdForNewTask(data, TodoViewIds.List("missing-list")));
    }

    [Fact]
    public void TodoStorageRootCanBeOverriddenForIsolatedRuntimeValidation()
    {
        const string variableName = "FOWAN_TODO_DATA_ROOT";
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, _rootPath);

            var paths = TodoStoragePaths.Resolve();

            Assert.Equal(Path.GetFullPath(_rootPath), paths.TodoRoot);
            Assert.Equal(Path.Combine(_rootPath, TodoStoragePaths.DataFileName), paths.DataPath);
            Assert.Equal(Path.Combine(_rootPath, TodoStoragePaths.SettingsFileName), paths.SettingsPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [Theory]
    [InlineData(0, 1920, 16, 16, TodoStickyFloatingEdges.Left)]
    [InlineData(0, 1920, -200, 16, TodoStickyFloatingEdges.Left)]
    [InlineData(0, 1920, 1904, 16, TodoStickyFloatingEdges.Right)]
    [InlineData(0, 1920, 2200, 16, TodoStickyFloatingEdges.Right)]
    [InlineData(-1920, 0, -1900, 20, TodoStickyFloatingEdges.Left)]
    [InlineData(0, 2400, 1200, 20, null)]
    public void StickyDockEdgeUsesWindowCenterAndIncludesCrossedEdges(
        double workLeft,
        double workRight,
        double windowCenter,
        double threshold,
        string? expected)
    {
        Assert.Equal(expected, TodoStickyPlacement.FindDockEdgeByCenter(
            workLeft,
            workRight,
            windowCenter,
            threshold));
    }

    [Fact]
    public void StickyNearestEdgeUsesLeftForEqualDistances()
    {
        Assert.Equal(TodoStickyFloatingEdges.Left, TodoStickyPlacement.NearestEdge(-1920, 1920, 0));
        Assert.Equal(TodoStickyFloatingEdges.Right, TodoStickyPlacement.NearestEdge(-1920, 1920, 1));
    }

    [Fact]
    public void StickyFloatingTopAlignsIconCenters()
    {
        Assert.Equal(102, TodoStickyPlacement.AlignCenters(116, 24, 52));
        Assert.Equal(-1918, TodoStickyPlacement.AlignCenters(-1904, 24, 52));
    }

    [Fact]
    public void ListColorsMigrateValidateAndPersist()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, TodoStoragePaths.DataFileName), """
        {
          "schemaVersion": 4,
          "lists": [
            { "id": "default", "name": "默认清单" },
            { "id": "personal", "name": "个人" },
            { "id": "study", "name": "学习" },
            { "id": "project", "name": "项目", "colorId": "unknown" }
          ],
          "tasks": []
        }
        """);

        var store = new TodoStore(_rootPath);
        var data = store.LoadData();

        Assert.Equal(12, TodoListColorIds.All.Count);
        Assert.Equal(5, data.SchemaVersion);
        Assert.Equal(TodoListColorIds.Cyan, data.Lists.Single(list => list.Id == "default").ColorId);
        Assert.Equal(TodoListColorIds.Green, data.Lists.Single(list => list.Id == "personal").ColorId);
        Assert.Equal(TodoListColorIds.Purple, data.Lists.Single(list => list.Id == "study").ColorId);
        var project = data.Lists.Single(list => list.Id == "project");
        Assert.Equal(TodoListColorIds.Blue, project.ColorId);

        project.ColorId = TodoListColorIds.Pink;
        store.SaveData(data);

        var reloaded = new TodoStore(_rootPath).LoadData();
        Assert.Equal(TodoListColorIds.Pink, reloaded.Lists.Single(list => list.Id == "project").ColorId);
    }

    [Fact]
    public void LoadDoesNotReplaceUnreadableDataAndPreservesABackup()
    {
        Directory.CreateDirectory(_rootPath);
        var dataPath = Path.Combine(_rootPath, TodoStoragePaths.DataFileName);
        const string unreadableJson = "{ \"tasks\": ";
        File.WriteAllText(dataPath, unreadableJson);

        var data = new TodoStore(_rootPath).LoadData();

        Assert.Empty(data.Tasks);
        Assert.Equal(unreadableJson, File.ReadAllText(dataPath));
        Assert.Single(Directory.GetFiles(_rootPath, $"{TodoStoragePaths.DataFileName}.unreadable-*.bak"));
    }

    [Fact]
    public void UpdateDataReadsTheLatestSnapshotInsideTheStorageLock()
    {
        Directory.CreateDirectory(_rootPath);
        var firstStore = new TodoStore(_rootPath);
        var secondStore = new TodoStore(_rootPath);

        Assert.True(firstStore.UpdateData((data, _) =>
        {
            data.Tasks.Add(Task("first"));
            return true;
        }));
        Assert.True(secondStore.UpdateData((data, _) =>
        {
            data.Tasks.Add(Task("second"));
            return true;
        }));

        var reloaded = new TodoStore(_rootPath).LoadData();
        Assert.Equal(["first", "second"], reloaded.Tasks.Select(task => task.Id).OrderBy(id => id));
    }

    [Fact]
    public void SoftDeleteRestoreAndPermanentDeleteOperateOnWholeTaskTree()
    {
        var data = Data(
            Task("root"),
            Task("child", parentId: "root"),
            Task("grand", parentId: "child"),
            Task("sibling"));
        var settings = new TodoSettings { IsRecycleBinEnabled = true };
        var deletedAt = new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero);

        Assert.True(TodoRecycleBin.DeleteTaskTree(data, settings, "child", deletedAt));

        Assert.Null(data.Tasks.Single(task => task.Id == "root").DeletedAt);
        Assert.Null(data.Tasks.Single(task => task.Id == "sibling").DeletedAt);
        Assert.Equal(deletedAt, data.Tasks.Single(task => task.Id == "child").DeletedAt);
        Assert.Equal(deletedAt, data.Tasks.Single(task => task.Id == "grand").DeletedAt);
        Assert.DoesNotContain("child", TodoQuery.ActiveTasksForView(data, TodoViewIds.All).Select(task => task.Id));
        Assert.Equal(["child", "grand"], TodoQuery.RecycleBinTaskNodes(data).Select(node => node.Task.Id));

        Assert.True(TodoRecycleBin.RestoreTaskTree(data, "grand", deletedAt.AddMinutes(1)));
        Assert.All(data.Tasks, task => Assert.Null(task.DeletedAt));

        Assert.True(TodoRecycleBin.DeleteTaskTree(data, settings, "root", deletedAt.AddMinutes(2)));
        Assert.True(TodoRecycleBin.PermanentlyDeleteTaskTree(data, "grand"));
        Assert.Equal(["sibling"], data.Tasks.Select(task => task.Id));
    }

    [Fact]
    public void DisabledRecycleBinDeletesFutureTasksPermanentlyWithoutPurgingExistingTrash()
    {
        var deletedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var data = Data(
            Task("trash", deletedAt: deletedAt),
            Task("root"),
            Task("child", parentId: "root"));
        var settings = new TodoSettings
        {
            IsRecycleBinEnabled = false,
            RecycleBinRetentionPreset = TodoRecycleBinRetentionPresets.SevenDays
        };

        Assert.True(TodoRecycleBin.DeleteTaskTree(data, settings, "root", deletedAt.AddDays(1)));
        Assert.Equal(["trash"], data.Tasks.Select(task => task.Id));
        Assert.Equal(0, TodoRecycleBin.PurgeExpired(data, settings, deletedAt.AddDays(90)));
        Assert.Equal(["trash"], data.Tasks.Select(task => task.Id));
    }

    [Fact]
    public void RecycleBinRetentionUsesPresetAndCustomCleanupWindows()
    {
        Assert.Equal(7, TodoRecycleBin.RetentionDays(new TodoSettings { RecycleBinRetentionPreset = TodoRecycleBinRetentionPresets.SevenDays }));
        Assert.Equal(30, TodoRecycleBin.RetentionDays(new TodoSettings { RecycleBinRetentionPreset = TodoRecycleBinRetentionPresets.ThirtyDays }));
        Assert.Equal(90, TodoRecycleBin.RetentionDays(new TodoSettings { RecycleBinRetentionPreset = TodoRecycleBinRetentionPresets.NinetyDays }));
        Assert.Equal(365, TodoRecycleBin.RetentionDays(new TodoSettings { RecycleBinRetentionPreset = TodoRecycleBinRetentionPresets.Custom, RecycleBinCustomRetentionDays = 500 }));

        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var settings = new TodoSettings
        {
            RecycleBinRetentionPreset = TodoRecycleBinRetentionPresets.Custom,
            RecycleBinCustomRetentionDays = 45
        };
        var data = Data(
            Task("old", deletedAt: now.AddDays(-46)),
            Task("edge", deletedAt: now.AddDays(-45)),
            Task("fresh", deletedAt: now.AddDays(-44)));

        Assert.Equal(2, TodoRecycleBin.PurgeExpired(data, settings, now));
        Assert.Equal(["fresh"], data.Tasks.Select(task => task.Id));
    }

    [Fact]
    public void HierarchyRangeFiltersVisibleDepthWithoutAddingUnmatchedAncestors()
    {
        var data = Data(
            Task("root", startDate: new DateTime(2026, 7, 1)),
            Task("child", parentId: "root", startDate: new DateTime(2026, 7, 5)),
            Task("grand", parentId: "child", startDate: new DateTime(2026, 7, 6)));

        Assert.Equal(["root"], TodoQuery.ActiveTaskNodesForView(data, TodoViewIds.All, maximumDepth: 1).Select(node => node.Task.Id));
        Assert.Equal(["root", "child"], TodoQuery.ActiveTaskNodesForView(data, TodoViewIds.All, maximumDepth: 2).Select(node => node.Task.Id));
        Assert.Equal(["root", "child", "grand"], TodoQuery.ActiveTaskNodesForView(data, TodoViewIds.All, maximumDepth: 3).Select(node => node.Task.Id));

        var childOnly = new TodoDateRangeFilter
        {
            Mode = TodoDateFilterMode.StartDate,
            StartDate = new DateTime(2026, 7, 5),
            EndDate = new DateTime(2026, 7, 5)
        };

        var node = Assert.Single(TodoQuery.ActiveTaskNodesForView(data, TodoViewIds.All, dateFilter: childOnly, maximumDepth: 3));
        Assert.Equal("child", node.Task.Id);
        Assert.Equal(1, node.Depth);
    }

    [Fact]
    public void DateFiltersMatchStartDatesAndExecutionPeriodBoundaries()
    {
        var today = new DateTime(2026, 7, 11);
        var data = Data(
            Task("start-edge", startDate: new DateTime(2026, 7, 1)),
            Task("active-no-due", startDate: new DateTime(2026, 7, 1)),
            Task("completed-no-due", startDate: new DateTime(2026, 7, 1), completedAt: new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero)),
            Task("due-before-start", startDate: new DateTime(2026, 7, 10), dueDate: new DateTime(2026, 7, 5)),
            Task("completed-today", completedAt: new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            Task("completed-yesterday", completedAt: new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero)));

        var startFilter = new TodoDateRangeFilter
        {
            Mode = TodoDateFilterMode.StartDate,
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 1)
        };
        Assert.Equal(["start-edge", "active-no-due"], TodoQuery.ActiveTasksForView(data, TodoViewIds.All, today, startFilter).Select(task => task.Id));

        var executionFilter = new TodoDateRangeFilter
        {
            Mode = TodoDateFilterMode.ExecutionPeriod,
            StartDate = new DateTime(2026, 7, 9),
            EndDate = new DateTime(2026, 7, 12)
        };
        Assert.Equal(["start-edge", "active-no-due", "due-before-start"], TodoQuery.ActiveTasksForView(data, TodoViewIds.All, today, executionFilter).Select(task => task.Id));
        Assert.DoesNotContain("completed-no-due", TodoQuery.CompletedTasksForView(data, TodoViewIds.All, today, executionFilter).Select(task => task.Id));

        var completedToday = TodoQuery.CompletedTasksForView(data, TodoViewIds.Today, today).Select(task => task.Id);
        Assert.Contains("completed-today", completedToday);
        Assert.DoesNotContain("completed-yesterday", completedToday);
    }

    [Fact]
    public void EditingCompletedTimestampMovesTaskIntoTheMatchingTodayView()
    {
        var today = new DateTime(2026, 7, 11);
        var task = Task("completed", completedAt: new DateTimeOffset(2026, 7, 10, 12, 15, 0, TimeSpan.Zero));
        var data = Data(task);

        Assert.DoesNotContain("completed", TodoQuery.CompletedTasksForView(data, TodoViewIds.Today, today).Select(item => item.Id));

        task.CompletedAt = new DateTimeOffset(2026, 7, 11, 12, 30, 0, TimeSpan.Zero);

        Assert.Contains("completed", TodoQuery.CompletedTasksForView(data, TodoViewIds.Today, today).Select(item => item.Id));
    }

    [Fact]
    public void ListFilterIntersectsDateFilterAndClearsWithoutChangingTheQueryShape()
    {
        var inboxTask = Task("inbox", startDate: new DateTime(2026, 7, 11));
        var otherTask = Task("other", startDate: new DateTime(2026, 7, 11));
        otherTask.ListId = "list-other";
        var data = Data(inboxTask, otherTask);
        data.Lists.Add(new TodoList { Id = "list-other", Name = "Other" });
        var dateFilter = new TodoDateRangeFilter
        {
            Mode = TodoDateFilterMode.StartDate,
            StartDate = new DateTime(2026, 7, 11),
            EndDate = new DateTime(2026, 7, 11)
        };

        Assert.Equal(
            ["other"],
            TodoQuery.ActiveTasksForView(data, TodoViewIds.All, dateFilter: dateFilter, listIdFilter: "list-other")
                .Select(task => task.Id));
        Assert.Equal(
            ["inbox", "other"],
            TodoQuery.ActiveTasksForView(data, TodoViewIds.All, dateFilter: dateFilter, listIdFilter: null)
                .Select(task => task.Id));
    }

    [Fact]
    public void SharedTaskCommandsCompleteTheSameTreeForMainAndStickyShells()
    {
        var data = Data(Task("root"), Task("child", "root"), Task("grand", "child"));
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.True(TodoTaskCommands.HasIncompleteDescendants(data, "root"));
        Assert.Equal(3, TodoTaskCommands.SetCompleted(data, "root", true, includeDescendants: true, now));
        Assert.All(data.Tasks, task =>
        {
            Assert.True(task.IsCompleted);
            Assert.Equal(now, task.CompletedAt);
            Assert.Equal(now, task.UpdatedAt);
        });
        Assert.False(TodoTaskCommands.HasIncompleteDescendants(data, "root"));
    }

    [Fact]
    public void SharedTaskCommandsDoNotChangeUnrelatedTasks()
    {
        var root = Task("root");
        var sibling = Task("sibling");
        var data = Data(root, sibling);
        var originalSiblingUpdate = sibling.UpdatedAt;

        Assert.Equal(1, TodoTaskCommands.SetCompleted(
            data,
            "root",
            completed: true,
            includeDescendants: false,
            now: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(sibling.IsCompleted);
        Assert.Equal(originalSiblingUpdate, sibling.UpdatedAt);
    }

    [Fact]
    public void SharedDropControllerRejectsCyclesAndReordersForBothShells()
    {
        var root = Task("root");
        var child = Task("child", "root");
        var grand = Task("grand", "child");
        var sibling = Task("sibling");
        var data = Data(root, child, grand, sibling);
        var settings = new TodoSettings { CollapsedTaskIds = ["root"] };

        Assert.False(TodoTaskDropController.CanApply(
            data,
            "root",
            "grand",
            TodoTaskDropPlacement.Child));
        Assert.True(TodoTaskDropController.TryApply(
            data,
            settings,
            "sibling",
            "root",
            TodoTaskDropPlacement.Child,
            new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal("root", sibling.ParentTaskId);
        Assert.Equal(sibling.Id, data.Tasks
            .Where(task => task.ParentTaskId == "root")
            .OrderBy(task => task.SortOrder)
            .First().Id);
        Assert.DoesNotContain("root", settings.CollapsedTaskIds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static TodoData Data(params TodoTask[] tasks)
    {
        return new TodoData
        {
            Lists = [new TodoList { Id = TodoStore.DefaultListId, Name = "Inbox" }],
            Tasks = tasks.ToList()
        };
    }

    private static TodoTask Task(
        string id,
        string? parentId = null,
        DateTime? startDate = null,
        DateTime? dueDate = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? deletedAt = null)
    {
        var createdAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        return new TodoTask
        {
            Id = id,
            Title = id,
            ListId = TodoStore.DefaultListId,
            ParentTaskId = parentId,
            SortOrder = id switch
            {
                "root" => 1000,
                "child" => 1000,
                "grand" => 1000,
                "sibling" => 2000,
                _ => 1000
            },
            StartDate = startDate ?? new DateTime(2026, 7, 11),
            DueDate = dueDate,
            IsCompleted = completedAt is not null,
            CompletedAt = completedAt,
            DeletedAt = deletedAt,
            CreatedAt = createdAt,
            UpdatedAt = completedAt ?? deletedAt ?? createdAt
        };
    }

    private static void AssertJsonSubset(string expectedJson, string actualJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        using var actual = JsonDocument.Parse(actualJson);
        AssertJsonSubset(expected.RootElement, actual.RootElement);
    }

    private sealed class MemoryTodoRepository : ITodoRepository
    {
        private readonly TodoData _data = new();
        private readonly TodoSettings _settings = new();

        public TodoData? SavedData { get; private set; }
        public TodoSettings? SavedSettings { get; private set; }

        public TodoData LoadData() => _data;
        public TodoSettings LoadSettings() => _settings;
        public void SaveData(TodoData data) => SavedData = data;
        public void SaveSettings(TodoSettings settings) => SavedSettings = settings;
        public bool UpdateData(Func<TodoData, TodoSettings, bool> update) => update(_data, _settings);
    }

    private static void AssertJsonSubset(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in expected.EnumerateObject())
            {
                Assert.True(actual.TryGetProperty(property.Name, out var value), property.Name);
                AssertJsonSubset(property.Value, value);
            }
            return;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            Assert.True(actualItems.Length >= expectedItems.Length);
            for (var index = 0; index < expectedItems.Length; index++)
            {
                AssertJsonSubset(expectedItems[index], actualItems[index]);
            }
            return;
        }
        switch (expected.ValueKind)
        {
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual.GetString());
                break;
            case JsonValueKind.Number:
                Assert.Equal(expected.GetDecimal(), actual.GetDecimal());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), actual.GetBoolean());
                break;
            case JsonValueKind.Null:
                break;
            default:
                Assert.Equal(expected.GetRawText(), actual.GetRawText());
                break;
        }
    }
}
