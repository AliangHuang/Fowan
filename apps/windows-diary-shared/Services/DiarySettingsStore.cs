using Fowan.Diary.Shared.Models;
using System.Text.Json;

namespace Fowan.Diary.Shared.Services;

public sealed class DiarySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string RootPath { get; }
    public string SettingsPath { get; }

    public DiarySettingsStore()
        : this(DiaryStore.ResolveRootPath())
    {
    }

    public DiarySettingsStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
        SettingsPath = Path.Combine(RootPath, "diary-settings.json");
    }

    public DiarySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<DiarySettings>(File.ReadAllText(SettingsPath), JsonOptions)
                    ?? new DiarySettings();
                Normalize(settings);
                return settings;
            }
        }
        catch
        {
            // Fall back to defaults when the local diary settings file is malformed.
        }

        return new DiarySettings();
    }

    public void Save(DiarySettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(RootPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void Normalize(DiarySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Theme) ||
            settings.Theme is not (DiaryThemeIds.System or DiaryThemeIds.Light or DiaryThemeIds.Dark))
        {
            settings.Theme = DiaryThemeIds.System;
        }

        if (string.IsNullOrWhiteSpace(settings.CurrentViewId))
        {
            settings.CurrentViewId = DiaryViewIds.Today;
        }

        if (string.IsNullOrWhiteSpace(settings.TimelineNotebookId))
        {
            settings.TimelineNotebookId = DiaryTimeline.AllNotebooksId;
        }

        settings.ReverseGeocoderEndpoint = NormalizeHttpsEndpoint(settings.ReverseGeocoderEndpoint, DiaryLocationEndpoints.NominatimReverse);
        settings.WeatherEndpoint = NormalizeHttpsEndpoint(settings.WeatherEndpoint, DiaryWeatherEndpoints.OpenMeteoForecast);
        if (!settings.LocationFeatureEnabled)
        {
            settings.WeatherFeatureEnabled = false;
        }
    }

    private static string NormalizeHttpsEndpoint(string? value, string fallback) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? uri.ToString().TrimEnd('/') : fallback;
}
