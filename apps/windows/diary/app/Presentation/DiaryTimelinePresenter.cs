using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryTimelinePresenter(
    Func<DiaryData> data,
    Func<DiarySettings> settings,
    DiaryTimelineStateController timeline,
    DiaryThemePalette theme,
    DiaryUiFactory ui,
    Func<IReadOnlyList<DiaryEntry>, FrameworkElement> buildTimeline,
    Func<string> pageListTitle,
    Func<string, Task> showSearch,
    Func<Task> createEntry,
    Action ensureSelectedEntryVisible,
    Action saveSettings,
    Action rebuild)
{
    private const double NavigatorWidth = 360;

    public FrameworkElement BuildWorkspace()
    {
        var layout = new Grid { Background = theme.Brush("AppBackground") };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(BuildHeader());
        var content = new Grid { Margin = new Thickness(34, 20, 34, 24) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NavigatorWidth) });
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.Children.Add(BuildFilterBar());
        var stream = new ScrollViewer
        {
            Padding = new Thickness(0, 24, 28, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            Content = BuildStream()
        };
        Grid.SetRow(stream, 1);
        main.Children.Add(stream);
        content.Children.Add(main);
        var navigator = new Border
        {
            BorderBrush = theme.Brush("Divider"), BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(24, 0, 0, 0), Child = BuildNavigator()
        };
        Grid.SetColumn(navigator, 1);
        content.Children.Add(navigator);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        return layout;
    }

    public ComboBox BuildNotebookSelector()
    {
        var currentSettings = settings();
        var selector = new ComboBox { Width = 176, Height = 36, Padding = new Thickness(10, 0, 8, 0) };
        var all = new ComboBoxItem { Content = "全部日记本", Tag = DiaryTimeline.AllNotebooksId };
        selector.Items.Add(all);
        foreach (var notebook in data().Notebooks)
        {
            var itemContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            itemContent.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = theme.HexBrush(notebook.AccentColor), VerticalAlignment = VerticalAlignment.Center });
            itemContent.Children.Add(ui.Text(notebook.Name, 14, "TextPrimary"));
            selector.Items.Add(new ComboBoxItem { Content = itemContent, Tag = notebook.Id });
        }
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), NotebookId, StringComparison.Ordinal)) ?? all;
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is not ComboBoxItem selected) return;
            var notebookId = selected.Tag?.ToString() ?? DiaryTimeline.AllNotebooksId;
            if (string.Equals(currentSettings.TimelineNotebookId, notebookId, StringComparison.Ordinal)) return;
            currentSettings.TimelineNotebookId = notebookId;
            saveSettings();
            ensureSelectedEntryVisible();
            rebuild();
        };
        return selector;
    }

    public IReadOnlyList<DiaryEntry> SourceEntries() => DiaryTimeline.Query(data(), NotebookId);

    public IReadOnlyList<DiaryEntry> Entries()
    {
        var window = timeline.DateWindow();
        return DiaryTimeline.Query(data(), NotebookId, window.Start, window.End);
    }

    public string DateHeading(DateTime date)
    {
        var prefix = date.Date == DiaryRuntime.Today ? "今天 · " : date.Date == DiaryRuntime.Today.AddDays(-1) ? "昨天 · " : string.Empty;
        return $"{prefix}{date.ToString("M月d日 ddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"))}";
    }

    public void NavigateToDate(DateTime month, DateTime date)
    {
        timeline.NavigateToDate(month, date);
        rebuild();
    }

    private FrameworkElement BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(34, 58, 34, 0), ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 5 };
        title.Children.Add(ui.Text("时间线", 30, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        title.Children.Add(ui.Text(DateRangeLabel(), 14, "TextSecondary"));
        header.Children.Add(title);
        var notebook = BuildNotebookSelector();
        Grid.SetColumn(notebook, 1);
        header.Children.Add(notebook);
        var search = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var searchBox = new TextBox
        {
            Width = 236, Height = 36, PlaceholderText = "搜索日记标题、内容或标签",
            Padding = new Thickness(11, 0, 8, 0), Background = theme.Brush("ControlBackground"),
            BorderBrush = theme.Brush("CardStroke"), BorderThickness = new Thickness(1),
            Foreground = theme.Brush("TextPrimary"), PlaceholderForeground = theme.Brush("TextMuted")
        };
        searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Enter) await showSearch(searchBox.Text);
        };
        search.Children.Add(searchBox);
        var searchButton = ui.IconButton("\uE721", "搜索日记");
        searchButton.Click += async (_, _) => await showSearch(searchBox.Text);
        search.Children.Add(searchButton);
        Grid.SetColumn(search, 2);
        header.Children.Add(search);
        var create = ui.PrimaryButton("\uE710", "新建日记");
        create.Click += async (_, _) => await createEntry();
        Grid.SetColumn(create, 3);
        header.Children.Add(create);
        return header;
    }

    private FrameworkElement BuildFilterBar()
    {
        var content = new Grid { Margin = new Thickness(18, 0, 18, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ranges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        ranges.Children.Add(RangeButton("全部", DiaryTimelineStateController.RangeAll));
        ranges.Children.Add(RangeButton("今天", DiaryTimelineStateController.RangeToday));
        ranges.Children.Add(RangeButton("本周", DiaryTimelineStateController.RangeWeek));
        ranges.Children.Add(RangeButton("本月", DiaryTimelineStateController.RangeMonth));
        ranges.Children.Add(RangeButton("本年", DiaryTimelineStateController.RangeYear));
        content.Children.Add(ranges);
        var navigator = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var previous = ui.TimelineIconButton("\uE76B", "上一个时间范围");
        previous.Click += (_, _) => MoveRange(-1);
        navigator.Children.Add(previous);
        navigator.Children.Add(ui.Text(NavigatorTitle(), 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var next = ui.TimelineIconButton("\uE76C", "下一个时间范围");
        next.Click += (_, _) => MoveRange(1);
        navigator.Children.Add(next);
        Grid.SetColumn(navigator, 1);
        content.Children.Add(navigator);
        return ui.Card(content, 58);
    }

    private Button RangeButton(string label, string rangeId)
    {
        var selected = string.Equals(timeline.RangeId, rangeId, StringComparison.Ordinal) && timeline.DateFilter is null;
        var button = new Button
        {
            Height = 42, Padding = new Thickness(16, 0, 16, 0), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6), Background = selected ? theme.Brush("SelectedNav") : DiaryUiFactory.TransparentBrush(),
            Content = ui.Text(label, 14, selected ? "OnAccent" : "TextSecondary", Microsoft.UI.Text.FontWeights.SemiBold)
        };
        button.Click += (_, _) => SelectRange(rangeId);
        return button;
    }

    private FrameworkElement BuildStream()
    {
        var entries = Entries();
        var stack = new StackPanel { Spacing = 14 };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(ui.Text($"{pageListTitle()} · {entries.Count} 篇日记", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var sort = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7,
            Children = { new FontIcon { Glyph = "\uE8CB", FontSize = 15, Foreground = theme.Brush("TextSecondary") }, ui.Text("从新到旧", 13, "TextSecondary") }
        };
        Grid.SetColumn(sort, 1);
        header.Children.Add(sort);
        stack.Children.Add(header);
        stack.Children.Add(entries.Count == 0 ? ui.EmptyCard("当前时间范围没有日记。") : buildTimeline(entries));
        return stack;
    }

    private FrameworkElement BuildNavigator()
    {
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(ui.Text("活动日历", 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        stack.Children.Add(BuildActivityCalendar());
        stack.Children.Add(new Border { Height = 1, Background = theme.Brush("Divider"), Margin = new Thickness(0, 2, 0, 0) });
        stack.Children.Add(ui.Text("时间导航", 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        stack.Children.Add(BuildNavigationRows());
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, Content = stack };
    }

    private FrameworkElement BuildActivityCalendar()
    {
        var stack = new StackPanel { Spacing = 10 };
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var previous = ui.TextButton("‹", "上个月", 20, "TextSecondary");
        previous.Click += (_, _) => MoveNavigatorMonth(-1);
        controls.Children.Add(previous);
        controls.Children.Add(ui.Text(timeline.NavigatorMonth.ToString("yyyy年M月"), 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var next = ui.TextButton("›", "下个月", 20, "TextSecondary");
        next.Click += (_, _) => MoveNavigatorMonth(1);
        controls.Children.Add(next);
        Grid.SetColumn(controls, 1);
        title.Children.Add(controls);
        stack.Children.Add(title);
        var calendar = new Grid { RowSpacing = 5, ColumnSpacing = 5 };
        for (var column = 0; column < 7; column++) calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 7; row++) calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labels = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (var column = 0; column < labels.Length; column++)
        {
            var label = ui.Text(labels[column], 12, "TextMuted");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(label, column);
            calendar.Children.Add(label);
        }
        var start = timeline.NavigatorMonth.AddDays(-((int)timeline.NavigatorMonth.DayOfWeek + 6) % 7);
        for (var index = 0; index < 42; index++)
        {
            var cell = ActivityCell(start.AddDays(index));
            Grid.SetRow(cell, index / 7 + 1);
            Grid.SetColumn(cell, index % 7);
            calendar.Children.Add(cell);
        }
        stack.Children.Add(calendar);
        return stack;
    }

    private Button ActivityCell(DateTime date)
    {
        var inMonth = date.Month == timeline.NavigatorMonth.Month && date.Year == timeline.NavigatorMonth.Year;
        var selected = timeline.DateFilter?.Date == date.Date;
        var hasEntries = SourceEntries().Any(entry => entry.CreatedAt.LocalDateTime.Date == date.Date);
        var button = new Button
        {
            Height = 28, Padding = new Thickness(0), BorderThickness = hasEntries && !selected ? new Thickness(1) : new Thickness(0),
            BorderBrush = hasEntries && !selected ? theme.Brush("Accent") : DiaryUiFactory.TransparentBrush(),
            CornerRadius = new CornerRadius(14), Background = selected ? theme.Brush("Accent") : DiaryUiFactory.TransparentBrush(),
            Content = new TextBlock
            {
                Text = date.Day.ToString(), FontSize = 12,
                Foreground = selected ? theme.Brush("OnAccent") : inMonth ? theme.Brush("TextPrimary") : theme.Brush("TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) => SelectDate(date);
        return button;
    }

    private FrameworkElement BuildNavigationRows()
    {
        var source = SourceEntries();
        if (source.Count == 0) return ui.Text("当前日记本还没有可导航的记录。", 13, "TextSecondary");
        var stack = new StackPanel { Spacing = 5 };
        var groups = source.GroupBy(entry => new DateTime(entry.CreatedAt.LocalDateTime.Year, entry.CreatedAt.LocalDateTime.Month, 1)).OrderByDescending(group => group.Key);
        foreach (var month in groups)
        {
            var monthContent = CountRow($"{month.Key:yyyy年M月}", $"{month.Count()} 篇", 300, 14, 13);
            var monthButton = new Button
            {
                Height = 34, Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0),
                Background = month.Key == timeline.NavigatorMonth ? theme.Brush("ControlBackground") : DiaryUiFactory.TransparentBrush(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = monthContent
            };
            var targetMonth = month.Key;
            monthButton.Click += (_, _) => NavigateToDate(targetMonth, month.First().CreatedAt.LocalDateTime.Date);
            stack.Children.Add(monthButton);
            if (month.Key != timeline.NavigatorMonth) continue;
            foreach (var day in month.GroupBy(entry => entry.CreatedAt.LocalDateTime.Date).OrderByDescending(group => group.Key))
            {
                var dayButton = new Button
                {
                    Height = 30, Padding = new Thickness(16, 0, 6, 0), BorderThickness = new Thickness(0),
                    Background = timeline.DateFilter?.Date == day.Key ? theme.Brush("SelectedNav") : DiaryUiFactory.TransparentBrush(),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = CountRow(day.Key.ToString("M月d日"), $"{day.Count()} 篇", 290, 13, 12)
                };
                var targetDate = day.Key;
                dayButton.Click += (_, _) => NavigateToDate(targetMonth, targetDate);
                stack.Children.Add(dayButton);
            }
        }
        return stack;
    }

    private Grid CountRow(string label, string count, double width, double labelSize, double countSize)
    {
        var row = new Grid { Width = width };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(ui.Text(label, labelSize, labelSize >= 14 ? "TextPrimary" : "TextSecondary", labelSize >= 14 ? Microsoft.UI.Text.FontWeights.SemiBold : null));
        var countText = new TextBlock { Text = count, FontSize = countSize, Foreground = theme.Brush(countSize >= 13 ? "TextSecondary" : "TextMuted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(countText, 1);
        row.Children.Add(countText);
        return row;
    }

    private string DateRangeLabel()
    {
        var entries = Entries();
        if (entries.Count == 0) return "当前时间范围还没有记录";
        var window = timeline.DateWindow();
        var newest = entries[0].CreatedAt.LocalDateTime.Date;
        var oldest = entries[^1].CreatedAt.LocalDateTime.Date;
        var start = window.Start ?? oldest;
        var end = window.End ?? newest;
        var range = start == end ? start.ToString("yyyy年M月d日") : $"{start:yyyy年M月d日} – {end:yyyy年M月d日}";
        return $"{range}（{entries.Count} 篇日记）";
    }

    private string NavigatorTitle() => timeline.DateFilter is not null
        ? timeline.DateFilter.Value.ToString("yyyy年M月d日")
        : timeline.RangeId == DiaryTimelineStateController.RangeAll
            ? timeline.NavigatorMonth.ToString("yyyy年M月")
            : timeline.RangeId switch
            {
                DiaryTimelineStateController.RangeToday => "按天浏览",
                DiaryTimelineStateController.RangeWeek => "按周浏览",
                DiaryTimelineStateController.RangeMonth => "按月浏览",
                DiaryTimelineStateController.RangeYear => "按年浏览",
                _ => timeline.NavigatorMonth.ToString("yyyy年M月")
            };

    private string NotebookId => string.IsNullOrWhiteSpace(settings().TimelineNotebookId) ? DiaryTimeline.AllNotebooksId : settings().TimelineNotebookId;

    private void SelectRange(string rangeId)
    {
        timeline.SelectRange(rangeId);
        ensureSelectedEntryVisible();
        rebuild();
    }

    private void MoveRange(int offset)
    {
        timeline.MoveRange(offset);
        ensureSelectedEntryVisible();
        rebuild();
    }

    private void MoveNavigatorMonth(int offset)
    {
        timeline.MoveNavigatorMonth(offset);
        rebuild();
    }

    private void SelectDate(DateTime date)
    {
        timeline.ToggleDate(date);
        ensureSelectedEntryVisible();
        rebuild();
    }
}
