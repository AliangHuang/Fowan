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
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Globalization;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.Core;
using Windows.System;

namespace Fowan.Ai.Chat.Windows;

public sealed partial class ChatWindow : Window
{
    private const int DesignWindowWidth = 1920;
    private const int DesignWindowHeight = 1080;

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
    private ListView _messagePanel = new();
    private ComboBox _credentialBox = new();
    private ComboBox _modelBox = new();
    private TextBox _inputBox = new();
    private Border _composerBorder = new();
    private Button _primaryActionButton = new();
    private FontIcon _primaryActionIcon = new();
    private readonly List<Button> _regenerateMessageButtons = [];
    private Border _statusBar = new();
    private TextBlock _statusText = new();
    private TextBlock _contextUsageText = new();
    private CancellationTokenSource? _estimateCts;
    private TextBlock? _streamingText;
    private ChatVisualFixture? _visualFixtureData = null;
    private int _conversationSelectionVersion;
    private int _notificationRefreshQueued;
    private bool _suppressConversationSelection;
    private string? _pendingCompressionDraft;
    private string? _pendingCompressionConversationId;
    private string? _pendingCompressionModelId;
    private string? _pendingCompressionBranchLeafId;
    private string? _activeBranchLeafMessageId;
    private readonly List<AiChatMessage> _loadedMessages = [];
    private string? _nextMessageCursor;
    private bool _hasMoreMessages;
    private TaskCompletionSource<bool>? _terminalCompletion;
    private Brush _composerRestingBorderBrush = new SolidColorBrush(Colors.Transparent);
    private Brush _composerFocusedBorderBrush = new SolidColorBrush(Colors.Transparent);

    internal ChatWindow(bool visualFixture = false)
    {
        _visualFixture = visualFixture;
        _applicationLauncher = new WindowsAiApplicationLauncher();
        _controller = AiChatCompositionRoot.CreateSession();
        _controller.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLifecycleControls);
        _messageView = new ChatMessageView(
            L,
            _clipboard,
            RegenerateMessageAsync,
            SelectBranchAsync,
            button => _regenerateMessageButtons.Add(button),
            AiChatCompositionRoot.LoadCurrentToolboxAvatar());
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

    private async Task DeleteConversationAsync()
    {
        if (await StopGenerationBeforeNavigationAsync()) await _conversationCoordinator.DeleteAsync();
    }

    private async Task RegenerateMessageAsync(AiChatMessage message)
    {
        if (_visualFixture ||
            _controller.State.LifecycleState != AiChatLifecycleState.Idle ||
            message.Role != "assistant" ||
            string.IsNullOrWhiteSpace(message.ParentMessageId))
        {
            return;
        }

        if (HasSubsequentMessages(message) && !await _dialogs.ConfirmRegenerateAfterMessageAsync())
        {
            return;
        }

        await RegenerateAsync(message.ParentMessageId);
    }

    private bool HasSubsequentMessages(AiChatMessage message)
    {
        var messagesById = _loadedMessages.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return _loadedMessages.Any(candidate =>
            candidate.Id != message.Id && IsDescendantOf(candidate, message.Id, messagesById));
    }

