using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Services;

namespace Fowan.Ai.Chat.Windows.Platform.Windows;

internal static class AiChatCompositionRoot
{
    public static AiChatSession CreateSession() =>
        new(new AiCoreClient(new WindowsAiCoreProcessLauncher()));
}

