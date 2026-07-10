using System.Globalization;
using System.Text.Json;

namespace Fowan.Diary.Core.Services;

public static class DiaryLocationEndpoints
{
    public const string NominatimReverse = "https://nominatim.openstreetmap.org/reverse";
}

public sealed record DiaryReverseGeocodeResult(string DisplayName, double Latitude, double Longitude);

public interface IDiaryReverseGeocoder
{
    Task<DiaryReverseGeocodeResult?> ReverseAsync(double latitude, double longitude, string endpoint, CancellationToken cancellationToken = default);
}

public sealed class NominatimReverseGeocoder : IDiaryReverseGeocoder
{
    private readonly HttpClient _httpClient;

    public NominatimReverseGeocoder(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Fowan-Diary/1.0");
        }
    }

    public async Task<DiaryReverseGeocodeResult?> ReverseAsync(double latitude, double longitude, string endpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var url = string.Create(CultureInfo.InvariantCulture, $"{endpoint}{separator}lat={latitude:F5}&lon={longitude:F5}&format=jsonv2&addressdetails=1&accept-language=zh-CN");
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("display_name", out var displayNameNode))
        {
            return null;
        }
        var displayName = displayNameNode.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(displayName) ? null : new DiaryReverseGeocodeResult(displayName, latitude, longitude);
    }
}
