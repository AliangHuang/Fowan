using Fowan.Windows.Platform.Contracts;
using Windows.ApplicationModel.DataTransfer;

namespace Fowan.Ai.Chat.Windows.Platform.Windows;

internal sealed class WindowsClipboardService : IClipboardService
{
    public PlatformOperationResult SetText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            return PlatformOperationResult.Success();
        }
        catch (Exception exception)
        {
            return PlatformOperationResult.Failure(exception.Message);
        }
    }
}
