using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Services;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class PomodoroWakeCoordinatorTests
{
    [Fact]
    public void StartupDoesNotLaunchPomodoroBeforeAWakeIsDue()
    {
        var launcher = new RecordingLauncher();
        using var coordinator = new PomodoroWakeCoordinator(launcher);

        coordinator.Start();

        Assert.Empty(launcher.Requests);
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public List<ProcessLaunchRequest> Requests { get; } = [];

        public PlatformOperationResult Launch(ProcessLaunchRequest request)
        {
            Requests.Add(request);
            return PlatformOperationResult.Success();
        }
    }
}
