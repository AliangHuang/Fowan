using Fowan.Windows.Application;
using Fowan.Windows.Services;

namespace Fowan.Windows.Platform.Windows;

internal static class ToolboxCompositionRoot
{
    public static ToolboxSession CreateSession() => new(new SettingsStore());
}

