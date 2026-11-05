using Fowan.Windows.Services;
using System.Text.Json;

namespace Fowan.Windows.Platform.Windows;

internal static class AvatarStore
{
    public static IReadOnlyList<string> ImageExtensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];
    private static readonly JsonSerializerOptions ProfileReadJsonOptions = new(JsonSerializerDefaults.Web);

    public static string ResolveCurrentProfileAvatar(string? localApplicationDataPath = null)
    {
        try
        {
            var settingsPath = Path.Combine(
                string.IsNullOrWhiteSpace(localApplicationDataPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    : localApplicationDataPath,
                "Fowan",
                "client-settings.json");
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<ClientSettings>(
                    File.ReadAllText(settingsPath),
                    ProfileReadJsonOptions);
                return Resolve(settings?.AvatarPath ?? string.Empty);
            }
        }
        catch
        {
            // Fall through to the bundled default avatar.
        }

        return Resolve(string.Empty);
    }

    public static string Resolve(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var normalized = UserDefaults.NormalizeAvatarPath(configuredPath);
                if (UserDefaults.IsBuiltInAvatarPath(normalized))
                {
                    var builtIn = Path.Combine(AppContext.BaseDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(builtIn)) return builtIn;
                }
                var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
                var fullPath = Path.IsPathRooted(expanded)
                    ? Path.GetFullPath(expanded)
                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
                if (File.Exists(fullPath)) return fullPath;
            }
            catch
            {
                // Fall through to the bundled default avatar.
            }
        }
        return Path.Combine(AppContext.BaseDirectory, UserDefaults.BuiltInAvatarPaths[0]);
    }

    public static string Save(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;
        try
        {
            var normalized = UserDefaults.NormalizeAvatarPath(sourcePath);
            if (UserDefaults.IsBuiltInAvatarPath(normalized)) return normalized;
            var source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourcePath));
            if (!File.Exists(source)) return string.Empty;
            var profileRoot = Path.GetFullPath(ProfileRootPath());
            Directory.CreateDirectory(profileRoot);
            if (source.StartsWith(profileRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return source;
            var extension = Path.GetExtension(source);
            if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) extension = ".png";
            var destination = Path.Combine(profileRoot, $"avatar{extension.ToLowerInvariant()}");
            File.Copy(source, destination, overwrite: true);
            return destination;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ProfileRootPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "Profile");
}
