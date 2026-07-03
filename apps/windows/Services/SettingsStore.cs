using System.Text.Json;

namespace Fowan.Windows.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fowan");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "client-settings.json");
    }

    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ClientSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save(ClientSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
