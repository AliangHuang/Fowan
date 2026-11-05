using Fowan.Todo.Shared.Models;
using Microsoft.Win32;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyThemePalette(Func<TodoSettings> settings)
{
    public bool IsDark => settings().Theme switch
    {
        TodoThemeIds.Dark => true,
        TodoThemeIds.Light => false,
        _ => IsSystemDark()
    };

    public SolidColorBrush Text => Brush(0x17242A);
    public SolidColorBrush SecondaryText => Brush(0x6F7F86);
    public SolidColorBrush MutedText => Brush(0x8FA2AA);
    public SolidColorBrush Accent => Brush(0x128CA2);
    public SolidColorBrush AccentDark => Brush(0x0C6F82);
    public SolidColorBrush TaskCheckBorder => IsDark ? Brush(0x9BB2BC) : Brush(0x667B84);
    public SolidColorBrush Surface => Brush(0xFFFFFF, Math.Clamp(
        settings().StickyOpacity, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity));
    public SolidColorBrush Panel(uint rgb) => Brush(rgb, Math.Clamp(
        settings().StickyOpacity + 0.08, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity));
    public SolidColorBrush TaskHoverBackground(bool completed) => Panel(IsDark
        ? (completed ? 0x1B2A23u : 0x19282Fu)
        : (completed ? 0xF5FBF6u : 0xF4FAFBu));
    public SolidColorBrush TaskHoverBorder(bool completed) => Brush(IsDark
        ? (completed ? 0x4E7C63u : 0x3F7480u)
        : (completed ? 0xA9D7BDu : 0x9BCED7u));
    public SolidColorBrush ContextMenuSurface => Panel(0xEAF4F7);
    public SolidColorBrush ContextMenuBorder => Brush(0x8BAEB8, Math.Clamp(
        settings().StickyOpacity + 0.18, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity));
    public SolidColorBrush ContextMenuItemHover => Panel(0xD7EDF2);

    public SolidColorBrush Brush(uint rgb) => Brush(rgb, 1.0);

    public SolidColorBrush Brush(uint rgb, double opacity)
    {
        rgb = ThemeRgb(rgb);
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255),
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF)));
    }

    public string HexColor(uint rgb, double opacity = 1.0)
    {
        rgb = ThemeRgb(rgb);
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255);
        return $"#{alpha:X2}{(byte)((rgb >> 16) & 0xFF):X2}{(byte)((rgb >> 8) & 0xFF):X2}{(byte)(rgb & 0xFF):X2}";
    }

    private uint ThemeRgb(uint rgb)
    {
        if (!IsDark) return rgb;
        return rgb switch
        {
            0xFFFFFF => 0x151B22,
            0xF7FAFB => 0x11161C,
            0xF5FAFB => 0x11161C,
            0xF5F8F9 => 0x1A242B,
            0xEEF9FA => 0x17323A,
            0xDFF4F7 => 0x17323A,
            0xCBEFF4 => 0x1E4650,
            0xDCE7EA => 0x28333E,
            0xE7EEF0 => 0x212B35,
            0xEAF4F7 => 0x1B2A33,
            0xD7EDF2 => 0x234652,
            0x8BAEB8 => 0x536B76,
            0x17242A => 0xEEF3F8,
            0x6F7F86 => 0x98A7B6,
            0x8FA2AA => 0x91A4B1,
            0x9BB2BC => 0x7F90A4,
            0x128CA2 => 0x34B7C8,
            0x0C6F82 => 0x58CDF0,
            0x001B3D => 0x001B3D,
            _ => rgb
        };
    }

    private static bool IsSystemDark()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1) is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
