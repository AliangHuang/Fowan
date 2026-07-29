using Fowan.Pomodoro.Shared.Services;
using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Platform.Windows;

namespace Fowan.Windows.Services;

/// <summary>Toolbox only reads the constrained next-wake handoff; reminder business data stays in Pomodoro.</summary>
internal sealed class PomodoroWakeCoordinator : IDisposable
{
    private readonly IProcessLauncher _launcher;
    private readonly Timer _timer;
    private DateTimeOffset _lastDispatch = DateTimeOffset.MinValue;

    public PomodoroWakeCoordinator(IProcessLauncher launcher)
    {
        _launcher = launcher;
        _timer = new Timer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        // The next wake is persisted by Pomodoro whenever its state changes.  Starting
        // the GUI executable merely to rebuild that value can surface the Pomodoro
        // window while the user is opening Toolbox, so startup only begins polling.
        // A process is launched only when the persisted wake is actually due.
        _timer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20));
    }

    public void Dispose() => _timer.Dispose();

    private void Tick()
    {
        var nextWake = PomodoroWakeSchedule.Read();
        if (nextWake is null || nextWake > DateTimeOffset.Now) return;
        if (DateTimeOffset.Now - _lastDispatch < TimeSpan.FromSeconds(15)) return;
        var executable = ToolExecutableResolver.ResolvePomodoro();
        if (executable is null) return;
        var result = _launcher.Launch(new ProcessLaunchRequest(executable, Arguments: "--dispatch-due"));
        if (result.Succeeded) _lastDispatch = DateTimeOffset.Now;
    }
}
