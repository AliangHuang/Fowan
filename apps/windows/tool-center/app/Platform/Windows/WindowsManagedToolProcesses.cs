using System.Diagnostics;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Application.Ports;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal sealed class WindowsManagedToolProcesses : IManagedToolProcesses
{
    public bool IsRunning(string ownedDirectory)
    {
        var root = ManagedToolPaths.Canonical(ownedDirectory) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcessesByName("yym"))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    var executable = process.MainModule?.FileName;
                    if (executable is null) throw new ToolManagementException("Center_ProcessInspectionFailed");
                    if (Path.GetFullPath(executable).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    throw new ToolManagementException("Center_ProcessInspectionFailed", exception);
                }
            }
        }
        return false;
    }

    public bool AnyYymRunning()
    {
        var processes = Process.GetProcessesByName("yym");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Launch(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable)
            { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable) });
        if (process is null) throw new ToolManagementException("Center_LaunchFailed");
    }

    public void OpenDirectory(string directory)
    {
        ManagedToolPaths.RejectLinksOnPath(directory);
        if (!Directory.Exists(directory)) throw new ToolManagementException("Center_DirectoryMissing");
        using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        if (process is null) throw new ToolManagementException("Center_DirectoryMissing");
    }

    public void OpenPage(Uri uri)
    {
        if (uri != YymPackageIdentity.SourceUri &&
            (!uri.AbsoluteUri.StartsWith(YymPackageIdentity.SourceUri.AbsoluteUri + "/releases/tag/", StringComparison.Ordinal) ||
             uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length != 0))
            throw new ToolManagementException("Center_InvalidRelease");
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        if (process is null) throw new ToolManagementException("Center_OperationFailed");
    }
}
