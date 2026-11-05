using Fowan.Windows.AppPorts;
using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Services;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class UpdateCheckCoordinatorTests
{
    private static readonly UpdateInfo Update = new(
        "9.9.9",
        "https://github.com/AliangHuang/Fowan/releases/download/v9.9.9/setup.exe",
        new string('a', 64),
        string.Empty,
        string.Empty);

    [Fact]
    public async Task StartsOnlyOnceAndReportsNoUpdate()
    {
        var coordinator = Create(new StubChecker((UpdateInfo?)null));

        Assert.True(coordinator.Start());
        Assert.False(coordinator.Start());
        Assert.Equal(UpdateCheckStatus.NoUpdate, (await coordinator.Completion).Status);
    }

    [Fact]
    public async Task PresentsAvailableUpdateThroughDispatcher()
    {
        UpdateInfo? presented = null;
        var coordinator = Create(new StubChecker(Update), present: update => presented = update);

        coordinator.Start();

        Assert.Equal(UpdateCheckStatus.UpdatePresented, (await coordinator.Completion).Status);
        Assert.Same(Update, presented);
    }

    [Fact]
    public async Task CancellationCompletesWithObservableOutcome()
    {
        var coordinator = new UpdateCheckCoordinator(
            new StubChecker((UpdateInfo?)null),
            new StubDispatcher(),
            _ => Task.CompletedTask,
            _ => { },
            TimeSpan.FromMinutes(1));

        coordinator.Start();
        coordinator.Cancel();

        Assert.Equal(UpdateCheckStatus.Cancelled, (await coordinator.Completion).Status);
    }

    [Fact]
    public async Task DispatcherRejectionIsDistinguishedFromPromptFailure()
    {
        var coordinator = Create(new StubChecker(Update), new StubDispatcher(reject: true));

        coordinator.Start();

        Assert.Equal(UpdateCheckStatus.DispatcherUnavailable, (await coordinator.Completion).Status);
    }

    [Fact]
    public async Task CheckerAndPromptFailuresAreObserved()
    {
        var checkerFailure = Create(new StubChecker(new InvalidOperationException("network failed")));
        checkerFailure.Start();
        Assert.Equal(UpdateCheckStatus.Failed, (await checkerFailure.Completion).Status);

        var promptFailure = Create(
            new StubChecker(Update),
            presentAsync: _ => Task.FromException(new InvalidOperationException("dialog failed")));
        promptFailure.Start();
        Assert.Equal(UpdateCheckStatus.Failed, (await promptFailure.Completion).Status);
    }

    private static UpdateCheckCoordinator Create(
        IUpdateChecker checker,
        IUiDispatcher? dispatcher = null,
        Action<UpdateInfo>? present = null,
        Func<UpdateInfo, Task>? presentAsync = null) =>
        new(
            checker,
            dispatcher ?? new StubDispatcher(),
            presentAsync ?? (update =>
            {
                present?.Invoke(update);
                return Task.CompletedTask;
            }),
            _ => { },
            TimeSpan.Zero);

    private sealed class StubChecker : IUpdateChecker
    {
        private readonly UpdateInfo? _update;
        private readonly Exception? _exception;

        public StubChecker(UpdateInfo? update) => _update = update;

        public StubChecker(Exception exception) => _exception = exception;

        public Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default) =>
            _exception is null
                ? Task.FromResult(_update)
                : Task.FromException<UpdateInfo?>(_exception);
    }

    private sealed class StubDispatcher(bool reject = false) : IUiDispatcher
    {
        public bool TryEnqueue(Action action)
        {
            if (!reject)
            {
                action();
            }
            return !reject;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            reject ? Task.FromException(new UiDispatcherUnavailableException()) : action();
    }
}
