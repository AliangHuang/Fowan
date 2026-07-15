using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoThemePalette(Func<bool> isDark)
{
    public bool IsDark => isDark();
    public SolidColorBrush Text => Brush(0x17242A);
    public SolidColorBrush SecondaryText => Brush(0x6F7F86);
    public SolidColorBrush Muted => Brush(0x96A4AA);
    public SolidColorBrush Accent => Brush(0x128CA2);
    public SolidColorBrush AccentDark => Brush(0x0C6F82);
    public static SolidColorBrush Transparent => new(Colors.Transparent);
    public static SolidColorBrush PureWhite => new(Colors.White);

    public SolidColorBrush TaskHoverBorder(bool completed) => isDark()
        ? Brush(completed ? 0x4E7C63u : 0x3F7480u)
        : Brush(completed ? 0xA9D7BDu : 0x9BCED7u);
    public SolidColorBrush TaskHoverBackground(bool completed) => isDark()
        ? Brush(completed ? 0x1B2A23u : 0x19282Fu)
        : Brush(completed ? 0xF5FBF6u : 0xF4FAFBu);
    public SolidColorBrush FilterActiveBorder => isDark() ? Brush(0x3F8694) : Brush(0x8CC9D3);
    public SolidColorBrush FilterActiveBackground => isDark() ? Brush(0x19313A) : Brush(0xEEF9FA);
    public SolidColorBrush FilterActiveText => isDark() ? Brush(0x9DDFE8) : Brush(0x0C6F82);
    public SolidColorBrush FilterHoverBorder(bool active) => isDark()
        ? Brush(active ? 0x63BBC9u : 0x4F94A1u)
        : Brush(active ? 0x5EB3C1u : 0x7ABCC7u);
    public SolidColorBrush FilterHoverBackground(bool active) => isDark()
        ? Brush(active ? 0x20444Fu : 0x1C2B33u)
        : Brush(active ? 0xDDF4F7u : 0xF1F8FAu);
    public SolidColorBrush FilterPressedBorder(bool active) => isDark()
        ? Brush(active ? 0x4FAABAu : 0x407D89u)
        : Brush(active ? 0x3599A9u : 0x4B9FAAu);
    public SolidColorBrush FilterPressedBackground(bool active) => isDark()
        ? Brush(active ? 0x173942u : 0x16252Cu)
        : Brush(active ? 0xCFEFF3u : 0xE6F3F6u);
    public SolidColorBrush PaletteCardBorder => isDark() ? Brush(0x40505E) : Brush(0xD8E2E6);

    public SolidColorBrush LightDark(uint lightRgb, uint darkRgb) => Solid(isDark() ? darkRgb : lightRgb);

    public SolidColorBrush Brush(uint rgb) => Solid(isDark() ? DarkThemeColor(rgb) : rgb);

    public static SolidColorBrush Solid(uint rgb) => new(ColorHelper.FromArgb(
        255,
        (byte)((rgb >> 16) & 0xFF),
        (byte)((rgb >> 8) & 0xFF),
        (byte)(rgb & 0xFF)));

    private static uint DarkThemeColor(uint rgb) => rgb switch
    {
        0xFFFFFF => 0x151B22,
        0xF7FAFB => 0x11161C,
        0xFBFCFC => 0x151F26,
        0xF8FAFB => 0x151F26,
        0xEEF9FA => 0x17323A,
        0xEEF8F1 => 0x143E34,
        0xDFF4F7 => 0x0B3A7A,
        0xDCE7EA => 0x28333E,
        0xE7EEF0 => 0x212B35,
        0xE1EAED => 0x28333E,
        0xBFDCCB => 0x2A5A45,
        0xF2C8C8 => 0x7A3534,
        0xFFF7F7 => 0x27181A,
        0xB42318 => 0xFF615C,
        0x17242A => 0xEEF3F8,
        0x6F7F86 => 0x98A7B6,
        0x96A4AA => 0x9EACBA,
        0x128CA2 => 0x34B7C8,
        0x0C6F82 => 0x58CDF0,
        0x8BA0AE => 0x7F90A4,
        0x138A43 => 0x25B765,
        0xF06423 => 0xFF8A3D,
        0xF2B01E => 0xF2B01E,
        0xE8F1FF => 0x19304B,
        0xE5F7EA => 0x173524,
        0xF0E8FF => 0x2A2541,
        0xE6F7F9 => 0x123740,
        0xEDEBFF => 0x292542,
        0xFCE7F3 => 0x421F35,
        0xFEE2E2 => 0x421E22,
        0xFFF0E5 => 0x43281B,
        0x1D6DFF => 0x7EB0FF,
        0x4F46E5 => 0xA5B4FC,
        0x18A957 => 0x8CE9AD,
        0x8B5CF6 => 0xC8B5FF,
        0xDB2777 => 0xF472B6,
        0xDC2626 => 0xF87171,
        0xEA580C => 0xFB923C,
        0x2B7F82 => 0x75C1C2,
        0x426DAD => 0x94B7EC,
        0x5D6F9D => 0xA7B4C9,
        0x8064A7 => 0xC1AFE0,
        0xB86B87 => 0xE5A8B9,
        0xB86A62 => 0xE2A8A0,
        0xB98B45 => 0xE4C182,
        0x4A8B6B => 0x9DCEB3,
        0xE5F1F1 => 0x244344,
        0xE8EEF8 => 0x26364F,
        0xEBEEF4 => 0x303948,
        0xF0ECF7 => 0x383044,
        0xF8ECEF => 0x472E38,
        0xF8ECEA => 0x482D2A,
        0xF8F1E4 => 0x493B25,
        0xEAF3EE => 0x294235,
        0x526C91 => 0xAAB9CD,
        0x967B9B => 0xD3BAD5,
        0x7F8A59 => 0xC7D0A0,
        0xA17461 => 0xD8B2A1,
        0xECF0F5 => 0x303943,
        0xF3EDF4 => 0x3D3340,
        0xF1F2E8 => 0x3B4130,
        0xF5EDE8 => 0x43342F,
        _ => rgb
    };
}
