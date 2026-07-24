using Fowan.Ai.Shared.Application.Ports;
using System.Diagnostics;

namespace Fowan.Report.Windows.Platform.Windows;

internal sealed class WindowsAiCoreProcessLauncher : IAiCoreProcessLauncher
{
    public void Start(string executablePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
