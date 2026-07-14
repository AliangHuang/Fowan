using Microsoft.UI.Xaml;

namespace Fowan.Diary.Windows.Presentation;

internal interface IDiaryFilePickerService
{
    Task<string?> PickOpenPathAsync(Window owner, IReadOnlyCollection<string> extensions);

    Task<bool> ExportMarkdownAsync(Window owner, string suggestedFileName, string content);
}
