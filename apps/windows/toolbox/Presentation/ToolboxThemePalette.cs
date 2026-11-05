using Fowan.Windows.Application;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxThemePalette(Func<ToolboxSnapshot> settings)
{
    private static readonly HashSet<string> CustomKeys = new(StringComparer.Ordinal)
    {
        "AppBackground", "TopBarBackground", "SidebarBackground", "DetailBackground", "ControlSurface",
        "SelectedNavigationBackground", "NavigationIconBrush", "IconTileBackground", "AvatarBackground",
        "DisabledButtonBackground", "ToastBackground", "ToastWarningBackground", "ToastErrorBackground",
        "ApplicationPageBackgroundThemeBrush", "LayerFillColorDefaultBrush", "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush", "ToolCardHoverStrokeBrush", "ToolCardHoverBackgroundBrush",
        "DividerStrokeColorDefaultBrush", "AccentFillColorDefaultBrush", "AccentStrokeColorDefaultBrush",
        "AccentTextFillColorPrimaryBrush", "TextOnAccentFillColorPrimaryBrush", "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush", "ControlFillColorSecondaryBrush"
    };

    public static Style? Style(string resourceKey) =>
        global::Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var resource) ? resource as Style : null;

    public Brush Brush(string resourceKey)
    {
        if (global::Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush && !CustomKeys.Contains(resourceKey)) return brush;
        return new SolidColorBrush(Color(resourceKey));
    }

    public Color Color(string resourceKey)
    {
        var dark = IsDark;
        return resourceKey switch
        {
            "AppBackground" or "ApplicationPageBackgroundThemeBrush" => dark ? C(0x0F141B) : C(0xF8F9FB),
            "TopBarBackground" => dark ? C(0x10161D) : C(0xFFFFFF),
            "SidebarBackground" or "LayerFillColorDefaultBrush" => dark ? C(0x10171F) : C(0xFAFBFD),
            "DetailBackground" => dark ? C(0x161D26) : C(0xFFFFFF),
            "ControlSurface" => dark ? C(0x151C25) : C(0xFFFFFF),
            "SelectedNavigationBackground" => dark ? C(0x1A2635) : C(0xECF5FF),
            "NavigationIconBrush" => dark ? C(0xC5CEDA) : C(0x4D5563),
            "IconTileBackground" => dark ? C(0x1A222D) : C(0xFBFCFE),
            "AvatarBackground" or "ControlFillColorSecondaryBrush" => dark ? C(0x2A3442) : C(0xE8ECF2),
            "DisabledButtonBackground" => dark ? C(0x25303C) : C(0xD8DEE7),
            "ToastBackground" => dark ? C(0x17251F) : C(0xEEF8F2),
            "ToastWarningBackground" => dark ? C(0x2A2415) : C(0xFFF7E2),
            "ToastErrorBackground" => dark ? C(0x2A181B) : C(0xFDEEEE),
            "CardBackgroundFillColorDefaultBrush" => dark ? C(0x171E27) : C(0xFFFFFF),
            "CardStrokeColorDefaultBrush" => dark ? C(0x303945) : C(0xDDE3EA),
            "ToolCardHoverStrokeBrush" => dark ? C(150, 0xB8D9FF) : C(160, 0x70B6F2),
            "ToolCardHoverBackgroundBrush" => dark ? C(28, 0x58A6FF) : C(22, 0xD6ECFF),
            "DividerStrokeColorDefaultBrush" => dark ? C(0x29323D) : C(0xE1E6EE),
            "AccentFillColorDefaultBrush" or "AccentStrokeColorDefaultBrush" or "AccentTextFillColorPrimaryBrush" => C(0x2A82F3),
            "TextOnAccentFillColorPrimaryBrush" => C(0xFFFFFF),
            "TextFillColorPrimaryBrush" => dark ? C(0xF5F7FA) : C(0x151A21),
            "TextFillColorSecondaryBrush" => dark ? C(0xA7B0BE) : C(0x606A78),
            _ => dark ? C(0x303945) : C(0xE2E7EF)
        };
    }

    public bool IsDark => settings().Theme switch
    {
        "dark" => true,
        "light" => false,
        _ => global::Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark
    };

    private static Color C(uint rgb) => ColorHelper.FromArgb(
        255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    private static Color C(byte alpha, uint rgb) => ColorHelper.FromArgb(
        alpha, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
