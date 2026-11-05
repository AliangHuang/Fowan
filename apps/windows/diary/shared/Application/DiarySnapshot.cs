using Fowan.Diary.Shared.Models;
using System.Collections.Immutable;

namespace Fowan.Diary.Shared.Application;

public sealed record DiaryLocationSnapshot(string Source, double Latitude, double Longitude, DateTimeOffset ResolvedAt);
public sealed record DiaryWeatherSnapshot(string Source, double TemperatureCelsius, int WeatherCode,
    double Latitude, double Longitude, DateTimeOffset FetchedAt);
public sealed record DiaryAttachmentSnapshot(string Id, string FileName, string RelativePath, string ContentType);
public sealed record DiaryTodoLinkSnapshot(string TaskId, string TitleSnapshot, string ListNameSnapshot, DateTime StartDate);
public sealed record DiaryNotebookSnapshot(string Id, string Name, string AccentColor);
public sealed record DiaryTagSnapshot(string Id, string Name, string ColorId);

public sealed record DiaryEntrySnapshot(
    string Id,
    string Title,
    string Body,
    string NotebookId,
    string Mood,
    string Weather,
    string Location,
    DiaryLocationSnapshot? LocationDetails,
    DiaryWeatherSnapshot? WeatherDetails,
    ImmutableArray<string> Tags,
    ImmutableArray<DiaryAttachmentSnapshot> Attachments,
    ImmutableArray<DiaryTodoLinkSnapshot> TodoLinks,
    bool IsFavorite,
    bool IsDraft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DiarySettingsSnapshot(
    string Theme,
    string CurrentViewId,
    string TimelineNotebookId,
    bool LocationFeatureEnabled,
    bool WeatherFeatureEnabled,
    DateTimeOffset? LocationConsentAcceptedAt,
    DateTimeOffset? WeatherConsentAcceptedAt,
    string ReverseGeocoderEndpoint,
    string WeatherEndpoint);

public sealed record DiarySnapshot(
    int SchemaVersion,
    ImmutableArray<DiaryNotebookSnapshot> Notebooks,
    ImmutableArray<DiaryTagSnapshot> Tags,
    ImmutableArray<DiaryEntrySnapshot> Entries,
    DiarySettingsSnapshot Settings)
{
    internal static DiarySnapshot From(DiaryData data, DiarySettings settings) => new(
        data.SchemaVersion,
        data.Notebooks.Select(item => new DiaryNotebookSnapshot(item.Id, item.Name, item.AccentColor)).ToImmutableArray(),
        data.TagCatalog.Select(item => new DiaryTagSnapshot(item.Id, item.Name, item.ColorId)).ToImmutableArray(),
        data.Entries.Select(Entry).ToImmutableArray(),
        new DiarySettingsSnapshot(
            settings.Theme, settings.CurrentViewId, settings.TimelineNotebookId,
            settings.LocationFeatureEnabled, settings.WeatherFeatureEnabled,
            settings.LocationConsentAcceptedAt, settings.WeatherConsentAcceptedAt,
            settings.ReverseGeocoderEndpoint, settings.WeatherEndpoint));

    public DiaryData ToQueryData() => new()
    {
        SchemaVersion = SchemaVersion,
        Notebooks = Notebooks.Select(item => new DiaryNotebook { Id = item.Id, Name = item.Name, AccentColor = item.AccentColor }).ToList(),
        TagCatalog = Tags.Select(item => new DiaryTagDefinition { Id = item.Id, Name = item.Name, ColorId = item.ColorId }).ToList(),
        Entries = Entries.Select(Entry).ToList()
    };

    public DiarySettings ToQuerySettings() => new()
    {
        Theme = Settings.Theme,
        CurrentViewId = Settings.CurrentViewId,
        TimelineNotebookId = Settings.TimelineNotebookId,
        LocationFeatureEnabled = Settings.LocationFeatureEnabled,
        WeatherFeatureEnabled = Settings.WeatherFeatureEnabled,
        LocationConsentAcceptedAt = Settings.LocationConsentAcceptedAt,
        WeatherConsentAcceptedAt = Settings.WeatherConsentAcceptedAt,
        ReverseGeocoderEndpoint = Settings.ReverseGeocoderEndpoint,
        WeatherEndpoint = Settings.WeatherEndpoint
    };

    private static DiaryEntrySnapshot Entry(DiaryEntry item) => new(
        item.Id, item.Title, item.Body, item.NotebookId, item.Mood, item.Weather, item.Location,
        item.LocationDetails is null ? null : new DiaryLocationSnapshot(
            item.LocationDetails.Source, item.LocationDetails.Latitude, item.LocationDetails.Longitude,
            item.LocationDetails.ResolvedAt),
        item.WeatherDetails is null ? null : new DiaryWeatherSnapshot(
            item.WeatherDetails.Source, item.WeatherDetails.TemperatureCelsius,
            item.WeatherDetails.WeatherCode, item.WeatherDetails.Latitude,
            item.WeatherDetails.Longitude, item.WeatherDetails.FetchedAt),
        item.Tags.ToImmutableArray(),
        item.Attachments.Select(value => new DiaryAttachmentSnapshot(
            value.Id, value.FileName, value.RelativePath, value.ContentType)).ToImmutableArray(),
        item.TodoLinks.Select(value => new DiaryTodoLinkSnapshot(
            value.TaskId, value.TitleSnapshot, value.ListNameSnapshot, value.StartDate)).ToImmutableArray(),
        item.IsFavorite, item.IsDraft, item.CreatedAt, item.UpdatedAt);

    private static DiaryEntry Entry(DiaryEntrySnapshot item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Body = item.Body,
        NotebookId = item.NotebookId,
        Mood = item.Mood,
        Weather = item.Weather,
        Location = item.Location,
        LocationDetails = item.LocationDetails is null ? null : new DiaryLocationDetails
        {
            Source = item.LocationDetails.Source,
            Latitude = item.LocationDetails.Latitude,
            Longitude = item.LocationDetails.Longitude,
            ResolvedAt = item.LocationDetails.ResolvedAt
        },
        WeatherDetails = item.WeatherDetails is null ? null : new DiaryWeatherDetails
        {
            Source = item.WeatherDetails.Source,
            TemperatureCelsius = item.WeatherDetails.TemperatureCelsius,
            WeatherCode = item.WeatherDetails.WeatherCode,
            Latitude = item.WeatherDetails.Latitude,
            Longitude = item.WeatherDetails.Longitude,
            FetchedAt = item.WeatherDetails.FetchedAt
        },
        Tags = [.. item.Tags],
        Attachments = item.Attachments.Select(value => new DiaryAttachment
        {
            Id = value.Id, FileName = value.FileName, RelativePath = value.RelativePath,
            ContentType = value.ContentType
        }).ToList(),
        TodoLinks = item.TodoLinks.Select(value => new DiaryTodoLink
        {
            TaskId = value.TaskId, TitleSnapshot = value.TitleSnapshot,
            ListNameSnapshot = value.ListNameSnapshot, StartDate = value.StartDate
        }).ToList(),
        IsFavorite = item.IsFavorite,
        IsDraft = item.IsDraft,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };
}

public sealed record DiaryDraftInput(string NotebookId, string Mood, string Weather, string Location);

public interface IDiaryCommands
{
    DiarySaveResult SaveData(DiaryData candidate);
    DiarySaveResult SaveSettings(DiarySettings candidate);
    DiaryEntrySnapshot EnsureDraft(DiaryDraftInput input);
    DiarySaveResult UpdateDraftText(string entryId, string body);
    DiarySaveResult UpdateEntryMetadata(string entryId, string? mood = null, string? weather = null,
        DiaryWeatherDetails? weatherDetails = null, string? location = null,
        DiaryLocationDetails? locationDetails = null);
    DiarySaveResult FinalizeDraft(string entryId);
    DiarySaveResult ToggleFavorite(string entryId);
    DiarySaveResult SetCurrentView(string viewId);
    DiarySaveResult SetTimelineNotebook(string notebookId);
}
