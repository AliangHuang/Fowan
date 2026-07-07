using System.Diagnostics;

namespace Fowan.Todo.Windows;

internal static class StickyLauncher
{
    public static bool TryLaunch(out Process? process)
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
            process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            process = null;
            return false;
        }
    }
}
