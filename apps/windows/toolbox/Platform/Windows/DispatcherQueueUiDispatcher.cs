using Fowan.Windows.Platform.Contracts;
using Microsoft.UI.Dispatching;

namespace Fowan.Windows.Platform.Windows;

internal sealed class DispatcherQueueUiDispatcher(DispatcherQueue dispatcherQueue) : IUiDispatcher
{
    public bool TryEnqueue(Action action) => dispatcherQueue.TryEnqueue(() => action());

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await action();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetException(new UiDispatcherUnavailableException());
        }

        return completion.Task;
    }
}
