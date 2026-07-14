namespace Fowan.Ai.Shared.Services;

public interface IUiDispatcher
{
    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
