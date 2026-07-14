namespace Fowan.Todo.Windows.AppPorts;

internal interface IStickyProcessCoordinator
{
    bool TryPrewarm();

    bool TryShow();

    bool TryShutdown();
}
