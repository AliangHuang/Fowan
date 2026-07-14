namespace Fowan.Windows.Services;

internal interface ITrayService : IDisposable
{
    event Action? MinimizeRequested;
    event Action? RestoreRequested;
    event Action? ExitRequested;

    void EnsureVisible();
}
