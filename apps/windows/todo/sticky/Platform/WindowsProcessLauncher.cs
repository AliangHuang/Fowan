using System.Diagnostics;
using System.IO;

namespace Fowan.Todo.Sticky.Windows.Platform;

internal sealed class WindowsProcessLauncher : IProcessLauncher
{
    public bool TryLaunch(string executablePath)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false
            }) is not null;
        }
        catch
        {
            return false;
        }
    }
}
