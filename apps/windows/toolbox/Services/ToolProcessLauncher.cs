using System.Diagnostics;

namespace Fowan.Windows.Services;

internal sealed class ToolProcessLauncher : IProcessLauncher
{
    public bool TryLaunch(string path, out string? error, bool elevated = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = elevated ? "runas" : string.Empty
            });
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Opening a URL is a convenience action and must not terminate the client.
        }
    }
}
