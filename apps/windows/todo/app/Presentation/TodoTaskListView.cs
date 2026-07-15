using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinTextDecorations = Windows.UI.Text.TextDecorations;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoTaskListPalette(
    Brush Transparent,
    Brush Text,
    Brush SecondaryText,
    Brush Muted,
    Brush Accent,
    Brush NormalBorder,
    Brush NormalBackground,
    Brush CompletedBackground,
    Brush SelectedBackground,
    Brush CompletedSelectedBorder,
    Brush CompletedSelectedBackground,
    Brush DueToday,
    Brush Danger,
    Func<bool, Brush> HoverBorder,
    Func<bool, Brush> HoverBackground);

internal sealed record TodoTaskListActions(
    Func<TodoTask, Button> TreeToggleButton,
    Func<TodoTask, Button> CheckButton,
    Func<string, Border> ListPill,
    Func<TodoTask, string> TimeText,
    Func<string, string, Brush, Button> RowIconButton,
    Func<string, string, Button> HeaderActionButton,
    Action<Button, TodoTask> AttachDrag,
    Func<string, bool> ConsumeSuppressedClick,
    Action<TodoTask> Select,
    Func<TodoTask, Task> AddSubtask,
    Func<TodoTask, Task> Delete,
    Action<TodoTask> RestoreTree,
    Func<TodoTask, Task> PermanentlyDeleteTree,
    Action<TodoTask> RestoreCompleted,
    Action<TodoTask> ToggleImportant,
    Action ToggleCompletedExpanded,
    Action RestoreAllCompleted,
    Action ClearAllCompleted);

