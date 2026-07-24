using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryEntryListPresenter(
    Func<DiaryData> data,
    Func<bool> isTimelineView,
    Func<string> timelineNotebookId,
    Func<DiaryEntry?> selectedEntry,
    Action<DiaryEntry> setSelectedEntry,
    IDictionary<DateTime, FrameworkElement> timelineDateAnchors,
    DiaryThemePalette theme,
    DiaryUiFactory ui,
    Func<DiaryEntry, Task> showEditor,
    Action<string> toggleFavorite,
    Action<Button, DiaryEntry> showEntryMenu,
    Action rebuild)
{
    private const double TimelineColumnWidth = 104;
    private const double TimelineRowHeight = 110;
    private const double TimelineCardMinHeight = 108;

    public FrameworkElement Build(IReadOnlyList<DiaryEntry> entries)
    {
        var timelineView = isTimelineView();
        var rowHeight = timelineView ? 92 : TimelineRowHeight;
        var grid = new Grid { Margin = new Thickness(0, timelineView ? 2 : 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimelineColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Background = theme.Brush("TimelineLine"), Margin = new Thickness(22, timelineView ? 26 : 30, 0, 80) });
        var times = new StackPanel { Spacing = timelineView ? 10 : 16 };
        var cards = new StackPanel { Spacing = timelineView ? 10 : 13 };
        DateTime? previousDate = null;
        var notebookMode = timelineNotebookId();
        foreach (var entry in entries)
        {
            var selected = selectedEntry()?.Id == entry.Id;
            var entryDate = entry.CreatedAt.LocalDateTime.Date;
            var showDate = timelineView && previousDate != entryDate;
            previousDate = entryDate;
            var dateHeaderHeight = showDate ? 28 : 0;
            var timeRow = new Grid
            {
                Height = rowHeight + dateHeaderHeight,
                Children =
                {
                    new Microsoft.UI.Xaml.Shapes.Ellipse
                    {
                        Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(17, (timelineView ? 22 : 25) + dateHeaderHeight, 0, 0),
                        Fill = selected ? theme.Brush("Accent") : theme.Brush("TimelineDot")
                    },
                    new TextBlock
                    {
                        Text = entry.CreatedAt.ToString("HH:mm"), Margin = new Thickness(44, (timelineView ? 16 : 19) + dateHeaderHeight, 0, 0),
                        Foreground = selected ? theme.Brush("Accent") : theme.Brush("TextSecondary"), FontSize = 14
                    }
                }
            };
            times.Children.Add(timeRow);
            var card = BuildCard(entry, selected, timelineView && string.Equals(notebookMode, DiaryTimeline.AllNotebooksId, StringComparison.Ordinal), timelineView);
            if (showDate && timelineView)
            {
                var cardBlock = new Grid();
                cardBlock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(dateHeaderHeight) });
                cardBlock.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cardBlock.Children.Add(new TextBlock { Text = DateHeading(entryDate), Foreground = theme.Brush("TextSecondary"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetRow(card, 1);
                cardBlock.Children.Add(card);
                timelineDateAnchors[entryDate] = cardBlock;
                cards.Children.Add(cardBlock);
            }
            else cards.Children.Add(card);
        }
        grid.Children.Add(times);
        Grid.SetColumn(cards, 1);
        grid.Children.Add(cards);
        return grid;
    }

    private FrameworkElement BuildCard(DiaryEntry entry, bool selected, bool showNotebook, bool compact)
    {
        var border = new Border
        {
            MinHeight = compact ? 82 : TimelineCardMinHeight, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(selected ? 1.5 : 1),
            BorderBrush = selected ? theme.Brush("Accent") : theme.Brush("CardStroke"),
            Background = selected ? theme.Brush("SelectedCard") : theme.CardBackground(),
            Padding = compact ? new Thickness(18, 10, 14, 10) : new Thickness(22, 14, 18, 15)
        };
        border.Tapped += async (_, _) =>
        {
            setSelectedEntry(entry);
            if (isTimelineView()) await showEditor(entry); else rebuild();
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(ui.Text(entry.Title, compact ? 17 : 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var favorite = ui.TimelineIconButton("\uE734", entry.IsFavorite ? "取消收藏" : "收藏", entry.IsFavorite ? "Favorite" : "TextSecondary");
        favorite.Click += (_, _) => toggleFavorite(entry.Id);
        icons.Children.Add(favorite);
        var more = ui.TimelineIconButton("\uE712", "更多");
        more.Click += (_, _) => showEntryMenu(more, entry);
        icons.Children.Add(more);
        Grid.SetColumn(icons, 1);
        grid.Children.Add(icons);
        var snippet = ui.Text(Snippet(entry.Body), compact ? 14 : 15, "TextSecondary");
        snippet.Margin = new Thickness(0, compact ? 4 : 6, 0, 0);
        Grid.SetRow(snippet, 1);
        grid.Children.Add(snippet);
        var notebook = showNotebook ? data().Notebooks.FirstOrDefault(candidate => candidate.Id == entry.NotebookId) : null;
        var tags = ui.TagRow(entry.Tags, notebook);
        tags.Margin = new Thickness(0, compact ? 5 : 7, 0, 0);
        Grid.SetRow(tags, 2);
        grid.Children.Add(tags);
        border.Child = grid;
        return border;
    }

    private static string DateHeading(DateTime date)
    {
        var prefix = date.Date == DiaryRuntime.Today ? "今天 · " : date.Date == DiaryRuntime.Today.AddDays(-1) ? "昨天 · " : string.Empty;
        return $"{prefix}{date.ToString("M月d日 ddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"))}";
    }

    private static string Snippet(string body)
    {
        var line = body.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(line) ? "还没有正文内容。" : line.Length <= 52 ? line : $"{line[..52]}...";
    }
}
