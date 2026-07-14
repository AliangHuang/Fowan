namespace Fowan.Todo.Sticky.Windows.Platform;

internal interface IProcessLauncher
{
    bool TryLaunch(string executablePath);
}
