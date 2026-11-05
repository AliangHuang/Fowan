using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Application.Ports;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.ToolCenter.Windows.Presentation;

internal sealed class ToolCenterManagementPanel : StackPanel
{
    private readonly ToolCenterSession _session;
    private readonly IToolCenterCommands _commands;
    private readonly Func<Task<string?>> _pickInstallParent;
    private readonly TextBlock _versions = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _installPath = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 12 };
    private readonly TextBlock _dataPath = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 12 };
    private readonly TextBlock _installHeading = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _installHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _dataHeading = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _dataHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly StackPanel _dataSection = new() { Spacing = 12 };
    private readonly TextBlock _progressText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly InfoBar _outcome = new() { IsClosable = false };
    private readonly Button _install;
    private readonly Button _launch;
    private readonly Button _uninstall;
    private readonly Button _chooseLocation;
    private readonly Button _openInstall;
    private readonly Button _openData;
    private readonly Button _clean;
    private readonly Button _cancel;
    private readonly List<Button> _allActions = [];

    internal ToolCenterManagementPanel(ToolCenterSession session, Func<Task<string?>> pickInstallParent)
    {
        _session = session;
        _commands = session;
        _pickInstallParent = pickInstallParent;
        Spacing = 12;
        Children.Add(_versions);
        _install = Action("Center_DownloadInstall", () => _commands.InstallAsync());
        if (global::Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style))
            _install.Style = style as Style;
        _launch = Action("Center_Launch", () => _commands.LaunchAsync());
        _ = Action("Center_CheckUpdates", () => _commands.CheckUpdatesAsync());
        Children.Add(_progressText);
        Children.Add(_progress);
        _cancel = new Button { Content = T("Center_Cancel"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _cancel.Click += (_, _) => _commands.CancelCurrentOperation();
        Children.Add(_cancel);
        Children.Add(_outcome);
        Children.Add(_installHeading);
        Children.Add(_installPath);
        Children.Add(_installHint);
        _chooseLocation = Action("Center_ChooseLocation", ChangeLocationAsync);
        ToolTipService.SetToolTip(_chooseLocation, T("Center_LocationHelp"));
        _openInstall = Action("Center_OpenInstallFolder", () => _commands.OpenDirectoryAsync(false));
        _dataSection.Children.Add(_dataHeading);
        _dataSection.Children.Add(_dataPath);
        _dataSection.Children.Add(_dataHint);
        _openData = Action("Center_OpenDataFolder", () => _commands.OpenDirectoryAsync(true), _dataSection);
        Children.Add(_dataSection);
        _clean = Action("Center_CleanCache", () => _commands.CleanAsync());
        _uninstall = Action("Center_Uninstall", UninstallAsync);
        _ = Action("Center_SourcePage", () => _commands.OpenPageAsync(true));
        _ = Action("Center_ReleasePage", () => _commands.OpenPageAsync(false));
        Children.Add(new TextBlock { Text = T("Center_YymLicense"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Refresh();
    }

    private Button Action(string key, Func<Task<ToolOperationResult>> execute, Panel? container = null)
    {
        var button = new Button { Content = T(key), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) =>
        {
            try { await execute(); }
            catch (Exception exception)
            {
                _outcome.IsOpen = true;
                _outcome.Severity = InfoBarSeverity.Error;
                _outcome.Title = T("Center_OperationFailed");
                _outcome.Message = exception.Message;
            }
        };
        _allActions.Add(button);
        (container?.Children ?? Children).Add(button);
        return button;
    }

    internal void Refresh()
    {
        var state = _session.Management;
        var installation = state.Installation;
        foreach (var button in _allActions) button.IsEnabled = !state.IsBusy;
        _versions.Text = string.Format(T("Center_Versions"), state.Release.Version,
            installation.IsInstalled ? installation.Version ?? T("Center_UnknownVersion") : T("Center_NotInstalled"));
        _installPath.Text = installation.InstallDirectory;
        _dataPath.Text = installation.UserDataDirectory;
        _installHeading.Text = T(installation.IsInstalled ? "Center_InstallLocation" :
            installation.HasManagedFiles ? "Center_ResidualInstallLocation" : "Center_PlannedInstallLocation");
        _installHint.Text = T("Center_PlannedInstallHint");
        _installHint.Visibility = !installation.IsInstalled && !installation.HasManagedFiles ? Visibility.Visible : Visibility.Collapsed;
        _dataSection.Visibility = installation.IsInstalled || installation.HasUserData ? Visibility.Visible : Visibility.Collapsed;
        _dataHeading.Text = T(installation.HasUserData ? installation.IsInstalled ? "Center_UserData" : "Center_ExistingUserData" : "Center_FutureUserData");
        _dataHint.Text = T(installation.HasUserData ? "Center_ExistingUserDataHint" : "Center_UserDataNotCreated");
        _dataHint.Visibility = installation.IsInstalled && installation.HasUserData ? Visibility.Collapsed : Visibility.Visible;
        _install.Content = T(!installation.IsInstalled ? "Center_DownloadInstall" : !installation.IsHealthy
            ? "Center_Repair" : installation.Version == state.Release.Version ? "Center_Reinstall" : "Center_Update");
        _install.IsEnabled = state.IsInitialized && !state.IsBusy && !installation.IsRunning &&
            !YymPackageIdentity.IsNewerVersion(installation.Version, state.Release.Version);
        _launch.IsEnabled = !state.IsBusy && installation.IsHealthy && !installation.IsRunning;
        _launch.Visibility = installation.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        _launch.Content = T(installation.IsRunning ? "Center_Running" : "Center_Launch");
        _chooseLocation.IsEnabled = !state.IsBusy && !installation.IsInstalled;
        _openInstall.IsEnabled = !state.IsBusy && installation.HasManagedFiles;
        _openInstall.Visibility = installation.HasManagedFiles ? Visibility.Visible : Visibility.Collapsed;
        _openData.IsEnabled = !state.IsBusy && installation.HasUserData;
        _openData.Visibility = installation.HasUserData ? Visibility.Visible : Visibility.Collapsed;
        _uninstall.IsEnabled = state.IsInitialized && !state.IsBusy && !installation.IsRunning &&
            (installation.IsInstalled || installation.HasManagedFiles || installation.HasUserData || installation.CacheBytes > 0);
        _clean.IsEnabled = state.IsInitialized && !state.IsBusy && !installation.IsRunning &&
            (installation.HasManagedFiles || installation.CacheBytes > 0);
        _clean.Visibility = installation.HasManagedFiles || installation.CacheBytes > 0 ? Visibility.Visible : Visibility.Collapsed;
        _uninstall.Content = T(installation.IsInstalled ? "Center_Uninstall" : "Center_CleanResiduals");
        _uninstall.Visibility = installation.IsInstalled || installation.HasManagedFiles || installation.HasUserData || installation.CacheBytes > 0
            ? Visibility.Visible : Visibility.Collapsed;
        _cancel.Visibility = state.IsBusy && state.OperationKey is "Center_Installing" or "Center_CheckingUpdates"
            ? Visibility.Visible : Visibility.Collapsed;
        _progress.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        _progressText.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        _progress.IsIndeterminate = state.Progress is null || state.Progress.TotalBytes <= 0;
        _progress.Value = state.Progress is { TotalBytes: > 0 } progress
            ? Math.Clamp(100.0 * progress.CompletedBytes / progress.TotalBytes, 0, 100) : 0;
        _progressText.Text = T(state.Progress?.StageKey ?? state.OperationKey ?? "Center_ReadingInstallation");
        var issue = state.MessageKey ?? installation.IssueKey;
        _outcome.IsOpen = issue is not null;
        _outcome.Severity = state.IsError ? InfoBarSeverity.Error : installation.IssueKey is not null
            ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        _outcome.Title = issue is null ? string.Empty : T(issue);
        _outcome.Message = state.ErrorDetail ?? string.Empty;
    }

    private async Task<ToolOperationResult> ChangeLocationAsync()
    {
        var parent = await _pickInstallParent();
        return parent is null ? new(false, Canceled: true) : await _commands.ChangeInstallParentAsync(parent);
    }

    private async Task<ToolOperationResult> UninstallAsync()
    {
        var installation = _session.Management.Installation;
        var removeData = new CheckBox { Content = T("Center_RemoveUserData"), IsChecked = false };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = T(installation.IsInstalled ? "Center_UninstallDescription" : "Center_CleanupDescription"), TextWrapping = TextWrapping.Wrap });
        if (!installation.IsInstalled)
            content.Children.Add(new TextBlock { Text = T("Center_ManagedCleanupScope"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = installation.InstallDirectory, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (installation.HasUserData)
        {
            content.Children.Add(removeData);
            content.Children.Add(new TextBlock { Text = installation.UserDataDirectory, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }
        content.Children.Add(new TextBlock { Text = T("Center_UninstallDataHelp"), TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = T(installation.IsInstalled ? "Center_UninstallTitle" : "Center_CleanupTitle"),
            PrimaryButtonText = T(installation.IsInstalled ? "Center_Uninstall" : "Center_CleanResiduals"), CloseButtonText = T("Center_Cancel"),
            DefaultButton = ContentDialogButton.Close, Content = content
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? await _commands.UninstallAsync(removeData.IsChecked == true) : new(false, Canceled: true);
    }

    private string T(string key) => _session.Text(key);
}
