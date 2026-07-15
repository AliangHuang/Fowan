using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MuxFontWeights = Microsoft.UI.Text.FontWeights;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoTaskDraft(
    string Title,
    string ListId,
    bool IsImportant,
    DateTime StartDate,
    DateTime? DueDate,
    string? ParentTaskId,
    string Notes);

internal sealed record TodoFilterSelection(
    bool Clear,
    string? ListId,
    int MaximumDepth,
    TodoDateRangeFilter? DateRange);

internal enum TodoSettingsDialogAction { Cancel, Save, OpenSticky, RestartOnboarding }

internal sealed record TodoSettingsSelection(
    TodoSettingsDialogAction Action,
    string Theme,
    bool RecycleBinEnabled,
    string RetentionPreset,
    int CustomRetentionDays);

internal sealed class TodoDialogService(
    Func<XamlRoot?> xamlRoot,
    Func<ElementTheme> theme,
    Func<ContentDialog, Task<ContentDialogResult>> showModal,
    Func<string, string, Button> createPillButton)
{
    public async Task<TodoTaskDraft?> ShowAddTaskAsync(
        IEnumerable<TodoList> lists,
        string defaultListId,
        bool initiallyImportant)
    {
        var title = new TextBox { PlaceholderText = "任务名称", MinWidth = 340, FontSize = 15 };
        var notes = NotesBox();
        var list = ListBox(lists, defaultListId);
        var start = new CalendarDatePicker
        {
            Date = new DateTimeOffset(DateTime.Today),
            PlaceholderText = "选择开始时间",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var due = new CalendarDatePicker
        {
            PlaceholderText = "选择截止日期",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var important = new CheckBox { Content = "标为重要", IsChecked = initiallyImportant };
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        AddField(content, "任务名称", title);
        AddField(content, "备注", notes);
        AddField(content, "所属清单", list);
        AddField(content, "开始时间", start);
        AddField(content, "截止日期", due);
        content.Children.Add(important);

        var result = await ShowValidatedAsync("添加任务", "添加", content, title);
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(title.Text)) return null;
        var listId = list.SelectedItem is ComboBoxItem { Tag: string selected } ? selected : defaultListId;
        return new(
            title.Text.Trim(),
            listId,
            important.IsChecked == true,
            start.Date?.DateTime.Date ?? DateTime.Today,
            due.Date?.DateTime.Date,
            null,
            notes.Text);
    }

    public async Task<TodoTaskDraft?> ShowAddSubtaskAsync(TodoTask parent, Brush secondaryText)
    {
        var title = new TextBox { PlaceholderText = "子任务名称", MinWidth = 340, FontSize = 15 };
        var notes = NotesBox();
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        AddField(content, "父任务", new TextBlock
        {
            Text = parent.Title,
            FontSize = 14,
            Foreground = secondaryText,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        AddField(content, "子任务名称", title);
        AddField(content, "备注", notes);
        var result = await ShowValidatedAsync("添加子任务", "添加", content, title);
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(title.Text)) return null;
        return new(
            title.Text.Trim(),
            parent.ListId,
            parent.IsImportant,
            parent.StartDate == default ? DateTime.Today : parent.StartDate.Date,
            parent.DueDate,
            parent.Id,
            notes.Text);
    }

    public async Task<string?> ShowListNameAsync(string title, string primaryText, string? initialName = null)
    {
        var name = new TextBox { Text = initialName ?? string.Empty, PlaceholderText = "清单名称", MinWidth = 280 };
        var result = await ShowValidatedAsync(title, primaryText, name, name);
        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(name.Text)
            ? name.Text.Trim()
            : null;
    }

    public Task ShowDefaultListDeleteBlockedAsync() => showModal(new ContentDialog
    {
        XamlRoot = xamlRoot(),
        RequestedTheme = theme(),
        Title = "无法删除默认清单",
        Content = "默认清单用于承接未分类任务，不能删除。",
        CloseButtonText = "知道了"
    });

    public async Task<bool> ConfirmDeleteListAsync(TodoList list, int taskCount)
    {
        var result = await showModal(new ContentDialog
        {
            XamlRoot = xamlRoot(),
            RequestedTheme = theme(),
            Title = "删除清单",
            Content = taskCount > 0
                ? $"删除“{list.Name}”后，其中 {taskCount} 个任务会移动到默认清单。"
                : $"确定删除“{list.Name}”？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        });
        return result == ContentDialogResult.Primary;
    }

    public async Task<TodoFilterSelection?> ShowFilterAsync(
        IEnumerable<TodoList> lists,
        string? selectedListId,
        int maximumDepth,
        TodoDateRangeFilter? dateRange)
    {
        var listFilter = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var allLists = new ComboBoxItem { Tag = string.Empty, Content = "全部清单" };
        listFilter.Items.Add(allLists);
        listFilter.SelectedItem = allLists;
        foreach (var list in lists)
        {
            var item = new ComboBoxItem { Tag = list.Id, Content = list.Name };
            listFilter.Items.Add(item);
            if (string.Equals(selectedListId, list.Id, StringComparison.Ordinal)) listFilter.SelectedItem = item;
        }

        var hierarchy = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in new[] { (1, "仅一级"), (2, "一级和二级"), (TodoQuery.MaxTaskTreeDepth, "全部层级") })
        {
            var item = new ComboBoxItem { Tag = choice.Item1, Content = choice.Item2 };
            hierarchy.Items.Add(item);
            if (choice.Item1 == maximumDepth) hierarchy.SelectedItem = item;
        }

        var dateMode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in new[] { ("none", "不按日期筛选"), ("start", "开始日期"), ("period", "执行周期") })
        {
            var item = new ComboBoxItem { Tag = choice.Item1, Content = choice.Item2 };
            dateMode.Items.Add(item);
            var selected = dateRange switch
            {
                null => choice.Item1 == "none",
                { Mode: TodoDateFilterMode.StartDate } => choice.Item1 == "start",
                _ => choice.Item1 == "period"
            };
            if (selected) dateMode.SelectedItem = item;
        }

        var start = DatePicker("开始日期", dateRange?.StartDate);
        var end = DatePicker("结束日期", dateRange?.EndDate);
        var rangeFields = new StackPanel { Spacing = 12, Children = { start, end } };
        void UpdateRangeVisibility() => rangeFields.Visibility =
            dateMode.SelectedItem is ComboBoxItem { Tag: string mode } && mode != "none"
                ? Visibility.Visible
                : Visibility.Collapsed;
        void SetRange(DateTime first, DateTime last)
        {
            start.Date = new DateTimeOffset(first.Date);
            end.Date = new DateTimeOffset(last.Date);
            if (dateMode.SelectedItem is ComboBoxItem { Tag: string mode } && mode == "none")
            {
                dateMode.SelectedItem = dateMode.Items.OfType<ComboBoxItem>()
                    .First(item => string.Equals(item.Tag as string, "period", StringComparison.Ordinal));
            }
            UpdateRangeVisibility();
        }
        dateMode.SelectionChanged += (_, _) => UpdateRangeVisibility();
        var quickActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var week = createPillButton("本周", "\uE787");
        week.Click += (_, _) =>
        {
            var today = DateTime.Today;
            var offset = ((int)today.DayOfWeek + 6) % 7;
            SetRange(today.AddDays(-offset), today.AddDays(6 - offset));
        };
        quickActions.Children.Add(week);
        var month = createPillButton("本月", "\uE81C");
        month.Click += (_, _) =>
        {
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1);
            SetRange(first, first.AddMonths(1).AddDays(-1));
        };
        quickActions.Children.Add(month);

        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        AddField(content, "任务清单", listFilter);
        AddField(content, "任务层级", hierarchy);
        AddField(content, "日期范围", dateMode);
        content.Children.Add(quickActions);
        content.Children.Add(rangeFields);
        UpdateRangeVisibility();
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), RequestedTheme = theme(), Title = "筛选", Content = content,
            PrimaryButtonText = "应用", SecondaryButtonText = "清除筛选", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (dateMode.SelectedItem is ComboBoxItem { Tag: string mode } && mode != "none" &&
                (start.Date is null || end.Date is null || start.Date.Value.Date > end.Date.Value.Date))
            {
                args.Cancel = true;
            }
        };
        var result = await showModal(dialog);
        if (result == ContentDialogResult.None) return null;
        if (result == ContentDialogResult.Secondary)
        {
            return new(true, null, TodoQuery.MaxTaskTreeDepth, null);
        }
        var depth = hierarchy.SelectedItem is ComboBoxItem { Tag: int selectedDepth }
            ? Math.Clamp(selectedDepth, 1, TodoQuery.MaxTaskTreeDepth)
            : TodoQuery.MaxTaskTreeDepth;
        var selectedRange = dateMode.SelectedItem is ComboBoxItem { Tag: string selectedMode } &&
            selectedMode != "none" && start.Date is { } firstDate && end.Date is { } lastDate
            ? new TodoDateRangeFilter
            {
                Mode = selectedMode == "period" ? TodoDateFilterMode.ExecutionPeriod : TodoDateFilterMode.StartDate,
                StartDate = firstDate.DateTime.Date,
                EndDate = lastDate.DateTime.Date
            }
            : null;
        var listId = listFilter.SelectedItem is ComboBoxItem { Tag: string selectedList } &&
            !string.IsNullOrWhiteSpace(selectedList) ? selectedList : null;
        return new(false, listId, depth, selectedRange);
    }

    public async Task<TodoSettingsSelection> ShowSettingsAsync(
        TodoSettingsSnapshot settings,
        Brush textBrush,
        Brush secondaryText,
        Brush errorText)
    {
        var themeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 300 };
        AddChoice(themeBox, "跟随系统", TodoThemeIds.System, settings.Theme);
        AddChoice(themeBox, "浅色主题", TodoThemeIds.Light, settings.Theme);
        AddChoice(themeBox, "深色主题", TodoThemeIds.Dark, settings.Theme);
        var recycleBin = new CheckBox { Content = "启用回收站", IsChecked = settings.IsRecycleBinEnabled };
        var retention = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 300 };
        foreach (var preset in new[]
                 {
                     (TodoRecycleBinRetentionPresets.SevenDays, "7 天"),
                     (TodoRecycleBinRetentionPresets.ThirtyDays, "30 天"),
                     (TodoRecycleBinRetentionPresets.NinetyDays, "90 天"),
                     (TodoRecycleBinRetentionPresets.Custom, "自定义")
                 })
        {
            AddChoice(retention, preset.Item2, preset.Item1, settings.RecycleBinRetentionPreset);
        }
        var customDays = new TextBox
        {
            Text = settings.RecycleBinCustomRetentionDays.ToString(),
            PlaceholderText = "请输入 1–365 天",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }
        };
        var hint = new TextBlock
        {
            Text = "仅支持 1–365 的整数天数。", FontSize = 12,
            Foreground = secondaryText, TextWrapping = TextWrapping.Wrap
        };
        var error = new TextBlock
        {
            FontSize = 12, Foreground = errorText,
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed
        };
        bool Validate(bool showError)
        {
            var custom = retention.SelectedItem is ComboBoxItem { Tag: string preset } &&
                string.Equals(preset, TodoRecycleBinRetentionPresets.Custom, StringComparison.Ordinal);
            var valid = !custom || int.TryParse(customDays.Text, out var days) && days is >= 1 and <= 365;
            error.Text = valid ? string.Empty : "请输入 1–365 之间的整数天数。";
            error.Visibility = showError && !valid && recycleBin.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            return valid;
        }
        void UpdateControls()
        {
            var custom = retention.SelectedItem is ComboBoxItem { Tag: string preset } &&
                string.Equals(preset, TodoRecycleBinRetentionPresets.Custom, StringComparison.Ordinal);
            retention.IsEnabled = recycleBin.IsChecked == true;
            customDays.Visibility = custom && recycleBin.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            hint.Visibility = customDays.Visibility;
            Validate(true);
        }
        retention.SelectionChanged += (_, _) => UpdateControls();
        recycleBin.Checked += (_, _) => UpdateControls();
        recycleBin.Unchecked += (_, _) => UpdateControls();
        customDays.TextChanged += (_, _) => Validate(true);
        UpdateControls();

        var content = new StackPanel { Spacing = 14, MinWidth = 320 };
        AddSection(content, "主题", textBrush);
        content.Children.Add(themeBox);
        AddSection(content, "新手引导", textBrush, new Thickness(0, 8, 0, 0));
        content.Children.Add(new TextBlock
        {
            Text = "重新查看便签模式和帮助入口的两步操作指引。",
            FontSize = 13, Foreground = secondaryText, TextWrapping = TextWrapping.Wrap
        });
        var restart = createPillButton("重新开始新手引导", "\uE72C");
        AutomationProperties.SetName(restart, "重新开始新手引导");
        restart.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(restart);
        AddSection(content, "回收站", textBrush, new Thickness(0, 8, 0, 0));
        content.Children.Add(recycleBin);
        content.Children.Add(new TextBlock { Text = "自动清理周期", FontSize = 13, Foreground = secondaryText });
        content.Children.Add(retention);
        content.Children.Add(customDays);
        content.Children.Add(hint);
        content.Children.Add(error);
        content.Children.Add(new TextBlock
        {
            Text = "便签模式可在这里打开，打开后主界面会暂时隐藏。",
            TextWrapping = TextWrapping.Wrap, Foreground = secondaryText, FontSize = 13
        });
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "设置",
            Content = new ScrollViewer
            {
                Content = content, MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto
            },
            PrimaryButtonText = "保存", SecondaryButtonText = "打开便签模式",
            CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        var restartRequested = false;
        restart.Click += (_, _) => { restartRequested = true; dialog.Hide(); };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (retention.SelectedItem is not ComboBoxItem { Tag: string preset } ||
                recycleBin.IsChecked == true &&
                string.Equals(preset, TodoRecycleBinRetentionPresets.Custom, StringComparison.Ordinal) &&
                !Validate(true))
            {
                args.Cancel = true;
                customDays.Focus(FocusState.Programmatic);
            }
        };
        var result = await showModal(dialog);
        if (restartRequested)
        {
            return new(TodoSettingsDialogAction.RestartOnboarding, settings.Theme,
                settings.IsRecycleBinEnabled, settings.RecycleBinRetentionPreset,
                settings.RecycleBinCustomRetentionDays);
        }
        var action = result switch
        {
            ContentDialogResult.Primary => TodoSettingsDialogAction.Save,
            ContentDialogResult.Secondary => TodoSettingsDialogAction.OpenSticky,
            _ => TodoSettingsDialogAction.Cancel
        };
        var selectedTheme = (themeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? settings.Theme;
        var selectedRetention = (retention.SelectedItem as ComboBoxItem)?.Tag as string ?? settings.RecycleBinRetentionPreset;
        var selectedDays = int.TryParse(customDays.Text, out var days) ? days : settings.RecycleBinCustomRetentionDays;
        return new(action, selectedTheme, recycleBin.IsChecked == true, selectedRetention, selectedDays);
    }

    private async Task<ContentDialogResult> ShowValidatedAsync(
        string title,
        string primaryText,
        object content,
        TextBox required)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(),
            RequestedTheme = theme(),
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(required.Text)) return;
            args.Cancel = true;
            required.Focus(FocusState.Programmatic);
        };
        return await showModal(dialog);
    }

    private static TextBox NotesBox() => new()
    {
        PlaceholderText = "备注（可选）",
        MinWidth = 340,
        MinHeight = 96,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14
    };

    private static CalendarDatePicker DatePicker(string placeholder, DateTime? value) => new()
    {
        Date = value is null ? null : new DateTimeOffset(value.Value.Date),
        PlaceholderText = placeholder,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static void AddChoice(ComboBox box, string label, string value, string selectedValue)
    {
        var item = new ComboBoxItem { Tag = value, Content = label };
        box.Items.Add(item);
        if (string.Equals(value, selectedValue, StringComparison.Ordinal)) box.SelectedItem = item;
    }

    private static void AddSection(StackPanel content, string text, Brush foreground, Thickness margin = default) =>
        content.Children.Add(new TextBlock
        {
            Text = text, FontSize = 14, FontWeight = MuxFontWeights.SemiBold,
            Foreground = foreground, Margin = margin
        });

    private static ComboBox ListBox(IEnumerable<TodoList> lists, string selectedListId)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var list in lists)
        {
            var item = new ComboBoxItem { Content = list.Name, Tag = list.Id };
            box.Items.Add(item);
            if (string.Equals(selectedListId, list.Id, StringComparison.Ordinal)) box.SelectedItem = item;
        }
        if (box.SelectedItem is null && box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    private static void AddField(StackPanel content, string label, UIElement field)
    {
        content.Children.Add(new TextBlock { Text = label, FontSize = 13 });
        content.Children.Add(field);
    }
}
