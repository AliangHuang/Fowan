using Fowan.Todo.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using MuxFontWeights = Microsoft.UI.Text.FontWeights;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoTaskDetailActions(
    Action<string> UpdateTitle,
    Action ToggleImportant,
    Action Close,
    Action<DateTimeOffset> UpdateCompletion,
    Action<string> MoveToList,
    Action<DateTime> UpdateStartDate,
    Action<DateTime?> UpdateDueDate,
    Action<string> SaveNotes,
    Func<Task> AddSubtask,
    Func<Task> ToggleCompleted,
    Func<Task> Delete);

internal sealed record TodoTaskDetailPalette(
    Brush Transparent,
    Brush Text,
    Brush SecondaryText,
    Brush Important,
    Brush Success,
    Brush Divider,
    Brush SoftDivider,
    Brush Background);

internal sealed record TodoTaskDetailControls(
    Action<TextBox> StyleTextBox,
    Func<string, string, Brush, Button> RowIconButton,
    Func<string, string, Button> PillButton,
    Func<string, string, Button> PrimaryButton,
    Func<string, string, Button> IconOnlyButton,
    Func<string, string, Button> DangerButton);

internal sealed class TodoTaskDetailView(
    TodoTaskDetailPalette palette,
    TodoTaskDetailControls controls)
{
    public UIElement Empty() => new Grid
    {
        Padding = new Thickness(24),
        Children =
        {
            new TextBlock
            {
                Text = "选择一个任务查看详情", FontSize = 16,
                Foreground = palette.SecondaryText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        }
    };

    public UIElement Build(
        TodoTask task,
        IReadOnlyList<TodoList> lists,
        string? childBlockedReason,
        TodoTaskDetailActions actions)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new StackPanel { Padding = new Thickness(28, 78, 28, 24), Spacing = 18 };
        panel.Children.Add(Title(task, actions));
        panel.Children.Add(Status(task));
        if (task.IsCompleted) AddCompletionEditor(panel, task, actions);
        panel.Children.Add(new Border
        {
            Height = 1, Background = palette.Divider, Margin = new Thickness(0, 22, 0, 4)
        });
        AddSchedulingFields(panel, task, lists, actions);
        panel.Children.Add(new Border
        {
            Height = 1, Background = palette.SoftDivider, Margin = new Thickness(0, 2, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "备注", FontSize = 14, FontWeight = MuxFontWeights.SemiBold,
            Foreground = palette.Text
        });
        var notes = new TextBox
        {
            Text = task.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 120, PlaceholderText = "添加备注"
        };
        notes.LostFocus += (_, _) => actions.SaveNotes(notes.Text.Trim());
        panel.Children.Add(notes);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
        root.Children.Add(scroll);
        var actionPanel = Actions(task, childBlockedReason, actions);
        Grid.SetRow(actionPanel, 1);
        root.Children.Add(actionPanel);
        return root;
    }

    private UIElement Title(TodoTask task, TodoTaskDetailActions actions)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBox
        {
            Text = task.Title, BorderThickness = new Thickness(0), Background = palette.Transparent,
            Padding = new Thickness(0), FontSize = 25, FontWeight = MuxFontWeights.SemiBold,
            MinHeight = 42, TextWrapping = TextWrapping.Wrap
        };
        controls.StyleTextBox(title);
        title.LostFocus += (_, _) => actions.UpdateTitle(title.Text);
        title.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Enter) return;
            actions.UpdateTitle(title.Text);
            args.Handled = true;
        };
        grid.Children.Add(title);
        var important = controls.RowIconButton(
            task.IsImportant ? "\uE735" : "\uE734",
            task.IsImportant ? "取消重要" : "标为重要",
            task.IsImportant ? palette.Important : palette.SecondaryText);
        important.Click += (_, _) => actions.ToggleImportant();
        Grid.SetColumn(important, 1);
        grid.Children.Add(important);
        var more = controls.RowIconButton("\uE712", "更多", palette.SecondaryText);
        Grid.SetColumn(more, 2);
        grid.Children.Add(more);
        var close = controls.RowIconButton("\uE711", "关闭详情", palette.SecondaryText);
        close.Click += (_, _) => actions.Close();
        Grid.SetColumn(close, 3);
        grid.Children.Add(close);
        return grid;
    }

    private UIElement Status(TodoTask task)
    {
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        var foreground = task.IsCompleted ? palette.Success : palette.SecondaryText;
        status.Children.Add(new FontIcon
        {
            Glyph = task.IsCompleted ? "\uE73E" : "\uE73A", FontSize = 16,
            Foreground = foreground, VerticalAlignment = VerticalAlignment.Center
        });
        status.Children.Add(new TextBlock
        {
            Text = task.IsCompleted ? "已完成" : "未完成", FontSize = 14,
            FontWeight = MuxFontWeights.SemiBold, Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (task.CompletedAt is { } completedAt)
        {
            var local = completedAt.ToLocalTime();
            status.Children.Add(new TextBlock
            {
                Text = local.Date == DateTimeOffset.Now.Date ? $"今天 {local:HH:mm} 完成" : $"{local:MM-dd HH:mm} 完成",
                FontSize = 13, Foreground = palette.SecondaryText,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0)
            });
        }
        return status;
    }

    private void AddCompletionEditor(StackPanel panel, TodoTask task, TodoTaskDetailActions actions)
    {
        const double editorWidth = 216;
        var original = task.CompletedAt ?? DateTimeOffset.Now;
        var local = original.ToLocalTime();
        var date = new CalendarDatePicker
        {
            Date = new DateTimeOffset(local.Date), PlaceholderText = "选择完成日期",
            Width = editorWidth, HorizontalAlignment = HorizontalAlignment.Left
        };
        var hour = CompletionPartBox(24, local.Hour, "完成小时");
        var minute = CompletionPartBox(60, local.Minute, "完成分钟");
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Visibility = Visibility.Collapsed
        };
        var discard = controls.PillButton("撤销修改", "\uE711");
        discard.MinWidth = 112;
        var apply = controls.PrimaryButton("应用修改", "\uE73E");
        apply.MinWidth = 112;
        actionPanel.Children.Add(discard);
        actionPanel.Children.Add(apply);
        var synchronizing = false;
        void Discard()
        {
            synchronizing = true;
            var originalLocal = original.ToLocalTime();
            date.Date = new DateTimeOffset(originalLocal.Date);
            hour.SelectedIndex = originalLocal.Hour;
            minute.SelectedIndex = originalLocal.Minute;
            synchronizing = false;
            actionPanel.Visibility = Visibility.Collapsed;
        }
        void MarkChanged()
        {
            if (!synchronizing) actionPanel.Visibility = Visibility.Visible;
        }
        date.DateChanged += (_, _) => MarkChanged();
        hour.SelectionChanged += (_, _) => MarkChanged();
        minute.SelectionChanged += (_, _) => MarkChanged();
        discard.Click += (_, _) => Discard();
        apply.Click += (_, _) =>
        {
            if (date.Date is not { } selectedDate) return;
            var time = new TimeSpan(Math.Max(0, hour.SelectedIndex), Math.Max(0, minute.SelectedIndex), 0);
            var localCompletedAt = selectedDate.DateTime.Date.Add(time);
            actions.UpdateCompletion(new DateTimeOffset(localCompletedAt, TimeZoneInfo.Local.GetUtcOffset(localCompletedAt)));
        };
        panel.Children.Add(DetailField("\uE787", "完成日期", date, editorWidth));
        panel.Children.Add(DetailField("\uE916", "完成时间", CompletionEditor(hour, minute), editorWidth));
        panel.Children.Add(actionPanel);
    }

    private void AddSchedulingFields(
        StackPanel panel,
        TodoTask task,
        IReadOnlyList<TodoList> lists,
        TodoTaskDetailActions actions)
    {
        var listBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var list in lists)
        {
            var item = new ComboBoxItem { Content = list.Name, Tag = list.Id };
            listBox.Items.Add(item);
            if (string.Equals(task.ListId, list.Id, StringComparison.Ordinal)) listBox.SelectedItem = item;
        }
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is ComboBoxItem { Tag: string listId } &&
                !string.Equals(task.ListId, listId, StringComparison.Ordinal)) actions.MoveToList(listId);
        };
        panel.Children.Add(DetailField("\uE8B7", "所属清单", listBox));
        var start = new CalendarDatePicker
        {
            Date = new DateTimeOffset(task.StartDate.Date), PlaceholderText = "选择开始时间",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        start.DateChanged += (_, args) => actions.UpdateStartDate(args.NewDate?.DateTime.Date ?? DateTime.Today);
        panel.Children.Add(DetailField("\uE163", "开始时间", start));
        var dueGrid = new Grid { ColumnSpacing = 8 };
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var due = new CalendarDatePicker
        {
            Date = task.DueDate.HasValue ? new DateTimeOffset(task.DueDate.Value) : null,
            PlaceholderText = "选择日期", HorizontalAlignment = HorizontalAlignment.Stretch
        };
        due.DateChanged += (_, args) => actions.UpdateDueDate(args.NewDate?.DateTime.Date);
        dueGrid.Children.Add(due);
        var clear = controls.IconOnlyButton("\uE711", "清除日期");
        clear.Click += (_, _) => actions.UpdateDueDate(null);
        Grid.SetColumn(clear, 1);
        dueGrid.Children.Add(clear);
        panel.Children.Add(DetailField("\uE787", "截止日期", dueGrid));
    }

    private StackPanel Actions(TodoTask task, string? childBlockedReason, TodoTaskDetailActions actions)
    {
        var panel = new StackPanel
        {
            Spacing = 10, Padding = new Thickness(28, 16, 28, 26), Background = palette.Background
        };
        var addChild = controls.PrimaryButton("添加子任务", "\uE710");
        addChild.IsEnabled = string.IsNullOrWhiteSpace(childBlockedReason);
        if (!addChild.IsEnabled) ToolTipService.SetToolTip(addChild, childBlockedReason);
        addChild.Click += async (_, _) => await actions.AddSubtask();
        panel.Children.Add(addChild);
        var toggle = controls.PrimaryButton(task.IsCompleted ? "恢复任务" : "完成任务", task.IsCompleted ? "\uE777" : "\uE73E");
        toggle.Click += async (_, _) => await actions.ToggleCompleted();
        panel.Children.Add(toggle);
        var delete = controls.DangerButton("删除任务", "\uE74D");
        delete.Click += async (_, _) => await actions.Delete();
        panel.Children.Add(delete);
        return panel;
    }

    private ComboBox CompletionPartBox(int itemCount, int selectedIndex, string automationName)
    {
        var box = new ComboBox
        {
            BorderThickness = new Thickness(0), Background = palette.Transparent,
            Padding = new Thickness(12, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        for (var value = 0; value < itemCount; value++) box.Items.Add(value.ToString("00"));
        box.SelectedIndex = Math.Clamp(selectedIndex, 0, itemCount - 1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, automationName);
        return box;
    }

    private Border CompletionEditor(ComboBox hour, ComboBox minute)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(hour);
        var separator = new TextBlock
        {
            Text = ":", FontSize = 16, FontWeight = MuxFontWeights.SemiBold,
            Foreground = palette.SecondaryText, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        Grid.SetColumn(separator, 1);
        layout.Children.Add(separator);
        Grid.SetColumn(minute, 2);
        layout.Children.Add(minute);
        var editor = new Border
        {
            Width = 216, Height = 34, CornerRadius = new CornerRadius(7),
            BorderBrush = palette.Divider, BorderThickness = new Thickness(1),
            Background = palette.Background, HorizontalAlignment = HorizontalAlignment.Left,
            Child = layout
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, "完成时间");
        return editor;
    }

    private UIElement DetailField(string glyph, string label, FrameworkElement value, double minimumValueWidth = 0)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star), MinWidth = minimumValueWidth
        });
        grid.Children.Add(new FontIcon
        {
            Glyph = glyph, FontSize = 18, Foreground = palette.SecondaryText,
            VerticalAlignment = VerticalAlignment.Center
        });
        var labelBlock = new TextBlock
        {
            Text = label, FontSize = 14, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 1);
        grid.Children.Add(labelBlock);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }
}
