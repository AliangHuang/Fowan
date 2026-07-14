using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Ai.Shared.Application.Ports;
using Fowan.Ai.Config.Windows.Presentation;
using Fowan.Ai.Config.Windows.Platform.Windows;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace Fowan.Ai.Config.Windows;

public sealed partial class ConfigWindow : Window
{
    private readonly AiLocalizationService _loc = new();
    private readonly AiCoreClient _client;
    private readonly IAiApplicationLauncher _applicationLauncher;
    private readonly AiConfigController _controller;
    private readonly List<AiChannel> _channels;
    private readonly List<AiCredential> _credentials;
    private readonly List<AiModelProfile> _models;
    private readonly List<AiPresetModel> _presets;
    private Grid _root = new();
    private Border _statusBar = new();
    private TextBlock _statusText = new();
    private ListView _credentialList = new();
    private ListView _modelList = new();
    private Border _credentialEmptyState = new();
    private Border _modelEmptyState = new();
    private ComboBox _bindingCredentialBox = new();
    private ComboBox _bindingModelBox = new();
    private readonly List<UIElement> _configPages = [];
    private readonly List<Button> _configNavigationButtons = [];

    public ConfigWindow(string initialPage)
    {
        _client = new AiCoreClient(new WindowsAiCoreProcessLauncher());
        _applicationLauncher = new WindowsAiApplicationLauncher();
        _controller = new AiConfigController(new AiCoreApi(_client), new AiConsentCoordinator(_client));
        _channels = _controller.Channels;
        _credentials = _controller.Credentials;
        _models = _controller.Models;
        _presets = _controller.Presets;
        Title = L("AI_ConfigAppTitle");
        BuildContent();
        ConfigureWindow();
        SelectConfigPage(PageIndex(initialPage));
        Closed += async (_, _) => await _client.DisposeAsync();
        _ = InitializeAsync();
    }

    private async void ModelTest_Click(object sender, RoutedEventArgs e)
    {
        SelectModelFromAction(sender);
        await TestModelAsync();
    }

    private async void ModelEdit_Click(object sender, RoutedEventArgs e)
    {
        SelectModelFromAction(sender);
        await EditModelAsync();
    }

    private async void ModelDelete_Click(object sender, RoutedEventArgs e)
    {
        SelectModelFromAction(sender);
        await DeleteModelAsync();
    }

