using System.Diagnostics;
using Fowan.Windows.Platform.Contracts;

namespace Fowan.Windows.Platform.Windows;

internal sealed class WindowsProcessLauncher : IProcessLauncher
{
    public PlatformOperationResult Launch(ProcessLaunchRequest request)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = request.Target,
                WorkingDirectory = request.WorkingDirectory
                    ?? Path.GetDirectoryName(request.Target)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = request.UseShellExecute,
                Verb = request.Elevated ? "runas" : string.Empty
            });
            return process is null
                ? PlatformOperationResult.Failure("The operating system did not start the process.")
                : PlatformOperationResult.Success();
        }
        catch (Exception exception)
        {
            return PlatformOperationResult.Failure(exception.Message);
        }
    }
}
