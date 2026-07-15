using Fowan.Todo.Shared.Models;
using Fowan.Todo.Sticky.Windows.Application;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyAppearanceController(
    Window window,
    StickyWindowCommands commands,
    Func<TodoSettings> settings,
    StickyThemePalette palette,
    ScaleTransform scaleTransform,
    Func<Border> shell,
    Func<Border> addRow,
    Func<Border> taskDivider,
    Func<TextBlock> titleText,
    Action<bool> updateMinimumWindowSize,
    Func<(double Width, double Height)> currentMonitorSizeDip,
    Action<bool> enforceGeometry,
    Action saveGeometry,
    Action refreshTasks,
    Action synchronizeChildWindows,
    Action buildUi,
    Action refreshAll,
    Action updateDismissOverlay)
{
    private bool _isApplyingScale;

    public void ApplyStored()
    {
        ApplyOpacity(shouldRefreshTasks: false);
        var scale = settings().IsStickyFloatingModeEnabled ? 1.0 : settings().StickyScale;
        scaleTransform.ScaleX = scale;
        scaleTransform.ScaleY = scale;
    }

    public void SetOpacity(double opacity)
    {
        commands.SetOpacity(opacity);
        ApplyOpacity(shouldRefreshTasks: true);
        synchronizeChildWindows();
    }

    public void SetTheme(string theme)
    {
        if (theme is not (TodoThemeIds.System or TodoThemeIds.Light or TodoThemeIds.Dark)) return;
        if (!commands.SetTheme(theme))
        {
            synchronizeChildWindows();
            return;
        }
        buildUi();
        ApplyStored();
        refreshAll();
        synchronizeChildWindows();
        updateDismissOverlay();
    }

    public void ApplyScale(double sliderValue)
    {
        if (_isApplyingScale) return;
        var current = settings();
        var next = Math.Clamp(Math.Round(sliderValue / 100, 2), TodoSettings.MinStickyScale, TodoSettings.MaxStickyScale);
        if (Math.Abs(next - current.StickyScale) < 0.001) return;
        _isApplyingScale = true;
        try
        {
            var old = current.StickyScale;
            commands.SetScale(sliderValue);
            scaleTransform.ScaleX = next;
            scaleTransform.ScaleY = next;
            updateMinimumWindowSize(false);
            var max = currentMonitorSizeDip();
            ResizeAroundCenter(
                Math.Clamp(window.Width * next / old, window.MinWidth, max.Width),
                Math.Clamp(window.Height * next / old, window.MinHeight, max.Height));
            enforceGeometry(false);
            synchronizeChildWindows();
            saveGeometry();
        }
        finally { _isApplyingScale = false; }
    }

    private void ApplyOpacity(bool shouldRefreshTasks)
    {
        shell().Background = palette.Surface;
        addRow().Background = palette.Panel(0xF5FAFB);
        taskDivider().Background = palette.Brush(0xE7EEF0, Math.Clamp(settings().StickyOpacity + 0.12, 0.0, 1.0));
        if (shouldRefreshTasks && titleText() is not null) refreshTasks();
    }

    private void ResizeAroundCenter(double width, double height)
    {
        var centerX = window.Left + window.Width / 2;
        var centerY = window.Top + window.Height / 2;
        window.Width = width;
        window.Height = height;
        window.Left = centerX - width / 2;
        window.Top = centerY - height / 2;
    }
}
