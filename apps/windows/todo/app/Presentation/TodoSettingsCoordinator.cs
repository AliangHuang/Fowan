using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoSettingsCoordinator(
    TodoDialogService dialogs,
    TodoOnboardingCoordinator onboarding,
    TodoThemePalette palette,
    Func<TodoSettingsSnapshot> settings,
    Func<string> currentView,
    Action<string> setView,
    Action<string?> selectTask,
    Func<string, TodoTask?> firstTask,
    Action<string> setTheme,
    Action<bool, string, int> updateRecycleBinSettings,
    Action<bool> setOnboardingCompleted,
    Action purgeRecycleBin,
    Action rebuildShell,
    Action refresh,
    Action openSticky,
    Action queueOnboarding)
{
    public async Task ShowAsync()
    {
        var currentSettings = settings();
        var selection = await dialogs.ShowSettingsAsync(
            currentSettings,
            palette.Text,
            palette.SecondaryText,
            palette.Brush(0xB42318));
        if (selection.Action == TodoSettingsDialogAction.RestartOnboarding)
        {
            onboarding.Dismiss();
            setOnboardingCompleted(false);
            queueOnboarding();
            return;
        }
        if (selection.Action == TodoSettingsDialogAction.Cancel) return;
        setTheme(selection.Theme);
        updateRecycleBinSettings(selection.RecycleBinEnabled, selection.RetentionPreset, selection.CustomRetentionDays);
        purgeRecycleBin();
        if (currentView() == TodoViewIds.RecycleBin && !currentSettings.IsRecycleBinEnabled)
        {
            setView(TodoViewIds.Today);
            selectTask(firstTask(TodoViewIds.Today)?.Id);
        }
        rebuildShell();
        refresh();
        if (selection.Action == TodoSettingsDialogAction.OpenSticky) openSticky();
    }
}
