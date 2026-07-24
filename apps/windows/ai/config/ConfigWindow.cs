using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Services;
using Fowan.Ai.Shared.Application.Ports;
using Fowan.Ai.Config.Windows.Platform.Windows;
using Fowan.Ai.Config.Windows.Presentation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace Fowan.Ai.Config.Windows;

public sealed partial class ConfigWindow : Window
{
    private const int DesignWindowWidth = 1920;
    private const int DesignWindowHeight = 1080;

    private readonly AiLocalizationService _loc = new();
    private readonly IAiApplicationLauncher _applicationLauncher;
    private readonly AiConfigSession _controller;
    private Grid _root = new();
    private Border _statusBar = new();
    private TextBlock _statusText = new();
    private ListView _credentialList = new();
    private ListView _modelList = new();
    private Border _credentialEmptyState = new();
    private Border _modelEmptyState = new();
    private ComboBox _bindingFeatureBox = new();
    private ComboBox _bindingCredentialBox = new();
    private ComboBox _bindingModelBox = new();
    private StackPanel _bindingThinkingEffortPanel = new();
    private ComboBox _bindingThinkingEffortBox = new();
    private StackPanel _bindingThinkingEffortOptions = new();
    private StackPanel _bindingSummaryRows = new();
    private TextBlock _bindingThinkingEffortHint = new();
    private string? _selectedThinkingEffort;
    private readonly List<UIElement> _configPages = [];
    private readonly List<Button> _configNavigationButtons = [];

