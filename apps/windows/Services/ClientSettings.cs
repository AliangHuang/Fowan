namespace Fowan.Windows.Services;

public sealed class ClientSettings
{
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "system";
    public string CloseBehavior { get; set; } = CloseBehaviorIds.MinimizeToTray;
    public string UserDisplayName { get; set; } = UserDefaults.DisplayName;
    public string AvatarPath { get; set; } = string.Empty;
    public bool IsProfileInitialized { get; set; }
    public bool IsSidebarCollapsed { get; set; }
}

public static class UserDefaults
{
    public const string DisplayName = "管理员";

    public static readonly string[] BuiltInAvatarPaths =
    [
        "Assets/Avatars/fowan-avatar-g1-01.png",
        "Assets/Avatars/fowan-avatar-g1-02.png",
        "Assets/Avatars/fowan-avatar-g1-03.png",
        "Assets/Avatars/fowan-avatar-g1-04.png",
        "Assets/Avatars/fowan-avatar-g1-05.png",
        "Assets/Avatars/fowan-avatar-g2-01.png",
        "Assets/Avatars/fowan-avatar-g2-02.png",
        "Assets/Avatars/fowan-avatar-g2-03.png",
        "Assets/Avatars/fowan-avatar-g2-04.png",
        "Assets/Avatars/fowan-avatar-g2-05.png",
        "Assets/Avatars/fowan-avatar-g2-06.png",
        "Assets/Avatars/fowan-avatar-g2-07.png",
        "Assets/Avatars/fowan-avatar-g2-08.png",
        "Assets/Avatars/fowan-avatar-g2-09.png",
        "Assets/Avatars/fowan-avatar-g3-02.png",
        "Assets/Avatars/fowan-avatar-g3-03.png",
        "Assets/Avatars/fowan-avatar-g3-04.png",
        "Assets/Avatars/fowan-avatar-g3-07.png",
        "Assets/Avatars/fowan-avatar-cat-moon-02.png",
        "Assets/Avatars/fowan-avatar-cat-moon-03.png",
        "Assets/Avatars/fowan-avatar-cat-moon-05.png",
        "Assets/Avatars/fowan-avatar-cat-moon-06.png",
        "Assets/Avatars/fowan-avatar-cat-moon-08.png",
        "Assets/Avatars/fowan-avatar-cat-moon-09.png",
        "Assets/Avatars/fowan-avatar-dog-02.png",
        "Assets/Avatars/fowan-avatar-dog-03.png",
        "Assets/Avatars/fowan-avatar-dog-04.png",
        "Assets/Avatars/fowan-avatar-dog-06.png",
        "Assets/Avatars/fowan-avatar-dog-07.png",
        "Assets/Avatars/fowan-avatar-dog-08.png",
        "Assets/Avatars/fowan-avatar-tiger-01.png",
        "Assets/Avatars/fowan-avatar-tiger-02.png",
        "Assets/Avatars/fowan-avatar-tiger-03.png",
        "Assets/Avatars/fowan-avatar-tiger-04.png",
        "Assets/Avatars/fowan-avatar-tiger-05.png",
        "Assets/Avatars/fowan-avatar-tiger-09.png"
    ];

    public static string RandomAvatarPath()
    {
        return BuiltInAvatarPaths[Random.Shared.Next(BuiltInAvatarPaths.Length)];
    }

    public static string NormalizeAvatarPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }

    public static bool IsBuiltInAvatarPath(string? path)
    {
        var normalized = NormalizeAvatarPath(path);
        return BuiltInAvatarPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }
}

public static class CloseBehaviorIds
{
    public const string MinimizeToTray = "minimizeToTray";
    public const string Exit = "exit";
}
