namespace Fowan.ToolCenter.Windows.Application.Ports;

internal interface IManagedToolProcesses
{
    bool IsRunning(string ownedDirectory);
    bool AnyYymRunning();
    void Launch(string executable);
    void OpenDirectory(string directory);
    void OpenPage(Uri uri);
}
