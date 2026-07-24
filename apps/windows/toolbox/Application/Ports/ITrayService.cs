namespace Fowan.Windows.AppPorts;

using Fowan.Windows.Platform.Contracts;

internal interface ITrayService : IDisposable
{
    event Action? MinimizeRequested;
    event Action? RestoreRequested;
    event Action? ExitRequested;

    PlatformOperationResult EnsureVisible();
}
