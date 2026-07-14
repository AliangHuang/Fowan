using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Ai.Shared.Application.Ports;
using Fowan.Ai.Chat.Windows.Platform.Windows;
using Fowan.Ai.Chat.Windows.Presentation;
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
    private readonly AiCoreClient _client;
    private readonly IAiApplicationLauncher _applicationLauncher;
    private readonly AiChatController _controller;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IClipboardService _clipboard = new WindowsClipboardService();
    private readonly List<AiChannel> _channels;
    private readonly List<AiCredential> _credentials;
    private readonly List<AiModelProfile> _models;
    private readonly List<AiConversationSummary> _conversations;
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
    private string? _currentConversationId;
    private string? _activeInvocationId;
    private TextBlock? _streamingText;
    private string _streamingContent = string.Empty;

    public ChatWindow()
    {
        _client = new AiCoreClient(new WindowsAiCoreProcessLauncher());
        _applicationLauncher = new WindowsAiApplicationLauncher();
        _controller = new AiChatController(new AiCoreApi(_client), new AiConsentCoordinator(_client));
        _channels = _controller.Channels;
        _credentials = _controller.Credentials;
        _models = _controller.Models;
        _conversations = _controller.Conversations;
        _uiDispatcher = new DispatcherQueueUiDispatcher(DispatcherQueue);
        Title = L("AI_ChatAppTitle");
        BuildContent();
        ConfigureWindow();
        _client.NotificationAsync = HandleNotificationDispatchedAsync;
        Closed += async (_, _) =>
        {
            _client.NotificationAsync = null;
            await _client.DisposeAsync();
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

    private async Task RenameConversationAsync()
    {
        if (_conversationList.SelectedItem is not AiConversationSummary selected)
        {
            return;
        }
        var title = new TextBox { Text = selected.Title, Header = L("AI_ConversationTitle"), MinWidth = 380 };
        if (await DialogAsync(L("AI_RenameChat"), title) != ContentDialogResult.Primary)
        {
            return;
        }
        try
        {
            await _controller.RenameConversationAsync(selected.Id, title.Text);
            await RefreshConversationsAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task DeleteConversationAsync()
    {
        if (_conversationList.SelectedItem is not AiConversationSummary selected)
        {
            return;
        }
        try
        {
            await _controller.DeleteConversationAsync(selected.Id);
            StartNewConversation();
            await RefreshConversationsAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private StackPanel BuildMarkdownContent(string content)
    {
        var root = new StackPanel { Spacing = 7 };
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        var inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }
            root.Children.Add(new TextBlock
            {
                Text = string.Join(Environment.NewLine, paragraph),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                LineHeight = 22
            });
            paragraph.Clear();
        }

        void FlushCode()
        {
            var value = string.Join(Environment.NewLine, code);
            var codeBox = new TextBox
            {
                Text = value,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 226, 232, 240)),
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 35, 43)),
                BorderThickness = new Thickness(0),
                MinHeight = 42,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var copyCode = new Button
            {
                Content = L("AI_CopyCode"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(8, 3, 8, 3)
            };
            copyCode.Click += (_, _) =>
            {
                _clipboard.SetText(value);
            };
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 35, 43)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10),
                Child = new StackPanel { Spacing = 6, Children = { codeBox, copyCode } }
            });
            code.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    FlushCode();
                }
                else
                {
                    FlushParagraph();
                }
                inCode = !inCode;
                continue;
            }
            if (inCode)
            {
                code.Add(line);
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushParagraph();
                root.Children.Add(new TextBlock
                {
                    Text = line[2..],
                    FontSize = 21,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushParagraph();
                root.Children.Add(new TextBlock
                {
                    Text = line[3..],
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                paragraph.Add($"• {line[2..]}");
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
            }
            else
            {
                paragraph.Add(line);
            }
        }
        if (inCode)
        {
            FlushCode();
        }
        FlushParagraph();
        return root;
    }

    private async Task RegenerateAsync()
    {
        if (_currentConversationId is null ||
            _credentialBox.SelectedItem is not AiCredential credential ||
            _modelBox.SelectedItem is not AiModelProfile model)
        {
            ShowMessage(L("AI_SelectConversation"), AiMessageSeverity.Warning);
            return;
        }

        if (!await EnsureConsentAsync(credential))
        {
            return;
        }

        _streamingContent = string.Empty;
        var assistant = MessageBubble("assistant", string.Empty, $"{ChannelName(credential.ChannelId)} · {model.ModelId}");
        _streamingText = assistant.Tag as TextBlock;
        _messagePanel.Children.Add(assistant);
        SetGenerating(true);
        try
        {
            var execution = await _controller.RegenerateAsync(
                credential,
                model,
                _currentConversationId,
                ConfirmConsentAsync);
            if (!execution.Executed)
            {
                SetGenerating(false);
                return;
            }
            var result = execution.Value!;
            _activeInvocationId = result.InvocationId;
        }
        catch (Exception exception)
        {
            SetGenerating(false);
            ShowError(exception);
        }
    }

    private async Task<bool> EnsureConsentAsync(AiCredential credential)
    {
        return await _controller.EnsureConsentAsync(credential.BaseUrl, ConfirmConsentAsync);
    }

    private async Task<bool> ConfirmConsentAsync(string endpoint)
    {
        var content = new TextBlock
        {
            Text = string.Format(L("AI_CloudConsentDescription"), endpoint),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = L("AI_CloudConsentTitle"),
            Content = content,
            PrimaryButtonText = L("AI_CloudConsentAllow"),
            CloseButtonText = L("Action_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        return true;
    }

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
            await _client.ConnectAsync(["ai.chat.v1"]);
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
        _credentialBox.ItemsSource = _credentials.Where(item => item.Enabled).ToList();
        await ApplyBindingAsync();
        if (_conversations.Count == 0)
        {
            ShowChatEmptyState();
        }
    }

    private void ShowChatEmptyState()
    {
        var hasCredential = _credentials.Any(item => item.Enabled);
        var hasModel = _models.Any(item => item.Enabled &&
            _credentials.Any(credential => credential.Enabled && credential.Id == item.CredentialId));
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

        _credentialBox.SelectedItem = _credentials.FirstOrDefault(item => item.Id == binding.CredentialId);
        RefreshChatModels();
        _modelBox.SelectedItem = _models.FirstOrDefault(item => item.Id == binding.ModelProfileId);
    }

    private void RefreshChatModels()
    {
        var credential = _credentialBox.SelectedItem as AiCredential;
        _modelBox.ItemsSource = credential is null
            ? Array.Empty<AiModelProfile>()
            : _models.Where(item => item.Enabled && item.CredentialId == credential.Id).ToList();
        if (_modelBox.Items.Count == 1)
        {
            _modelBox.SelectedIndex = 0;
        }
    }

    private void StartNewConversation()
    {
        _currentConversationId = null;
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
            _currentConversationId = conversation.Id;
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
                _messagePanel.Children.Add(VariantGroup(variants));
                continue;
            }
            _messagePanel.Children.Add(BubbleForMessage(message));
        }
        ScrollToEnd();
    }

    private UIElement VariantGroup(IReadOnlyList<AiChatMessage> variants)
    {
        if (variants.Count == 1)
        {
            return BubbleForMessage(variants[0]);
        }

        var host = new Grid();
        foreach (var variant in variants)
        {
            host.Children.Add(BubbleForMessage(variant));
        }
        var selectedIndex = variants.Count - 1;
        void SelectVariant(int index)
        {
            selectedIndex = Math.Clamp(index, 0, variants.Count - 1);
            for (var itemIndex = 0; itemIndex < host.Children.Count; itemIndex++)
            {
                host.Children[itemIndex].Visibility = itemIndex == selectedIndex
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        var previous = new Button { Content = "‹", Padding = new Thickness(8, 2, 8, 2) };
        var indicator = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var next = new Button { Content = "›", Padding = new Thickness(8, 2, 8, 2) };
        void RefreshNavigation()
        {
            indicator.Text = $"{selectedIndex + 1} / {variants.Count}";
            previous.IsEnabled = selectedIndex > 0;
            next.IsEnabled = selectedIndex + 1 < variants.Count;
            SelectVariant(selectedIndex);
        }
        previous.Click += (_, _) => { selectedIndex--; RefreshNavigation(); };
        next.Click += (_, _) => { selectedIndex++; RefreshNavigation(); };
        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { previous, indicator, next }
        };
        RefreshNavigation();
        return new StackPanel { Spacing = 5, Children = { host, navigation } };
    }

    private Border BubbleForMessage(AiChatMessage message)
    {
        var metadata = message.ModelId is null
            ? null
            : $"{message.ChannelName} · {message.CredentialName} · {message.ModelId} · {message.Status}";
        return MessageBubble(message.Role, message.Content, metadata);
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(_inputBox.Text) ||
            _credentialBox.SelectedItem is not AiCredential credential ||
            _modelBox.SelectedItem is not AiModelProfile model)
        {
            ShowMessage(L("AI_SelectConfiguration"), AiMessageSeverity.Warning);
            return;
        }

        var text = _inputBox.Text.Trim();
        if (!await EnsureConsentAsync(credential))
        {
            return;
        }
        _inputBox.Text = string.Empty;
        ChatEmptyState.Visibility = Visibility.Collapsed;
        _messagePanel.Children.Add(MessageBubble("user", text, null));
        _streamingContent = string.Empty;
        var assistant = MessageBubble("assistant", string.Empty, $"{ChannelName(credential.ChannelId)} · {model.ModelId}");
        _streamingText = assistant.Tag as TextBlock;
        _messagePanel.Children.Add(assistant);
        ScrollToEnd();
        SetGenerating(true);
        try
        {
            var execution = await _controller.SendAsync(
                credential,
                model,
                _currentConversationId,
                text,
                ConfirmConsentAsync);
            if (!execution.Executed)
            {
                SetGenerating(false);
                return;
            }
            var result = execution.Value!;
            _activeInvocationId = result.InvocationId;
            _currentConversationId = result.ConversationId;
        }
        catch (Exception exception)
        {
            SetGenerating(false);
            ShowError(exception);
        }
    }

    private async Task StopAsync()
    {
        if (_activeInvocationId is null)
        {
            return;
        }
        try
        {
            await _controller.CancelAsync(_activeInvocationId);
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
                    if (_activeInvocationId is null && _sendButton.IsEnabled == false && delta is not null)
                    {
                        _activeInvocationId = delta.InvocationId;
                    }
                    if (delta is not null && delta.InvocationId == _activeInvocationId && _streamingText is not null)
                    {
                        _streamingContent += delta.Delta;
                        _streamingText.Text = _streamingContent;
                        ScrollToEnd();
                    }
                    break;
                case AiProtocolNotifications.ChatCompleted:
                case AiProtocolNotifications.ChatCancelled:
                case AiProtocolNotifications.ChatFailed:
                    var finished = args.Parameters.Deserialize<AiChatFinished>();
                    if (finished is not null && finished.InvocationId == _activeInvocationId)
                    {
                        SetGenerating(false);
                        if (args.Method == AiProtocolNotifications.ChatFailed)
                        {
                            ShowMessage(ErrorText(finished.ErrorCode), AiMessageSeverity.Error);
                        }
                        await RefreshConversationsAsync();
                        if (_currentConversationId is not null)
                        {
                            var conversation = await _controller.GetConversationAsync(_currentConversationId);
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
            ? _conversations.ToList()
            : _conversations
                .Where(item => item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
    }

    private Border MessageBubble(string role, string content, string? metadata)
    {
        var isUser = role == "user";
        var stack = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            stack.Children.Add(new TextBlock
            {
                Text = metadata,
                FontSize = 13,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 105, 115, 134))
            });
        }
        var text = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 15,
            LineHeight = 24
        };
        if (string.IsNullOrEmpty(content))
        {
            stack.Children.Add(text);
        }
        else
        {
            stack.Children.Add(BuildMarkdownContent(content));
        }
        var copy = new Button
        {
            Content = L("AI_Copy"),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        copy.Click += (_, _) =>
        {
            _clipboard.SetText(text.Text);
        };
        stack.Children.Add(copy);
        return new Border
        {
            Tag = text,
            Child = stack,
            Background = isUser
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 244, 249, 255))
                : new SolidColorBrush(Colors.White),
            BorderBrush = isUser
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 186, 214, 255))
                : new SolidColorBrush(ColorHelper.FromArgb(255, 217, 225, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
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

    private string ChannelName(string id) => _channels.FirstOrDefault(item => item.Id == id)?.DisplayName ?? id;

    private void SetGenerating(bool generating)
    {
        _sendButton.IsEnabled = !generating;
        _regenerateButton.IsEnabled = !generating;
        _stopButton.IsEnabled = generating;
        if (!generating)
        {
            _activeInvocationId = null;
            _streamingText = null;
        }
    }

    private void ScrollToEnd()
    {
        _messageScroll.UpdateLayout();
        _messageScroll.ChangeView(null, _messageScroll.ScrollableHeight, null, true);
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
