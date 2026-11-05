using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoPresentationState
{
    public string CurrentViewId { get; set; } = TodoViewIds.Today;
    public string? SelectedTaskId { get; set; }

    public void Restore(TodoSettingsSnapshot settings, Func<string, bool> isKnownView)
    {
        CurrentViewId = isKnownView(settings.CurrentViewId) ? settings.CurrentViewId : TodoViewIds.Today;
        if (CurrentViewId == TodoViewIds.RecycleBin && !settings.IsRecycleBinEnabled)
            CurrentViewId = TodoViewIds.Today;
        SelectedTaskId = settings.SelectedTaskId;
    }
}
