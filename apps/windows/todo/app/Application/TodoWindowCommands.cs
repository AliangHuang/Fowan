using Fowan.Todo.Shared.Application;

namespace Fowan.Todo.Windows.Application;

internal sealed class TodoWindowCommands(TodoWorkspace workspace)
{
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

    public void Reload() => workspace.Reload();
}
