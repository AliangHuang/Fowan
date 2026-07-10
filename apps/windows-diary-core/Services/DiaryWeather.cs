using System.Globalization;
using System.Text.Json;

namespace Fowan.Diary.Core.Services;

public static class DiaryWeatherEndpoints
{
    public const string OpenMeteoForecast = "https://api.open-meteo.com/v1/forecast";
}

public sealed record DiaryWeatherObservation(string Condition, double TemperatureCelsius, int WeatherCode, double Latitude, double Longitude, DateTimeOffset FetchedAt);

public interface IDiaryWeatherProvider
{
    Task<DiaryWeatherObservation> GetCurrentAsync(double latitude, double longitude, string endpoint, CancellationToken cancellationToken = default);
}

public sealed class OpenMeteoWeatherProvider : IDiaryWeatherProvider
{
    private readonly HttpClient _httpClient;

    public OpenMeteoWeatherProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    }

    public async Task<DiaryWeatherObservation> GetCurrentAsync(double latitude, double longitude, string endpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Weather endpoint is not configured.");
        }
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var url = string.Create(CultureInfo.InvariantCulture, $"{endpoint}{separator}latitude={latitude:F5}&longitude={longitude:F5}&current=temperature_2m,weather_code");
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("current", out var current) ||
            !current.TryGetProperty("temperature_2m", out var temperatureNode) ||
            !current.TryGetProperty("weather_code", out var codeNode) ||
            !temperatureNode.TryGetDouble(out var temperature) || !codeNode.TryGetInt32(out var code))
        {
            throw new InvalidDataException("Weather response did not include current weather.");
        }
        return new DiaryWeatherObservation(ConditionFor(code), temperature, code, latitude, longitude, DateTimeOffset.Now);
    }

    public static string ConditionFor(int code) => code switch
    {
        0 or 1 => "晴",
        2 => "多云",
        3 => "阴",
        45 or 48 => "雾",
        >= 51 and <= 57 or 61 => "小雨",
        63 or 80 => "中雨",
        65 or 81 or 82 => "大雨",
        66 or 67 => "冻雨",
        >= 71 and <= 77 or 85 or 86 => "雪",
        95 or 96 or 99 => "雷雨",
        _ => "待补充"
    };
}
