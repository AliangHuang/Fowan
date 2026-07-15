using Fowan.Windows.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxControlFactory(
    Func<string, Brush> themeBrush,
    Func<ToolStatus, SolidColorBrush> statusBrush)
{
    public static Button IconButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 40, Height = 40, Padding = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 18 }
        };
        Describe(button, label);
        return button;
    }

    public Button HeaderPillButton(string text, string? leadingGlyph = null, bool showStatusDot = false,
        string? trailingGlyph = null, string? automationName = null)
    {
        var button = new Button
        {
            Height = 42, MinWidth = 132, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            BorderBrush = themeBrush("CardStrokeColorDefaultBrush"), Background = themeBrush("ControlSurface"),
            Padding = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = HeaderPillContent(text, leadingGlyph, showStatusDot, trailingGlyph)
        };
        Describe(button, automationName ?? text);
        return button;
    }

    public Border HeaderPill(string text, string? leadingGlyph = null, bool showStatusDot = false, string? trailingGlyph = null) => new()
    {
        Height = 42, MinWidth = 132, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
        BorderBrush = themeBrush("CardStrokeColorDefaultBrush"), Background = themeBrush("ControlSurface"),
        Padding = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = HeaderPillContent(text, leadingGlyph, showStatusDot, trailingGlyph)
    };

    public Button HeaderIconButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 42, Height = 42, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Content = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = themeBrush("TextFillColorSecondaryBrush") }
        };
        Describe(button, label);
        return button;
    }

    public Button SmallToolbarButton(string glyph, string label, bool selected)
    {
        var button = new Button
        {
            Width = 42, Height = 36, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = themeBrush("CardStrokeColorDefaultBrush"),
            Background = themeBrush(selected ? "SelectedNavigationBackground" : "ControlSurface"),
            Content = new FontIcon
            {
                Glyph = glyph, FontSize = 17,
                Foreground = themeBrush(selected ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush")
            }
        };
        Describe(button, label);
        return button;
    }

    public void ApplyFlatTextBoxStyle(TextBox textBox)
    {
        var transparent = new SolidColorBrush(Colors.Transparent);
        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"
        }) textBox.Resources[key] = transparent;
        textBox.Resources["TextControlForeground"] = themeBrush("TextFillColorPrimaryBrush");
        textBox.Resources["TextControlForegroundFocused"] = themeBrush("TextFillColorPrimaryBrush");
        textBox.Resources["TextControlPlaceholderForeground"] = themeBrush("TextFillColorSecondaryBrush");
        textBox.Resources["TextControlPlaceholderForegroundFocused"] = themeBrush("TextFillColorSecondaryBrush");
    }

    public Border IconTile(ToolCard tool, double size, double glyphSize)
    {
        UIElement icon = tool.Id == "ai-chat"
            ? new Image
            {
                Width = size - 8, Height = size - 8,
                Source = new BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-ai-chat-app-icon-256.png"))),
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new FontIcon
            {
                Glyph = tool.IconGlyph, FontSize = glyphSize, Foreground = ToolAccentBrush(tool),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        return new Border
        {
            Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = themeBrush("CardStrokeColorDefaultBrush"), Background = themeBrush("IconTileBackground"),
            Child = icon
        };
    }

    private StackPanel HeaderPillContent(string text, string? leadingGlyph, bool showStatusDot, string? trailingGlyph)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        if (showStatusDot) stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8, Height = 8, Fill = statusBrush(ToolStatus.Available), VerticalAlignment = VerticalAlignment.Center
        });
        if (!string.IsNullOrEmpty(leadingGlyph)) stack.Children.Add(new FontIcon
        {
            Glyph = leadingGlyph, FontSize = 15, Foreground = themeBrush("TextFillColorSecondaryBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = text, FontSize = 15, Foreground = themeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(trailingGlyph)) stack.Children.Add(new FontIcon
        {
            Glyph = trailingGlyph, FontSize = 12, Foreground = themeBrush("TextFillColorSecondaryBrush")
        });
        return stack;
    }

    private static Brush ToolAccentBrush(ToolCard tool) => new SolidColorBrush(tool.Id switch
    {
        "todo" => ColorHelper.FromArgb(255, 127, 92, 255), "diary" => ColorHelper.FromArgb(255, 47, 128, 255),
        "notes" => ColorHelper.FromArgb(255, 255, 174, 36), "knowledge" => ColorHelper.FromArgb(255, 45, 194, 154),
        "files" => ColorHelper.FromArgb(255, 47, 140, 255), "global-search" => ColorHelper.FromArgb(255, 156, 92, 255),
        "workflows" => ColorHelper.FromArgb(255, 123, 202, 91), "ai" => ColorHelper.FromArgb(255, 177, 123, 226),
        "plugins" => ColorHelper.FromArgb(255, 255, 125, 72), "settings" => ColorHelper.FromArgb(255, 86, 145, 255),
        "diagnostics" => ColorHelper.FromArgb(255, 42, 190, 178), _ => ColorHelper.FromArgb(255, 38, 128, 235)
    });

    private static Uri FileUri(string path) => new UriBuilder
    {
        Scheme = Uri.UriSchemeFile, Path = Path.GetFullPath(path)
    }.Uri;

    private static void Describe(FrameworkElement element, string label)
    {
        ToolTipService.SetToolTip(element, label);
        AutomationProperties.SetName(element, label);
    }
}
