using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Application;
using Fowan.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Coordination;

internal sealed class UpdateInteractionCoordinator(
    UpdateService updateService,
    IProcessLauncher processLauncher,
    Func<ToolboxSnapshot> settings,
    Func<XamlRoot> xamlRoot,
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    Action disableUpdateChecks,
    Action<string> ignoreUpdate,
    Action<string, InfoBarSeverity> showInfo,
    Action exitApplication)
{
    private bool _isDialogOpen;

    public async Task ShowPromptAsync(UpdateInfo update)
    {
        if (_isDialogOpen || !ShouldPrompt(update)) return;
        _isDialogOpen = true;
        try
        {
            var disableCheck = new CheckBox { Content = localize("Update_DisableCheck") };
            var stack = new StackPanel { Spacing = 14, MaxWidth = 520 };
            stack.Children.Add(new TextBlock
            {
                Text = string.Format(localize("Update_Description"), UpdateService.CurrentVersion(), update.Version),
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = themeBrush("TextFillColorPrimaryBrush"),
                LineHeight = 20
            });
            if (!string.IsNullOrWhiteSpace(update.Notes))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = localize("Update_Notes"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = themeBrush("TextFillColorPrimaryBrush")
                });
                stack.Children.Add(new TextBlock
                {
                    Text = update.Notes,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Foreground = themeBrush("TextFillColorSecondaryBrush"),
                    LineHeight = 20
                });
            }
            if (!string.IsNullOrWhiteSpace(update.ReleaseNotesUrl))
            {
                var releaseNotes = new Button
                {
                    Content = localize("Update_OpenReleaseNotes"),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                releaseNotes.Click += (_, _) => processLauncher.Launch(new ProcessLaunchRequest(
                    update.ReleaseNotesUrl, WorkingDirectory: AppContext.BaseDirectory));
                stack.Children.Add(releaseNotes);
            }
            stack.Children.Add(disableCheck);
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot(),
                Title = string.Format(localize("Update_Title"), update.Version),
                Content = stack,
                PrimaryButtonText = localize("Update_ActionInstall"),
                SecondaryButtonText = localize("Update_ActionIgnore"),
                CloseButtonText = localize("Update_ActionLater"),
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            var disable = disableCheck.IsChecked == true;
            if (disable)
            {
                disableUpdateChecks();
            }
            if (result == ContentDialogResult.Secondary)
            {
                if (!disable)
                {
                    ignoreUpdate(update.Version);
                }
                return;
            }
            if (result == ContentDialogResult.Primary) await DownloadAndRunAsync(update);
        }
        finally { _isDialogOpen = false; }
    }

    private bool ShouldPrompt(UpdateInfo update) => settings().UpdateCheckEnabled &&
        !string.Equals(settings().IgnoredUpdateVersion, update.Version, StringComparison.OrdinalIgnoreCase);

    private async Task DownloadAndRunAsync(UpdateInfo update)
    {
        showInfo(string.Format(localize("Update_DownloadStarted"), update.Version), InfoBarSeverity.Warning);
        try
        {
            var installerPath = await updateService.DownloadInstallerAsync(update);
            var launch = processLauncher.Launch(new ProcessLaunchRequest(installerPath, Elevated: true));
            if (!launch.Succeeded) throw new InvalidOperationException(launch.Error);
            exitApplication();
        }
        catch (Exception exception)
        {
            showInfo(string.Format(localize("Update_InstallFailed"), exception.Message), InfoBarSeverity.Error);
        }
    }
}
