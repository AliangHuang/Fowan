namespace Fowan.Todo.Sticky.Windows.AppPorts;

internal interface IStickyMainProcessCoordinator
{
    bool TryActivate(string executablePath);

    bool TryShutdown();
}
