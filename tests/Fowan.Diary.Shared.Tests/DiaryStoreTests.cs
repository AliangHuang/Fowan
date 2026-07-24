using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Fowan.Diary.Shared.Tests;

public sealed class DiaryStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Fowan.Diary.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void WorkspaceOwnsDataAndSettingsAndPublishesPersistedChanges()
    {
        var workspace = new DiaryWorkspace(
            new DiaryStore(_rootPath),
            new DiarySettingsStore(_rootPath));
        var changes = new List<DiaryChangeSet>();
        workspace.Changed += (_, change) => changes.Add(change);
        var data = workspace.QueryData();
        data.Entries.Add(new DiaryEntry
        {
            Id = "entry-1",
            NotebookId = DiaryStore.DefaultNotebookId,
            Title = "Synthetic",
            Body = "Synthetic content",
            CreatedAt = DateTimeOffset.Parse("2026-07-14T08:00:00+08:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-07-14T08:00:00+08:00")
        });
        var settings = workspace.QuerySettings();
        settings.Theme = "dark";

        var dataResult = workspace.SaveData(data);
        var settingsResult = workspace.SaveSettings(settings);

        Assert.True(dataResult.Succeeded);
        Assert.True(settingsResult.Succeeded);
        Assert.Contains(DiaryChangeSet.Entries | DiaryChangeSet.Metadata | DiaryChangeSet.Attachments, changes);
        Assert.Contains(DiaryChangeSet.Settings, changes);
        Assert.Single(new DiaryStore(_rootPath).LoadData().Entries);
        Assert.Equal("dark", new DiarySettingsStore(_rootPath).Load().Theme);
    }

    [Fact]
    public void FailedPersistenceKeepsTheOldSnapshotAndDoesNotPublish()
    {
        var repository = new MemoryDiaryRepository { FailSaves = true };
        var workspace = new DiaryWorkspace(repository, new MemoryDiarySettingsRepository());
        var before = workspace.State;
        var published = 0;
        workspace.Changed += (_, _) => published++;
        var candidate = workspace.QueryData();
        candidate.Entries.Add(new DiaryEntry { Id = "failed-entry", NotebookId = DiaryStore.DefaultNotebookId });

        var result = workspace.SaveData(candidate);

        Assert.False(result.Succeeded);
        Assert.Same(before, workspace.State);
        Assert.Empty(workspace.State.Entries);
        Assert.Equal(0, published);
    }

    [Fact]
    public void VersionOneGoldenFixturePreservesEveryExistingFieldAndValue()
    {
        Directory.CreateDirectory(_rootPath);
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "diary-data.json");
        var dataPath = Path.Combine(_rootPath, "diary-data.json");
        File.Copy(fixturePath, dataPath);
        var original = File.ReadAllText(dataPath);
        var store = new DiaryStore(_rootPath);

        store.SaveData(store.LoadData());

        AssertJsonSubset(original, File.ReadAllText(dataPath));
    }

    [Fact]
    public void FirstLoadCreatesAnEmptyLibraryWithTheInboxNotebook()
    {
        var store = new DiaryStore(_rootPath);

        var data = store.LoadData();

        var notebook = Assert.Single(data.Notebooks);
        Assert.Equal(DiaryStore.DefaultNotebookId, notebook.Id);
        Assert.Equal(DiaryStore.DefaultNotebookName, notebook.Name);
        Assert.Empty(data.Entries);
        Assert.True(File.Exists(store.DataPath));
    }

    [Fact]
    public void SaveAndReloadPreservesDiaryContentAndTodoLinkSnapshot()
    {
        var store = new DiaryStore(_rootPath);
        var data = store.LoadData();
        var now = new DateTimeOffset(2026, 7, 10, 9, 30, 0, TimeSpan.Zero);
        data.Entries.Add(new DiaryEntry
        {
            Id = "entry-1",
            Title = "晨间记录",
            Body = "写下今天最重要的一件事。",
            NotebookId = data.Notebooks[0].Id,
            Mood = "平静",
            Weather = "晴",
            Location = "上海",
            LocationDetails = new DiaryLocationDetails { Source = "nominatim", Latitude = 31.2304, Longitude = 121.4737, ResolvedAt = now },
            WeatherDetails = new DiaryWeatherDetails { Source = "open-meteo", TemperatureCelsius = 27.6, WeatherCode = 1, Latitude = 31.2304, Longitude = 121.4737, FetchedAt = now },
            Tags = ["工作", "复盘", "工作"],
            TodoLinks =
            [
                new DiaryTodoLink
                {
                    TaskId = "task-1",
                    TitleSnapshot = "完成日记功能",
                    ListNameSnapshot = "默认清单",
                    StartDate = now.Date
                }
            ],
            CreatedAt = now,
            UpdatedAt = now
        });

        store.SaveData(data);
        var reloaded = store.LoadData();

        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal("晨间记录", entry.Title);
        Assert.Equal(["工作", "复盘"], entry.Tags);
        var link = Assert.Single(entry.TodoLinks);
        Assert.Equal("task-1", link.TaskId);
        Assert.Equal("完成日记功能", link.TitleSnapshot);
        Assert.Equal(31.2304, entry.LocationDetails!.Latitude);
        Assert.Equal(27.6, entry.WeatherDetails!.TemperatureCelsius);
    }

    [Fact]
    public void MalformedDataIsBackedUpBeforeTheLibraryIsRecovered()
    {
        var store = new DiaryStore(_rootPath);
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(store.DataPath, "{ invalid json");

        var data = store.LoadData();

        Assert.Single(data.Notebooks);
        Assert.Single(Directory.GetFiles(_rootPath, "diary-data.json.invalid-*.json"));
        Assert.DoesNotContain("invalid json", File.ReadAllText(store.DataPath));
    }

    [Fact]
    public void ImageAttachmentIsCopiedAndRemovedWithItsEntryDirectory()
    {
        var store = new DiaryStore(_rootPath);
        var sourcePath = Path.Combine(_rootPath, "source.png");
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(sourcePath, "fixture image");

        var attachment = store.ImportAttachment("entry-image", sourcePath);
        var storedPath = store.ResolveAttachmentPath(attachment.RelativePath);

        Assert.True(File.Exists(storedPath));
        Assert.Equal("source.png", attachment.FileName);
        store.DeleteAttachmentDirectory("entry-image");
        Assert.False(File.Exists(storedPath));
    }

    [Fact]
    public void TemplatesTitleInferenceAndLocalSearchUseDiaryContentOnly()
    {
        var data = new DiaryData
        {
            Entries =
            [
                new DiaryEntry { Title = "下午想法", Body = "关于流程自动化的灵感", Tags = ["灵感"], UpdatedAt = DateTimeOffset.Now },
                new DiaryEntry { Title = "项目日记", Body = "发布前验证", Tags = ["工作"], UpdatedAt = DateTimeOffset.Now.AddMinutes(-1) }
            ]
        };

        Assert.Equal("第一行标题", DiaryText.InferTitle("第一行标题\n第二行正文"));
        Assert.Equal("未命名日记", DiaryText.InferTitle("\n\n"));
        Assert.Equal(3, DiaryText.Templates.Count);
        var result = Assert.Single(DiaryText.Search(data, "自动化"));
        Assert.Equal("下午想法", result.Title);
    }

    [Fact]
    public void TimelineFiltersNotebookAndUsesStableNewestFirstOrder()
    {
        var sameTime = new DateTimeOffset(2026, 7, 7, 14, 0, 0, TimeSpan.Zero);
        var data = new DiaryData
        {
            Entries =
            [
                new DiaryEntry { Id = "z-entry", NotebookId = "work", CreatedAt = sameTime, UpdatedAt = sameTime },
                new DiaryEntry { Id = "a-entry", NotebookId = "work", CreatedAt = sameTime, UpdatedAt = sameTime },
                new DiaryEntry { Id = "life-entry", NotebookId = "life", CreatedAt = sameTime.AddMinutes(5), UpdatedAt = sameTime.AddMinutes(5) },
                new DiaryEntry { Id = "old-entry", NotebookId = "work", CreatedAt = sameTime.AddDays(-1), UpdatedAt = sameTime.AddDays(-1) }
            ]
        };

        Assert.Equal(["life-entry", "a-entry", "z-entry", "old-entry"], DiaryTimeline.Query(data, DiaryTimeline.AllNotebooksId).Select(entry => entry.Id));
        Assert.Equal(["a-entry", "z-entry", "old-entry"], DiaryTimeline.Query(data, "work").Select(entry => entry.Id));
        Assert.Equal(["life-entry", "a-entry", "z-entry"], DiaryTimeline.Query(data, DiaryTimeline.AllNotebooksId, sameTime.Date, sameTime.Date).Select(entry => entry.Id));
        Assert.Equal(["a-entry", "z-entry"], DiaryTimeline.Query(data, "work", sameTime.Date, sameTime.Date).Select(entry => entry.Id));
        Assert.Empty(DiaryTimeline.Query(data, DiaryTimeline.AllNotebooksId, sameTime.Date, sameTime.AddDays(-1).Date));
        Assert.Empty(DiaryTimeline.Query(data, "missing"));
    }

    [Fact]
    public void TimelineDateWindowsIncludeBothBoundsAndComposeWithNotebookFiltering()
    {
        var data = new DiaryData
        {
            Entries =
            [
                new DiaryEntry { Id = "today", NotebookId = "work", CreatedAt = new DateTimeOffset(2026, 7, 7, 18, 0, 0, TimeSpan.FromHours(8)), UpdatedAt = new DateTimeOffset(2026, 7, 7, 18, 0, 0, TimeSpan.FromHours(8)) },
                new DiaryEntry { Id = "week-start", NotebookId = "work", CreatedAt = new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.FromHours(8)), UpdatedAt = new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.FromHours(8)) },
                new DiaryEntry { Id = "month-start", NotebookId = "work", CreatedAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(8)), UpdatedAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(8)) },
                new DiaryEntry { Id = "year-start", NotebookId = "work", CreatedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(8)), UpdatedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(8)) },
                new DiaryEntry { Id = "previous-year", NotebookId = "work", CreatedAt = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.FromHours(8)), UpdatedAt = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.FromHours(8)) },
                new DiaryEntry { Id = "life-today", NotebookId = "life", CreatedAt = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.FromHours(8)), UpdatedAt = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.FromHours(8)) }
            ]
        };

        Assert.Equal(["today", "life-today"], DiaryTimeline.Query(data, DiaryTimeline.AllNotebooksId, new DateTime(2026, 7, 7), new DateTime(2026, 7, 7)).Select(entry => entry.Id));
        Assert.Equal(["today", "week-start"], DiaryTimeline.Query(data, "work", new DateTime(2026, 7, 6), new DateTime(2026, 7, 12)).Select(entry => entry.Id));
        Assert.Equal(["today", "week-start", "month-start"], DiaryTimeline.Query(data, "work", new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Select(entry => entry.Id));
        Assert.Equal(["today", "week-start", "month-start", "year-start"], DiaryTimeline.Query(data, "work", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).Select(entry => entry.Id));
    }

    [Fact]
    public void OldSettingsReceiveTheAllNotebooksTimelineDefaultAndPersistSelection()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "diary-settings.json");
        File.WriteAllText(settingsPath, "{ \"theme\": \"dark\", \"currentViewId\": \"timeline\" }");
        var store = new DiarySettingsStore(_rootPath);

        var settings = store.Load();

        Assert.Equal(DiaryTimeline.AllNotebooksId, settings.TimelineNotebookId);
        settings.TimelineNotebookId = "work";
        store.Save(settings);
        Assert.Equal("work", store.Load().TimelineNotebookId);

        var data = new DiaryData { Notebooks = [new DiaryNotebook { Id = "work", Name = "工作记录" }] };
        Assert.Equal("work", DiaryTimeline.ResolveNotebookId(data, settings.TimelineNotebookId));
        data.Notebooks.Clear();
        settings.TimelineNotebookId = DiaryTimeline.ResolveNotebookId(data, settings.TimelineNotebookId);
        store.Save(settings);
        Assert.Equal(DiaryTimeline.AllNotebooksId, store.Load().TimelineNotebookId);
    }

    [Fact]
    public void OldTagListsAreMigratedIntoTheCatalogAndTagsCanBeRenamedOrRemoved()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "diary-data.json");
        File.WriteAllText(path, """
        {
          "schemaVersion": 1,
          "notebooks": [{ "id": "work", "name": "工作", "accentColor": "#2F80FF" }],
          "entries": [
            { "id": "entry-1", "title": "记录", "notebookId": "work", "mood": "平静", "weather": "晴", "location": "上海", "tags": ["工作", "复盘"] }
          ]
        }
        """);

        var store = new DiaryStore(_rootPath);
        var data = store.LoadData();

        Assert.Equal(DiaryData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(2, data.TagCatalog.Count);
        Assert.Contains(data.TagCatalog, tag => tag.Name == "工作");
        Assert.Contains(data.TagCatalog, tag => tag.Name == "复盘");
        var work = Assert.Single(data.TagCatalog, tag => tag.Name == "工作");
        Assert.Equal(DiaryMetadata.DefaultTagColorId, work.ColorId);
        Assert.True(DiaryTags.Rename(data, work.Id, "项目"));
        Assert.Contains("项目", Assert.Single(data.Entries).Tags);
        Assert.Contains("复盘", Assert.Single(data.Entries).Tags);
        Assert.True(DiaryTags.RemoveDefinition(data, work.Id));
        Assert.DoesNotContain(data.TagCatalog, tag => tag.Name == "项目");
        Assert.Contains("项目", Assert.Single(data.Entries).Tags);
    }

    [Fact]
    public void TagCatalogAppliesSelectedTagsAndUsesOneOfTwelveStableColors()
    {
        var data = new DiaryData();
        var entry = new DiaryEntry();

        DiaryTags.Apply(data, entry, ["旅行", "旅行", "周末"]);
        var travel = DiaryTags.Ensure(data, "旅行", "orange");

        Assert.Equal(2, entry.Tags.Count);
        Assert.Contains("旅行", entry.Tags);
        Assert.Contains("周末", entry.Tags);
        Assert.Equal("orange", travel.ColorId);
        Assert.Equal(12, DiaryMetadata.TagColors.Count);
        Assert.Contains(DiaryMetadata.TagColors, color => color.Id == "orange" && color.Hex == "#F59E0B");
    }

    [Fact]
    public void OldSettingsDefaultPrivacyFeaturesOffAndLocationDisablesWeather()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "diary-settings.json");
        File.WriteAllText(settingsPath, "{ \"theme\": \"dark\" }");
        var store = new DiarySettingsStore(_rootPath);

        var settings = store.Load();

        Assert.False(settings.LocationFeatureEnabled);
        Assert.False(settings.WeatherFeatureEnabled);
        settings.LocationFeatureEnabled = false;
        settings.WeatherFeatureEnabled = true;
        store.Save(settings);
        var reloaded = store.Load();
        Assert.False(reloaded.WeatherFeatureEnabled);
        Assert.Equal(DiaryLocationEndpoints.NominatimReverse, reloaded.ReverseGeocoderEndpoint);
        Assert.Equal(DiaryWeatherEndpoints.OpenMeteoForecast, reloaded.WeatherEndpoint);
    }

    [Theory]
    [InlineData(0, "晴")]
    [InlineData(2, "多云")]
    [InlineData(3, "阴")]
    [InlineData(45, "雾")]
    [InlineData(61, "小雨")]
    [InlineData(63, "中雨")]
    [InlineData(81, "大雨")]
    [InlineData(67, "冻雨")]
    [InlineData(71, "雪")]
    [InlineData(95, "雷雨")]
    public void OpenMeteoWeatherCodesMapToDiaryWeatherChoices(int code, string expected)
    {
        Assert.Equal(expected, OpenMeteoWeatherProvider.ConditionFor(code));
    }

    [Fact]
    public async Task OpenMeteoProviderReadsCurrentObservationWithoutARealNetworkRequest()
    {
        var handler = new StubHttpMessageHandler("""
        { "current": { "temperature_2m": 28.4, "weather_code": 2 } }
        """);
        var provider = new OpenMeteoWeatherProvider(new HttpClient(handler));

        var weather = await provider.GetCurrentAsync(31.2, 121.4, "https://weather.test/forecast");

        Assert.Equal("多云", weather.Condition);
        Assert.Equal(28.4, weather.TemperatureCelsius);
        Assert.Equal(2, weather.WeatherCode);
        Assert.Contains("latitude=31.20000", handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("current=temperature_2m,weather_code", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task NominatimProviderParsesReadableLocationWithoutARealNetworkRequest()
    {
        var handler = new StubHttpMessageHandler("""
        { "display_name": "静安区, 上海市, 中国" }
        """);
        var provider = new NominatimReverseGeocoder(new HttpClient(handler));

        var location = await provider.ReverseAsync(31.23, 121.47, "https://geocode.test/reverse");

        Assert.NotNull(location);
        Assert.Equal("静安区, 上海市, 中国", location.DisplayName);
        Assert.Contains("accept-language=zh-CN", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public void TimelineControllerOwnsRangeNavigationAndInclusiveDateWindows()
    {
        var controller = new DiaryTimelineStateController(new DateTime(2026, 7, 14));
        controller.SelectRange(DiaryTimelineStateController.RangeWeek);

        var firstWindow = controller.DateWindow();
        Assert.Equal(new DateTime(2026, 7, 13), firstWindow.Start);
        Assert.Equal(new DateTime(2026, 7, 19), firstWindow.End);
        controller.MoveRange(1);
        Assert.Equal(new DateTime(2026, 7, 21), controller.AnchorDate);
        var nextWindow = controller.DateWindow();
        Assert.Equal(new DateTime(2026, 7, 20), nextWindow.Start);
        Assert.Equal(new DateTime(2026, 7, 26), nextWindow.End);
    }

    [Fact]
    public void TimelineControllerNormalizesSessionInputAndConsumesNavigationState()
    {
        var controller = new DiaryTimelineStateController(new DateTime(2026, 7, 14));
        controller.Initialize("invalid", "2026-06-10", "2026-06-12", "2026-05-01");

        Assert.Equal(DiaryTimelineStateController.RangeAll, controller.RangeId);
        Assert.Equal(new DateTime(2026, 5, 1), controller.NavigatorMonth);
        var selectedWindow = controller.DateWindow();
        Assert.Equal(new DateTime(2026, 6, 12), selectedWindow.Start);
        Assert.Equal(new DateTime(2026, 6, 12), selectedWindow.End);

        controller.NavigateToDate(new DateTime(2026, 4, 20), new DateTime(2026, 4, 9));
        Assert.Equal(new DateTime(2026, 4, 9), controller.PendingScrollDate);
        controller.ClearPendingScroll();
        Assert.Null(controller.PendingScrollDate);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler(string content) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MemoryDiaryRepository : IDiaryRepository
    {
        public bool FailSaves { get; set; }
        public DiaryData LoadData() => new() { Notebooks = [new DiaryNotebook { Id = DiaryStore.DefaultNotebookId }] };
        public void SaveData(DiaryData data) { if (FailSaves) throw new IOException("synthetic failure"); }
        public DiaryAttachment ImportAttachment(string entryId, string sourcePath) => new();
        public void DeleteAttachment(DiaryAttachment attachment) { }
        public void DeleteAttachmentDirectory(string entryId) { }
        public string ResolveAttachmentPath(string relativePath) => relativePath;
    }

    private sealed class MemoryDiarySettingsRepository : IDiarySettingsRepository
    {
        public DiarySettings Load() => new();
        public void Save(DiarySettings settings) { }
    }

    private static void AssertJsonSubset(string expectedJson, string actualJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        using var actual = JsonDocument.Parse(actualJson);
        AssertJsonSubset(expected.RootElement, actual.RootElement);
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