    private static bool IsDescendantOf(AiChatMessage message, string ancestorId,
        IReadOnlyDictionary<string, AiChatMessage> messagesById)
    {
        var parentId = message.ParentMessageId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (parentId == ancestorId)
            {
                return true;
            }
            if (!messagesById.TryGetValue(parentId, out var parent))
            {
                return false;
            }
            parentId = parent.ParentMessageId;
        }
        return false;
    }

    private async Task RegenerateAsync(string userMessageId)
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
        _terminalCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        HideChatEmptyState();
        var assistant = _messageView.Bubble("assistant", string.Empty, $"{_statusPresenter.ChannelName(credential.ChannelId)} · {model.ModelId}");
        _streamingText = assistant.Tag as TextBlock;
        _messagePanel.Items.Add(assistant);
        UpdateLifecycleControls();
        try
        {
            var execution = await _controller.RegenerateAsync(
                credential,
                model,
                _controller.State.CurrentConversationId,
                ConfirmConsentAsync,
                userMessageId);
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
        if (_visualFixture)
        {
            appWindow.Resize(new SizeInt32(1600, 900));
            appWindow.Move(new PointInt32(0, 0));
        }
        else
        {
            var scale = Math.Clamp(NativeWindowMethods.GetDpiForWindow(hwnd) / 96.0, 1.0, 3.0);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var width = Math.Min(workArea.Width, (int)Math.Round(DesignWindowWidth * scale));
            var height = Math.Min(workArea.Height, (int)Math.Round(DesignWindowHeight * scale));
            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
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
        _credentialBox = CredentialComboBox;
        _modelBox = ModelComboBox;
        _inputBox = InputTextBox;
        _composerBorder = ComposerBorder;
        _primaryActionButton = PrimaryActionButton;
        _primaryActionIcon = PrimaryActionIcon;
        _statusBar = StatusBorder;
        _statusText = StatusTextBlock;
        _contextUsageText = ContextUsageTextBlock;
        _root.Loaded += (_, _) => UpdateToolbarLayout(_root.ActualWidth);
        _root.SizeChanged += (_, args) => UpdateToolbarLayout(args.NewSize.Width);

        NewChatButton.Content = L("AI_NewChat");
        ConversationSearchBox.PlaceholderText = L("AI_SearchConversations");
        InputTextBox.PlaceholderText = L("AI_InputPlaceholder");
        _conversationSearch.TextChanged += (_, _) => FilterConversations();
        _conversationList.SelectionChanged += async (_, args) => await SelectConversationAsync(args);
        _credentialBox.SelectionChanged += (_, _) => RefreshChatModels();
        _modelBox.SelectionChanged += (_, _) => _ = UpdateContextEstimateAsync();
        _inputBox.TextChanged += (_, _) => _ = UpdateContextEstimateAsync();
        _inputBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(InputBox_KeyDown), true);
        _inputBox.GotFocus += (_, _) => SetComposerFocus(true);
        _inputBox.LostFocus += (_, _) => SetComposerFocus(false);
        ComposerInputHost.Tapped += (_, _) => _inputBox.Focus(FocusState.Pointer);
        _composerRestingBorderBrush = (Brush)Application.Current.Resources["AiBorderBrush"];
        _composerFocusedBorderBrush = (Brush)Application.Current.Resources["AiPrimaryBrush"];
        NewChatButton.Click += async (_, _) => { if (await StopGenerationBeforeNavigationAsync()) StartNewConversation(); };
        OpenConfigButton.Click += (_, _) =>
        {
            try { _applicationLauncher.Launch(AiApplication.Config); }
            catch (Exception exception) { ShowError(exception); }
        };
        _primaryActionButton.Click += async (_, _) => await ExecutePrimaryActionAsync();
        ChatEmptyStateConfigButton.Click += (_, _) => OpenConfigurationForEmptyState();
        CloseStatusButton.Click += (_, _) => _statusBar.Visibility = Visibility.Collapsed;
    }

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (shift) return;
        e.Handled = true;
        if (_controller.State.LifecycleState != AiChatLifecycleState.Idle) return;
        await SendAsync();
    }

    private Task ExecutePrimaryActionAsync() => _controller.State.LifecycleState switch
    {
        AiChatLifecycleState.Generating or AiChatLifecycleState.Compacting => StopAsync(),
        AiChatLifecycleState.Idle => SendAsync(),
        _ => Task.CompletedTask
    };

    private void SetComposerFocus(bool focused) =>
        _composerBorder.BorderBrush = focused ? _composerFocusedBorderBrush : _composerRestingBorderBrush;

    private async Task UpdateContextEstimateAsync()
    {
        _estimateCts?.Cancel();
        _estimateCts?.Dispose();
        _estimateCts = new CancellationTokenSource();
        var cancellationToken = _estimateCts.Token;
        try
        {
            await Task.Delay(300, cancellationToken);
            if (_visualFixture || _modelBox.SelectedItem is not AiModelProfile model) return;
            if (_controller.State.LifecycleState != AiChatLifecycleState.Idle) return;
            if (!model.LimitsConfigured)
            {
                _contextUsageText.Text = "限制待配置，补全后才能发送";
                _contextUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.DarkOrange);
                return;
            }
            var estimate = await _controller.EstimateContextAsync(model, _inputBox.Text,
                _activeBranchLeafMessageId, cancellationToken);
            _contextUsageText.Text = $"约 {estimate.EstimatedInputTokens:N0} / {estimate.ContextWindowTokens:N0} Token";
            _contextUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                estimate.Action is "warning" or "compression_required" or "too_large" ? Colors.DarkOrange : Colors.SlateGray);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _contextUsageText.Text = exception.Message; }
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
        MessageStackPanel.Visibility = Visibility.Collapsed;
        ChatEmptyState.Visibility = Visibility.Visible;
    }

    private void HideChatEmptyState()
    {
        ChatEmptyState.Visibility = Visibility.Collapsed;
        MessageStackPanel.Visibility = Visibility.Visible;
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
        _activeBranchLeafMessageId = null;
        _conversationList.SelectedItem = null;
        _messagePanel.Items.Clear();
        _loadedMessages.Clear();
        _nextMessageCursor = null;
        _hasMoreMessages = false;
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
        if (!await StopGenerationBeforeNavigationAsync()) return;

        var selectionVersion = ++_conversationSelectionVersion;
        try
        {
            var page = await _controller.ListMessagesAsync(selected.Id);
            if (selectionVersion != _conversationSelectionVersion)
            {
                return;
            }
            _controller.SelectConversation(selected.Id);
            ApplyMessagePage(page, true);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void RenderMessages(IEnumerable<AiChatMessage> messages, AiConversationSummaryRecord? summary = null,
        string? activeLeafMessageId = null)
    {
        _messagePanel.Items.Clear();
        _regenerateMessageButtons.Clear();
        var timeline = messages.ToList();
        _activeBranchLeafMessageId = activeLeafMessageId;
        if (timeline.Count == 0)
        {
            ShowChatEmptyState();
            return;
        }
        HideChatEmptyState();
        if (_hasMoreMessages)
        {
            var loadMore = new Button
            {
                Content = "加载更早消息",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(16, 6, 16, 6)
            };
            loadMore.Click += async (_, _) => await LoadMoreMessagesAsync(loadMore);
            _messagePanel.Items.Add(loadMore);
        }
        if (summary is not null)
        {
            _messagePanel.Items.Add(new Expander
            {
                Header = "较早对话摘要（本地加密保存）",
                Content = new TextBlock { Text = summary.Content, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }
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
                var selectedVariantId = variants.FirstOrDefault(variant =>
                    variant.Id == activeLeafMessageId || timeline.Any(item =>
                        item.Role == "user" && item.ParentMessageId == variant.Id))?.Id;
                _messagePanel.Items.Add(_messageView.VariantGroup(variants, selectedVariantId));
                continue;
            }
            _messagePanel.Items.Add(_messageView.Bubble(message));
        }
        UpdateLifecycleControls();
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
        if (!model.LimitsConfigured)
        {
            _statusPresenter.ShowMessage("模型限制待配置，请先在配置中心补全。", ChatMessageSeverity.Warning);
            return;
        }
        var estimate = await _controller.EstimateContextAsync(model, text, _activeBranchLeafMessageId);
        if (estimate.Action == "compression_required")
        {
            if (_controller.State.CurrentConversationId is null || !await _dialogs.ConfirmCompressionAsync()) return;
            if (!await _controller.EnsureConsentAsync(credential.BaseUrl, ConfirmConsentAsync)) return;
            _pendingCompressionDraft = text;
            _pendingCompressionConversationId = _controller.State.CurrentConversationId;
            _pendingCompressionModelId = model.Id;
            _pendingCompressionBranchLeafId = _activeBranchLeafMessageId;
            await _controller.CompactAsync(credential, model, _pendingCompressionConversationId,
                _activeBranchLeafMessageId);
            _terminalCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            UpdateLifecycleControls();
            _statusPresenter.ShowMessage("正在压缩较早对话，可点击停止取消。", ChatMessageSeverity.Warning);
            return;
        }
        if (estimate.Action == "too_large")
        {
            _statusPresenter.ShowMessage("单条消息无法装入当前模型，请新建对话或选择更大窗口模型。", ChatMessageSeverity.Warning);
            return;
        }
        if (!await _controller.EnsureConsentAsync(credential.BaseUrl, ConfirmConsentAsync))
        {
            return;
        }
        _inputBox.Text = string.Empty;
        HideChatEmptyState();
        _messagePanel.Items.Add(_messageView.Bubble("user", text, null));
        _controller.BeginGeneration();
        _terminalCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var assistant = _messageView.Bubble("assistant", string.Empty, $"{_statusPresenter.ChannelName(credential.ChannelId)} · {model.ModelId}");
        _streamingText = assistant.Tag as TextBlock;
        _messagePanel.Items.Add(assistant);
        ScrollToEnd();
        UpdateLifecycleControls();
        try
        {
            var execution = await _controller.SendAsync(
                credential,
                model,
                _controller.State.CurrentConversationId,
                text,
                ConfirmConsentAsync,
                _activeBranchLeafMessageId);
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
                    var delta = args.DeserializeParameters<AiChatDelta>();
                    if (_controller.State.ActiveInvocationId is null &&
                        _controller.State.LifecycleState == AiChatLifecycleState.Generating)
                    {
                        _controller.AdoptInvocation(delta.InvocationId);
                    }
                    if (delta.InvocationId == _controller.State.ActiveInvocationId && _streamingText is not null)
                    {
                        _streamingText.Text = _controller.AppendDelta(delta.Delta);
                        ScrollToEnd();
                    }
                    break;
                case AiProtocolNotifications.ChatStarted:
                    var started = args.DeserializeParameters<AiChatStarted>();
                    if (_controller.State.LifecycleState == AiChatLifecycleState.Generating)
                    {
                        _controller.AdoptInvocation(started);
                    }
                    break;
                case AiProtocolNotifications.ChatCompleted:
                case AiProtocolNotifications.ChatCancelled:
                case AiProtocolNotifications.ChatFailed:
                    var finished = args.DeserializeParameters<AiChatFinished>();
                    if (_controller.FinishInvocation(finished.InvocationId))
                    {
                        _terminalCompletion?.TrySetResult(true);
                        UpdateLifecycleControls();
                        if (args.Method == AiProtocolNotifications.ChatFailed)
                        {
                            _statusPresenter.ShowMessage(_statusPresenter.ErrorText(finished.ErrorCode), ChatMessageSeverity.Error);
                        }
                        QueueNotificationRefresh();
                    }
                    break;
                case AiProtocolNotifications.ContextCompactStarted:
                    var compactStarted = args.DeserializeParameters<AiCompactStarted>();
                    _controller.AdoptInvocation(compactStarted.InvocationId);
                    break;
                case AiProtocolNotifications.ContextCompactCompleted:
                    var compactCompleted = args.DeserializeParameters<AiCompactCompleted>();
                    if (_controller.State.ActiveInvocationId == compactCompleted.InvocationId)
                    {
                        _terminalCompletion?.TrySetResult(true);
                        _controller.CompleteInvocation();
                        UpdateLifecycleControls();
                        DispatcherQueue.TryEnqueue(async () => await ContinueAfterCompressionAsync());
                    }
                    break;
                case AiProtocolNotifications.ContextCompactFailed:
                    var compactFailed = args.DeserializeParameters<AiCompactFailed>();
                    if (_controller.State.ActiveInvocationId == compactFailed.InvocationId)
                    {
                        _terminalCompletion?.TrySetResult(true);
                        _controller.CompleteInvocation(); UpdateLifecycleControls();
                        _statusPresenter.ShowMessage(_statusPresenter.ErrorText(compactFailed.ErrorCode), ChatMessageSeverity.Error);
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

    private async Task ContinueAfterCompressionAsync()
    {
        var draft = _pendingCompressionDraft;
        var unchanged = draft is not null && _inputBox.Text.Trim() == draft &&
            _controller.State.CurrentConversationId == _pendingCompressionConversationId &&
            (_modelBox.SelectedItem as AiModelProfile)?.Id == _pendingCompressionModelId &&
            _activeBranchLeafMessageId == _pendingCompressionBranchLeafId;
        _pendingCompressionDraft = _pendingCompressionConversationId = _pendingCompressionModelId =
            _pendingCompressionBranchLeafId = null;
        if (unchanged) await SendAsync();
        else _statusPresenter.ShowMessage("压缩已完成；由于会话、模型或草稿已变化，未自动发送。", ChatMessageSeverity.Warning);
    }

    private async Task<bool> StopGenerationBeforeNavigationAsync()
    {
        var invocationId = _controller.State.ActiveInvocationId;
        if (invocationId is null) return true;
        if (!await _dialogs.ConfirmStopGenerationAsync()) return false;
        try
        {
            await _controller.CancelAsync(invocationId);
            if (_terminalCompletion is not null)
            {
                await _terminalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            return true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return false;
        }
    }

    private async Task RefreshConversationsAsync()
    {
        await _controller.RefreshConversationsAsync();
        FilterConversations();
    }

    private async Task SelectBranchAsync(string leafMessageId)
    {
        if (_controller.State.CurrentConversationId is not { } conversationId) return;
        try
        {
            await _controller.SelectBranchAsync(conversationId, leafMessageId);
            await RefreshAndRenderCurrentConversationAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task RefreshAndRenderCurrentConversationAsync()
    {
        await RefreshConversationsAsync();
        if (_controller.State.CurrentConversationId is not null)
        {
            var page = await _controller.ListMessagesAsync(_controller.State.CurrentConversationId);
            ApplyMessagePage(page, true);
        }
    }

    private void ApplyMessagePage(AiMessagePage page, bool reset)
    {
        if (reset)
        {
            _loadedMessages.Clear();
            _loadedMessages.AddRange(page.Items);
        }
        else
        {
            var known = _loadedMessages.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            _loadedMessages.InsertRange(0, page.Items.Where(item => known.Add(item.Id)));
        }
        _nextMessageCursor = page.NextCursor;
        _hasMoreMessages = page.HasMore;
        RenderMessages(_loadedMessages, page.Summary, page.ActiveLeafMessageId);
    }

    private async Task LoadMoreMessagesAsync(Button button)
    {
        if (_controller.State.CurrentConversationId is not { } conversationId ||
            !_hasMoreMessages || _nextMessageCursor is null) return;
        button.IsEnabled = false;
        try
        {
            var page = await _controller.ListMessagesAsync(conversationId, _nextMessageCursor);
            ApplyMessagePage(page, false);
        }
        catch (Exception exception)
        {
            button.IsEnabled = true;
            ShowError(exception);
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

    private void UpdateLifecycleControls()
    {
        var state = _controller.State.LifecycleState;
        var busy = state != AiChatLifecycleState.Idle;
        var stoppable = state is AiChatLifecycleState.Compacting or AiChatLifecycleState.Generating;
        var showsStop = stoppable || state == AiChatLifecycleState.Cancelling;
        _primaryActionButton.IsEnabled = stoppable || state == AiChatLifecycleState.Idle;
        _primaryActionIcon.Glyph = showsStop ? "\uE71A" : "\uE724";
        var actionName = L(showsStop ? "AI_Stop" : "AI_Send");
        AutomationProperties.SetName(_primaryActionButton, actionName);
        ToolTipService.SetToolTip(_primaryActionButton, actionName);
        foreach (var button in _regenerateMessageButtons)
        {
            button.IsEnabled = !busy;
        }
        if (!busy)
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
        UpdateLifecycleControls();
    }

    private void ScrollToEnd()
    {
        if (_messagePanel.Items.Count > 0)
        {
            _messagePanel.UpdateLayout();
            _messagePanel.ScrollIntoView(_messagePanel.Items[^1], ScrollIntoViewAlignment.Leading);
        }
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
        UpdateLifecycleControls();
#else
        throw new InvalidOperationException("The visual fixture is available only in Debug builds.");
#endif
    }

}
