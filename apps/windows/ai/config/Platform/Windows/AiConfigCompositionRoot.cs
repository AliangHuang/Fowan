using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Services;

namespace Fowan.Ai.Config.Windows.Platform.Windows;

internal static class AiConfigCompositionRoot
{
    public static AiConfigSession CreateSession() =>
        new(new AiCoreClient(new WindowsAiCoreProcessLauncher()));
}

