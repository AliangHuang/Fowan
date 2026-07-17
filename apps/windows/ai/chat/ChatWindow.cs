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
using Microsoft.UI.Xaml.Data;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Globalization;
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
    private readonly bool _visualFixture;
    private readonly CollectionViewSource _conversationGroups = new() { IsSourceGrouped = true };
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
    private ChatVisualFixture? _visualFixtureData = null;
    private int _conversationSelectionVersion;
    private int _notificationRefreshQueued;
    private bool _suppressConversationSelection;

    internal ChatWindow(bool visualFixture = false)
    {
        _visualFixture = visualFixture;
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
        if (!_visualFixture)
        {
            _controller.NotificationAsync = HandleNotificationDispatchedAsync;
        }
        Closed += async (_, _) =>
        {
            if (!_visualFixture)
            {
                _controller.NotificationAsync = null;
            }
            await _controller.DisposeAsync();
        };
        if (_visualFixture)
        {
            InitializeVisualFixture();
        }
        else
        {
            _ = InitializeAsync();
        }
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
        if (_visualFixture) return;
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
        HideChatEmptyState();
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
                StopGenerationAfterLocalFailure();
                return;
            }
            var result = execution.Value!;
            if (_controller.AcceptInvocation(result))
            {
                await RefreshAndRenderCurrentConversationAsync();
            }
        }
        catch (Exception exception)
        {
            StopGenerationAfterLocalFailure();
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
        if (_visualFixture)
        {
            appWindow.Move(new PointInt32(0, 0));
        }
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
        _root.Loaded += (_, _) => UpdateToolbarLayout(_root.ActualWidth);
        _root.SizeChanged += (_, args) => UpdateToolbarLayout(args.NewSize.Width);

        NewChatButton.Content = L("AI_NewChat");
        ConversationSearchBox.PlaceholderText = L("AI_SearchConversations");
        InputTextBox.PlaceholderText = L("AI_InputPlaceholder");
        _conversationSearch.TextChanged += (_, _) => FilterConversations();
        _conversationList.SelectionChanged += async (_, args) => await SelectConversationAsync(args);
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
        ChatEmptyStateConfigButton.Click += (_, _) => OpenConfigurationForEmptyState();
        CloseStatusButton.Click += (_, _) => _statusBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateToolbarLayout(double windowWidth)
    {
        var isWide = windowWidth >= 1540;
        var isCompact = !isWide && windowWidth >= 1180;
        var unit = GridUnitType.Pixel;

        CredentialLabelColumn.Width = new GridLength(isCompact || isWide ? 42 : 0, unit);
        CredentialColumn.Width = isCompact || isWide
            ? new GridLength(isWide ? 236 : 168, unit)
            : new GridLength(1, GridUnitType.Star);
        LinkColumn.Width = new GridLength(isCompact || isWide ? (isWide ? 32 : 28) : 0, unit);
        ModelLabelColumn.Width = new GridLength(isCompact || isWide ? 42 : 0, unit);
        ModelColumn.Width = isCompact || isWide
            ? new GridLength(isWide ? 202 : 160, unit)
            : new GridLength(1, GridUnitType.Star);
        ToolbarSpacerColumn.Width = isCompact || isWide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0, unit);
        OpenConfigColumn.Width = new GridLength(isWide ? 230 : isCompact ? 172 : 48, unit);
        PrivacyColumn.Width = new GridLength(isWide ? 200 : 0, unit);

        var showLabels = isCompact || isWide;
        CredentialLabel.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
        CredentialLinkIcon.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
        ModelLabel.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
        OpenConfigText.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
        OpenConfigExternalIcon.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        PrivacyNotice.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
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
            ShowChatEmptyState();
            ShowError(exception);
        }
    }

    private async Task RefreshAllAsync()
    {
        await _controller.RefreshAsync();
        FilterConversations();
        _credentialBox.ItemsSource = _controller.State.Credentials.Where(item => item.Enabled).ToList();
        await ApplyBindingAsync();
        ShowChatEmptyState();
    }

    private void ShowChatEmptyState()
    {
        var hasCredential = _controller.State.Credentials.Any(item => item.Enabled);
        var hasModel = _controller.State.Models.Any(item => item.Enabled &&
            _controller.State.Credentials.Any(credential => credential.Enabled && credential.Id == item.CredentialId));
        var state = ChatConversationGrouping.EmptyState(hasCredential, hasModel);
        var title = state == AiChatEmptyStateKind.Welcome ? L("AI_ChatWelcome") : L("AI_ConfigurationRequired");
        var description = state switch
        {
            AiChatEmptyStateKind.ConfigurationRequired => L("AI_AddCredentialGuide"),
            AiChatEmptyStateKind.ModelRequired => L("AI_AddModelGuide"),
            _ => L("AI_ChatWelcomeDescription")
        };
        ChatEmptyStateTitle.Text = title;
        ChatEmptyStateDescription.Text = description;
        ChatEmptyStateConfigButton.Visibility = state is AiChatEmptyStateKind.ConfigurationRequired or AiChatEmptyStateKind.ModelRequired
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatEmptyStateConfigButton.Tag = state == AiChatEmptyStateKind.ModelRequired ? "models" : "credentials";
        MessageScrollViewer.Visibility = Visibility.Collapsed;
        ChatEmptyState.Visibility = Visibility.Visible;
    }

    private void HideChatEmptyState()
    {
        ChatEmptyState.Visibility = Visibility.Collapsed;
        MessageScrollViewer.Visibility = Visibility.Visible;
    }

    private void OpenConfigurationForEmptyState()
    {
        if (_visualFixture) return;
        try
        {
            _applicationLauncher.Launch(AiApplication.Config, $"--page={ChatEmptyStateConfigButton.Tag as string ?? "credentials"}");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
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
        if (_visualFixture) return;
        _conversationSelectionVersion++;
        _controller.StartNewConversation();
        _conversationList.SelectedItem = null;
        _messagePanel.Children.Clear();
        ShowChatEmptyState();
        _inputBox.Focus(FocusState.Programmatic);
    }

    private async Task SelectConversationAsync(SelectionChangedEventArgs args)
    {
        if (_visualFixture || _suppressConversationSelection) return;
        var selected = args.AddedItems.OfType<AiConversationSummary>().LastOrDefault();
        if (selected is null)
        {
            return;
        }

        var selectionVersion = ++_conversationSelectionVersion;
        try
        {
            var conversation = await _controller.GetConversationAsync(selected.Id);
            if (selectionVersion != _conversationSelectionVersion)
            {
                return;
            }
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
        _messagePanel.Children.Clear();
        var timeline = messages.ToList();
        if (timeline.Count == 0)
        {
            ShowChatEmptyState();
            return;
        }
        HideChatEmptyState();
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
        _regenerateButton.Visibility = timeline.Any(item => item.Role == "assistant")
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScrollToEnd();
    }

    private async Task SendAsync()
    {
        if (_visualFixture) return;
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
        HideChatEmptyState();
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
                StopGenerationAfterLocalFailure();
                return;
            }
            var result = execution.Value!;
            if (_controller.AcceptInvocation(result))
            {
                await RefreshAndRenderCurrentConversationAsync();
            }
        }
        catch (Exception exception)
        {
            StopGenerationAfterLocalFailure();
            ShowError(exception);
        }
    }

    private async Task StopAsync()
    {
        if (_visualFixture) return;
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
                case AiProtocolNotifications.ChatStarted:
                    var started = args.Parameters.Deserialize<AiChatStarted>();
                    if (started is not null && _sendButton.IsEnabled == false)
                    {
                        _controller.AdoptInvocation(started);
                    }
                    break;
                case AiProtocolNotifications.ChatCompleted:
                case AiProtocolNotifications.ChatCancelled:
                case AiProtocolNotifications.ChatFailed:
                    var finished = args.Parameters.Deserialize<AiChatFinished>();
                    if (finished is not null && _controller.FinishInvocation(finished.InvocationId))
                    {
                        SetGenerating(false);
                        if (args.Method == AiProtocolNotifications.ChatFailed)
                        {
                            _statusPresenter.ShowMessage(_statusPresenter.ErrorText(finished.ErrorCode), ChatMessageSeverity.Error);
                        }
                        QueueNotificationRefresh();
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            StopGenerationAfterLocalFailure();
            ShowError(exception);
        }
    }

    private async Task RefreshConversationsAsync()
    {
        await _controller.RefreshConversationsAsync();
        FilterConversations();
    }

    private async Task RefreshAndRenderCurrentConversationAsync()
    {
        await RefreshConversationsAsync();
        if (_controller.State.CurrentConversationId is not null)
        {
            var conversation = await _controller.GetConversationAsync(_controller.State.CurrentConversationId);
            RenderMessages(conversation.Messages);
        }
    }

    private void QueueNotificationRefresh()
    {
        if (Interlocked.Exchange(ref _notificationRefreshQueued, 1) != 0)
        {
            return;
        }

        _ = RefreshAfterNotificationAsync();
    }

    private async Task RefreshAfterNotificationAsync()
    {
        try
        {
            await Task.Yield();
            await RefreshAndRenderCurrentConversationAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _notificationRefreshQueued, 0);
        }
    }

    private void FilterConversations()
    {
        var query = _conversationSearch.Text.Trim();
        var conversations = _visualFixtureData?.Conversations ?? _controller.State.Conversations;
        var filtered = string.IsNullOrEmpty(query)
            ? conversations
            : conversations.Where(item => item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        var selectedId = (_conversationList.SelectedItem as AiConversationSummary)?.Id ?? _controller.State.CurrentConversationId;
        var grouped = ChatConversationGrouping.Group(
            filtered,
            DateTimeOffset.Now,
            L("AI_Today"),
            L("AI_Yesterday"),
            CultureInfo.CurrentCulture);
        var selected = selectedId is null
            ? null
            : grouped.SelectMany(group => group).FirstOrDefault(item => item.Id == selectedId);
        _suppressConversationSelection = true;
        try
        {
            _conversationGroups.Source = grouped;
            _conversationList.ItemsSource = _conversationGroups.View;
            _conversationList.SelectedItem = selected;
        }
        finally
        {
            _suppressConversationSelection = false;
        }
    }

    private void SetGenerating(bool generating)
    {
        _sendButton.IsEnabled = !generating;
        _regenerateButton.IsEnabled = !generating;
        _stopButton.IsEnabled = generating;
        _regenerateButton.Visibility = generating || _controller.State.CurrentConversationId is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!generating)
        {
            _streamingText = null;
        }
    }

    private void StopGenerationAfterLocalFailure()
    {
        if (_controller.State.IsGenerating)
        {
            _controller.CompleteInvocation();
        }
        SetGenerating(false);
    }

    private void ScrollToEnd()
    {
        _messageScroll.UpdateLayout();
        _messageScroll.ChangeView(null, _messageScroll.ScrollableHeight, null, true);
    }

    private void ShowError(Exception exception) => _statusPresenter.ShowError(exception);

    private void InitializeVisualFixture()
    {
#if DEBUG
        _visualFixtureData = ChatVisualFixture.Create();
        _credentialBox.ItemsSource = new[] { _visualFixtureData.Credential };
        _credentialBox.SelectedItem = _visualFixtureData.Credential;
        _modelBox.ItemsSource = new[] { _visualFixtureData.Model };
        _modelBox.SelectedItem = _visualFixtureData.Model;
        FilterConversations();
        _conversationList.SelectedItem = _visualFixtureData.Conversations[0];
        RenderMessages(_visualFixtureData.Messages);
        _regenerateButton.Visibility = Visibility.Collapsed;
        _stopButton.IsEnabled = true;
#else
        throw new InvalidOperationException("The visual fixture is available only in Debug builds.");
#endif
    }

}