    private void SelectModelFromAction(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is AiModelProfile model)
        {
            _modelList.SelectedItem = model;
        }
    }

    private async Task DeleteBindingAsync()
    {
        try
        {
            await _controller.DeleteBindingAsync();
            _bindingCredentialBox.SelectedItem = null;
            _bindingModelBox.ItemsSource = Array.Empty<AiModelProfile>();
            ShowMessage(L("AI_Saved"), AiMessageSeverity.Success);
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task TestCredentialAsync()
    {
        if (_credentialList.SelectedItem is not AiCredential selected) return;
        try
        {
            var execution = await _controller.TestCredentialAsync(selected, ConfirmConsentAsync);
            if (!execution.Executed) return;
            ShowMessage(L("AI_TestSuccess"), AiMessageSeverity.Success);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task EditModelAsync()
    {
        if (_modelList.SelectedItem is not AiModelProfile selectedModel)
        {
            return;
        }
        var credential = new ComboBox
        {
            Header = L("AI_Credential"),
            ItemsSource = _credentials.Where(item => item.Enabled).ToList(),
            DisplayMemberPath = nameof(AiCredential.DisplayLabel),
            MinWidth = 420,
            SelectedItem = _credentials.FirstOrDefault(item => item.Id == selectedModel.CredentialId)
        };
        var modelId = new TextBox { Header = L("AI_ModelId"), Text = selectedModel.ModelId };
        var displayName = new TextBox { Header = L("AI_ModelName"), Text = selectedModel.DisplayName };
        var stack = new StackPanel { Spacing = 12, Children = { credential, modelId, displayName } };
        if (await DialogAsync(L("AI_EditModel"), stack) != ContentDialogResult.Primary ||
            credential.SelectedItem is not AiCredential selectedCredential)
        {
            return;
        }
        try
        {
            await _controller.UpsertModelAsync(
                selectedModel.Id,
                selectedCredential.Id,
                modelId.Text,
                displayName.Text,
                selectedModel.Source);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task EditCredentialAsync()
    {
        if (_credentialList.SelectedItem is not AiCredential selectedCredential)
        {
            return;
        }
        var channel = new ComboBox
        {
            Header = L("AI_Channel"),
            ItemsSource = _channels.Where(item => item.Enabled).ToList(),
            DisplayMemberPath = nameof(AiChannel.DisplayName),
            MinWidth = 420,
            SelectedItem = _channels.FirstOrDefault(item => item.Id == selectedCredential.ChannelId)
        };
        var label = new TextBox { Header = L("AI_CredentialName"), Text = selectedCredential.Label };
        var endpoint = new TextBox { Header = L("AI_BaseUrl"), Text = selectedCredential.BaseUrl };
        var secret = new PasswordBox { Header = L("AI_ReplaceApiKey") };
        var stack = new StackPanel { Spacing = 12, Children = { channel, label, endpoint, secret } };
        if (await DialogAsync(L("AI_EditCredential"), stack) != ContentDialogResult.Primary ||
            channel.SelectedItem is not AiChannel selectedChannel)
        {
            return;
        }
        try
        {
            await _controller.UpsertCredentialAsync(
                selectedCredential.Id,
                selectedChannel.Id,
                label.Text,
                endpoint.Text,
                string.IsNullOrWhiteSpace(secret.Password) ? null : secret.Password);
            secret.Password = string.Empty;
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task DeleteChannelAsync()
    {
        var customChannels = _channels.Where(item => !item.BuiltIn).ToList();
        var channel = new ComboBox
        {
            Header = L("AI_Channel"),
            ItemsSource = customChannels,
            DisplayMemberPath = nameof(AiChannel.DisplayName),
            MinWidth = 380
        };
        if (await DialogAsync(L("AI_DeleteChannel"), channel) != ContentDialogResult.Primary ||
            channel.SelectedItem is not AiChannel selected)
        {
            return;
        }
        try
        {
            await _controller.DeleteChannelAsync(selected.Id);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private string L(string key) => _loc.Get(key);

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1600, 900));
    }

    public void RestoreFromExternalActivation()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        if (appWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        Activate();
        NativeWindowMethods.SetForegroundWindow(hwnd);
    }

    private void BuildContent()
    {
        InitializeComponent();
        _root = RootLayout;
        _credentialList = CredentialListView;
        _modelList = ModelListView;
        _credentialEmptyState = CredentialEmptyState;
        _modelEmptyState = ModelEmptyState;
        _bindingCredentialBox = BindingCredentialComboBox;
        _bindingModelBox = BindingModelComboBox;
        _statusBar = StatusBorder;
        _statusText = StatusTextBlock;
        _configPages.Add(CredentialsPage);
        _configPages.Add(ModelsPage);
        _configPages.Add(BindingsPage);
        _configNavigationButtons.Add(CredentialsNavButton);
        _configNavigationButtons.Add(ModelsNavButton);
        _configNavigationButtons.Add(BindingsNavButton);

        CredentialsNavButton.Click += (_, _) => SelectConfigPage(0);
        ModelsNavButton.Click += (_, _) => SelectConfigPage(1);
        BindingsNavButton.Click += (_, _) => SelectConfigPage(2);
        OpenChatButton.Click += (_, _) =>
        {
            try { _applicationLauncher.Launch(AiApplication.Chat); }
            catch (Exception exception) { ShowError(exception); }
        };
        AddCredentialButton.Click += async (_, _) => await AddCredentialAsync();
        AddChannelButton.Click += async (_, _) => await AddChannelAsync();
        AddModelButton.Click += async (_, _) => await AddModelAsync();
        BindingCredentialComboBox.SelectionChanged += (_, _) => RefreshBindingModels();
        SaveBindingButton.Click += async (_, _) => await SaveBindingAsync();
        ClearBindingButton.Click += async (_, _) => await DeleteBindingAsync();
        CloseStatusButton.Click += (_, _) => _statusBar.Visibility = Visibility.Collapsed;
    }

    private static int PageIndex(string page) => page.ToLowerInvariant() switch
    {
        "models" => 1,
        "bindings" => 2,
        _ => 0
    };

    private void SelectConfigPage(int selectedIndex)
    {
        for (var index = 0; index < _configPages.Count; index++)
        {
            _configPages[index].Visibility = index == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
            _configNavigationButtons[index].IsEnabled = true;
            _configNavigationButtons[index].Background = index == selectedIndex
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 234, 243, 255))
                : new SolidColorBrush(Colors.Transparent);
            _configNavigationButtons[index].Foreground = index == selectedIndex
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 11, 103, 246))
                : new SolidColorBrush(ColorHelper.FromArgb(255, 54, 65, 82));
            _configNavigationButtons[index].BorderBrush = index == selectedIndex
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 11, 103, 246))
                : new SolidColorBrush(Colors.Transparent);
            _configNavigationButtons[index].BorderThickness = index == selectedIndex
                ? new Thickness(4, 0, 0, 0)
                : new Thickness(0);
        }
    }

    private async void CredentialTest_Click(object sender, RoutedEventArgs e)
    {
        SelectCredentialFromAction(sender);
        await TestCredentialAsync();
    }

    private async void CredentialEdit_Click(object sender, RoutedEventArgs e)
    {
        SelectCredentialFromAction(sender);
        await EditCredentialAsync();
    }

    private async void CredentialDelete_Click(object sender, RoutedEventArgs e)
    {
        SelectCredentialFromAction(sender);
        await DeleteCredentialAsync();
    }

    private void SelectCredentialFromAction(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is AiCredential credential)
        {
            _credentialList.SelectedItem = credential;
        }
    }

    public void NavigateTo(string page)
    {
        SelectConfigPage(PageIndex(page));
        RestoreFromExternalActivation();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _client.ConnectAsync(["ai.config.v1"]);
            await RefreshAllAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task RefreshAllAsync()
    {
        await _controller.RefreshAsync();
        _credentialList.ItemsSource = _credentials.ToList();
        _modelList.ItemsSource = _models.ToList();
        _credentialEmptyState.Visibility = _credentials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _modelEmptyState.Visibility = _models.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _bindingCredentialBox.ItemsSource = _credentials.Where(item => item.Enabled).ToList();
        await ApplyBindingAsync();
    }

    private async Task ApplyBindingAsync()
    {
        var bindings = await _controller.ListBindingsAsync();
        var binding = bindings.FirstOrDefault(item => item.FeatureId == "ai.chat");
        if (binding is null)
        {
            return;
        }

        _bindingCredentialBox.SelectedItem = _credentials.FirstOrDefault(item => item.Id == binding.CredentialId);
        RefreshBindingModels();
        _bindingModelBox.SelectedItem = _models.FirstOrDefault(item => item.Id == binding.ModelProfileId);
    }

    private void RefreshBindingModels()
    {
        var credential = _bindingCredentialBox.SelectedItem as AiCredential;
        _bindingModelBox.ItemsSource = credential is null
            ? Array.Empty<AiModelProfile>()
            : _models.Where(item => item.Enabled && item.CredentialId == credential.Id).ToList();
        if (_bindingModelBox.Items.Count == 1)
        {
            _bindingModelBox.SelectedIndex = 0;
        }
    }

    private async Task AddChannelAsync()
    {
        var name = new TextBox { Header = L("AI_ChannelName"), MinWidth = 420 };
        var endpoint = new TextBox { Header = L("AI_BaseUrl"), PlaceholderText = "https://example.com/v1" };
        var stack = new StackPanel { Spacing = 12, Children = { name, endpoint } };
        if (await DialogAsync(L("AI_AddChannel"), stack) != ContentDialogResult.Primary)
        {
            return;
        }
        try
        {
            await _controller.CreateChannelAsync(name.Text, endpoint.Text);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task AddCredentialAsync()
    {
        var channel = new ComboBox
        {
            Header = L("AI_Channel"),
            ItemsSource = _channels.Where(item => item.Enabled).ToList(),
            DisplayMemberPath = nameof(AiChannel.DisplayName),
            MinWidth = 420
        };
        var label = new TextBox { Header = L("AI_CredentialName") };
        var endpoint = new TextBox { Header = L("AI_BaseUrl"), PlaceholderText = L("AI_BaseUrlDefault") };
        var secret = new PasswordBox { Header = L("AI_ApiKey") };
        var stack = new StackPanel { Spacing = 12, Children = { channel, label, endpoint, secret } };
        if (await DialogAsync(L("AI_AddCredential"), stack) != ContentDialogResult.Primary ||
            channel.SelectedItem is not AiChannel selected)
        {
            return;
        }
        try
        {
            await _controller.UpsertCredentialAsync(
                null,
                selected.Id,
                label.Text,
                endpoint.Text,
                secret.Password);
            secret.Password = string.Empty;
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task DeleteCredentialAsync()
    {
        if (_credentialList.SelectedItem is not AiCredential selected) return;
        try
        {
            await _controller.DeleteCredentialAsync(selected.Id);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task AddModelAsync()
    {
        var credential = new ComboBox
        {
            Header = L("AI_Credential"),
            ItemsSource = _credentials.Where(item => item.Enabled).ToList(),
            DisplayMemberPath = nameof(AiCredential.DisplayLabel),
            MinWidth = 420
        };
        var modelId = new TextBox { Header = L("AI_ModelId") };
        var displayName = new TextBox { Header = L("AI_ModelName") };
        var preset = new ComboBox
        {
            Header = L("AI_PresetModel"),
            DisplayMemberPath = nameof(AiPresetModel.DisplayName),
            PlaceholderText = L("AI_PresetOptional")
        };
        credential.SelectionChanged += (_, _) =>
        {
            if (credential.SelectedItem is AiCredential selectedCredential)
            {
                preset.ItemsSource = _presets.Where(item => item.ChannelId == selectedCredential.ChannelId).ToList();
            }
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is AiPresetModel selectedPreset)
            {
                modelId.Text = selectedPreset.ModelId;
                displayName.Text = selectedPreset.DisplayName;
            }
        };
        var stack = new StackPanel { Spacing = 12, Children = { credential, preset, modelId, displayName } };
        if (await DialogAsync(L("AI_AddModel"), stack) != ContentDialogResult.Primary ||
            credential.SelectedItem is not AiCredential selected)
        {
            return;
        }
        try
        {
            await _controller.UpsertModelAsync(
                null,
                selected.Id,
                modelId.Text,
                string.IsNullOrWhiteSpace(displayName.Text) ? modelId.Text : displayName.Text,
                preset.SelectedItem is AiPresetModel ? "preset" : "custom");
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task TestModelAsync()
    {
        if (_modelList.SelectedItem is not AiModelProfile selected) return;
        var credential = _credentials.FirstOrDefault(item => item.Id == selected.CredentialId);
        if (credential is null)
        {
            ShowMessage(L("AI_SelectConfiguration"), AiMessageSeverity.Warning);
            return;
        }
        try
        {
            var execution = await _controller.TestModelAsync(credential, selected, ConfirmConsentAsync);
            if (!execution.Executed) return;
            ShowMessage(L("AI_TestSuccess"), AiMessageSeverity.Success);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task DeleteModelAsync()
    {
        if (_modelList.SelectedItem is not AiModelProfile selected) return;
        try
        {
            await _controller.DeleteModelAsync(selected.Id);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task SaveBindingAsync()
    {
        if (_bindingCredentialBox.SelectedItem is not AiCredential credential ||
            _bindingModelBox.SelectedItem is not AiModelProfile model)
        {
            ShowMessage(L("AI_SelectConfiguration"), AiMessageSeverity.Warning);
            return;
        }
        try
        {
            await _controller.UpsertBindingAsync(model.Id);
            ShowMessage(L("AI_DefaultSaved"), AiMessageSeverity.Success);
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task<ContentDialogResult> DialogAsync(string title, UIElement content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = L("Action_Save"),
            CloseButtonText = L("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmConsentAsync(string endpoint)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = L("AI_CloudConsentTitle"),
            Content = new TextBlock
            {
                Text = string.Format(L("AI_CloudConsentDescription"), endpoint),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            },
            PrimaryButtonText = L("AI_CloudConsentAllow"),
            CloseButtonText = L("Action_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowError(Exception exception)
    {
        var message = exception is AiCoreException core ? ErrorText(core.Code) : exception.Message;
        ShowMessage(message, AiMessageSeverity.Error);
    }

    private string ErrorText(string? code) => code switch
    {
        "provider_auth_failed" => L("AI_Error_Auth"),
        "provider_model_not_found" => L("AI_Error_Model"),
        "provider_rate_limited" => L("AI_Error_RateLimit"),
        "provider_content_rejected" => L("AI_Error_Content"),
        "context_limit_exceeded" => L("AI_Error_Context"),
        "timeout" => L("AI_Error_Timeout"),
        "conflict" => L("AI_Error_Conflict"),
        "secret_store_unavailable" => L("AI_Error_SecretStore"),
        _ => L("AI_Error_Unavailable")
    };

    private void ShowMessage(string message, AiMessageSeverity severity)
    {
        _statusText.Text = message;
        _statusBar.Background = severity switch
        {
            AiMessageSeverity.Success => new SolidColorBrush(Color.FromArgb(255, 220, 252, 231)),
            AiMessageSeverity.Warning => new SolidColorBrush(Color.FromArgb(255, 254, 243, 199)),
            _ => new SolidColorBrush(Color.FromArgb(255, 254, 226, 226))
        };
        _statusBar.Visibility = Visibility.Visible;
    }

    private enum AiMessageSeverity
    {
        Success,
        Warning,
        Error
    }

    private static SolidColorBrush Brush(string key, Color fallback)
    {
        _ = key;
        return new SolidColorBrush(fallback);
    }

}
