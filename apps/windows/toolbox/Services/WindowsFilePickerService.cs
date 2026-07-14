using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace Fowan.Windows.Services;

internal sealed class WindowsFilePickerService : IFilePickerService
{
    public async Task<string?> PickImageAsync(Window owner, IReadOnlyCollection<string> extensions)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };

        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        return string.IsNullOrWhiteSpace(file?.Path) ? null : file.Path;
    }
}
