using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace Fowan.Report.Windows.Presentation;

/// <summary>Shared visual language for every report surface, including dialogs and secondary pages.</summary>
internal enum ReportButtonKind
{
    Primary,
    Secondary,
    Segment,
    Navigation,
    Ghost
}

internal static class ReportDesignSystem
{
    private sealed class ButtonProfile(ReportButtonKind kind)
    {
        public ReportButtonKind Kind { get; } = kind;
        public bool Active { get; set; }
        public bool Hovered { get; set; }
        public bool Pressed { get; set; }
    }

    private static readonly ConditionalWeakTable<Button, ButtonProfile> ButtonProfiles = new();

    public static readonly FontFamily IconFont = new("Segoe Fluent Icons");
    public static readonly SolidColorBrush Canvas = Brush("#091524");
    public static readonly SolidColorBrush Sidebar = Brush("#07111F");
    public static readonly SolidColorBrush Surface = Brush("#101D30");
    public static readonly SolidColorBrush SurfaceRaised = Brush("#17243A");
    public static readonly SolidColorBrush InputSurface = Brush("#0C182A");
    public static readonly SolidColorBrush Stroke = Brush("#2B3C56");
    public static readonly SolidColorBrush StrongStroke = Brush("#52627A");
    public static readonly SolidColorBrush Text = Brush("#F2F6FF");
    public static readonly SolidColorBrush SecondaryText = Brush("#A9B7CD");
    public static readonly SolidColorBrush MutedText = Brush("#8391A8");
    public static readonly SolidColorBrush Accent = Brush("#5268F4");
    public static readonly SolidColorBrush AccentBorder = Brush("#8A99FF");
    public static readonly SolidColorBrush Success = Brush("#39D98A");
    public static readonly SolidColorBrush Warning = Brush("#FFC145");
    public static readonly SolidColorBrush Danger = Brush("#FF9B9B");

