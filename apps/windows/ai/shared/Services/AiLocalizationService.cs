using System.Globalization;
using System.Text.Json;

namespace Fowan.Ai.Shared.Services;

public sealed class AiLocalizationService
{
    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public AiLocalizationService()
    {
        var language = ReadPreferredLanguage();
        var resolved = string.Equals(language, "system", StringComparison.OrdinalIgnoreCase)
            ? (CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US")
            : language;
        _strings = LoadFile(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings", "en-US.json"));
        if (!string.Equals(resolved, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in LoadFile(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings", $"{resolved}.json")))
            {
                _strings[pair.Key] = pair.Value;
            }
        }
    }

    public string Get(string key) => _strings.TryGetValue(key, out var value) ? value : key;

    private static string ReadPreferredLanguage()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fowan",
                "client-settings.json");
            if (!File.Exists(path))
            {
                return "system";
            }
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("language", out var language)
                ? language.GetString() ?? "system"
                : "system";
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return "system";
        }
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ??
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
