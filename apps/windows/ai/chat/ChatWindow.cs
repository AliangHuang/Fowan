using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Services;
using Fowan.Ai.Shared.Application.Ports;
using Fowan.Ai.Chat.Windows.Platform.Windows;
using Fowan.Ai.Chat.Windows.Presentation;
using Fowan.Ai.Chat.Windows.Coordination;
using Fowan.Windows.Platform.Contracts;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Graphics;
using Windows.UI;

namespace Fowan.Ai.Chat.Windows;

public sealed partial class ChatWindow : Window
{
    private readonly AiLocalizationService _loc = new();
    private readonly IAiApplicationLauncher _applicationLauncher;
    private readonly AiChatSession _controller;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IClipboardService _clipboard = new WindowsClipboardService();
    private readonly ChatMessageView _messageView;
    private readonly ChatDialogPresenter _dialogs;
    private readonly ChatStatusPresenter _statusPresenter;
    private readonly ChatConversationCoordinator _conversationCoordinator;
    private Grid _root = new();
    private ListView _conversationList = new();
    private TextBox _conversationSearch = new();
    private StackPanel _messagePanel = new();
    private ScrollViewer _messageScroll = new();
    private ComboBox _credentialBox = new();
    private ComboBox _modelBox = new();
    private TextBox _inputBox = new();
    private Button _sendButton = new();
    private Button _stopButton = new();
    private Button _regenerateButton = new();
    private Border _statusBar = new();
    private TextBlock _statusText = new();
    private TextBlock? _streamingText;

    public ChatWindow()
    {
        _applicationLauncher = new WindowsAiApplicationLauncher();
        _controller = AiChatCompositionRoot.CreateSession();
        _messageView = new ChatMessageView(L, _clipboard);
        _uiDispatcher = new DispatcherQueueUiDispatcher(DispatcherQueue);
        _dialogs = new ChatDialogPresenter(L, () => _root.XamlRoot);
        _statusPresenter = new ChatStatusPresenter(L, () => _controller.State.Channels, () => _statusBar, () => _statusText);
        _conversationCoordinator = new ChatConversationCoordinator(
            _controller, _dialogs, () => _conversationList.SelectedItem as AiConversationSummary,
            RefreshConversationsAsync, StartNewConversation, ShowError, L);
        Title = L("AI_ChatAppTitle");
        BuildContent();
        ConfigureWindow();
        _controller.NotificationAsync = HandleNotificationDispatchedAsync;
        Closed += async (_, _) =>
        {
            _controller.NotificationAsync = null;
            await _controller.DisposeAsync();
        };
        _ = InitializeAsync();
    }

