using Fowan.Diary.Shared.Services;

namespace Fowan.Diary.Shared.Models;

public sealed class DiaryData
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<DiaryNotebook> Notebooks { get; set; } = [];
    public List<DiaryTagDefinition> TagCatalog { get; set; } = [];
    public List<DiaryEntry> Entries { get; set; } = [];
}

public sealed class DiaryEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string NotebookId { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public string Weather { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DiaryLocationDetails? LocationDetails { get; set; }
    public DiaryWeatherDetails? WeatherDetails { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<DiaryAttachment> Attachments { get; set; } = [];
    public List<DiaryTodoLink> TodoLinks { get; set; } = [];
    public bool IsFavorite { get; set; }
    public bool IsDraft { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class DiaryAttachment
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

public sealed class DiaryTodoLink
{
    public string TaskId { get; set; } = string.Empty;
    public string TitleSnapshot { get; set; } = string.Empty;
    public string ListNameSnapshot { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.Today;
}

public sealed class DiaryNotebook
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
}

public sealed class DiaryTagDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ColorId { get; set; } = DiaryMetadata.DefaultTagColorId;
}

public sealed class DiaryLocationDetails
{
    public string Source { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset ResolvedAt { get; set; }
}

public sealed class DiaryWeatherDetails
{
    public string Source { get; set; } = string.Empty;
    public double TemperatureCelsius { get; set; }
    public int WeatherCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public sealed class DiarySettings
{
    public string Theme { get; set; } = DiaryThemeIds.System;
    public string CurrentViewId { get; set; } = DiaryViewIds.Today;
    public string TimelineNotebookId { get; set; } = "all";
    public bool LocationFeatureEnabled { get; set; }
    public bool WeatherFeatureEnabled { get; set; }
    public DateTimeOffset? LocationConsentAcceptedAt { get; set; }
    public DateTimeOffset? WeatherConsentAcceptedAt { get; set; }
    public string ReverseGeocoderEndpoint { get; set; } = DiaryLocationEndpoints.NominatimReverse;
    public string WeatherEndpoint { get; set; } = DiaryWeatherEndpoints.OpenMeteoForecast;
}

public sealed class TodoCandidate
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ListName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.Today;
}

public static class DiaryThemeIds
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
}

public static class DiaryViewIds
{
    public const string Today = "today";
    public const string Timeline = "timeline";
    public const string Calendar = "calendar";
    public const string Tags = "tags";
    public const string Favorites = "favorites";
    public const string Drafts = "drafts";

    public static string Notebook(string notebookId) => $"notebook:{notebookId}";

    public static bool IsNotebook(string viewId) => viewId.StartsWith("notebook:", StringComparison.Ordinal);

    public static string NotebookId(string viewId) => IsNotebook(viewId) ? viewId[9..] : string.Empty;
}
