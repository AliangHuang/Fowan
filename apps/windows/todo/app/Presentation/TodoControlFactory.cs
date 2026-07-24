using Fowan.Todo.Shared.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoControlFactory(TodoThemePalette palette)
{
    public UIElement EmptyState(string text) => new Border
    {
        MinHeight = 132, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
        BorderBrush = palette.Brush(0xE1EAED), Background = palette.Brush(0xFFFFFF),
        Child = new TextBlock
        {
            Text = text, FontSize = 15, Foreground = palette.SecondaryText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    public TextBlock SectionLabel(string text) => new()
    {
        Text = text, Margin = new Thickness(10, 0, 0, 4), FontSize = 12,
        FontWeight = FontWeights.SemiBold, Foreground = palette.SecondaryText
    };

    public TextBlock FieldLabel(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Foreground = palette.SecondaryText, Margin = new Thickness(0, 2, 0, -8)
    };

    public Button PillButton(string text, string glyph)
    {
        var button = new Button
        {
            Height = 38, MinWidth = 112, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = palette.Brush(0xDCE7EA),
            Background = palette.Brush(0xFFFFFF), Padding = new Thickness(12, 0, 12, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 15, Foreground = palette.Accent },
                    new TextBlock
                    {
                        Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold,
                        Foreground = palette.Text, VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        return button;
    }

    public Button PrimaryButton(string text, string glyph)
    {
        var button = PillButton(text, glyph);
        button.Background = palette.Accent;
        button.BorderBrush = palette.Accent;
        SetContentColors(button, TodoThemePalette.PureWhite, TodoThemePalette.PureWhite);
        return button;
    }

    public void ConfigureStableSecondaryButtonStates(Button button)
    {
        var background = TodoThemePalette.Solid(palette.IsDark ? 0x202A34u : 0xF1F6F8u);
        var hoverBackground = TodoThemePalette.Solid(palette.IsDark ? 0x293844u : 0xE5EEF1u);
        var pressedBackground = TodoThemePalette.Solid(palette.IsDark ? 0x314550u : 0xD8E5E9u);
        var border = TodoThemePalette.Solid(palette.IsDark ? 0x3A4854u : 0xC7D8DEu);
        var hoverBorder = TodoThemePalette.Solid(palette.IsDark ? 0x4B5D69u : 0xAFC7CEu);
        var pressedBorder = TodoThemePalette.Solid(palette.IsDark ? 0x5A707Du : 0x94B5BEu);
        var foreground = TodoThemePalette.Solid(palette.IsDark ? 0xEEF3F8u : 0x17242Au);
        var iconForeground = TodoThemePalette.Solid(palette.IsDark ? 0x58CDF0u : 0x128CA2u);
        button.Background = background;
        button.BorderBrush = border;
        button.Foreground = foreground;
        button.Resources["ButtonBackground"] = background;
        button.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        button.Resources["ButtonBackgroundDisabled"] = background;
        button.Resources["ButtonBorderBrush"] = border;
        button.Resources["ButtonBorderBrushPointerOver"] = hoverBorder;
        button.Resources["ButtonBorderBrushPressed"] = pressedBorder;
        button.Resources["ButtonBorderBrushDisabled"] = border;
        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonForegroundDisabled"] = foreground;
        SetContentColors(button, iconForeground, foreground);
    }

    public Button DangerButton(string text, string glyph)
    {
        var button = PillButton(text, glyph);
        button.BorderBrush = palette.Brush(0xF2C8C8);
        button.Background = palette.Brush(0xFFF7F7);
        var danger = palette.Brush(0xB42318);
        SetContentColors(button, danger, danger);
        return button;
    }

    public Button IconOnlyButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 34, Height = 34, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = TodoThemePalette.Transparent,
            Content = new FontIcon { Glyph = glyph, FontSize = 16, Foreground = palette.SecondaryText }
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    public void ApplyFlatTextBoxStyle(TextBox textBox)
    {
        var foreground = palette.Text;
        var transparent = TodoThemePalette.Transparent;
        textBox.Resources["TextControlBackground"] = transparent;
        textBox.Resources["TextControlBackgroundPointerOver"] = transparent;
        textBox.Resources["TextControlBackgroundFocused"] = transparent;
        textBox.Resources["TextControlBorderBrush"] = transparent;
        textBox.Resources["TextControlBorderBrushPointerOver"] = transparent;
        textBox.Resources["TextControlBorderBrushFocused"] = transparent;
        textBox.Resources["TextControlForeground"] = foreground;
        textBox.Resources["TextControlForegroundPointerOver"] = foreground;
        textBox.Resources["TextControlForegroundFocused"] = foreground;
        textBox.Resources["TextControlPlaceholderForeground"] = foreground;
        textBox.Resources["TextControlPlaceholderForegroundPointerOver"] = foreground;
        textBox.Resources["TextControlPlaceholderForegroundFocused"] = foreground;
        textBox.Foreground = foreground;
    }

    public Button TaskCheckButton(TodoTask task, Func<Task> toggleCompleted)
    {
        var completed = task.IsCompleted;
        var button = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = TodoThemePalette.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                BorderThickness = completed ? new Thickness(0) : new Thickness(1.6),
                BorderBrush = palette.Brush(0x8BA0AE),
                Background = completed ? palette.Brush(0x138A43) : TodoThemePalette.Transparent,
                Child = completed
                    ? new FontIcon
                    {
                        Glyph = "\uE73E", FontSize = 12, Foreground = TodoThemePalette.PureWhite,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                    : null
            }
        };
        button.Click += async (_, _) => await toggleCompleted();
        return button;
    }

    public Button TreeToggleButton(bool collapsed, Action toggle)
    {
        var button = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = TodoThemePalette.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = collapsed ? "\uE76C" : "\uE70D", FontSize = 12,
                Foreground = palette.SecondaryText
            }
        };
        ToolTipService.SetToolTip(button, collapsed ? "展开子任务" : "收起子任务");
        button.Click += (_, _) => toggle();
        return button;
    }

    public Border TaskListPill(string name, Brush background, Brush foreground) => new()
    {
        MinWidth = 72, Height = 24, CornerRadius = new CornerRadius(5),
        Background = background, Padding = new Thickness(10, 0, 10, 0),
        Child = new TextBlock
        {
            Text = name, FontSize = 12, Foreground = foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center
        }
    };

    public Button RowIconButton(string glyph, string label, Brush foreground)
    {
        var button = BareIconButton(32, glyph, 16, foreground);
        Describe(button, label);
        return button;
    }

    public Button HeaderActionButton(string glyph, string text)
    {
        var button = new Button
        {
            Height = 32, Padding = new Thickness(8, 0, 8, 0),
            BorderThickness = new Thickness(0), Background = TodoThemePalette.Transparent,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 15, Foreground = palette.SecondaryText },
                    new TextBlock
                    {
                        Text = text, FontSize = 13, Foreground = palette.SecondaryText,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        Describe(button, text);
        return button;
    }

    public Button SidebarIconButton(string glyph, string label)
    {
        var button = BareIconButton(36, glyph, 18, palette.Brush(0xAFC1D8));
        Describe(button, label);
        return button;
    }

    private static Button BareIconButton(double size, string glyph, double fontSize, Brush foreground) => new()
    {
        Width = size, Height = size, Padding = new Thickness(0),
        BorderThickness = new Thickness(0), Background = TodoThemePalette.Transparent,
        Content = new FontIcon { Glyph = glyph, FontSize = fontSize, Foreground = foreground }
    };

    private static void Describe(Button button, string label)
    {
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
    }

    private static void SetContentColors(Button button, Brush iconBrush, Brush textBrush)
    {
        if (button.Content is not StackPanel stack) return;
        foreach (var child in stack.Children)
        {
            if (child is FontIcon icon) icon.Foreground = iconBrush;
            else if (child is TextBlock text) text.Foreground = textBrush;
        }
    }
}