    private async void RenameConversationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiConversationSummary conversation)
        {
            _conversationList.SelectedItem = conversation;
        }
        await RenameConversationAsync();
    }

    private async void DeleteConversationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiConversationSummary conversation)
        {
            _conversationList.SelectedItem = conversation;
        }
        await DeleteConversationAsync();
    }

    private Task RenameConversationAsync() => _conversationCoordinator.RenameAsync();

    private Task DeleteConversationAsync() => _conversationCoordinator.DeleteAsync();

    private async Task RegenerateAsync()
    {
        if (_controller.State.CurrentConversationId is null ||
            _credentialBox.SelectedItem is not AiCredential credential ||
            _modelBox.SelectedItem is not AiModelProfile model)
        {
            _statusPresenter.ShowMessage(L("AI_SelectConversation"), ChatMessageSeverity.Warning);
            return;
        }

        if (!await _controller.EnsureConsentAsync(credential.BaseUrl, ConfirmConsentAsync))
        {
            return;
        }

        _controller.BeginGeneration();
        var assistant = _messageView.Bubble("assistant", string.Empty, $"{_statusPresenter.ChannelName(credential.ChannelId)} · {model.ModelId}");
        _streamingText = assistant.Tag as TextBlock;
        _messagePanel.Children.Add(assistant);
        SetGenerating(true);
        try
        {
            var execution = await _controller.RegenerateAsync(
                credential,
                model,
                _controller.State.CurrentConversationId,
                ConfirmConsentAsync);
            if (!execution.Executed)
            {
                SetGenerating(false);
                return;
            }
            var result = execution.Value!;
            _controller.AcceptInvocation(result);
        }
        catch (Exception exception)
        {
            SetGenerating(false);
            ShowError(exception);
        }
    }

    private Task<bool> ConfirmConsentAsync(string endpoint) => _dialogs.ConfirmCloudConsentAsync(endpoint);

    private string L(string key) => _loc.Get(key);

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1600, 900));
        if (TitleBarDragRegion is not null)
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDragRegion);
        }
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 23, 28, 38);
        appWindow.TitleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 105, 115, 134);
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
        _conversationList = ConversationListView;
        _conversationSearch = ConversationSearchBox;
        _messagePanel = MessageStackPanel;
        _messageScroll = MessageScrollViewer;
        _credentialBox = CredentialComboBox;
        _modelBox = ModelComboBox;
        _inputBox = InputTextBox;
        _sendButton = SendButton;
        _stopButton = StopButton;
        _regenerateButton = RegenerateButton;
        _statusBar = StatusBorder;
        _statusText = StatusTextBlock;

        NewChatButton.Content = L("AI_NewChat");
        ConversationSearchBox.PlaceholderText = L("AI_SearchConversations");
        InputTextBox.PlaceholderText = L("AI_InputPlaceholder");
        _conversationSearch.TextChanged += (_, _) => FilterConversations();
        _conversationList.SelectionChanged += async (_, _) => await SelectConversationAsync();
        _credentialBox.SelectionChanged += (_, _) => RefreshChatModels();
        NewChatButton.Click += (_, _) => StartNewConversation();
        OpenConfigButton.Click += (_, _) =>
        {
            try { _applicationLauncher.Launch(AiApplication.Config); }
            catch (Exception exception) { ShowError(exception); }
        };
        _regenerateButton.Click += async (_, _) => await RegenerateAsync();
        _stopButton.Click += async (_, _) => await StopAsync();
        _sendButton.Click += async (_, _) => await SendAsync();
        CloseStatusButton.Click += (_, _) => _statusBar.Visibility = Visibility.Collapsed;
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _controller.ConnectAsync();
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
        FilterConversations();
        _credentialBox.ItemsSource = _controller.State.Credentials.Where(item => item.Enabled).ToList();
        await ApplyBindingAsync();
        if (_controller.State.Conversations.Length == 0)
        {
            ShowChatEmptyState();
        }
    }

    private void ShowChatEmptyState()
    {
        var hasCredential = _controller.State.Credentials.Any(item => item.Enabled);
        var hasModel = _controller.State.Models.Any(item => item.Enabled &&
            _controller.State.Credentials.Any(credential => credential.Enabled && credential.Id == item.CredentialId));
        var title = hasCredential && hasModel ? L("AI_ChatWelcome") : L("AI_ConfigurationRequired");
        var description = !hasCredential
            ? L("AI_AddCredentialGuide")
            : !hasModel ? L("AI_AddModelGuide") : L("AI_ChatWelcomeDescription");
        var stack = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520
        };
        stack.Children.Add(new FontIcon { Glyph = "\uE950", FontSize = 36 });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = Brush("TextFillColorSecondaryBrush", Colors.Gray)
        });
        if (!hasCredential || !hasModel)
        {
            var configure = new Button
            {
                Content = L("AI_OpenConfigApp"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(16, 9, 16, 9)
            };
            var page = hasCredential ? "models" : "credentials";
            configure.Click += (_, _) =>
            {
                try { _applicationLauncher.Launch(AiApplication.Config, $"--page={page}"); }
                catch (Exception exception) { ShowError(exception); }
            };
            stack.Children.Add(configure);
        }
        _messagePanel.Children.Add(new Border
        {
            Margin = new Thickness(0, 150, 0, 0),
            Padding = new Thickness(28),
            CornerRadius = new CornerRadius(12),
            Background = Brush("CardBackgroundFillColorDefaultBrush", ColorHelper.FromArgb(255, 248, 250, 252)),
            Child = stack
        });
    }

    private async Task ApplyBindingAsync()
    {
        var bindings = await _controller.ListBindingsAsync();
        var binding = bindings.FirstOrDefault(item => item.FeatureId == "ai.chat");
        if (binding is null)
        {
            return;
        }

        _credentialBox.SelectedItem = _controller.State.Credentials.FirstOrDefault(item => item.Id == binding.CredentialId);
        RefreshChatModels();
        _modelBox.SelectedItem = _controller.State.Models.FirstOrDefault(item => item.Id == binding.ModelProfileId);
    }

    private void RefreshChatModels()
    {
        var credential = _credentialBox.SelectedItem as AiCredential;
        _modelBox.ItemsSource = credential is null
            ? Array.Empty<AiModelProfile>()
            : _controller.State.Models.Where(item => item.Enabled && item.CredentialId == credential.Id).ToList();
        if (_modelBox.Items.Count == 1)
        {
            _modelBox.SelectedIndex = 0;
        }
    }

    private void StartNewConversation()
    {
        _controller.StartNewConversation();
        _conversationList.SelectedItem = null;
        _messagePanel.Children.Clear();
        _inputBox.Focus(FocusState.Programmatic);
    }

    private async Task SelectConversationAsync()
    {
        if (_conversationList.SelectedItem is not AiConversationSummary selected)
        {
            return;
        }

        try
        {
            var conversation = await _controller.GetConversationAsync(selected.Id);
            _controller.SelectConversation(conversation.Id);
            RenderMessages(conversation.Messages);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void RenderMessages(IEnumerable<AiChatMessage> messages)
    {
        ChatEmptyState.Visibility = Visibility.Collapsed;
        _messagePanel.Children.Clear();
        var timeline = messages.ToList();
        var renderedVariants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in timeline)
        {
            if (message.Role == "assistant" && message.ParentMessageId is not null)
            {
                if (!renderedVariants.Add(message.ParentMessageId))
                {
                    continue;
                }
                var variants = timeline
                    .Where(item => item.Role == "assistant" && item.ParentMessageId == message.ParentMessageId)
                    .OrderBy(item => item.VariantIndex)
                    .ToList();
                _messagePanel.Children.Add(_messageView.VariantGroup(variants));
                continue;
            }
            _messagePanel.Children.Add(_messageView.Bubble(message));
        }
        ScrollToEnd();
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(_inputBox.Text) ||
            _credentialBox.SelectedItem is not AiCredential credential ||
            _modelBox.SelectedItem is not AiModelProfile model)
        {
            _statusPresenter.ShowMessage(L("AI_SelectConfiguration"), ChatMessageSeverity.Warning);
            return;
        }

        var text = _inputBox.Text.Trim();
        if (!await _controller.EnsureConsentAsync(credential.BaseUrl, ConfirmConsentAsync))
        {
            return;
        }
        _inputBox.Text = string.Empty;
        ChatEmptyState.Visibility = Visibility.Collapsed;
        _messagePanel.Children.Add(_messageView.Bubble("user", text, null));
        _controller.BeginGeneration();
        var assistant = _messageView.Bubble("assistant", string.Empty, $"{_statusPresenter.ChannelName(credential.ChannelId)} · {model.ModelId}");
        _streamingText = assistant.Tag as TextBlock;
        _messagePanel.Children.Add(assistant);
        ScrollToEnd();
        SetGenerating(true);
        try
        {
            var execution = await _controller.SendAsync(
                credential,
                model,
                _controller.State.CurrentConversationId,
                text,
                ConfirmConsentAsync);
            if (!execution.Executed)
            {
                SetGenerating(false);
                return;
            }
            var result = execution.Value!;
            _controller.AcceptInvocation(result);
        }
        catch (Exception exception)
        {
            SetGenerating(false);
            ShowError(exception);
        }
    }

    private async Task StopAsync()
    {
        if (_controller.State.ActiveInvocationId is null)
        {
            return;
        }
        try
        {
            await _controller.CancelAsync(_controller.State.ActiveInvocationId);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private Task HandleNotificationDispatchedAsync(
        AiCoreNotificationEventArgs args,
        CancellationToken cancellationToken) =>
        _uiDispatcher.InvokeAsync(() => HandleNotificationAsync(args), cancellationToken);

    private async Task HandleNotificationAsync(AiCoreNotificationEventArgs args)
    {
        try
        {
            switch (args.Method)
            {
                case AiProtocolNotifications.ChatDelta:
                    var delta = args.Parameters.Deserialize<AiChatDelta>();
                    if (_controller.State.ActiveInvocationId is null && _sendButton.IsEnabled == false && delta is not null)
                    {
                        _controller.AdoptInvocation(delta.InvocationId);
                    }
                    if (delta is not null && delta.InvocationId == _controller.State.ActiveInvocationId && _streamingText is not null)
                    {
                        _streamingText.Text = _controller.AppendDelta(delta.Delta);
                        ScrollToEnd();
                    }
                    break;
                case AiProtocolNotifications.ChatCompleted:
                case AiProtocolNotifications.ChatCancelled:
                case AiProtocolNotifications.ChatFailed:
                    var finished = args.Parameters.Deserialize<AiChatFinished>();
                    if (finished is not null && finished.InvocationId == _controller.State.ActiveInvocationId)
                    {
                        SetGenerating(false);
                        if (args.Method == AiProtocolNotifications.ChatFailed)
                        {
                            _statusPresenter.ShowMessage(_statusPresenter.ErrorText(finished.ErrorCode), ChatMessageSeverity.Error);
                        }
                        await RefreshConversationsAsync();
                        if (_controller.State.CurrentConversationId is not null)
                        {
                            var conversation = await _controller.GetConversationAsync(_controller.State.CurrentConversationId);
                            RenderMessages(conversation.Messages);
                        }
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            SetGenerating(false);
            ShowError(exception);
        }
    }

    private async Task RefreshConversationsAsync()
    {
        await _controller.RefreshConversationsAsync();
        FilterConversations();
    }

    private void FilterConversations()
    {
        var query = _conversationSearch.Text.Trim();
        _conversationList.ItemsSource = string.IsNullOrEmpty(query)
            ? _controller.State.Conversations.ToList()
            : _controller.State.Conversations
                .Where(item => item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
    }

    private void SetGenerating(bool generating)
    {
        _sendButton.IsEnabled = !generating;
        _regenerateButton.IsEnabled = !generating;
        _stopButton.IsEnabled = generating;
        if (!generating)
        {
            _controller.CompleteInvocation();
            _streamingText = null;
        }
    }

    private void ScrollToEnd()
    {
        _messageScroll.UpdateLayout();
        _messageScroll.ChangeView(null, _messageScroll.ScrollableHeight, null, true);
    }

    private void ShowError(Exception exception) => _statusPresenter.ShowError(exception);


    private static SolidColorBrush Brush(string key, Color fallback)
    {
        _ = key;
        return new SolidColorBrush(fallback);
    }

}
