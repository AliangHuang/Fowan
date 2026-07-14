using Microsoft.UI.Xaml;

namespace Fowan.Windows.Services;

internal interface IFilePickerService
{
    Task<string?> PickImageAsync(Window owner, IReadOnlyCollection<string> extensions);
}
