using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryMainPresenter(
    Func<DiaryData> data,
    Func<DiarySettings> settings,
    DiaryThemePalette theme,
    DiaryUiFactory ui,
    DiaryComposerPresenter composer,
    DiaryEntryListPresenter entryList,
    DiaryDetailPresenter detail,
    Func<IEnumerable<DiaryEntry>> filteredEntries,
    Func<string> pageTitle,
    Func<string> pageListTitle,
    Func<ComboBox> buildTimelineNotebookSelector,
    Action<bool> beginDraft,
    Action<Button> showHeaderMenu,
    Func<Task> showCreateTagDialog,
    Func<DiaryTagDefinition, Task> showEditTagDialog)
{
    public FrameworkElement BuildColumn() => new Border
    {
        Background = theme.Brush("AppBackground"), BorderBrush = theme.Brush("Divider"),
        BorderThickness = new Thickness(0, 0, 1, 0),
        Child = new ScrollViewer
        {
            Padding = new Thickness(30, 58, 30, 24), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, Content = BuildContent()
        }
    };

    private UIElement BuildContent()
    {
        if (string.Equals(settings().CurrentViewId, DiaryViewIds.Tags, StringComparison.Ordinal)) return BuildTagManagementContent();
        var stack = new StackPanel { Spacing = 16 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
        title.Children.Add(ui.Text(pageTitle(), 28, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        title.Children.Add(ui.Text(DiaryRuntime.Today.ToString("yyyy年M月d日　dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), 16, "TextSecondary"));
        header.Children.Add(title);
        if (string.Equals(settings().CurrentViewId, DiaryViewIds.Timeline, StringComparison.Ordinal))
        {
            var timelineNotebook = buildTimelineNotebookSelector();
            timelineNotebook.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(timelineNotebook, 1);
            header.Children.Add(timelineNotebook);
        }
        var create = ui.PrimaryButton("\uE710", "新建日记");
        create.Click += (_, _) => beginDraft(true);
        Grid.SetColumn(create, 2);
        header.Children.Add(create);
        var more = ui.IconButton("\uE712", "更多");
        more.Margin = new Thickness(0, 0, 12, 0);
        more.Click += (_, _) => showHeaderMenu(more);
        Grid.SetColumn(more, 3);
        header.Children.Add(more);
        stack.Children.Add(header);
        stack.Children.Add(composer.BuildMoodStrip());
        stack.Children.Add(composer.BuildEditorCard());
        if (string.Equals(settings().CurrentViewId, DiaryViewIds.Calendar, StringComparison.Ordinal)) stack.Children.Add(detail.BuildCalendarCard(true));
        var entries = filteredEntries().ToList();
        var listHeader = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listHeader.Children.Add(ui.Text($"{pageListTitle()} · {entries.Count} 篇日记", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var sort = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new FontIcon { Glyph = "\uE8CB", FontSize = 15, Foreground = theme.Brush("TextSecondary") }, ui.Text("按时间排序", 14, "TextSecondary") }
        };
        Grid.SetColumn(sort, 1);
        listHeader.Children.Add(sort);
        stack.Children.Add(listHeader);
        stack.Children.Add(entries.Count == 0 ? ui.EmptyCard("当前视图还没有日记。") : entryList.Build(entries));
        return stack;
    }

    private UIElement BuildTagManagementContent()
    {
        var currentData = data();
        var stack = new StackPanel { Spacing = 16 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
        title.Children.Add(ui.Text("标签", 28, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        title.Children.Add(ui.Text("维护标签、配色与日记筛选", 16, "TextSecondary"));
        header.Children.Add(title);
        var create = ui.PrimaryButton("\uE710", "新建标签");
        create.Click += async (_, _) => await showCreateTagDialog();
        Grid.SetColumn(create, 1);
        header.Children.Add(create);
        stack.Children.Add(header);
        stack.Children.Add(composer.BuildTagFilters());
        var catalog = new StackPanel { Spacing = 0, Margin = new Thickness(18, 14, 18, 14) };
        catalog.Children.Add(ui.Text($"标签表 · {currentData.TagCatalog.Count} 个", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        catalog.Children.Add(ui.Text("删除标签定义不会删除历史日记中的标签文字。", 12, "TextMuted"));
        foreach (var tag in currentData.TagCatalog.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)) catalog.Children.Add(BuildTagCatalogRow(tag));
        stack.Children.Add(ui.Card(catalog, null));
        var entries = filteredEntries().ToList();
        stack.Children.Add(ui.Text($"{(string.IsNullOrWhiteSpace(composer.TagFilter) ? "全部标签日记" : composer.TagFilter)} · {entries.Count} 篇日记", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        stack.Children.Add(entries.Count == 0 ? ui.EmptyCard("当前标签下还没有日记。") : entryList.Build(entries));
        return stack;
    }

    private FrameworkElement BuildTagCatalogRow(DiaryTagDefinition tag)
    {
        var row = new Grid { Height = 48, Margin = new Thickness(0, 10, 0, 0), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var color = DiaryMetadata.TagColor(tag.ColorId);
        row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = theme.HexBrush(color.Hex), VerticalAlignment = VerticalAlignment.Center });
        var pill = ui.MetaTagPill(tag.Name);
        pill.VerticalAlignment = VerticalAlignment.Center;
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(pill, 1);
        row.Children.Add(pill);
        var count = data().Entries.Count(entry => entry.Tags.Any(name => string.Equals(name, tag.Name, StringComparison.OrdinalIgnoreCase)));
        var usage = ui.Text($"{count} 篇", 13, "TextSecondary");
        Grid.SetColumn(usage, 2);
        row.Children.Add(usage);
        var edit = ui.TextButton("编辑", "编辑标签", 13, "Accent");
        edit.Click += async (_, _) => await showEditTagDialog(tag);
        Grid.SetColumn(edit, 3);
        row.Children.Add(edit);
        return row;
    }
}
