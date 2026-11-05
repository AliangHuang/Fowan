using System.Diagnostics;
using Fowan.Todo.Windows.AppPorts;

namespace Fowan.Todo.Windows.Platform.Windows;

internal sealed class WindowsStickyProcessCoordinator : IStickyProcessCoordinator
{
#if FOWAN_DEVELOPMENT_RUNTIME
    private const string StickyExecutableName = "Fowan.Todo.Sticky.Windows.Dev.exe";
    private const string MainExecutableName = "Fowan.Todo.Windows.Dev.exe";
#else
    private const string StickyExecutableName = "Fowan.Todo.Sticky.Windows.exe";
    private const string MainExecutableName = "Fowan.Todo.Windows.exe";
#endif
    private const string ActivationEventName = @"Local\Fowan.Todo.Sticky.Windows.Activate";
    private const string ActivationAcknowledgedEventName = @"Local\Fowan.Todo.Sticky.Windows.Activated";
    private const string ReadyEventName = @"Local\Fowan.Todo.Sticky.Windows.Ready";
    private const string ShutdownEventName = @"Local\Fowan.Todo.Sticky.Windows.Shutdown";
    private const string StartHiddenArgument = "--start-hidden";
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _transitionGate = new(initialCount: 1, maxCount: 1);

    public Task<bool> TryPrewarmAsync(CancellationToken cancellationToken = default) =>
        TryEnsureStartedAsync(show: false, cancellationToken);

    public Task<bool> TryShowAsync(CancellationToken cancellationToken = default) =>
        TryEnsureStartedAsync(show: true, cancellationToken);

    public async Task<bool> TryShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                using var process = FindRunningStickyProcess();
                if (process is null || process.HasExited)
                {
                    return true;
                }

                if (!await TrySignalEventAsync(ShutdownEventName, process, cancellationToken))
                {
                    return process.HasExited;
                }

                return await WaitForExitAsync(process, ShutdownTimeout, cancellationToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryEnsureStartedAsync(bool show, CancellationToken cancellationToken)
    {
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                using var runningProcess = FindRunningStickyProcess();
                if (runningProcess is not null && !runningProcess.HasExited)
                {
                    if (!show)
                    {
                        return true;
                    }

                    if (await TryActivateAsync(runningProcess, cancellationToken))
                    {
                        return true;
                    }

                    if (!runningProcess.HasExited)
                    {
                        return false;
                    }
                }

                using var launchedProcess = TryLaunchHidden();
                if (launchedProcess is null ||
                    !await WaitForNamedEventAsync(
                        ReadyEventName,
                        launchedProcess,
                        ActivationTimeout,
                        cancellationToken))
                {
                    return false;
                }

                return !show || await TryActivateAsync(launchedProcess, cancellationToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static Process? TryLaunchHidden()
    {
        var stickyExe = Path.Combine(AppContext.BaseDirectory, StickyExecutableName);
        if (!File.Exists(stickyExe))
        {
            return null;
        }

        var mainExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, MainExecutableName);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = stickyExe,
                WorkingDirectory = Path.GetDirectoryName(stickyExe) ?? AppContext.BaseDirectory,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--main-exe");
            startInfo.ArgumentList.Add(mainExe);
            startInfo.ArgumentList.Add(StartHiddenArgument);

            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static Process? FindRunningStickyProcess()
    {
        var stickyExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, StickyExecutableName));
        var processName = Path.GetFileNameWithoutExtension(stickyExe);
        foreach (var candidate in Process.GetProcessesByName(processName))
        {
            try
            {
                if (!candidate.HasExited &&
                    candidate.MainModule?.FileName is string path &&
                    string.Equals(Path.GetFullPath(path), stickyExe, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch
            {
                // Access to another process can disappear while it is being inspected.
            }

            candidate.Dispose();
        }

        return null;
    }

    private static async Task<bool> TryActivateAsync(Process process, CancellationToken cancellationToken)
    {
        using var acknowledged = await TryOpenEventAsync(
            ActivationAcknowledgedEventName,
            process,
            ActivationTimeout,
            cancellationToken);
        if (acknowledged is null)
        {
            return false;
        }

        acknowledged.Reset();
        return await TrySignalEventAsync(ActivationEventName, process, cancellationToken) &&
               await WaitForEventAsync(acknowledged, process, ActivationTimeout, cancellationToken);
    }

    private static async Task<bool> TrySignalEventAsync(
        string eventName,
        Process process,
        CancellationToken cancellationToken)
    {
        using var signal = await TryOpenEventAsync(
            eventName,
            process,
            ActivationTimeout,
            cancellationToken);
        if (signal is null)
        {
            return false;
        }

        signal.Set();
        return true;
    }

    private static async Task<EventWaitHandle?> TryOpenEventAsync(
        string eventName,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < timeout)
        {
            if (process.HasExited)
            {
                return null;
            }

            try
            {
                return EventWaitHandle.OpenExisting(eventName);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                await Task.Delay(50, cancellationToken);
            }
        }

        return null;
    }

    private static async Task<bool> WaitForNamedEventAsync(
        string eventName,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var signal = await TryOpenEventAsync(eventName, process, timeout, cancellationToken);
        return signal is not null &&
               await WaitForEventAsync(signal, process, timeout, cancellationToken);
    }

    private static async Task<bool> WaitForEventAsync(
        EventWaitHandle signal,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < timeout)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (signal.WaitOne(0))
            {
                return true;
            }

            await Task.Delay(50, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < timeout)
        {
            if (process.HasExited)
            {
                return true;
            }

            await Task.Delay(50, cancellationToken);
        }

        return process.HasExited;
    }
}
