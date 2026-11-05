using Fowan.Windows.Application;
using Fowan.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxSettingsDialog(
    AutoStartService autoStart,
    Func<ToolboxSnapshot> settings,
    Func<XamlRoot> xamlRoot,
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    Action<ToolboxSettingsSelection> saveAndRebuild,
    Action<string, InfoBarSeverity> showInfo)
{
    public async Task ShowAsync()
    {
        var current = settings();
        var theme = Combo("Settings_Theme",
            ("Settings_Theme_System", "system"), ("Settings_Theme_Light", "light"), ("Settings_Theme_Dark", "dark"));
        theme.SelectedIndex = current.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        var language = Combo("Settings_Language",
            ("Settings_Language_System", "system"), ("Settings_Language_Chinese", "zh-CN"), ("Settings_Language_English", "en-US"));
        language.SelectedIndex = current.Language switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };
        var closeBehavior = Combo("Settings_CloseBehavior",
            ("Settings_CloseBehavior_MinimizeToTray", CloseBehaviorIds.MinimizeToTray),
            ("Settings_CloseBehavior_Exit", CloseBehaviorIds.Exit));
        closeBehavior.SelectedIndex = current.CloseBehavior == CloseBehaviorIds.Exit ? 1 : 0;
        var updateCheck = Toggle("Settings_UpdateCheck", "Settings_UpdateCheck_On", "Settings_UpdateCheck_Off", current.UpdateCheckEnabled);
        var autoStartWasEnabled = autoStart.IsEnabled();
        var autoStartSwitch = Toggle("Settings_AutoStart", "Settings_AutoStart_On", "Settings_AutoStart_Off", autoStartWasEnabled);
        var stack = new StackPanel { Spacing = 18 };
        stack.Children.Add(theme);
        stack.Children.Add(language);
        stack.Children.Add(closeBehavior);
        stack.Children.Add(autoStartSwitch);
        AddDescription(stack, "Settings_AutoStart_Description");
        stack.Children.Add(updateCheck);
        AddDescription(stack, "Settings_UpdateCheck_Description");
        AddDescription(stack, "Settings_Startup");
        AddDescription(stack, "Settings_Privacy");
        AddDescription(stack, "Settings_About");
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = localize("Settings_Title"), Content = stack,
            PrimaryButtonText = localize("Action_Save"), CloseButtonText = localize("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { autoStart.SetEnabled(autoStartSwitch.IsOn); }
        catch (Exception exception)
        {
            autoStartSwitch.IsOn = autoStartWasEnabled;
            showInfo(string.Format(localize("Settings_AutoStart_Error"), exception.Message), InfoBarSeverity.Error);
            return;
        }
        saveAndRebuild(new ToolboxSettingsSelection(
            SelectedTag(theme, "system"),
            SelectedTag(language, "system"),
            SelectedTag(closeBehavior, CloseBehaviorIds.MinimizeToTray),
            updateCheck.IsOn));
    }

    private ComboBox Combo(string headerKey, params (string LabelKey, string Tag)[] items)
    {
        var box = new ComboBox { Header = localize(headerKey), MinWidth = 260 };
        foreach (var item in items) box.Items.Add(new ComboBoxItem { Content = localize(item.LabelKey), Tag = item.Tag });
        return box;
    }

    private ToggleSwitch Toggle(string header, string on, string off, bool value) => new()
    {
        Header = localize(header), IsOn = value, OnContent = localize(on), OffContent = localize(off), MinWidth = 260
    };

    private void AddDescription(StackPanel stack, string key) => stack.Children.Add(new TextBlock
    {
        Text = localize(key), TextWrapping = TextWrapping.WrapWholeWords,
        Foreground = themeBrush("TextFillColorSecondaryBrush")
    });

    private static string SelectedTag(ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
}
