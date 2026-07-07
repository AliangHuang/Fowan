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
        ClientSettings settings;
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ClientSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            settings = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
        }
        catch
        {
            settings = new ClientSettings();
        }

        return settings;
    }

    public void Save(ClientSettings settings)
    {
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public static bool Normalize(ClientSettings settings)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(settings.Theme))
        {
            settings.Theme = "system";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            settings.Language = "system";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.CloseBehavior) ||
            settings.CloseBehavior is not (CloseBehaviorIds.MinimizeToTray or CloseBehaviorIds.Exit))
        {
            settings.CloseBehavior = CloseBehaviorIds.MinimizeToTray;
            changed = true;
        }

        var ignoredUpdateVersion = settings.IgnoredUpdateVersion?.Trim() ?? string.Empty;
        if (!string.Equals(settings.IgnoredUpdateVersion, ignoredUpdateVersion, StringComparison.Ordinal))
        {
            settings.IgnoredUpdateVersion = ignoredUpdateVersion;
            changed = true;
        }

        var userDisplayName = settings.UserDisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userDisplayName))
        {
            userDisplayName = UserDefaults.DisplayName;
        }

        if (!string.Equals(settings.UserDisplayName, userDisplayName, StringComparison.Ordinal))
        {
            settings.UserDisplayName = userDisplayName;
            changed = true;
        }

        var normalizedAvatarPath = UserDefaults.NormalizeAvatarPath(settings.AvatarPath);
        if (!string.Equals(settings.AvatarPath, normalizedAvatarPath, StringComparison.Ordinal))
        {
            settings.AvatarPath = normalizedAvatarPath;
            changed = true;
        }

        if (!settings.IsProfileInitialized)
        {
            if (string.IsNullOrWhiteSpace(settings.AvatarPath))
            {
                settings.AvatarPath = UserDefaults.RandomAvatarPath();
            }

            settings.IsProfileInitialized = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.AvatarPath))
        {
            settings.AvatarPath = UserDefaults.RandomAvatarPath();
            changed = true;
        }

        return changed;
    }
}
