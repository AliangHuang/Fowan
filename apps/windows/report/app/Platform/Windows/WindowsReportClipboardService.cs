using Fowan.Report.Shared.Application.Ports;
using Windows.ApplicationModel.DataTransfer;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>Windows clipboard adapter; the editor receives only portable text/table content.</summary>
internal sealed class WindowsReportClipboardService : IReportClipboardService
{
    public async Task<ReportClipboardContent> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var content = Clipboard.GetContent();
            var table = await ReportTableClipboardImporter.ReadTableAsync(content);
            var text = content.Contains(StandardDataFormats.Text)
                ? await content.GetTextAsync().AsTask(cancellationToken)
                : null;
            return new(text, table);
        }
        catch
        {
            // Clipboard ownership can change between the paste request and asynchronous read.
            return new(null, null);
        }
    }

    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
    }
}
