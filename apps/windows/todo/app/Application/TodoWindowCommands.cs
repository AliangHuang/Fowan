using Fowan.Todo.Shared.Application;

namespace Fowan.Todo.Windows.Application;

internal sealed class TodoWindowCommands(TodoWorkspace workspace)
{
    public bool CanUndo => workspace.CanUndo;

    public bool CanRedo => workspace.CanRedo;

    public bool Undo() => workspace.Undo();

    public bool Redo() => workspace.Redo();

    public void PersistNavigation(string currentViewId, string? selectedTaskId)
        => workspace.SetPresentationPreferences(currentViewId, selectedTaskId);

    public void PersistPreferences() => workspace.SaveSettings();

    public void PersistTasks() => workspace.SaveData();

    public void SetTheme(string theme) => workspace.SetTheme(theme);

    public void SetStickyModeEnabled(bool enabled) => workspace.SetStickyModeEnabled(enabled);

    public void SetOnboardingCompleted(bool completed) => workspace.SetOnboardingCompleted(completed);

    public void UpdateRecycleBinSettings(bool enabled, string preset, int customDays) =>
        workspace.UpdateRecycleBinSettings(enabled, preset, customDays);

    public int PurgeExpiredRecycleBin() => workspace.PurgeExpiredRecycleBin();

    public int CreateDueRecurringTasks() => workspace.CreateDueRecurringTasks();

    public void Reload() => workspace.Reload();
}