    public ConfigWindow(string initialPage)
    {
        _applicationLauncher = new WindowsAiApplicationLauncher();
        _controller = AiConfigCompositionRoot.CreateSession();
        Title = L("AI_ConfigAppTitle");
        BuildContent();
        ConfigureWindow();
        SelectConfigPage(PageIndex(initialPage));
        Closed += async (_, _) => await _controller.DisposeAsync();
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
        if (_bindingFeatureBox.SelectedItem is not AiToolFeature feature) return;
        try
        {
            await _controller.DeleteBindingAsync(feature.FeatureId);
            await ApplyBindingAsync();
            ShowMessage(L("AI_Saved"), AiMessageSeverity.Success);
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task TestCredentialAsync()
    {
        if (_credentialList.SelectedItem is not AiCredential selected) return;
        if (!_controller.HasEnabledModelForCredential(selected.Id))
        {
            ShowMessage(L("AI_CredentialTestRequiresModel"), AiMessageSeverity.Warning);
            return;
        }
        try
        {
            var execution = await _controller.TestCredentialAsync(selected, ConfirmConsentAsync);
            if (!execution.Executed) return;
            ShowMessage(L("AI_TestSuccess"), AiMessageSeverity.Success);
            await RefreshAllAsync();
        }
        catch (AiCoreException exception) when (exception.Code == "conflict")
        {
            ShowMessage(L("AI_CredentialTestRequiresModel"), AiMessageSeverity.Warning);
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task EditModelAsync()
    {
        if (_modelList.SelectedItem is not AiModelProfile selectedModel)
        {
            return;
        }
        var form = ConfigDialogForms.Model(L, _controller.State.Credentials, _controller.State.Presets, selectedModel);
        if (await DialogAsync(L("AI_EditModel"), form.Content) != ContentDialogResult.Primary ||
            form.Credential.SelectedItem is not AiCredential selectedCredential)
        {
            return;
        }
        if (!TryModelLimits(form, out var contextWindowTokens, out var maxOutputTokens)) return;
        try
        {
            await _controller.UpsertModelAsync(
                selectedModel.Id,
                selectedCredential.Id,
                form.ModelId.Text,
                form.DisplayName.Text,
                selectedModel.Source,
                contextWindowTokens,
                maxOutputTokens,
                selectedModel.ThinkingEnabled);
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
        var form = ConfigDialogForms.Credential(L, _controller.State.Channels, selectedCredential);
        if (await DialogAsync(L("AI_EditCredential"), form.Content) != ContentDialogResult.Primary ||
            form.Channel.SelectedItem is not AiChannel selectedChannel)
        {
            return;
        }
        try
        {
            await _controller.UpsertCredentialAsync(
                selectedCredential.Id,
                selectedChannel.Id,
                form.Label.Text,
                form.Endpoint.Text,
                string.IsNullOrWhiteSpace(form.Secret.Password) ? null : form.Secret.Password);
            form.Secret.Password = string.Empty;
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task DeleteChannelAsync()
    {
        var customChannels = _controller.State.Channels.Where(item => !item.BuiltIn).ToList();
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
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }
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

    private OverlappedPresenter? GetOverlappedPresenter()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd)).Presenter as OverlappedPresenter;
    }

    private void TitleBarDragSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(TitleBarDragSurface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        NativeWindowMethods.BeginWindowDrag(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) => GetOverlappedPresenter()?.Minimize();

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var presenter = GetOverlappedPresenter();
        if (presenter is null) return;
        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
            return;
        }
        presenter.Maximize();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

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
        _bindingFeatureBox = BindingFeatureComboBox;
        _bindingCredentialBox = BindingCredentialComboBox;
        _bindingModelBox = BindingModelComboBox;
        BuildBindingSummary();
        ConfigureNativeComboBoxChrome(_bindingCredentialBox);
        ConfigureNativeComboBoxChrome(_bindingModelBox);
        _bindingThinkingEffortPanel = BindingThinkingEffortPanel;
        _bindingThinkingEffortBox = BindingThinkingEffortComboBox;
        if (_bindingThinkingEffortBox.Parent is FrameworkElement thinkingEffortDropDownHost)
        {
            thinkingEffortDropDownHost.Visibility = Visibility.Collapsed;
        }
        _bindingThinkingEffortOptions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18
        };
        _bindingThinkingEffortPanel.Children.Insert(1, _bindingThinkingEffortOptions);
        _bindingThinkingEffortPanel.Loaded += (_, _) =>
        {
            if (_bindingThinkingEffortBox.Parent is FrameworkElement thinkingEffortDropDownHost)
            {
                thinkingEffortDropDownHost.Visibility = Visibility.Collapsed;
            }
        };
        _bindingThinkingEffortHint = BindingThinkingEffortHintTextBlock;
        _statusBar = StatusBorder;
        _statusText = StatusTextBlock;
        _configPages.Add(CredentialsPage);
        _configPages.Add(ModelsPage);
        _configPages.Add(BindingsPage);
        _configPages.Add(UsageGuidePage);
        _configNavigationButtons.Add(CredentialsNavButton);
        _configNavigationButtons.Add(ModelsNavButton);
        _configNavigationButtons.Add(BindingsNavButton);
        _configNavigationButtons.Add(UsageGuideNavButton);

        CredentialsNavButton.Click += (_, _) => SelectConfigPage(0);
        ModelsNavButton.Click += (_, _) => SelectConfigPage(1);
        BindingsNavButton.Click += (_, _) => SelectConfigPage(2);
        UsageGuideNavButton.Click += (_, _) => SelectConfigPage(3);
        MinimizeWindowButton.Click += MinimizeWindowButton_Click;
        MaximizeWindowButton.Click += MaximizeWindowButton_Click;
        CloseWindowButton.Click += CloseWindowButton_Click;
        SecurityAndPrivacyButton.Click += async (_, _) => await ShowSecurityAndPrivacyAsync();
        AboutButton.Click += async (_, _) => await ShowAboutAsync();
        OpenChatButton.Click += (_, _) =>
        {
            try { _applicationLauncher.Launch(AiApplication.Chat); }
            catch (Exception exception) { ShowError(exception); }
        };
        AddCredentialButton.Click += async (_, _) => await AddCredentialAsync();
        AddChannelButton.Click += async (_, _) => await AddChannelAsync();
        AddModelButton.Click += async (_, _) => await AddModelAsync();
        BindingCredentialComboBox.SelectionChanged += (_, _) => RefreshBindingModels();
        BindingModelComboBox.SelectionChanged += (_, _) => RefreshBindingThinkingEffort();
        BindingFeatureComboBox.SelectionChanged += async (_, _) => await ApplyBindingAsync();
        SaveBindingButton.Click += async (_, _) => await SaveBindingAsync();
        ClearBindingButton.Click += async (_, _) => await DeleteBindingAsync();
        CloseStatusButton.Click += (_, _) => _statusBar.Visibility = Visibility.Collapsed;
    }

    private async Task ShowSecurityAndPrivacyAsync()
    {
        var content = new StackPanel { Spacing = 18, MaxWidth = 620 };
        content.Children.Add(CreateInformationSection(
            "密钥保存",
            "API Key 只在你添加或更新时输入，由 FowanCore 保存到 Windows 凭据管理器。配置中心不会显示完整密钥，也不会把密钥写入界面日志。"));
        content.Children.Add(CreateInformationSection(
            "本地数据保护",
            "配置、模型绑定和对话数据由本机 Core 管理；对话内容使用 Windows DPAPI 保护。删除本地配置不会影响 AI 服务商已保存的数据。"));
        content.Children.Add(CreateInformationSection(
            "云端发送确认",
            "首次测试连接或向云端发送内容前，应用会显示确认提示。确认后，请求内容会发送到你选择的 AI 服务商。"));
        content.Children.Add(CreateInformationSection(
            "你的控制权",
            "你可以随时删除密钥、模型或默认绑定。使用前请确认服务商的隐私政策、数据处理规则和账户权限符合你的要求。"));
        await ShowInformationDialogAsync("安全与隐私", content);
    }

    private async Task ShowAboutAsync()
    {
        var version = typeof(ConfigWindow).Assembly.GetName().Version?.ToString(3) ?? "未知版本";
        var content = new StackPanel { Spacing = 14, MaxWidth = 560 };
        content.Children.Add(new TextBlock
        {
            Text = "Fowan AI 配置中心",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = $"当前版本：v{version}",
            Foreground = Brush("AiSecondaryTextBrush", Color.FromArgb(255, 105, 115, 134))
        });
        content.Children.Add(new TextBlock
        {
            Text = "用于管理 AI 服务密钥、模型和各项工具的默认模型设置。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "GitHub 仓库地址",
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = "https://github.com/AliangHuang/Fowan ↗",
            NavigateUri = new Uri("https://github.com/AliangHuang/Fowan"),
            Foreground = Brush("AiPrimaryBrush", Color.FromArgb(255, 11, 103, 246)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 4, 0, 4)
        });
        content.Children.Add(new TextBlock
        {
            Text = "许可证：GPL-3.0",
            Foreground = Brush("AiSecondaryTextBrush", Color.FromArgb(255, 105, 115, 134))
        });
        await ShowInformationDialogAsync("关于", content);
    }

    private async Task ShowInformationDialogAsync(string title, UIElement content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "我知道了",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private static StackPanel CreateInformationSection(string title, string description)
    {
        var section = new StackPanel { Spacing = 6 };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16
        });
        section.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("AiSecondaryTextBrush", Color.FromArgb(255, 105, 115, 134))
        });
        return section;
    }

    private static void ConfigureNativeComboBoxChrome(ComboBox comboBox)
    {
        comboBox.BorderThickness = new Thickness(1);
        comboBox.Padding = new Thickness(14, 0, 14, 0);
        comboBox.Loaded += (_, _) =>
        {
            if (comboBox.Parent is not Grid host) return;
            foreach (var overlay in host.Children.Where(child => !ReferenceEquals(child, comboBox)))
            {
                overlay.Visibility = Visibility.Collapsed;
            }
        };
    }

    private void BuildBindingSummary()
    {
        if (BindingsPage.Children.OfType<StackPanel>().FirstOrDefault() is not { } bindingPageContent)
        {
            return;
        }

        _bindingSummaryRows = new StackPanel { Spacing = 0 };
        var header = new Border
        {
            Background = Brush("AiSelectionBrush", Color.FromArgb(255, 234, 243, 255)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 8, 12, 8),
            Child = CreateBindingSummaryHeader()
        };
        var scrollViewer = new ScrollViewer
        {
            Content = _bindingSummaryRows,
            MaxHeight = 184,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "当前配置总览",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(header);
        content.Children.Add(scrollViewer);
        bindingPageContent.Children.Insert(2, new Border
        {
            Background = Brush("AiCardBrush", Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = Brush("AiBorderBrush", Color.FromArgb(255, 217, 225, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 16, 18, 16),
            Child = content
        });
    }

    private Grid CreateBindingSummaryHeader()
    {
        var header = CreateBindingSummaryGrid();
        AddBindingSummaryCell(header, "功能", 0, true);
        AddBindingSummaryCell(header, "密钥", 1, true);
        AddBindingSummaryCell(header, "模型", 2, true);
        AddBindingSummaryCell(header, "思考", 3, true);
        AddBindingSummaryCell(header, "状态", 4, true);
        return header;
    }

    private static Grid CreateBindingSummaryGrid()
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        return grid;
    }

    private static void AddBindingSummaryCell(Grid row, string text, int column, bool secondary = false, Brush? foreground = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = secondary ? 13 : 14,
            FontWeight = secondary ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = foreground ?? (secondary
                ? Brush("AiSecondaryTextBrush", Color.FromArgb(255, 105, 115, 134))
                : Brush("AiTextBrush", Color.FromArgb(255, 23, 28, 38))),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private void RefreshBindingSummary(IReadOnlyList<AiBinding> bindings)
    {
        _bindingSummaryRows.Children.Clear();
        var snapshot = _controller.State;
        if (snapshot.ToolFeatures.Length == 0)
        {
            _bindingSummaryRows.Children.Add(new TextBlock
            {
                Text = "暂无可配置功能",
                Foreground = Brush("AiSecondaryTextBrush", Color.FromArgb(255, 105, 115, 134)),
                Padding = new Thickness(12, 10, 12, 10)
            });
            return;
        }

        foreach (var feature in snapshot.ToolFeatures)
        {
            var binding = bindings.FirstOrDefault(item => item.FeatureId == feature.FeatureId);
            var credential = binding is null ? null : snapshot.Credentials.FirstOrDefault(item => item.Id == binding.CredentialId);
            var model = binding is null ? null : snapshot.Models.FirstOrDefault(item => item.Id == binding.ModelProfileId);
            var configured = binding is not null && credential is not null && model is not null;
            var thinkingEffort = model is { ThinkingEnabled: true }
                ? (binding?.ThinkingEffort is null ? "默认" : ThinkingEffortLabel(binding.ThinkingEffort))
                : "—";
            var status = binding is null ? "未配置" : configured ? "已配置" : "需修复";
            var row = CreateBindingSummaryGrid();
            AddBindingSummaryCell(row, feature.DisplayName, 0);
            AddBindingSummaryCell(row, credential?.Label ?? "—", 1);
            AddBindingSummaryCell(row, model?.DisplayLabel ?? "—", 2);
            AddBindingSummaryCell(row, thinkingEffort, 3);
            AddBindingSummaryCell(
                row,
                status,
                4,
                foreground: configured
                    ? Brush("AiSuccessBrush", Color.FromArgb(255, 22, 163, 74))
                    : Brush("AiSecondaryTextBrush", Color.FromArgb(255, 105, 115, 134)));
            _bindingSummaryRows.Children.Add(new Border
            {
                BorderBrush = Brush("AiBorderBrush", Color.FromArgb(255, 217, 225, 236)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10, 12, 10),
                Child = row
            });
        }
    }

    private static int PageIndex(string page) => page.ToLowerInvariant() switch
    {
        "models" => 1,
        "bindings" => 2,
        "guide" => 3,
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
        var snapshot = _controller.State;
        _credentialList.ItemsSource = snapshot.Credentials.ToList();
        _modelList.ItemsSource = snapshot.Models.ToList();
        _credentialEmptyState.Visibility = snapshot.Credentials.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _modelEmptyState.Visibility = snapshot.Models.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _bindingFeatureBox.ItemsSource = snapshot.ToolFeatures.ToList();
        _bindingFeatureBox.SelectedItem ??= snapshot.ToolFeatures.FirstOrDefault(item => item.FeatureId == "ai.chat")
            ?? snapshot.ToolFeatures.FirstOrDefault();
        _bindingCredentialBox.ItemsSource = snapshot.Credentials.Where(item => item.Enabled).ToList();
        await ApplyBindingAsync();
    }

    private async Task ApplyBindingAsync()
    {
        if (_bindingFeatureBox.SelectedItem is not AiToolFeature feature)
        {
            return;
        }
        var bindings = await _controller.ListBindingsAsync();
        RefreshBindingSummary(bindings);
        var binding = bindings.FirstOrDefault(item => item.FeatureId == feature.FeatureId);
        if (binding is null)
        {
            _bindingCredentialBox.SelectedItem = null;
            _bindingModelBox.ItemsSource = Array.Empty<AiModelProfile>();
            RefreshBindingThinkingEffort();
            return;
        }

        _bindingCredentialBox.SelectedItem = _controller.State.Credentials.FirstOrDefault(item => item.Id == binding.CredentialId);
        RefreshBindingModels();
        _bindingModelBox.SelectedItem = _controller.State.Models.FirstOrDefault(item => item.Id == binding.ModelProfileId);
        RefreshBindingThinkingEffort(binding.ThinkingEffort);
    }

    private void RefreshBindingModels()
    {
        var credential = _bindingCredentialBox.SelectedItem as AiCredential;
        _bindingModelBox.ItemsSource = credential is null
            ? Array.Empty<AiModelProfile>()
            : _controller.State.Models.Where(item => item.Enabled && item.CredentialId == credential.Id).ToList();
        if (_bindingModelBox.Items.Count == 1)
        {
            _bindingModelBox.SelectedIndex = 0;
        }
        RefreshBindingThinkingEffort();
    }

    private void RefreshBindingThinkingEffort(string? selectedEffort = null)
    {
        var model = _bindingModelBox.SelectedItem as AiModelProfile;
        var options = model is { ThinkingEnabled: true }
            ? (model.ThinkingEffortOptions ?? []).Distinct(StringComparer.Ordinal).ToArray()
            : [];
        if (options.Length == 0)
        {
            _bindingThinkingEffortOptions.Children.Clear();
            _selectedThinkingEffort = null;
            _bindingThinkingEffortHint.Text = string.Empty;
            _bindingThinkingEffortPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var selections = new List<ThinkingEffortOption>
        {
            new(null, "使用模型默认档位（高）")
        };
        selections.AddRange(options.Select(option => new ThinkingEffortOption(option, ThinkingEffortLabel(option))));
        _selectedThinkingEffort = selections.Any(option => option.Value == selectedEffort) ? selectedEffort : null;
        _bindingThinkingEffortOptions.Children.Clear();
        foreach (var option in selections)
        {
            var button = new RadioButton
            {
                Content = option.DisplayLabel,
                Tag = option,
                GroupName = "BindingThinkingEffort",
                FontSize = 15,
                Padding = new Thickness(0, 4, 12, 4),
                IsChecked = option.Value == _selectedThinkingEffort
            };
            button.Checked += BindingThinkingEffortOption_Checked;
            _bindingThinkingEffortOptions.Children.Add(button);
        }
        _bindingThinkingEffortHint.Text = $"当前模型支持：{string.Join("、", options.Select(ThinkingEffortLabel))}；不设置时使用模型默认档位。";
        _bindingThinkingEffortPanel.Visibility = Visibility.Visible;
    }

    private void BindingThinkingEffortOption_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ThinkingEffortOption option)
        {
            _selectedThinkingEffort = option.Value;
        }
    }

    private static string ThinkingEffortLabel(string value) => value switch
    {
        "high" => "高",
        "max" => "最高",
        _ => value
    };

    private async Task AddChannelAsync()
    {
        var form = ConfigDialogForms.Channel(L);
        if (await DialogAsync(L("AI_AddChannel"), form.Content) != ContentDialogResult.Primary)
        {
            return;
        }
        try
        {
            await _controller.CreateChannelAsync(form.Name.Text, form.Endpoint.Text);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task AddCredentialAsync()
    {
        var form = ConfigDialogForms.CredentialCreate(L, _controller.State.Channels, _controller.State.Presets);
        if (await DialogAsync(L("AI_AddCredential"), form.Content) != ContentDialogResult.Primary ||
            form.Channel.SelectedItem is not AiChannel selected)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(form.Label.Text) || string.IsNullOrWhiteSpace(form.Secret.Password))
        {
            ShowMessage("请填写密钥名称和 API Key。", AiMessageSeverity.Warning);
            return;
        }
        var endpoint = form.Advanced.IsExpanded && !string.IsNullOrWhiteSpace(form.Endpoint.Text)
            ? form.Endpoint.Text
            : null;
        var initialModelIds = form.Advanced.IsExpanded
            ? form.ModelSelections.SelectedItems.OfType<AiPresetModel>().Select(item => item.ModelId).ToArray()
            : [];
        var thinkingEnabled = form.Advanced.IsExpanded && selected.Id == "deepseek"
            ? form.ThinkingEnabled.IsOn
            : (bool?)null;
        try
        {
            await _controller.CreateCredentialAsync(
                selected.Id,
                form.Label.Text,
                endpoint,
                form.Secret.Password,
                initialModelIds.Length == 0 ? null : initialModelIds,
                thinkingEnabled);
            form.Secret.Password = string.Empty;
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
        var form = ConfigDialogForms.Model(L, _controller.State.Credentials, _controller.State.Presets);
        if (await DialogAsync(L("AI_AddModel"), form.Content) != ContentDialogResult.Primary ||
            form.Credential.SelectedItem is not AiCredential selected)
        {
            return;
        }
        if (!TryModelLimits(form, out var contextWindowTokens, out var maxOutputTokens)) return;
        try
        {
            await _controller.UpsertModelAsync(
                null,
                selected.Id,
                form.ModelId.Text,
                string.IsNullOrWhiteSpace(form.DisplayName.Text) ? form.ModelId.Text : form.DisplayName.Text,
                form.Preset?.SelectedItem is AiPresetModel ? "preset" : "custom",
                contextWindowTokens,
                maxOutputTokens,
                null);
            await RefreshAllAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private bool TryModelLimits(ConfigDialogForms.ModelForm form, out int contextWindowTokens, out int maxOutputTokens)
    {
        contextWindowTokens = maxOutputTokens = 0;
        if (double.IsNaN(form.ContextWindowTokens.Value) || double.IsNaN(form.MaxOutputTokens.Value) ||
            form.ContextWindowTokens.Value > int.MaxValue || form.MaxOutputTokens.Value > int.MaxValue)
        {
            ShowMessage("请填写有效的上下文窗口和最大输出 Token。", AiMessageSeverity.Warning);
            return false;
        }
        contextWindowTokens = checked((int)form.ContextWindowTokens.Value);
        maxOutputTokens = checked((int)form.MaxOutputTokens.Value);
        if (contextWindowTokens < 2048 || maxOutputTokens < 1 || maxOutputTokens >= contextWindowTokens)
        {
            ShowMessage("最大输出必须大于 0 且小于上下文窗口。", AiMessageSeverity.Warning);
            return false;
        }
        return true;
    }

    private async Task TestModelAsync()
    {
        if (_modelList.SelectedItem is not AiModelProfile selected) return;
        var credential = _controller.State.Credentials.FirstOrDefault(item => item.Id == selected.CredentialId);
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
        if (_bindingFeatureBox.SelectedItem is not AiToolFeature feature ||
            _bindingCredentialBox.SelectedItem is not AiCredential credential ||
            _bindingModelBox.SelectedItem is not AiModelProfile model)
        {
            ShowMessage(L("AI_SelectConfiguration"), AiMessageSeverity.Warning);
            return;
        }
        try
        {
            await _controller.UpsertBindingAsync(feature.FeatureId, model.Id, _selectedThinkingEffort);
            await ApplyBindingAsync();
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

    private sealed record ThinkingEffortOption(string? Value, string DisplayLabel);

    private static SolidColorBrush Brush(string key, Color fallback)
    {
        _ = key;
        return new SolidColorBrush(fallback);
    }

}
