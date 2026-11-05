using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryThemePalette(Func<DiarySettings> settings, Func<DiaryData> data)
{
    public ElementTheme ResolveElementTheme() => settings().Theme switch
    {
        DiaryThemeIds.Light => ElementTheme.Light,
        DiaryThemeIds.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public bool IsDark => settings().Theme switch
    {
        DiaryThemeIds.Dark => true,
        DiaryThemeIds.Light => false,
        _ => global::Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark
    };

    public SolidColorBrush Brush(string key)
    {
        var dark = IsDark;
        var color = key switch
        {
            "AppBackground" => dark ? C(0x111820) : C(0xFBFCFE), "SidebarBackground" => dark ? C(0x111C28) : C(0xF4F8FD), "DetailBackground" => dark ? C(0x121820) : C(0xFFFFFF), "CardBackground" => dark ? C(0x182028) : C(0xFFFFFF), "ControlBackground" => dark ? C(0x1A232C) : C(0xFFFFFF),
            "SelectedNav" => dark ? C(0x14325A) : C(0xE7F0FF), "SelectedCard" => dark ? C(0x142235) : C(0xFBFDFF), "Divider" => dark ? ColorHelper.FromArgb(150, 55, 64, 74) : C(0xE2E7EF), "InnerDivider" => dark ? ColorHelper.FromArgb(78, 72, 82, 92) : ColorHelper.FromArgb(130, 214, 222, 232), "SoftDivider" => dark ? ColorHelper.FromArgb(88, 82, 92, 104) : ColorHelper.FromArgb(150, 214, 222, 232),
            "CardStroke" => dark ? ColorHelper.FromArgb(145, 66, 76, 88) : C(0xDCE3EC), "TimelineLine" => dark ? ColorHelper.FromArgb(145, 60, 72, 86) : C(0xCBD4DF), "TimelineDot" => dark ? C(0x93A0AF) : C(0xC7D0DB), "TextPrimary" => dark ? C(0xF2F5F8) : C(0x17202B), "TextSecondary" => dark ? C(0xAAB3BE) : C(0x5E6977), "TextMuted" => dark ? C(0x7A8796) : C(0x96A0AD),
            "Accent" => C(0x2F80FF), "OnAccent" => C(0xFFFFFF), "Favorite" => C(0xF2A900), "Danger" => C(0xE5484D), "TagBlue" => dark ? C(0x1F3D68) : C(0xE6F0FF), "TagGreen" => dark ? C(0x1C4937) : C(0xE4F7EE), "TagPurple" => dark ? C(0x3C2B5A) : C(0xF0E6FF), "TagCyan" => dark ? C(0x1B4653) : C(0xE2F7FB), "TagYellow" => dark ? C(0x5C491B) : C(0xFFF3D8),
            "TagBlueText" => dark ? C(0x8DBEFF) : C(0x1B5FB8), "TagGreenText" => dark ? C(0x86E0B0) : C(0x1F7A4C), "TagPurpleText" => dark ? C(0xD3B7FF) : C(0x6B3FB0), "TagCyanText" => dark ? C(0x8BE4F2) : C(0x217A88), "TagYellowText" => dark ? C(0xFFD56A) : C(0x8A6500),
            _ => dark ? C(0x303B47) : C(0xDCE3EC)
        };
        return new SolidColorBrush(color);
    }

    public Microsoft.UI.Xaml.Media.Brush CardBackground() => !IsDark
        ? Brush("CardBackground")
        : VerticalGradient((0x1C252F, 0d), (0x182028, 0.55), (0x151C24, 1));

    public Microsoft.UI.Xaml.Media.Brush SidebarBackground() => !IsDark
        ? Brush("SidebarBackground")
        : new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(0.9, 1),
            GradientStops = { new GradientStop { Color = C(0x121D2A), Offset = 0 }, new GradientStop { Color = C(0x0F1B27), Offset = 0.55 }, new GradientStop { Color = C(0x0C1824), Offset = 1 } }
        };

    public SolidColorBrush HexBrush(string value)
    {
        if (value.Length == 7 && byte.TryParse(value[1..3], System.Globalization.NumberStyles.HexNumber, null, out var r) && byte.TryParse(value[3..5], System.Globalization.NumberStyles.HexNumber, null, out var g) && byte.TryParse(value[5..7], System.Globalization.NumberStyles.HexNumber, null, out var b))
            return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
        return Brush("Accent");
    }

    public SolidColorBrush TagBackground(string tag)
    {
        var color = HexBrush(TagColor(tag).Hex).Color;
        return new SolidColorBrush(ColorHelper.FromArgb(IsDark ? (byte)68 : (byte)32, color.R, color.G, color.B));
    }

    public SolidColorBrush TagForeground(string tag)
    {
        var color = HexBrush(TagColor(tag).Hex).Color;
        return new SolidColorBrush(IsDark
            ? ColorHelper.FromArgb(255, (byte)Math.Min(255, color.R + 55), (byte)Math.Min(255, color.G + 55), (byte)Math.Min(255, color.B + 55))
            : ColorHelper.FromArgb(255, (byte)(color.R * 0.72), (byte)(color.G * 0.72), (byte)(color.B * 0.72)));
    }

    private DiaryTagColor TagColor(string tag) => DiaryMetadata.TagColor(DiaryTags.Find(data(), tag)?.ColorId);
    private static LinearGradientBrush VerticalGradient(params (uint Rgb, double Offset)[] stops) => new()
    {
        StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(0, 1),
        GradientStops = { new GradientStop { Color = C(stops[0].Rgb), Offset = stops[0].Offset }, new GradientStop { Color = C(stops[1].Rgb), Offset = stops[1].Offset }, new GradientStop { Color = C(stops[2].Rgb), Offset = stops[2].Offset } }
    };
    private static Color C(uint rgb) => ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
