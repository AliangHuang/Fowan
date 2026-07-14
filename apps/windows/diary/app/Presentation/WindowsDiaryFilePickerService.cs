using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class WindowsDiaryFilePickerService : IDiaryFilePickerService
{
    public async Task<string?> PickOpenPathAsync(
        Window owner,
        IReadOnlyCollection<string> extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        Initialize(picker, owner);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<bool> ExportMarkdownAsync(
        Window owner,
        string suggestedFileName,
        string content)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName
        };
        picker.FileTypeChoices.Add("Markdown 文档", [".md"]);
        Initialize(picker, owner);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        await FileIO.WriteTextAsync(file, content);
        return true;
    }

    private static void Initialize(object picker, Window owner)
    {
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(owner));
    }
}
