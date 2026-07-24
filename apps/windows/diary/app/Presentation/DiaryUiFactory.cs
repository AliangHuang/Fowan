using Fowan.Diary.Shared.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Diary.Windows.Presentation;

internal readonly record struct DiaryTagVisual(string BackgroundKey, string ForegroundKey);

internal sealed class DiaryUiFactory(DiaryThemePalette theme)
{
    private const double CardCornerRadius = 8;

    public StackPanel TagRow(IReadOnlyList<string> tags, DiaryNotebook? notebook = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (notebook is not null) row.Children.Add(NotebookPill(notebook));
        foreach (var tag in tags.Where(tag => notebook is null || !string.Equals(tag, notebook.Name, StringComparison.OrdinalIgnoreCase)))
            row.Children.Add(Pill(tag));
        return row;
    }

    public Border NotebookPill(DiaryNotebook notebook) => new()
    {
        CornerRadius = new CornerRadius(6), Background = theme.HexBrush(notebook.AccentColor),
        Padding = new Thickness(10, 5, 10, 5),
        Child = new TextBlock { Text = notebook.Name, FontSize = 13, Foreground = theme.Brush("OnAccent"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
    };

    public Border MetaTagPill(string tag) => new()
    {
        Height = 28, MinWidth = 50, CornerRadius = new CornerRadius(6), Background = theme.TagBackground(tag),
        Padding = new Thickness(10, 0, 10, 0),
        Child = new TextBlock { Text = tag, FontSize = 13, Foreground = theme.TagForeground(tag), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
    };

    public Border Pill(string tag) => new()
    {
        CornerRadius = new CornerRadius(6), Background = theme.TagBackground(tag), Padding = new Thickness(10, 5, 10, 5),
        Child = new TextBlock { Text = tag, FontSize = 13, Foreground = theme.TagForeground(tag), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
    };

    public Border TodoStatusPill(string value, string brushKey, string foregroundKey) => new()
    {
        CornerRadius = new CornerRadius(6), Background = theme.Brush(brushKey), Padding = new Thickness(9, 5, 9, 5),
        Child = Text(value, 12, foregroundKey, Microsoft.UI.Text.FontWeights.SemiBold)
    };

    public Button PrimaryButton(string glyph, string value) => new()
    {
        Height = 40, Padding = new Thickness(14, 0, 14, 0), BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(7), Background = theme.Brush("Accent"),
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new FontIcon { Glyph = glyph, FontSize = 15, Foreground = theme.Brush("OnAccent") }, Text(value, 14, "OnAccent", Microsoft.UI.Text.FontWeights.SemiBold) }
        }
    };

    public Button SecondaryButton(string value) => new()
    {
        Height = 36, Padding = new Thickness(14, 0, 14, 0), BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(7), Background = theme.Brush("ControlBackground"),
        Content = Text(value, 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold)
    };

    public Button OutlineButton(string glyph, string value, string foregroundKey) => new()
    {
        Height = 44, Padding = new Thickness(9, 0, 9, 0), BorderBrush = theme.Brush("CardStroke"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Background = TransparentBrush(),
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new FontIcon { Glyph = glyph, FontSize = 16, Foreground = theme.Brush(foregroundKey) }, Text(value, 14, foregroundKey, Microsoft.UI.Text.FontWeights.SemiBold) }
        }
    };

    public Button IconButton(string glyph, string label, string foregroundKey = "TextSecondary")
    {
        var button = new Button
        {
            Width = 32, Height = 32, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = TransparentBrush(), Content = new FontIcon { Glyph = glyph, FontSize = 16, Foreground = theme.Brush(foregroundKey) }
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    public Button TimelineIconButton(string glyph, string label, string foregroundKey = "TextSecondary")
    {
        var button = new Button
        {
            Width = 28, Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = TransparentBrush(), Content = new FontIcon { Glyph = glyph, FontSize = 15, Foreground = theme.Brush(foregroundKey) }
        };
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    public Button TextButton(string value, string label, double size, string foregroundKey)
    {
        var button = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = TransparentBrush(), Content = Text(value, size, foregroundKey) };
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    public Border Card(UIElement child, double? minHeight) => new()
    {
        MinHeight = minHeight ?? 0, CornerRadius = new CornerRadius(CardCornerRadius), BorderThickness = new Thickness(1),
        BorderBrush = theme.Brush("CardStroke"), Background = theme.CardBackground(), Child = child
    };

    public FrameworkElement EmptyCard(string value) => Card(new TextBlock { Text = value, Foreground = theme.Brush("TextSecondary"), FontSize = 14, Margin = new Thickness(18) }, 64);

    public TextBlock Text(string value, double size, string brushKey, global::Windows.UI.Text.FontWeight? weight = null) => new()
    {
        Text = value, FontSize = size, Foreground = theme.Brush(brushKey),
        FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    public TextBlock EmojiText(string value, double size) => new()
    {
        Text = value, FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji"),
        Foreground = theme.Brush("TextPrimary"), VerticalAlignment = VerticalAlignment.Center
    };

    public static DiaryTagVisual TagVisualFor(string tag) => tag switch
    {
        "复盘" or "进行中" => new DiaryTagVisual("TagGreen", "TagGreenText"),
        "灵感" => new DiaryTagVisual("TagPurple", "TagPurpleText"),
        "思考" => new DiaryTagVisual("TagCyan", "TagCyanText"),
        "项目进展" => new DiaryTagVisual("TagYellow", "TagYellowText"),
        _ => new DiaryTagVisual("TagBlue", "TagBlueText")
    };

    public static string MoodGlyph(string mood) => mood switch
    {
        "愉快" => "\uE76E", "平静" => "\uE7BA", "专注" => "\uE8D4", "满足" => "\uE8FB",
        "放松" => "\uE706", "疲惫" => "\uE708", "低落" => "\uE7F4", "兴奋" => "\uE7F3", _ => "\uE76E"
    };

    public static bool IsSegoeGlyph(string value) => value.Length == 1 && value[0] >= '\uE000' && value[0] <= '\uF8FF';

    public static SolidColorBrush TransparentBrush() => new(Colors.Transparent);
}
