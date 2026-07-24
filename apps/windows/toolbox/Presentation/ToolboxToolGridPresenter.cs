using Fowan.Windows.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxToolGridPresenter(
    Func<Grid> toolGrid,
    Func<TextBlock> resultSummary,
    Func<Border> detailPanel,
    Func<IEnumerable<ToolCard>> currentTools,
    Func<ToolCard> selectedTool,
    Action<ToolCard> setSelectedTool,
    Action<bool> setHasVisibleTools,
    Func<bool> isGridView,
    Func<ToolCard, FrameworkElement> buildCard,
    Func<ToolCard, FrameworkElement> buildListItem,
    Func<UIElement> buildDetail,
    Func<string, string> localize,
    Func<string, Brush> themeBrush)
{
    public void Refresh()
    {
        var grid = toolGrid();
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        var tools = currentTools().ToList();
        setHasVisibleTools(tools.Count > 0);
        resultSummary().Text = string.Format(localize("Search_ResultCount"), tools.Count);
        if (tools.Count == 0)
        {
            var empty = new StackPanel { Spacing = 8, Margin = new Thickness(4, 20, 0, 0) };
            empty.Children.Add(new TextBlock
            {
                Text = localize("Empty_NoTools"), FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = themeBrush("TextFillColorPrimaryBrush")
            });
            empty.Children.Add(new TextBlock
            {
                Text = localize("Search_NoResultsDescription"), FontSize = 14,
                Foreground = themeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.WrapWholeWords
            });
            grid.Children.Add(empty);
            RefreshDetail();
            return;
        }
        if (tools.All(tool => tool.Id != selectedTool().Id)) setSelectedTool(tools[0]);
        RefreshDetail();
        var columns = isGridView() ? 3 : 1;
        for (var index = 0; index < columns; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = (int)Math.Ceiling(tools.Count / (double)columns);
        for (var index = 0; index < rows; index++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < tools.Count; index++)
        {
            var card = isGridView() ? buildCard(tools[index]) : buildListItem(tools[index]);
            Grid.SetColumn(card, index % columns);
            Grid.SetRow(card, index / columns);
            grid.Children.Add(card);
        }
    }

    private void RefreshDetail()
    {
        if (detailPanel().Child is not null) detailPanel().Child = buildDetail();
    }
}