internal sealed class TodoTaskListView(
    TodoData data,
    bool completedExpanded,
    string? selectedTaskId,
    TodoTaskListPalette palette,
    TodoTaskListActions actions)
{
    public IReadOnlyList<Button> Rows => _rows;
    private readonly List<Button> _rows = [];

    public UIElement Row(TodoTask task, bool completed, int depth = 0, bool recycleBin = false)
    {
        var selected = string.Equals(selectedTaskId, task.Id, StringComparison.Ordinal);
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0), MinHeight = 54, BorderThickness = new Thickness(0),
            Background = palette.Transparent, CornerRadius = new CornerRadius(8), Tag = task.Id,
            Margin = new Thickness(Math.Clamp(depth, 0, TodoQuery.MaxTaskTreeDepth - 1) * 22, 0, 0, 0)
        };
        ConfigureButtonChrome(button);
        _rows.Add(button);
        if (!recycleBin)
        {
            actions.AttachDrag(button, task);
            button.ContextFlyout = ContextMenu(task);
        }
        AutomationProperties.SetName(button, task.Title);
        var shell = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = selected ? (completed ? palette.CompletedSelectedBorder : palette.Accent) : palette.NormalBorder,
            Background = selected
                ? (completed ? palette.CompletedSelectedBackground : palette.SelectedBackground)
                : (completed ? palette.CompletedBackground : palette.NormalBackground),
            Padding = new Thickness(14, 8, 10, 8)
        };
        var pointerOver = false;
        void RefreshVisual()
        {
            var isSelected = string.Equals(selectedTaskId, task.Id, StringComparison.Ordinal);
            shell.BorderBrush = isSelected
                ? (completed ? palette.CompletedSelectedBorder : palette.Accent)
                : pointerOver ? palette.HoverBorder(completed) : palette.NormalBorder;
            shell.Background = isSelected
                ? (completed ? palette.CompletedSelectedBackground : palette.SelectedBackground)
                : pointerOver ? palette.HoverBackground(completed)
                : completed ? palette.CompletedBackground : palette.NormalBackground;
        }
        button.AddHandler(UIElement.PointerEnteredEvent,
            new PointerEventHandler((_, _) => { pointerOver = true; RefreshVisual(); }), true);
        button.AddHandler(UIElement.PointerExitedEvent,
            new PointerEventHandler((_, _) => { pointerOver = false; RefreshVisual(); }), true);
        shell.Child = RowContent(task, completed, recycleBin);
        button.Content = shell;
        button.Click += (_, _) =>
        {
            if (!actions.ConsumeSuppressedClick(task.Id)) actions.Select(task);
        };
        return button;
    }

    public UIElement CompletedSection(IReadOnlyList<TodoTaskNode> completedTasks)
    {
        var section = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        section.Children.Add(new Border
        {
            Height = 1, Background = palette.NormalBorder, Margin = new Thickness(0, 0, 0, 4)
        });
        var header = new Grid { Height = 38, ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = actions.RowIconButton(
            completedExpanded ? "\uE70D" : "\uE76C",
            completedExpanded ? "收起已完成" : "展开已完成",
            palette.SecondaryText);
        chevron.Click += (_, _) => actions.ToggleCompletedExpanded();
        header.Children.Add(chevron);
        var label = new TextBlock
        {
            Text = $"已完成 · {completedTasks.Count}", FontSize = 16,
            FontWeight = FontWeights.SemiBold, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        header.Children.Add(label);
        var restore = actions.HeaderActionButton("\uE777", "恢复");
        restore.IsEnabled = completedTasks.Count > 0;
        restore.Click += (_, _) => actions.RestoreAllCompleted();
        Grid.SetColumn(restore, 2);
        header.Children.Add(restore);
        var clear = actions.HeaderActionButton("\uE74D", "清除");
        clear.IsEnabled = completedTasks.Count > 0;
        clear.Click += (_, _) => actions.ClearAllCompleted();
        Grid.SetColumn(clear, 3);
        header.Children.Add(clear);
        section.Children.Add(header);
        if (completedExpanded)
        {
            foreach (var node in completedTasks) section.Children.Add(Row(node.Task, true, node.Depth));
        }
        return section;
    }

    private Grid RowContent(TodoTask task, bool completed, bool recycleBin)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var leading = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (recycleBin)
        {
            leading.Children.Add(new Border { Width = 24, Height = 24 });
            leading.Children.Add(new Border { Width = 24, Height = 24 });
        }
        else if (TodoQuery.DirectChildCount(data, task.Id) > 0) leading.Children.Add(actions.TreeToggleButton(task));
        else leading.Children.Add(new Border { Width = 24, Height = 24 });
        leading.Children.Add(actions.CheckButton(task));
        grid.Children.Add(leading);
        var title = new TextBlock
        {
            Text = task.Title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = completed ? palette.Muted : palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = completed ? WinTextDecorations.Strikethrough : WinTextDecorations.None,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var list = actions.ListPill(task.ListId);
        Grid.SetColumn(list, 2);
        grid.Children.Add(list);
        var time = new TextBlock
        {
            Text = actions.TimeText(task), FontSize = 13,
            Foreground = task.DueDate?.Date == DateTime.Today && !completed ? palette.DueToday : palette.SecondaryText,
            TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(time, 3);
        grid.Children.Add(time);
        AddTrailingActions(grid, task, completed, recycleBin);
        return grid;
    }

    private void AddTrailingActions(Grid grid, TodoTask task, bool completed, bool recycleBin)
    {
        if (recycleBin || completed)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var restore = actions.RowIconButton(
                "\uE777", recycleBin ? "恢复任务树" : "恢复任务", palette.SecondaryText);
            restore.Click += (_, _) =>
            {
                if (recycleBin) actions.RestoreTree(task); else actions.RestoreCompleted(task);
            };
            buttons.Children.Add(restore);
            var delete = actions.RowIconButton(
                "\uE74D", recycleBin ? "永久删除任务树" : "删除任务",
                recycleBin ? palette.Danger : palette.SecondaryText);
            delete.Click += async (_, _) =>
            {
                if (recycleBin) await actions.PermanentlyDeleteTree(task); else await actions.Delete(task);
            };
            buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 4);
            grid.Children.Add(buttons);
            return;
        }
        var star = actions.RowIconButton(
            task.IsImportant ? "\uE735" : "\uE734",
            task.IsImportant ? "取消重要" : "标为重要",
            task.IsImportant ? palette.Accent : palette.SecondaryText);
        star.Click += (_, _) => actions.ToggleImportant(task);
        Grid.SetColumn(star, 4);
        grid.Children.Add(star);
    }

    private MenuFlyout ContextMenu(TodoTask task)
    {
        var menu = new MenuFlyout();
        var createChild = new MenuFlyoutItem
        {
            Text = "创建子任务", IsEnabled = TodoQuery.CanAddChild(data, task)
        };
        if (!createChild.IsEnabled) ToolTipService.SetToolTip(createChild, TodoQuery.AddChildBlockedReason(data, task));
        createChild.Click += async (_, _) => await actions.AddSubtask(task);
        menu.Items.Add(createChild);
        var delete = new MenuFlyoutItem { Text = "删除任务" };
        delete.Click += async (_, _) => await actions.Delete(task);
        menu.Items.Add(delete);
        return menu;
    }

    private void ConfigureButtonChrome(Button button)
    {
        button.Resources["ButtonBackground"] = palette.Transparent;
        button.Resources["ButtonBackgroundPointerOver"] = palette.Transparent;
        button.Resources["ButtonBackgroundPressed"] = palette.Transparent;
        button.Resources["ButtonBorderBrush"] = palette.Transparent;
        button.Resources["ButtonBorderBrushPointerOver"] = palette.Transparent;
        button.Resources["ButtonBorderBrushPressed"] = palette.Transparent;
        button.Resources["ButtonBorderThickness"] = new Thickness(0);
        button.Resources["ButtonBorderThicknessPointerOver"] = new Thickness(0);
        button.Resources["ButtonBorderThicknessPressed"] = new Thickness(0);
        button.Resources["ButtonCornerRadius"] = new CornerRadius(8);
    }
}
