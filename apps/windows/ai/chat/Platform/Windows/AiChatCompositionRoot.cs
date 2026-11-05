using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Services;
using Fowan.Windows.Platform.Windows;
using Fowan.Windows.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fowan.Ai.Chat.Windows.Platform.Windows;

internal static class AiChatCompositionRoot
{
    public static AiChatSession CreateSession() =>
        new(new AiCoreClient(new WindowsAiCoreProcessLauncher()));

    public static ImageSource? LoadCurrentToolboxAvatar()
    {
        try
        {
            var avatarPath = AvatarStore.ResolveCurrentProfileAvatar();
            return File.Exists(avatarPath)
                ? new BitmapImage(new Uri(avatarPath, UriKind.Absolute))
                : null;
        }
        catch
        {
            return null;
        }
    }
}
