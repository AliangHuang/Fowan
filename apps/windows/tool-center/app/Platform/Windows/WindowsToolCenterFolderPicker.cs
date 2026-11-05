using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal sealed class WindowsToolCenterFolderPicker(Window window)
{
    internal async Task<string?> PickAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
