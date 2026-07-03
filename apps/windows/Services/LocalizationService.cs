using System.Globalization;
using System.Text.Json;

namespace Fowan.Windows.Services;

public sealed class LocalizationService
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, Dictionary<string, string>> StringCache = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private string _language = "system";

    public string Language => _language;

    public void SetLanguage(string language)
    {
        if (string.Equals(_language, language, StringComparison.OrdinalIgnoreCase) &&
            _strings.Count > 0)
        {
            return;
        }

        _language = language;
        _strings = LoadStrings(ResolveLanguage(language));
    }

    public string Get(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    private static string ResolveLanguage(string language)
    {
        if (!string.Equals(language, "system", StringComparison.OrdinalIgnoreCase))
        {
            return language;
        }

        var culture = CultureInfo.CurrentUICulture.Name;
        return culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    }

    private static Dictionary<string, string> LoadStrings(string language)
    {
        lock (CacheGate)
        {
            if (StringCache.TryGetValue(language, out var cached))
            {
                return new Dictionary<string, string>(cached, StringComparer.Ordinal);
            }
        }

        var baseDir = AppContext.BaseDirectory;
        var fallback = LoadFile(Path.Combine(baseDir, "Resources", "Strings", "en-US.json"));

        if (string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return Cache(language, fallback);
        }

        var localized = LoadFile(Path.Combine(baseDir, "Resources", "Strings", $"{language}.json"));
        foreach (var pair in localized)
        {
            fallback[pair.Key] = pair.Value;
        }

        return Cache(language, fallback);
    }

    private static Dictionary<string, string> Cache(string language, Dictionary<string, string> strings)
    {
        lock (CacheGate)
        {
            StringCache[language] = new Dictionary<string, string>(strings, StringComparer.Ordinal);
        }

        return strings;
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ??
               new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
