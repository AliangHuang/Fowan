using System.Text.Json;
using Fowan.Windows.Platform.Windows;
using Fowan.Windows.Services;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class AvatarStoreTests : IDisposable
{
    private readonly string _localApplicationDataPath = Path.Combine(
        Path.GetTempPath(),
        "FowanAvatarStoreTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveCurrentProfileAvatarReadsTheLatestToolboxSelection()
    {
        var profileRoot = Path.Combine(_localApplicationDataPath, "Fowan");
        Directory.CreateDirectory(profileRoot);
        var firstAvatar = Path.Combine(_localApplicationDataPath, "first-avatar.png");
        var secondAvatar = Path.Combine(_localApplicationDataPath, "second-avatar.png");
        File.WriteAllBytes(firstAvatar, []);
        File.WriteAllBytes(secondAvatar, []);

        SaveSelection(firstAvatar);
        Assert.Equal(Path.GetFullPath(firstAvatar), AvatarStore.ResolveCurrentProfileAvatar(_localApplicationDataPath));

        SaveSelection(secondAvatar);
        Assert.Equal(Path.GetFullPath(secondAvatar), AvatarStore.ResolveCurrentProfileAvatar(_localApplicationDataPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_localApplicationDataPath))
        {
            Directory.Delete(_localApplicationDataPath, recursive: true);
        }
    }

    private void SaveSelection(string avatarPath)
    {
        var settings = new ClientSettings { AvatarPath = avatarPath };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(Path.Combine(_localApplicationDataPath, "Fowan", "client-settings.json"), json);
    }
}
