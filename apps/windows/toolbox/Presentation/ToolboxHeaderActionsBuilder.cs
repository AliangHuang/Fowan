using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxHeaderActionsBuilder(
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    ToolboxControlFactory controls,
    Func<string> sortLabel,
    Func<int> sortMode,
    Action<int> setSortMode)
{
    public Button BuildEngineStatusButton()
    {
        var button = controls.HeaderPillButton(
            localize("Engine_Online"), null, true, "\uE70D", localize("Engine_StatusDetails"));
        button.MinWidth = 142;
        var stack = new StackPanel { Width = 300, Spacing = 12, Padding = new Thickness(2) };
        stack.Children.Add(new TextBlock
        {
            Text = localize("Engine_StatusTitle"), FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = themeBrush("TextFillColorPrimaryBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = localize("Engine_MockDescription"), TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = themeBrush("TextFillColorSecondaryBrush"), LineHeight = 20
        });
        stack.Children.Add(BuildStatusRow(localize("Engine_State"), localize("Engine_Online")));
        stack.Children.Add(BuildStatusRow(localize("Diagnostics_ProtocolVersion"), localize("Mock_ProtocolVersion")));
        stack.Children.Add(BuildStatusRow(localize("Diagnostics_Capabilities"), localize("Mock_Capabilities")));
        var flyout = new Flyout { Content = stack };
        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    public Button BuildSortButton()
    {
        var button = controls.HeaderPillButton(sortLabel(), null, false, "\uE70D", localize("Sort_Tooltip"));
        button.MinWidth = 132;
        var flyout = new MenuFlyout();
        AddSortItem(flyout, 0, "Sort_Name");
        AddSortItem(flyout, 1, "Sort_Status");
        AddSortItem(flyout, 2, "Sort_Category");
        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private void AddSortItem(MenuFlyout flyout, int mode, string labelKey)
    {
        var item = new MenuFlyoutItem
        {
            Text = localize(labelKey), Icon = sortMode() == mode ? new FontIcon { Glyph = "\uE73E" } : null
        };
        item.Click += (_, _) => setSortMode(mode);
        flyout.Items.Add(item);
    }

    private UIElement BuildStatusRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, Foreground = themeBrush("TextFillColorSecondaryBrush") });
        var valueBlock = new TextBlock
        {
            Text = value, TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = themeBrush("TextFillColorPrimaryBrush")
        };
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
        return grid;
    }
}
