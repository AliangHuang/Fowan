using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Services;

namespace Fowan.Windows.AppPorts;

internal interface IUpdateChecker
{
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
}

internal enum UpdateCheckStatus
{
    NotStarted,
    NoUpdate,
    UpdatePresented,
    Cancelled,
    DispatcherUnavailable,
    Failed
}

internal sealed record UpdateCheckOutcome(UpdateCheckStatus Status, string? Error = null);

internal sealed class UpdateCheckCoordinator(
    IUpdateChecker checker,
    IUiDispatcher dispatcher,
    Func<UpdateInfo, Task> presentUpdate,
    Action<string> trace,
    TimeSpan? startupDelay = null)
{
    private readonly object _sync = new();
    private readonly TimeSpan _startupDelay = startupDelay ?? TimeSpan.FromMilliseconds(1200);
    private CancellationTokenSource? _cancellation;
    private bool _started;

    public Task<UpdateCheckOutcome> Completion { get; private set; } =
        Task.FromResult(new UpdateCheckOutcome(UpdateCheckStatus.NotStarted));

    public bool Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return false;
            }

            _started = true;
            _cancellation = new CancellationTokenSource();
            Completion = RunObservedAsync(_cancellation);
            return true;
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _cancellation?.Cancel();
        }
    }

    private async Task<UpdateCheckOutcome> RunObservedAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_startupDelay, cancellation.Token);
            var update = await checker.CheckForUpdateAsync(cancellation.Token);
            if (update is null)
            {
                return new UpdateCheckOutcome(UpdateCheckStatus.NoUpdate);
            }

            await dispatcher.InvokeAsync(() => presentUpdate(update), cancellation.Token);
            return new UpdateCheckOutcome(UpdateCheckStatus.UpdatePresented);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            trace("Update check cancelled");
            return new UpdateCheckOutcome(UpdateCheckStatus.Cancelled);
        }
        catch (UiDispatcherUnavailableException exception)
        {
            trace("Update check UI dispatcher unavailable");
            return new UpdateCheckOutcome(UpdateCheckStatus.DispatcherUnavailable, exception.Message);
        }
        catch (Exception exception)
        {
            trace($"Update check failed: {exception.GetType().Name}");
            return new UpdateCheckOutcome(UpdateCheckStatus.Failed, exception.Message);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                }
            }
            cancellation.Dispose();
        }
    }
}
