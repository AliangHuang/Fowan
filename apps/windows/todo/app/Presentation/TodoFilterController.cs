using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoFilterController(TodoDialogService dialogs, TodoThemePalette palette)
{
    public TodoDateRangeFilter? DateRange { get; private set; }
    public string? ListId { get; private set; }
    public int MaximumDepth { get; private set; } = TodoQuery.MaxTaskTreeDepth;

    public bool IsActive => DateRange is { IsValid: true } ||
        !string.IsNullOrWhiteSpace(ListId) || MaximumDepth < TodoQuery.MaxTaskTreeDepth;

    public async Task ShowAsync(
        IEnumerable<TodoList> lists,
        Func<string> currentView,
        Action<string> setView,
        Action<string?> selectTask,
        Func<string, TodoTask?> firstTask,
        Action saveSettings,
        Action refresh)
    {
        var selection = await dialogs.ShowFilterAsync(lists, ListId, MaximumDepth, DateRange);
        if (selection is null) return;
        MaximumDepth = selection.MaximumDepth;
        DateRange = selection.DateRange;
        ListId = selection.ListId;
        if (selection.Clear)
        {
            refresh();
            return;
        }
        if (DateRange is not null || ListId is not null) setView(TodoViewIds.All);
        selectTask(firstTask(currentView())?.Id);
        saveSettings();
        refresh();
    }

    public void Clear()
    {
        DateRange = null;
        ListId = null;
    }

    public void StyleButton(Button button)
    {
        if (button.Content is StackPanel stack)
        {
            var label = stack.Children.OfType<TextBlock>().FirstOrDefault();
            if (label is not null)
            {
                label.Text = IsActive ? "筛选中" : "筛选";
                label.Foreground = IsActive ? palette.FilterActiveText : palette.Text;
            }
        }
        var border = IsActive ? palette.FilterActiveBorder : palette.Brush(0xDCE7EA);
        var background = IsActive ? palette.FilterActiveBackground : palette.Brush(0xFFFFFF);
        button.Resources["ButtonBackground"] = background;
        button.Resources["ButtonBackgroundPointerOver"] = palette.FilterHoverBackground(IsActive);
        button.Resources["ButtonBackgroundPressed"] = palette.FilterPressedBackground(IsActive);
        button.Resources["ButtonBorderBrush"] = border;
        button.Resources["ButtonBorderBrushPointerOver"] = palette.FilterHoverBorder(IsActive);
        button.Resources["ButtonBorderBrushPressed"] = palette.FilterPressedBorder(IsActive);
        ToolTipService.SetToolTip(button, IsActive
            ? "已应用筛选，点击可调整或清空"
            : "筛选任务");
    }
}
