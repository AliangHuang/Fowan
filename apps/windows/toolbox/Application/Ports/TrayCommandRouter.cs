namespace Fowan.Windows.AppPorts;

internal enum TrayCommand
{
    None,
    Restore,
    Exit
}

internal sealed class TrayCommandRouter
{
    internal const uint RestoreCommandId = 1001;
    internal const uint ExitCommandId = 1002;

    public TrayCommand Route(uint command) => command switch
    {
        RestoreCommandId => TrayCommand.Restore,
        ExitCommandId => TrayCommand.Exit,
        _ => TrayCommand.None
    };
}
