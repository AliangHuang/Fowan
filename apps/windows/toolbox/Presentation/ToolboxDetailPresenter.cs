using Fowan.Windows.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxDetailPresenter(
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    Func<ToolCard> selectedTool,
    Func<bool> hasVisibleTools,
    Func<string> searchText,
    Func<int> captureCount,
    Func<ToolCard, double, double, Border> iconTile,
    Func<ToolStatus, FrameworkElement> statusPill,
    Func<ToolCard, bool> canPin,
    Func<ToolCard, string> pinLabel,
    Action<ToolCard> togglePin,
    Func<ToolCard, Task> executePrimary,
    Func<string, string> categoryNameKey,
    Action clearSearch)
{
    public Border BuildPanel()
    {
        var panel = new Border
        {
            BorderBrush = themeBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Background = themeBrush("DetailBackground"),
            Padding = new Thickness(28, 62, 28, 28)
        };
        panel.Child = BuildCurrent();
        return panel;
    }

    public UIElement BuildCurrent() => hasVisibleTools() ? BuildDetail() : BuildEmpty();

    private UIElement BuildEmpty()
    {
        var stack = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(new Border
        {
            Width = 82, Height = 82, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = themeBrush("CardStrokeColorDefaultBrush"),
            Background = themeBrush("IconTileBackground"),
            Child = new FontIcon
            {
                Glyph = "\uE721", FontSize = 34, Foreground = themeBrush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        });
        stack.Children.Add(new TextBlock
        {
            Text = localize("Search_NoResultsTitle"), FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = themeBrush("TextFillColorPrimaryBrush"), Margin = new Thickness(0, 6, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = localize("Search_NoResultsDescription"), FontSize = 14,
            Foreground = themeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.WrapWholeWords,
            LineHeight = 21
        });
        var clear = new Button
        {
            Content = localize("Search_Clear"), IsEnabled = !string.IsNullOrWhiteSpace(searchText()),
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 12, 16, 12),
            Background = themeBrush("ControlSurface"), Foreground = themeBrush("TextFillColorPrimaryBrush"),
            BorderBrush = themeBrush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 16, 0, 0)
        };
        clear.Click += (_, _) => clearSearch();
        stack.Children.Add(clear);
        return stack;
    }

    private UIElement BuildDetail()
    {
        var tool = selectedTool();
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(iconTile(tool, 82, 38));
        stack.Children.Add(new TextBlock
        {
            Text = localize(tool.NameKey), FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = themeBrush("TextFillColorPrimaryBrush"), Margin = new Thickness(0, 6, 0, 0)
        });
        stack.Children.Add(statusPill(tool.Status));
        stack.Children.Add(new Border
        {
            Height = 1, Background = themeBrush("DividerStrokeColorDefaultBrush"),
            Margin = new Thickness(0, 2, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = localize(tool.DescriptionKey), FontSize = 14,
            Foreground = themeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.WrapWholeWords,
            LineHeight = 21
        });
        var primary = new Button
        {
            Content = localize(tool.PrimaryAction.LabelKey), IsEnabled = tool.PrimaryAction.Enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 12, 16, 12),
            Background = themeBrush(tool.Status == ToolStatus.Available ? "AccentFillColorDefaultBrush" : "DisabledButtonBackground"),
            Foreground = themeBrush("TextOnAccentFillColorPrimaryBrush"), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 20, 0, 0)
        };
        primary.Click += async (_, _) => await executePrimary(tool);
        stack.Children.Add(primary);
        var secondary = new Button
        {
            Content = pinLabel(tool), IsEnabled = canPin(tool), HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 10, 16, 10), Background = new SolidColorBrush(Colors.Transparent),
            Foreground = themeBrush("TextFillColorPrimaryBrush"), BorderBrush = themeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
        };
        secondary.Click += (_, _) => togglePin(tool);
        stack.Children.Add(secondary);
        stack.Children.Add(BuildRows(tool));
        return stack;
    }

    private UIElement BuildRows(ToolCard tool)
    {
        var grid = new Grid { Margin = new Thickness(0, 18, 0, 0), RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddRow(grid, 0, localize("Detail_Category"), localize(categoryNameKey(tool.CategoryId)));
        AddRow(grid, 1, localize("Detail_Version"), string.IsNullOrWhiteSpace(tool.Version) ? "-" : tool.Version);
        AddRow(grid, 2, localize("Detail_Updated"), string.IsNullOrWhiteSpace(tool.UpdatedAt) ? "-" : tool.UpdatedAt);
        AddRow(grid, 3, localize("Detail_Publisher"), "Fowan");
        AddRow(grid, 4, localize("Detail_RequiredCapabilities"), tool.RequiredCapabilities.Count == 0 ? "-" : string.Join(", ", tool.RequiredCapabilities));
        AddRow(grid, 5, localize("Detail_RecentActivity"), captureCount() == 0 ? localize("Detail_NoRecentActivity") : $"{captureCount()} capture(s)");
        return grid;
    }

    private static void AddRow(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new TextBlock { Text = label, Foreground = new SolidColorBrush(Colors.Gray), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);
        var valueBlock = new TextBlock { Text = value, TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 };
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
    }
}
