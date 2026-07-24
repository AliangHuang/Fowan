using Fowan.Windows.Platform.Contracts;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Fowan.Diary.Windows.Platform.Windows;

internal sealed class WindowsFileDialogService(nint ownerWindow) : IFileDialogService
{
    public async Task<string?> PickOpenFileAsync(
        FileOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker();
        foreach (var extension in request.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindow);
        var file = await picker.PickSingleFileAsync().AsTask(cancellationToken);
        return file?.Path;
    }

    public async Task<PlatformOperationResult> SaveTextFileAsync(
        TextFileSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = request.SuggestedFileName
        };
        picker.FileTypeChoices.Add(request.DisplayName, [request.Extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindow);
        var file = await picker.PickSaveFileAsync().AsTask(cancellationToken);
        if (file is null)
        {
            return PlatformOperationResult.Failure("The file dialog was cancelled.");
        }

        await FileIO.WriteTextAsync(file, request.Content).AsTask(cancellationToken);
        return PlatformOperationResult.Success();
    }
}
