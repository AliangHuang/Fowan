using Fowan.Windows.Platform.Contracts;

namespace Fowan.Windows.AppPorts;

internal enum TrayMessageDisposition
{
    Forward,
    Handled
}

internal sealed class TrayMessageRouter(
    IUiDispatcher dispatcher,
    Func<bool> shouldMinimizeOnClose,
    Action minimize,
    Action restore,
    Action showContextMenu)
{
    internal const uint CloseMessage = 0x0010;
    internal const uint TrayIconMessage = 0x8000 + 0x46;
    internal const int LeftButtonUp = 0x0202;
    internal const int LeftButtonDoubleClick = 0x0203;
    internal const int RightButtonUp = 0x0205;

    public TrayMessageDisposition Route(uint message, nint eventData)
    {
        if (message == CloseMessage && shouldMinimizeOnClose())
        {
            return Dispatch(minimize);
        }

        if (message != TrayIconMessage)
        {
            return TrayMessageDisposition.Forward;
        }

        return eventData.ToInt32() switch
        {
            LeftButtonUp or LeftButtonDoubleClick => Dispatch(restore),
            RightButtonUp => Dispatch(showContextMenu),
            _ => TrayMessageDisposition.Forward
        };
    }

    private TrayMessageDisposition Dispatch(Action action) =>
        dispatcher.TryEnqueue(action) ? TrayMessageDisposition.Handled : TrayMessageDisposition.Forward;
}
