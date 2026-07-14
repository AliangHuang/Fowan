namespace Fowan.Windows.Services;

internal interface IProcessLauncher
{
    bool TryLaunch(string path, out string? error, bool elevated = false);

    void TryOpenUrl(string url);
}
