using System.Globalization;
using System.Text.Json;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Application.Ports;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal static class ToolCenterCompositionRoot
{
    public static ToolCenterSession CreateSession()
    {
        string? json = null;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fowan", "client-settings.json");
            if (File.Exists(path)) json = File.ReadAllText(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "ToolCenter");
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yym");
        var releases = new GitHubToolReleaseClient();
        var installations = new WindowsToolInstallationPlatform(dataRoot, userData, releases, new WindowsManagedToolProcesses());
        return CreateSession(json, CultureInfo.CurrentUICulture, releases, installations);
    }

    internal static ToolCenterSession CreateSession(string? preferencesJson, CultureInfo systemCulture,
        IToolReleaseClient? releaseClient = null, IToolInstallationPlatform? platform = null)
    {
        var preferences = ParsePreferences(preferencesJson);
        var language = preferences.Language == "system"
            ? (systemCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US")
            : preferences.Language;
        var strings = LoadStrings("en-US");
        if (language != "en-US")
            foreach (var pair in LoadStrings(language)) strings[pair.Key] = pair.Value;
        return new(preferences, strings, releaseClient: releaseClient, platform: platform);
    }

    internal static ToolCenterPreferences ParsePreferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new();
            string Read(string name)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                        return property.Value.GetString() ?? "system";
                return "system";
            }
            var theme = Read("Theme");
            var language = Read("Language");
            return new(theme is "light" or "dark" ? theme : "system",
                language is "zh-CN" or "en-US" ? language : "system");
        }
        catch (JsonException) { return new(); }
    }

    private static Dictionary<string, string> LoadStrings(string language)
    {
        using var stream = typeof(ToolCenterCompositionRoot).Assembly.GetManifestResourceStream(
            $"Fowan.ToolCenter.Windows.Resources.Strings.{language}.json")
            ?? throw new InvalidOperationException($"Missing built-in language resource: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
