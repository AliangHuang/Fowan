using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Sticky.Windows.Application;

internal sealed class StickyWindowCommands(TodoWorkspace workspace)
{
    public void PersistPreferences() => workspace.SaveSettings();

    public void PersistTasks() => workspace.SaveData();

    public void Reload() => workspace.Reload();

    public void SetStickyModeEnabled(bool enabled) => workspace.SetStickyModeEnabled(enabled);

    public int PurgeExpiredRecycleBin() => workspace.PurgeExpiredRecycleBin();

    public int CreateDueRecurringTasks() => workspace.CreateDueRecurringTasks();

    public double SetOpacity(double opacity)
    {
        workspace.Settings.StickyOpacity = Math.Clamp(
            opacity, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity);
        workspace.SaveSettings();
        return workspace.Settings.StickyOpacity;
    }

    public bool SetTheme(string theme)
    {
        if (theme is not (TodoThemeIds.System or TodoThemeIds.Light or TodoThemeIds.Dark) ||
            string.Equals(workspace.Settings.Theme, theme, StringComparison.Ordinal)) return false;
        workspace.Settings.Theme = theme;
        workspace.SaveSettings();
        return true;
    }

    public double SetScale(double sliderValue)
    {
        workspace.Settings.StickyScale = Math.Clamp(
            Math.Round(sliderValue / 100, 2), TodoSettings.MinStickyScale, TodoSettings.MaxStickyScale);
        workspace.SaveSettings();
        return workspace.Settings.StickyScale;
    }

    public bool SetStickyTitleFontSize(double fontSize)
    {
        if (double.IsNaN(fontSize) || double.IsInfinity(fontSize)) return false;
        var normalized = Math.Clamp(
            Math.Round(fontSize),
            TodoSettings.MinStickyTitleFontSize,
            TodoSettings.MaxStickyTitleFontSize);
        if (Math.Abs(workspace.Settings.StickyTitleFontSize - normalized) < 0.01) return false;
        workspace.Settings.StickyTitleFontSize = normalized;
        workspace.SaveSettings();
        return true;
    }

    public bool ToggleTopmost()
    {
        workspace.Settings.IsStickyTopmost = !workspace.Settings.IsStickyTopmost;
        workspace.SaveSettings();
        return workspace.Settings.IsStickyTopmost;
    }

    public bool ToggleCompletedExpanded()
    {
        workspace.Settings.IsStickyCompletedExpanded = !workspace.Settings.IsStickyCompletedExpanded;
        workspace.SaveSettings();
        return workspace.Settings.IsStickyCompletedExpanded;
    }

    public bool SetStickyTitleHidden(bool hidden)
    {
        if (workspace.Settings.IsStickyTitleHidden == hidden) return false;
        workspace.Settings.IsStickyTitleHidden = hidden;
        workspace.SaveSettings();
        return true;
    }

    public bool SetStickyAddTaskMinimized(bool minimized)
    {
        if (workspace.Settings.IsStickyAddTaskMinimized == minimized) return false;
        workspace.Settings.IsStickyAddTaskMinimized = minimized;
        workspace.SaveSettings();
        return true;
    }

    public bool SetStickyMenuAutoHideEnabled(bool enabled)
    {
        if (workspace.Settings.IsStickyMenuAutoHideEnabled == enabled) return false;
        workspace.Settings.IsStickyMenuAutoHideEnabled = enabled;
        workspace.SaveSettings();
        return true;
    }

    public bool ToggleCollapsed(string taskId)
    {
        var collapsed = workspace.Settings.CollapsedTaskIds;
        var wasCollapsed = collapsed.Contains(taskId, StringComparer.Ordinal);
        if (wasCollapsed) collapsed.RemoveAll(id => string.Equals(id, taskId, StringComparison.Ordinal));
        else collapsed.Add(taskId);
        workspace.SaveSettings();
        return !wasCollapsed;
    }
}