    public static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(
        0xFF,
        byte.Parse(hex[1..3], NumberStyles.HexNumber),
        byte.Parse(hex[3..5], NumberStyles.HexNumber),
        byte.Parse(hex[5..7], NumberStyles.HexNumber)));

    public static Border Card(UIElement child, Thickness padding, double radius = 16) => new()
    {
        Background = Surface,
        BorderBrush = Stroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = padding,
        Child = child
    };

    public static FontIcon Icon(string glyph, double size = 18, Brush? foreground = null) => new()
    {
        FontFamily = IconFont,
        Glyph = glyph,
        FontSize = size,
        Foreground = foreground ?? SecondaryText,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static StackPanel IconText(string glyph, string text, double iconSize = 17, double textSize = 16, double spacing = 8) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = spacing,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            Icon(glyph, iconSize),
            new TextBlock { Text = text, FontSize = textSize, Foreground = Text, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    public static void ConfigureButton(Button button, ReportButtonKind kind, bool active = false)
    {
        button.CornerRadius = new CornerRadius(kind is ReportButtonKind.Primary or ReportButtonKind.Secondary ? 12 : 10);
        button.MinHeight = kind is ReportButtonKind.Primary or ReportButtonKind.Secondary ? 46 : 42;
        button.Padding = kind switch
        {
            ReportButtonKind.Primary => new Thickness(24, 11, 24, 11),
            ReportButtonKind.Secondary => new Thickness(20, 10, 20, 10),
            ReportButtonKind.Navigation => new Thickness(18, 12, 16, 12),
            ReportButtonKind.Ghost => new Thickness(10, 8, 10, 8),
            _ => new Thickness(16, 10, 16, 10)
        };
        button.BorderThickness = new Thickness(kind is ReportButtonKind.Ghost ? 0 : 1);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.FontSize = 16;
        ButtonProfiles.Remove(button);
        ButtonProfiles.Add(button, new ButtonProfile(kind) { Active = active });
        button.IsEnabledChanged += (_, _) => Apply(button);
        button.PointerEntered += (_, _) => { if (button.IsEnabled) { ButtonProfiles.GetValue(button, _ => new(kind)).Hovered = true; Apply(button); } };
        button.PointerExited += (_, _) => { var profile = ButtonProfiles.GetValue(button, _ => new(kind)); profile.Hovered = false; profile.Pressed = false; Apply(button); };
        button.PointerPressed += (_, _) => { if (button.IsEnabled) { ButtonProfiles.GetValue(button, _ => new(kind)).Pressed = true; Apply(button); } };
        button.PointerReleased += (_, _) => { var profile = ButtonProfiles.GetValue(button, _ => new(kind)); profile.Pressed = false; Apply(button); };
        Apply(button);
    }

    public static void SetActive(Button button, bool active)
    {
        var profile = ButtonProfiles.GetValue(button, _ => new ButtonProfile(ReportButtonKind.Segment));
        profile.Active = active;
        Apply(button);
    }

    public static void SetKind(Button button, ReportButtonKind kind)
    {
        var profile = ButtonProfiles.GetValue(button, _ => new ButtonProfile(kind));
        if (profile.Kind == kind) { Apply(button); return; }
        ButtonProfiles.Remove(button);
        ButtonProfiles.Add(button, new ButtonProfile(kind) { Active = profile.Active });
        Apply(button);
    }

    public static void ConfigureTextBox(TextBox textBox, double minHeight = 44)
    {
        textBox.Background = InputSurface;
        textBox.BorderBrush = StrongStroke;
        textBox.Foreground = Text;
        textBox.PlaceholderForeground = MutedText;
        textBox.MinHeight = minHeight;
        textBox.Padding = new Thickness(14, 11, 14, 11);
        textBox.CornerRadius = new CornerRadius(10);
    }

    public static void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.Background = InputSurface;
        comboBox.BorderBrush = StrongStroke;
        comboBox.Foreground = Text;
        comboBox.MinHeight = 44;
        comboBox.Padding = new Thickness(12, 7, 10, 7);
    }

    public static void ConfigureDatePicker(DatePicker datePicker)
    {
        datePicker.Background = InputSurface;
        datePicker.BorderBrush = StrongStroke;
        datePicker.Foreground = Text;
        datePicker.MinHeight = 44;
    }

    public static ContentDialog Dialog(XamlRoot root, string title, UIElement content, string primaryText, string closeText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            RequestedTheme = ElementTheme.Dark,
            Background = Surface,
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1),
            Content = new Border { Background = Surface, Child = content, Padding = new Thickness(2) },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary
        };
        return dialog;
    }

    public static void SetIconTextForeground(Button button, Brush foreground)
    {
        if (button.Content is not StackPanel stack) return;
        foreach (var child in stack.Children)
        {
            if (child is FontIcon icon) icon.Foreground = foreground;
            else if (child is TextBlock text) text.Foreground = foreground;
        }
    }

    private static void Apply(Button button)
    {
        if (!ButtonProfiles.TryGetValue(button, out var profile)) return;
        var (background, border, foreground) = ColorsFor(profile, button.IsEnabled);
        button.Background = background;
        button.BorderBrush = border;
        button.Foreground = foreground;
        ApplyNativeNavigationVisuals(button, profile);
        SetIconTextForeground(button, foreground);
    }

    /// <summary>
    /// The default WinUI Button template paints its PointerOver resource before the managed
    /// PointerEntered handler runs. Keep those native resources aligned with the navigation
    /// palette so crossing into a left navigation item does not flash the default button color.
    /// </summary>
    private static void ApplyNativeNavigationVisuals(Button button, ButtonProfile profile)
    {
        if (profile.Kind != ReportButtonKind.Navigation) return;
        var (background, border, foreground) = profile.Active
            ? (Brush("#30477A"), Brush("#536FBB"), (Brush)Text)
            : (Brush("#13233A"), Brush("#000000"), (Brush)SecondaryText);
        button.Resources["ButtonBackgroundPointerOver"] = background;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonBackgroundPressed"] = background;
        button.Resources["ButtonBorderBrushPressed"] = border;
        button.Resources["ButtonForegroundPressed"] = foreground;
    }

    private static (Brush Background, Brush Border, Brush Foreground) ColorsFor(ButtonProfile profile, bool enabled)
    {
        if (!enabled) return (Brush("#172138"), Brush("#273752"), Brush("#75839A"));
        if (profile.Kind == ReportButtonKind.Primary)
        {
            return profile.Pressed
                ? (Brush("#4055D6"), AccentBorder, Text)
                : profile.Hovered ? (Brush("#6578FF"), Brush("#AAB5FF"), Text) : (Accent, AccentBorder, Text);
        }
        if (profile.Kind == ReportButtonKind.Navigation)
        {
            if (profile.Active) return (profile.Hovered ? Brush("#30477A") : Brush("#263862"), Brush("#536FBB"), Text);
            return (profile.Hovered ? Brush("#13233A") : Sidebar, Brush("#000000"), SecondaryText);
        }
        if (profile.Kind == ReportButtonKind.Ghost)
        {
            return (profile.Hovered ? Brush("#17243A") : Sidebar, Brush("#000000"), SecondaryText);
        }
        if (profile.Kind == ReportButtonKind.Segment)
        {
            if (profile.Active) return (profile.Pressed ? Brush("#4055D6") : Accent, AccentBorder, Text);
            return (profile.Hovered ? Brush("#1A2A45") : Brush("#142238"), Stroke, SecondaryText);
        }
        return (profile.Hovered ? Brush("#182A48") : Brush("#101D30"), profile.Hovered ? Brush("#7288D0") : Brush("#5A70A0"), Text);
    }
}
