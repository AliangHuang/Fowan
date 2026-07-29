using System.Diagnostics;
using Fowan.Pomodoro.Windows.Application.Ports;

namespace Fowan.Pomodoro.Windows.Platform.Windows;

internal sealed class WindowsToolboxResidentConfigurator : IToolboxResidentConfigurator
{
    public bool Configure()
    {
        var executable = ResolveToolbox();
        if (executable is null) return false;
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--configure-reminder-residency",
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = true
            }) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ResolveToolbox()
    {
#if FOWAN_DEVELOPMENT_RUNTIME
        const string executableName = "Fowan.Windows.Dev.exe";
#else
        const string executableName = "Fowan.Windows.exe";
#endif
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, executableName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", executableName)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
}
