namespace Fowan.Todo.Windows.AppPorts;

internal interface IStickyProcessCoordinator
{
    Task<bool> TryPrewarmAsync(CancellationToken cancellationToken = default);

    Task<bool> TryShowAsync(CancellationToken cancellationToken = default);

    Task<bool> TryShutdownAsync(CancellationToken cancellationToken = default);
}
