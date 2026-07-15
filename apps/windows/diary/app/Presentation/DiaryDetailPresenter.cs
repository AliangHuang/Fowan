using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryDetailPresenter(
    Func<DiaryData> data,
    Func<DiarySettings> settings,
    Func<DiaryEntry?> selectedEntry,
    Action<DiaryEntry?> setSelectedEntry,
    DiaryThemePalette theme,
    DiaryUiFactory ui,
    Func<DiaryEntry, Task> showTagPicker,
    Action<string> toggleFavorite,
    Action<Button, DiaryEntry> showEntryMenu,
    Func<DiaryEntry, Task> showEditor,
    Func<DiaryEntry, Task> showTodoPicker,
    Action<string> selectView,
    Func<DiaryEntry, Task> exportEntry,
    Func<DiaryEntry, Task> deleteEntry,
    Action rebuild)
{
    private DateTime _calendarMonth = new(DiaryRuntime.Today.Year, DiaryRuntime.Today.Month, 1);
    private DateTime? _calendarDate;

    public FrameworkElement BuildColumn() => new Border
    {
        Background = theme.Brush("DetailBackground"), Padding = new Thickness(34, 62, 34, 28),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            Content = BuildContent()
        }
    };

    public IEnumerable<DiaryEntry> FilterCalendar(IEnumerable<DiaryEntry> entries) => _calendarDate is not null
        ? entries.Where(entry => entry.CreatedAt.LocalDateTime.Date == _calendarDate.Value.Date)
        : entries.Where(entry => entry.CreatedAt.Year == _calendarMonth.Year && entry.CreatedAt.Month == _calendarMonth.Month);

    public FrameworkElement BuildCalendarCard(bool large)
    {
        var stack = new StackPanel { Spacing = large ? 13 : 10, Margin = new Thickness(18, 14, 18, 16) };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(ui.Text("日历", large ? 18 : 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var month = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var previous = ui.TextButton("‹", "上个月", 20, "TextSecondary");
        previous.Click += (_, _) => MoveCalendarMonth(-1);
        month.Children.Add(previous);
        month.Children.Add(ui.Text(_calendarMonth.ToString("yyyy年M月"), 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var next = ui.TextButton("›", "下个月", 20, "TextSecondary");
        next.Click += (_, _) => MoveCalendarMonth(1);
        month.Children.Add(next);
        Grid.SetColumn(month, 1);
        header.Children.Add(month);
        var view = ui.TimelineIconButton("\uE787", "日历视图");
        view.Click += (_, _) => selectView(DiaryViewIds.Calendar);
        Grid.SetColumn(view, 2);
        header.Children.Add(view);
        stack.Children.Add(header);
        var calendar = new Grid { RowSpacing = 5, ColumnSpacing = 6 };
        for (var i = 0; i < 7; i++) calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++) calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labels = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (var column = 0; column < labels.Length; column++)
        {
            var label = ui.Text(labels[column], 13, "TextMuted");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(label, column);
            calendar.Children.Add(label);
        }
        var start = _calendarMonth.AddDays(-((int)_calendarMonth.DayOfWeek + 6) % 7);
        for (var index = 0; index < 42; index++)
        {
            var cell = CalendarCell(start.AddDays(index));
            Grid.SetRow(cell, index / 7 + 1);
            Grid.SetColumn(cell, index % 7);
            calendar.Children.Add(cell);
        }
        stack.Children.Add(calendar);
        return ui.Card(stack, null);
    }

    private UIElement BuildContent()
    {
        if (string.Equals(settings().CurrentViewId, DiaryViewIds.Tags, StringComparison.Ordinal)) return BuildTagDetailContent();
        var entry = selectedEntry();
        if (entry is null) return ui.EmptyCard("请选择一篇日记。");
        var stack = new StackPanel { Spacing = 14 };
        var title = new Grid { RowSpacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        title.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        title.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(ui.Text(entry.Title, 30, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        var date = ui.Text(entry.CreatedAt.ToString("yyyy年M月d日 HH:mm"), 14, "TextSecondary");
        Grid.SetRow(date, 1);
        title.Children.Add(date);
        var titleActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var favorite = ui.IconButton("\uE734", entry.IsFavorite ? "取消收藏" : "收藏", entry.IsFavorite ? "Favorite" : "TextSecondary");
        favorite.Click += (_, _) => toggleFavorite(entry.Id);
        titleActions.Children.Add(favorite);
        var more = ui.IconButton("\uE712", "更多");
        more.Click += (_, _) => showEntryMenu(more, entry);
        titleActions.Children.Add(more);
        var close = ui.IconButton("\uE711", "关闭");
        close.Click += (_, _) => { setSelectedEntry(null); rebuild(); };
        titleActions.Children.Add(close);
        Grid.SetColumn(titleActions, 1);
        title.Children.Add(titleActions);
        var edit = ui.TextButton("\uE70F  编辑", "编辑日记", 14, "TextSecondary");
        edit.Click += async (_, _) => await showEditor(entry);
        Grid.SetColumn(edit, 1);
        Grid.SetRow(edit, 1);
        title.Children.Add(edit);
        stack.Children.Add(title);
        stack.Children.Add(BuildMetaCard(entry));
        stack.Children.Add(BuildCalendarCard(false));
        stack.Children.Add(BuildTodoLinksCard(entry));
        stack.Children.Add(BuildDetailActions(entry));
        return stack;
    }

    private UIElement BuildTagDetailContent()
    {
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(ui.Text("标签管理", 30, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        stack.Children.Add(ui.Text("为日记建立可复用的主题与颜色。", 14, "TextSecondary"));
        var guide = new StackPanel { Spacing = 10, Margin = new Thickness(16, 14, 16, 14) };
        guide.Children.Add(ui.Text("使用说明", 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        guide.Children.Add(ui.Text("新建标签后，可在快速记录工具栏和日记详情中选择；标签页顶部可按标签筛选日记。", 13, "TextSecondary"));
        guide.Children.Add(ui.Text("删除定义不会删除旧日记中的标签文本。", 13, "TextSecondary"));
        stack.Children.Add(ui.Card(guide, null));
        var palette = new StackPanel { Spacing = 10, Margin = new Thickness(16, 14, 16, 14) };
        palette.Children.Add(ui.Text("第一版配色 · 12 色", 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var colors = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var column = 0; column < 3; column++) colors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 4; row++) colors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < DiaryMetadata.TagColors.Count; index++)
        {
            var color = DiaryMetadata.TagColors[index];
            var swatch = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            swatch.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = theme.HexBrush(color.Hex), VerticalAlignment = VerticalAlignment.Center });
            swatch.Children.Add(ui.Text(color.Name, 12, "TextSecondary"));
            Grid.SetColumn(swatch, index % 3);
            Grid.SetRow(swatch, index / 3);
            colors.Children.Add(swatch);
        }
        palette.Children.Add(colors);
        stack.Children.Add(ui.Card(palette, null));
        return stack;
    }

    private FrameworkElement BuildMetaCard(DiaryEntry entry)
    {
        var stack = new StackPanel { Spacing = 0, Margin = new Thickness(16, 10, 16, 10) };
        stack.Children.Add(MetaRow("\uE76E", "心情", entry.Mood, "🙂"));
        stack.Children.Add(MetaRow("\uE787", "天气", entry.Weather, "⛅"));
        stack.Children.Add(MetaRow("\uE707", "地点", entry.Location, "\uE707"));
        stack.Children.Add(MetaTagRow(entry));
        return ui.Card(stack, null);
    }

    private FrameworkElement MetaRow(string glyph, string label, string value, string valueIcon)
    {
        var layout = new Grid { Height = 44 };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 17, Foreground = theme.Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center });
        var labelBlock = ui.Text(label, 14, "TextSecondary");
        Grid.SetColumn(labelBlock, 1);
        grid.Children.Add(labelBlock);
        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        valueRow.Children.Add(DiaryUiFactory.IsSegoeGlyph(valueIcon) ? new FontIcon { Glyph = valueIcon, FontSize = 16, Foreground = theme.Brush("TextSecondary") } : ui.EmojiText(valueIcon, 18));
        valueRow.Children.Add(ui.Text(value, 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        Grid.SetColumn(valueRow, 2);
        grid.Children.Add(valueRow);
        layout.Children.Add(grid);
        var divider = new Border { Height = 1, Background = theme.Brush("InnerDivider"), Margin = new Thickness(30, 0, 0, 0) };
        Grid.SetRow(divider, 1);
        layout.Children.Add(divider);
        return layout;
    }

    private FrameworkElement MetaTagRow(DiaryEntry entry)
    {
        var grid = new Grid { Height = 44, ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = "\uE8EC", FontSize = 17, Foreground = theme.Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center });
        var label = ui.Text("标签", 14, "TextSecondary");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tag in entry.Tags) row.Children.Add(ui.MetaTagPill(tag));
        var add = ui.TextButton("+", "编辑标签", 18, "TextPrimary");
        add.Click += async (_, _) => await showTagPicker(entry);
        row.Children.Add(add);
        Grid.SetColumn(row, 2);
        grid.Children.Add(row);
        return grid;
    }

    private Button CalendarCell(DateTime date)
    {
        var inMonth = date.Month == _calendarMonth.Month && date.Year == _calendarMonth.Year;
        var selected = _calendarDate?.Date == date.Date;
        var hasDot = data().Entries.Any(entry => entry.CreatedAt.LocalDateTime.Date == date.Date);
        var button = new Button
        {
            Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = selected ? theme.Brush("Accent") : DiaryUiFactory.TransparentBrush(), CornerRadius = new CornerRadius(14),
            Content = new TextBlock
            {
                Text = date.Day.ToString(), FontSize = 13,
                Foreground = selected ? theme.Brush("OnAccent") : inMonth ? theme.Brush("TextPrimary") : theme.Brush("TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        if (hasDot && !selected)
        {
            button.BorderBrush = theme.Brush("Accent");
            button.BorderThickness = new Thickness(1);
        }
        button.Click += (_, _) => SelectCalendarDate(date);
        return button;
    }

    private FrameworkElement BuildTodoLinksCard(DiaryEntry entry)
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(18, 16, 18, 16) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(ui.Text("关联的待办", 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var add = ui.TextButton("+ 添加待办", "添加待办", 14, "Accent");
        add.Click += async (_, _) => await showTodoPicker(entry);
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        stack.Children.Add(header);
        if (entry.TodoLinks.Count == 0) stack.Children.Add(ui.Text("没有已关联的待办。", 14, "TextSecondary"));
        else foreach (var link in entry.TodoLinks.Take(4)) stack.Children.Add(TodoLinkRow(link));
        return ui.Card(stack, null);
    }

    private FrameworkElement TodoLinkRow(DiaryTodoLink link)
    {
        var grid = new Grid { Height = 42 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 16, Height = 16, Stroke = theme.Brush("TextSecondary"), StrokeThickness = 1.4, VerticalAlignment = VerticalAlignment.Center });
        var title = ui.Text(link.TitleSnapshot, 15, "TextPrimary");
        title.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var visual = DiaryUiFactory.TagVisualFor(link.ListNameSnapshot);
        var pill = ui.TodoStatusPill(link.ListNameSnapshot, visual.BackgroundKey, visual.ForegroundKey);
        Grid.SetColumn(pill, 2);
        grid.Children.Add(pill);
        return new Border { BorderBrush = theme.Brush("InnerDivider"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private FrameworkElement BuildDetailActions(DiaryEntry entry)
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 18, 0, 0) };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var favorite = ui.OutlineButton("\uE734", entry.IsFavorite ? "已收藏" : "收藏", "Favorite");
        favorite.Click += (_, _) => toggleFavorite(entry.Id);
        grid.Children.Add(favorite);
        var export = ui.OutlineButton("\uE898", "导出", "TextPrimary");
        export.Click += async (_, _) => await exportEntry(entry);
        Grid.SetColumn(export, 1);
        grid.Children.Add(export);
        var delete = ui.OutlineButton("\uE74D", "删除", "Danger");
        delete.Click += async (_, _) => await deleteEntry(entry);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        return grid;
    }

    private void MoveCalendarMonth(int offset)
    {
        _calendarMonth = _calendarMonth.AddMonths(offset);
        rebuild();
    }

    private void SelectCalendarDate(DateTime date)
    {
        _calendarDate = _calendarDate?.Date == date.Date ? null : date.Date;
        rebuild();
    }
}
