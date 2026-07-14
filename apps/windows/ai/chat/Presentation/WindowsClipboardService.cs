using Fowan.Ai.Shared.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
