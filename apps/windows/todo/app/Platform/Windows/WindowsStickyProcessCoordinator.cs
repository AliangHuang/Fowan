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
    private const string ShutdownEventName = @"Local\Fowan.Todo.Sticky.Windows.Shutdown";
    private const string StartHiddenArgument = "--start-hidden";

    public bool TryPrewarm() => TryEnsureStarted(show: false);

    public bool TryShow() => TryEnsureStarted(show: true);

    public bool TryShutdown() => TrySignalEvent(ShutdownEventName, attempts: 4);

    private static bool TryEnsureStarted(bool show)
    {
        using var process = FindRunningStickyProcess();
        if (process is not null && !process.HasExited)
        {
            return !show || TrySignalEvent(ActivationEventName);
        }

        return TryLaunch(show);
    }

    private static bool TryLaunch(bool show)
    {
        var stickyExe = Path.Combine(AppContext.BaseDirectory, StickyExecutableName);
        if (!File.Exists(stickyExe))
        {
            return false;
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
            if (!show)
            {
                startInfo.ArgumentList.Add(StartHiddenArgument);
            }

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
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

    private static bool TrySignalEvent(string eventName, int attempts = 20)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(eventName);
                activationEvent.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }
}
