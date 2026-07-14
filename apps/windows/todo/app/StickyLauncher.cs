using System.Diagnostics;

namespace Fowan.Todo.Windows;

internal static class StickyLauncher
{
    private const string ActivationEventName = @"Local\Fowan.Todo.Sticky.Windows.Activate";
    private const string ShutdownEventName = @"Local\Fowan.Todo.Sticky.Windows.Shutdown";
    private const string StartHiddenArgument = "--start-hidden";

    public static bool TryPrewarm(out Process? process)
    {
        return TryEnsureStarted(show: false, out process);
    }

    public static bool TryShow(out Process? process)
    {
        return TryEnsureStarted(show: true, out process);
    }

    public static bool TryShutdown()
    {
        return TrySignalEvent(ShutdownEventName, attempts: 4);
    }

    private static bool TryEnsureStarted(bool show, out Process? process)
    {
        process = FindRunningStickyProcess();
        if (process is not null && !process.HasExited)
        {
            if (show)
            {
                TrySignalEvent(ActivationEventName);
            }

            return true;
        }

        return TryLaunch(show, out process);
    }

    private static bool TryLaunch(bool show, out Process? process)
    {
        process = null;
        var stickyExe = Path.Combine(AppContext.BaseDirectory, "Fowan.Todo.Sticky.Windows.exe");
        if (!File.Exists(stickyExe))
        {
            return false;
        }

        var mainExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "Fowan.Todo.Windows.exe");
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

            process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            process = null;
            return false;
        }
    }

    private static Process? FindRunningStickyProcess()
    {
        var stickyExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fowan.Todo.Sticky.Windows.exe"));
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
                candidate.Dispose();
            }
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
