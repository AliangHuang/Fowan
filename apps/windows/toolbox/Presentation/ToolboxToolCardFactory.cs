using Fowan.Windows.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxToolCardFactory(
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    Func<string> selectedToolId,
    Func<ToolCard, double, double, Border> iconTile,
    Func<ToolCard, bool> canPin,
    Func<ToolCard, bool> isPinned,
    Func<ToolCard, string> pinLabel,
    Action<ToolCard> togglePin,
    Func<ToolCard, Task> handleClick,
    Action<ToolCard> selectTool,
    Func<ToolCard, Task> executePrimary)
{
    public FrameworkElement BuildCard(ToolCard tool)
    {
        var selected = tool.Id == selectedToolId();
        var button = CreateCardButton(140);
        AttachActivation(button, tool);
        var border = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = selected ? new Thickness(2) : new Thickness(1),
            BorderBrush = themeBrush(selected ? "AccentStrokeColorDefaultBrush" : "CardStrokeColorDefaultBrush"),
            Background = themeBrush("CardBackgroundFillColorDefaultBrush"), Padding = new Thickness(18)
        };
        var stack = new StackPanel { Spacing = 11 };
        stack.Children.Add(iconTile(tool, 58, 28));
        stack.Children.Add(new TextBlock
        {
            Text = localize(tool.NameKey), FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = themeBrush("TextFillColorPrimaryBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        stack.Children.Add(StatusPill(tool.Status));
        border.Child = stack;
        ApplyHover(button, border, selected);
        button.Content = border;
        return HostWithPin(button, tool, new Thickness(0, 10, 10, 0));
    }

    public FrameworkElement BuildListItem(ToolCard tool)
    {
        var selected = tool.Id == selectedToolId();
        var button = CreateCardButton(86);
        AttachActivation(button, tool);
        var border = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = selected ? new Thickness(2) : new Thickness(1),
            BorderBrush = themeBrush(selected ? "AccentStrokeColorDefaultBrush" : "CardStrokeColorDefaultBrush"),
            Background = themeBrush("CardBackgroundFillColorDefaultBrush"), Padding = new Thickness(14)
        };
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconTile(tool, 48, 23));
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = localize(tool.NameKey), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = themeBrush("TextFillColorPrimaryBrush")
        });
        text.Children.Add(new TextBlock
        {
            Text = localize(tool.DescriptionKey), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = themeBrush("TextFillColorSecondaryBrush")
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var right = new StackPanel
        {
            Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = canPin(tool) ? new Thickness(0, 0, 40, 0) : new Thickness(0)
        };
        right.Children.Add(StatusPill(tool.Status));
        if (selected) right.Children.Add(new FontIcon
        {
            Glyph = "\uE73E", FontSize = 13, Foreground = themeBrush("AccentTextFillColorPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        border.Child = grid;
        ApplyHover(button, border, selected);
        button.Content = border;
        return HostWithPin(button, tool, new Thickness(0, 12, 12, 0));
    }

    public FrameworkElement StatusPill(ToolStatus status)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8, Height = 8, Fill = StatusBrush(status), VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = StatusText(status), Foreground = themeBrush("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14
        });
        return stack;
    }

    private Button CreateCardButton(double minHeight)
    {
        var button = new Button
        {
            Padding = new Thickness(0), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent), MinHeight = minHeight,
            UseSystemFocusVisuals = false, IsDoubleTapEnabled = true
        };
        var transparent = new SolidColorBrush(Colors.Transparent);
        foreach (var key in new[]
        {
            "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled",
            "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled",
            "FocusVisualPrimaryBrush", "FocusVisualSecondaryBrush"
        }) button.Resources[key] = transparent;
        return button;
    }

    private void AttachActivation(Button button, ToolCard tool)
    {
        button.Click += async (_, _) => await handleClick(tool);
        button.DoubleTapped += async (_, args) => { args.Handled = true; selectTool(tool); await executePrimary(tool); };
        button.KeyDown += async (_, args) =>
        {
            if (args.Key != global::Windows.System.VirtualKey.Enter || tool.Status != ToolStatus.Available) return;
            args.Handled = true;
            await executePrimary(tool);
        };
        AutomationProperties.SetName(button, string.Format(
            localize("Accessibility_ToolCard"), localize(tool.NameKey), StatusText(tool.Status), localize(tool.DescriptionKey)));
    }

    private Grid HostWithPin(Button button, ToolCard tool, Thickness margin)
    {
        var host = new Grid();
        host.Children.Add(button);
        if (canPin(tool)) host.Children.Add(BuildPinButton(tool, margin));
        return host;
    }

    private Button BuildPinButton(ToolCard tool, Thickness margin)
    {
        var pinned = isPinned(tool);
        var label = pinLabel(tool);
        var button = new Button
        {
            Width = 30, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = themeBrush(pinned ? "AccentStrokeColorDefaultBrush" : "CardStrokeColorDefaultBrush"),
            Background = themeBrush(pinned ? "SelectedNavigationBackground" : "ControlSurface"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = margin,
            Content = new FontIcon
            {
                Glyph = "\uE840", FontSize = 14,
                Foreground = themeBrush(pinned ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush")
            }
        };
        button.Resources["ButtonBackground"] = button.Background;
        button.Resources["ButtonBackgroundPointerOver"] = themeBrush("ToolCardHoverBackgroundBrush");
        button.Resources["ButtonBackgroundPressed"] = themeBrush("SelectedNavigationBackground");
        button.Resources["ButtonBorderBrush"] = button.BorderBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = themeBrush("ToolCardHoverStrokeBrush");
        button.Resources["ButtonBorderBrushPressed"] = themeBrush("AccentStrokeColorDefaultBrush");
        button.Resources["FocusVisualPrimaryBrush"] = themeBrush("ToolCardHoverStrokeBrush");
        button.Resources["FocusVisualSecondaryBrush"] = new SolidColorBrush(Colors.Transparent);
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, $"{label}: {localize(tool.NameKey)}");
        button.Click += (_, _) => togglePin(tool);
        return button;
    }

    private void ApplyHover(Button button, Border border, bool selected)
    {
        var normalBorder = themeBrush(selected ? "AccentStrokeColorDefaultBrush" : "CardStrokeColorDefaultBrush");
        var hoverBorder = themeBrush("ToolCardHoverStrokeBrush");
        var normalBackground = themeBrush("CardBackgroundFillColorDefaultBrush");
        var hoverBackground = themeBrush("ToolCardHoverBackgroundBrush");
        void Hover() { border.BorderBrush = hoverBorder; border.Background = hoverBackground; }
        void Normal() { border.BorderBrush = normalBorder; border.Background = normalBackground; }
        Normal();
        var enter = new PointerEventHandler((_, _) => Hover());
        var exit = new PointerEventHandler((_, _) => Normal());
        button.AddHandler(UIElement.PointerEnteredEvent, enter, true);
        button.AddHandler(UIElement.PointerMovedEvent, enter, true);
        button.AddHandler(UIElement.PointerExitedEvent, exit, true);
        border.AddHandler(UIElement.PointerEnteredEvent, enter, true);
        border.AddHandler(UIElement.PointerMovedEvent, enter, true);
        border.AddHandler(UIElement.PointerExitedEvent, exit, true);
    }

    public static SolidColorBrush StatusBrush(ToolStatus status) => new(status switch
    {
        ToolStatus.Available => Colors.LimeGreen,
        ToolStatus.Disabled => Colors.DimGray,
        ToolStatus.RequiresEngine => Colors.Orange,
        ToolStatus.RequiresSignIn => Colors.DodgerBlue,
        _ => Colors.DarkGray
    });

    public string StatusText(ToolStatus status) => localize(status switch
    {
        ToolStatus.Available => "Status_Available",
        ToolStatus.Disabled => "Status_Disabled",
        ToolStatus.RequiresEngine => "Status_RequiresEngine",
        ToolStatus.RequiresSignIn => "Status_RequiresSignIn",
        _ => "Status_Planned"
    });
}
