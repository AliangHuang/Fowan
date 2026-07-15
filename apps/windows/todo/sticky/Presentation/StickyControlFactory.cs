using Fowan.Todo.Shared.Models;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyControlFactory(Func<TodoSettings> settings, StickyThemePalette palette)
{
    public Button HeaderPillButton(string glyph, string label)
    {
        var button = HeaderButton(32, 60, new Thickness(10, 0, 10, 0), palette.Brush(0xDFF4F7));
        button.Content = HeaderButtonContent(glyph, label, palette.Accent);
        button.Template = ButtonTemplate(new CornerRadius(7), palette.Brush(0xDFF4F7), palette.Brush(0xCBEFF4));
        Describe(button, label);
        return button;
    }

    public Button HeaderTextButton(string glyph, string label)
    {
        var button = HeaderButton(32, 92, new Thickness(8, 0, 8, 0), Brushes.Transparent);
        button.Content = HeaderButtonContent(glyph, label, palette.Text);
        button.Template = ButtonTemplate(new CornerRadius(7), Brushes.Transparent, palette.Brush(0xEEF9FA));
        Describe(button, label);
        return button;
    }

    public Button HeaderIconButton(string glyph, string label)
    {
        var button = HeaderButton(30, 30, new Thickness(0), Brushes.Transparent);
        button.Content = MdIcon(glyph, 13, palette.SecondaryText);
        button.Template = ButtonTemplate(new CornerRadius(7), Brushes.Transparent, palette.Brush(0xEEF9FA));
        Describe(button, label);
        return button;
    }

    public Button CircleIconButton(string glyph, string label, Brush background, Brush foreground)
    {
        var button = new Button
        {
            Width = 20, Height = 20, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = background, Content = MdIcon(glyph, 11, foreground), ToolTip = label,
            VerticalAlignment = VerticalAlignment.Center,
            Template = ButtonTemplate(new CornerRadius(10), background, palette.AccentDark)
        };
        return button;
    }

    public Button CompletedToggleButton() => new()
    {
        Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Template = ButtonTemplate(new CornerRadius(6), Brushes.Transparent, palette.Brush(0xEEF9FA))
    };

    public Grid CompletedToggleContent(int count)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = $"已完成 {count}", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = palette.SecondaryText, VerticalAlignment = VerticalAlignment.Center
        });
        var chevron = MdIcon(
            settings().IsStickyCompletedExpanded ? "\uE70D" : "\uE76C",
            12,
            palette.SecondaryText);
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);
        return grid;
    }

    public ControlTemplate ButtonTemplate(CornerRadius radius, Brush normal, Brush hover)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(Border.BackgroundProperty, normal);
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Chrome"));
        template.Triggers.Add(hoverTrigger);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "Chrome"));
        template.Triggers.Add(pressed);
        return template;
    }

    public TextBlock SmallLabel(string text) => Text(text, palette.SecondaryText, TextAlignment.Left);

    public TextBlock SmallValue(string text) => Text(text, palette.SecondaryText, TextAlignment.Right);

    public TextBlock EmptyText(string text) => new()
    {
        Text = text, Foreground = palette.SecondaryText, FontSize = 13,
        Margin = new Thickness(2, 8, 0, 8)
    };

    public static TextBlock MdIcon(string glyph, double fontSize, Brush foreground) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = fontSize,
        Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center
    };

    public static ImageSource? LoadIconImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo-app-icon-256.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path, UriKind.Absolute)) : null;
    }

    private static Button HeaderButton(double height, double minWidth, Thickness padding, Brush background) => new()
    {
        Height = height, MinWidth = minWidth, Padding = padding, Margin = new Thickness(4, 0, 0, 0),
        BorderThickness = new Thickness(0), Background = background, Cursor = Cursors.Arrow,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static StackPanel HeaderButtonContent(string glyph, string label, Brush foreground) => new()
    {
        Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            MdIcon(glyph, 12, foreground),
            new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = foreground, Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
        }
    };

    private static TextBlock Text(string value, Brush foreground, TextAlignment alignment) => new()
    {
        Text = value, FontSize = 12, Foreground = foreground,
        TextAlignment = alignment, VerticalAlignment = VerticalAlignment.Center
    };

    private static void Describe(Button button, string label)
    {
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }
}
