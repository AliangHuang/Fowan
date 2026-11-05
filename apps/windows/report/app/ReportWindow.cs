using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Report.Shared;
using Fowan.Report.Shared.Application;
using Fowan.Report.Shared.Application.Ports;
using Fowan.Report.Windows.Platform.Windows;
using Fowan.Report.Windows.Presentation;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace Fowan.Report.Windows;

/// <summary>Fluent report shell; ReportWorkspace remains the only business-state owner.</summary>
public sealed class ReportWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly ReportWorkspaceOwner _workspaceOwner;
    private ReportWorkspace _workspace => _workspaceOwner.Workspace;
    private readonly ReportAiGateway _ai = new();
    private readonly IReportPreferences _preferences = new ReportPreferenceStore();
    private readonly IReportClipboardService _clipboard;
    private readonly IReportFileDialogService _fileDialogs;
    private readonly Dictionary<ReportRangeKind, Button> _rangeButtons = [];
    private readonly Dictionary<ReportStyle, Button> _styleButtons = [];
    private readonly Dictionary<ReportTemplateMode, Button> _templateModeButtons = [];
    private readonly Dictionary<ReportPage, NavigationItem> _navigationItems = [];
    private readonly TextBlock _rangeText = new();
    private readonly TextBlock _filterListText = new();
    private readonly TextBlock _filterDepthText = new();
    private readonly TextBlock _filterDateModeText = new();
    private readonly TextBlock _completedCount = new();
    private readonly TextBlock _unfinishedCount = new();
    private readonly StackPanel _previewRows = new();
    private readonly TextBlock _customCounter = new();
    private readonly TextBlock _statusText = new();
    private readonly Border _statusSurface = new();
    private readonly TextBox _customRequirements = new();
    private readonly ReportBlockEditor _templateInput;
    private readonly ReportBlockEditor _exampleInput;
    private readonly ReportBlockEditor _output;
    private readonly ComboBox _style = new();
    private readonly ComboBox _mode = new();
    private readonly TextBlock _templateFile = new();
    private readonly TextBlock _exampleFile = new();
    private readonly TextBlock _preferenceStatus = new();
    private readonly TextBlock _richTextPreferenceStatus = new();
    private readonly Button _generate = new();
    private readonly Button _cancel = new();
    private readonly Button _templateAction = new();
    private readonly Button _collapse = new();
    private readonly ComboBox _recordDateFilter = new();
    private readonly ComboBox _recordStatusFilter = new();
    private readonly ComboBox _recordRangeFilter = new();
    private readonly ComboBox _recordModeFilter = new();
    private readonly StackPanel _recordRows = new();
    private readonly ColumnDefinition _sidebarColumn = new() { Width = new GridLength(336) };
    private readonly Grid _pageHost = new();
    private readonly Grid _root = new();
    private readonly Grid _titleBarDragRegion = new();
    private readonly StackPanel _textTemplateFields = new();
    private readonly StackPanel _fileTemplateFields = new();
    private Grid? _generatorPage;
    private UIElement? _recordsPage;
    private UIElement? _preferencesPage;
    private UIElement? _helpPage;
    private Grid? _generatorBody;
    private Border? _previewCard;
    private Border? _writingCard;
    private Border? _resultCard;
    private TextBlock? _brandLabel;
    private ReportPage _activePage = ReportPage.Generate;
    private bool _sidebarCollapsed;
    private string? _templatePath;
    private string? _examplePath;
    private ReportTemplateInspection? _templateInspection;
    private ReportGenerationInput? _activeGeneration;
    private int _generationAttempt;
    private Grid? _progressOverlay;
    private TextBlock? _progressStage;
    private TextBlock? _progressDetail;
    private ProgressRing? _progressRing;
    private FontIcon? _progressTerminalIcon;
    private Button? _progressCancel;
    private Button? _progressDismiss;

    public ReportWindow(bool visualFixture = false)
    {
        _clipboard = new WindowsReportClipboardService();
        _fileDialogs = new WindowsReportFileDialogService(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _templateInput = new ReportBlockEditor("输入模板；可使用 / 或左侧 + 插入内容块。", 260, _clipboard);
        _exampleInput = new ReportBlockEditor("输入填写示例；可使用 / 或左侧 + 插入内容块。", 220, _clipboard);
        _output = new ReportBlockEditor("生成的汇报会显示在这里。", 260, _clipboard);
        _workspaceOwner = new ReportWorkspaceOwner(
            visualFixture ? new FixtureTodoReader() : new WindowsReportTodoReader(),
            visualFixture ? null : new ReportGenerationRecordStore());
        Title = "Fowan 汇报";
        _workspace.StateChanged += (_, state) => Render(state);
        _ai.Notification += (_, notification) => DispatcherQueue.TryEnqueue(() => HandleNotification(notification));
        Closed += async (_, _) => await _ai.DisposeAsync();
        Content = BuildContent();
        ConfigureWindow();
        _root.SizeChanged += (_, args) => UpdateResponsiveLayout(args.NewSize);
        _ = visualFixture ? InitializeVisualFixtureAsync() : InitializeAsync();
    }

    /// <summary>Called by App after Window.Activate so block editors never construct native TextBox controls during window construction.</summary>
    public void InitializeEditorSurfaces()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _templateInput.Initialize();
            _exampleInput.Initialize();
            _output.Initialize();
        });
    }

    private UIElement BuildContent()
    {
        _root.Background = ReportDesignSystem.Canvas;
        _root.RequestedTheme = ElementTheme.Dark;
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _titleBarDragRegion.Background = ReportDesignSystem.Canvas;
        _root.Children.Add(_titleBarDragRegion);

        var shell = new Grid();
        shell.ColumnDefinitions.Add(_sidebarColumn);
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(shell, 1);
        shell.Children.Add(BuildSidebar());

        var scroll = new ScrollViewer
        {
            Background = ReportDesignSystem.Canvas,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        scroll.Content = _pageHost;
        Grid.SetColumn(scroll, 1);
        shell.Children.Add(scroll);
        _root.Children.Add(shell);

        InitializeEditorControls();
        _generatorPage = BuildGeneratorPage();
        _recordsPage = BuildRecordsPage();
        _preferencesPage = BuildPreferencesPage();
        _helpPage = BuildHelpPage();
        _pageHost.Children.Add(_generatorPage);
        _pageHost.Children.Add(_recordsPage);
        _pageHost.Children.Add(_preferencesPage);
        _pageHost.Children.Add(_helpPage);
        NavigateTo(ReportPage.Generate);
        Render(_workspace.State);
        return _root;
    }

    private UIElement BuildSidebar()
    {
        var sidebar = new Border
        {
            Background = ReportDesignSystem.Sidebar,
            BorderBrush = ReportDesignSystem.Brush("#18263A"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(18, 16, 18, 20)
        };
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new StackPanel { Spacing = 10 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 18, 0, 42) };
        var brandIcon = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.Uniform,
            Source = new BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-app-icon-256.png")))
        };
        AutomationProperties.SetName(brandIcon, "Fowan");
        brand.Children.Add(brandIcon);
        _brandLabel = new TextBlock
        {
            Text = "Fowan 汇报工具",
            FontSize = 21,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ReportDesignSystem.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        brand.Children.Add(_brandLabel);
        top.Children.Add(brand);
        top.Children.Add(CreateNavigationItem(ReportPage.Generate, "生成汇报", "\uE8A5"));
        top.Children.Add(CreateNavigationItem(ReportPage.Records, "生成记录", "\uE81C"));
        top.Children.Add(CreateNavigationItem(ReportPage.Preferences, "模板偏好", "\uEB51"));
        top.Children.Add(CreateNavigationItem(ReportPage.Help, "使用说明", "\uE897"));
        panel.Children.Add(top);

        _collapse.Content = ReportDesignSystem.IconText("\uE76C", "收起", 17, 15);
        _collapse.HorizontalAlignment = HorizontalAlignment.Left;
        ReportDesignSystem.ConfigureButton(_collapse, ReportButtonKind.Ghost);
        AutomationProperties.SetName(_collapse, "收起侧栏");
        ToolTipService.SetToolTip(_collapse, "收起侧栏");
        _collapse.Click += (_, _) => ToggleSidebar();
        Grid.SetRow(_collapse, 2);
        panel.Children.Add(_collapse);
        sidebar.Child = panel;
        return sidebar;
    }

    private UIElement CreateNavigationItem(ReportPage page, string text, string glyph)
    {
        var container = new Grid { Height = 56, Margin = new Thickness(0, 0, 0, 2) };
        var accent = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = ReportDesignSystem.Accent,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(ReportDesignSystem.Icon(glyph, 20));
        var label = new TextBlock { Text = text, FontSize = 18, Foreground = ReportDesignSystem.Text, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(label);
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        ReportDesignSystem.ConfigureButton(button, ReportButtonKind.Navigation);
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
        button.Click += async (_, _) =>
        {
            NavigateTo(page);
            if (page == ReportPage.Generate) await RefreshPreviewAsync();
        };
        container.Children.Add(button);
        container.Children.Add(accent);
        _navigationItems[page] = new NavigationItem(button, accent, label);
        return container;
    }

    private void NavigateTo(ReportPage page)
    {
        if (_generatorPage is null || _recordsPage is null || _preferencesPage is null || _helpPage is null)
            throw new InvalidOperationException("汇报页面尚未完成初始化。");

        _activePage = page;
        _generatorPage.Visibility = page == ReportPage.Generate ? Visibility.Visible : Visibility.Collapsed;
        _recordsPage.Visibility = page == ReportPage.Records ? Visibility.Visible : Visibility.Collapsed;
        _preferencesPage.Visibility = page == ReportPage.Preferences ? Visibility.Visible : Visibility.Collapsed;
        _helpPage.Visibility = page == ReportPage.Help ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, item) in _navigationItems)
        {
            var selected = key == page;
            ReportDesignSystem.SetActive(item.Button, selected);
            item.Accent.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateResponsiveLayout(new Size(_root.ActualWidth, _root.ActualHeight));
    }

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        _sidebarColumn.Width = new GridLength(_sidebarCollapsed ? 80 : 336);
        if (_brandLabel is not null) _brandLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in _navigationItems.Values)
        {
            item.Label.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            item.Button.HorizontalContentAlignment = _sidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            item.Button.Padding = _sidebarCollapsed ? new Thickness(0) : new Thickness(18, 12, 16, 12);
        }
        _collapse.Content = _sidebarCollapsed
            ? ReportDesignSystem.Icon("\uE76D", 17, ReportDesignSystem.SecondaryText)
            : ReportDesignSystem.IconText("\uE76C", "收起", 17, 15);
        AutomationProperties.SetName(_collapse, _sidebarCollapsed ? "展开侧栏" : "收起侧栏");
        ToolTipService.SetToolTip(_collapse, _sidebarCollapsed ? "展开侧栏" : "收起侧栏");
        UpdateResponsiveLayout(new Size(_root.ActualWidth, _root.ActualHeight));
    }

    private Grid BuildGeneratorPage()
    {
        var page = new Grid
        {
            Margin = new Thickness(64, 14, 64, 20),
            MinHeight = 800,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        page.Children.Add(BuildHeader());
        var range = BuildRangeControl();
        range.Margin = new Thickness(0, 24, 0, 0);
        Grid.SetRow(range, 1);
        page.Children.Add(range);
        var filter = BuildFilterBar();
        filter.Margin = new Thickness(0, 16, 0, 0);
        Grid.SetRow(filter, 2);
        page.Children.Add(filter);

        _generatorBody = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 16, 0, 0), MinHeight = 430 };
        _generatorBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _generatorBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _generatorBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _generatorBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        _previewCard = BuildPreviewCard();
        _writingCard = BuildWritingCard();
        _generatorBody.Children.Add(_previewCard);
        Grid.SetColumn(_writingCard, 1);
        _generatorBody.Children.Add(_writingCard);
        Grid.SetRow(_generatorBody, 3);
        page.Children.Add(_generatorBody);

        var action = BuildActionCard();
        action.Margin = new Thickness(0, 16, 0, 0);
        Grid.SetRow(action, 4);
        page.Children.Add(action);
        Grid.SetRow(_statusSurface, 5);
        page.Children.Add(_statusSurface);
        var result = BuildResultCard();
        Grid.SetRow(result, 6);
        page.Children.Add(result);
        _generatorPage = page;
        return page;
    }

    private UIElement BuildHeader() => new StackPanel
    {
        Spacing = 7,
        Children =
        {
            new TextBlock
            {
                Text = "生成汇报",
                FontSize = 48,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ReportDesignSystem.Text
            },
            new TextBlock
            {
                Text = "根据待办自动生成周报、月报与阶段总结",
                FontSize = 18,
                Foreground = ReportDesignSystem.SecondaryText
            }
        }
    };

    private FrameworkElement BuildRangeControl()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var segments = new Grid { Height = 56 };
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) });
        AddSegment(segments, RangeButton("本周", ReportRangeKind.ThisWeek), 0, true, false);
        AddSegment(segments, RangeButton("上周", ReportRangeKind.PreviousWeek), 1, false, false);
        AddSegment(segments, RangeButton("本月", ReportRangeKind.ThisMonth), 2, false, false);
        AddSegment(segments, RangeButton("自定义区间", ReportRangeKind.Custom), 3, false, true);
        row.Children.Add(new Border
        {
            Background = ReportDesignSystem.Surface,
            BorderBrush = ReportDesignSystem.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(3),
            Child = segments
        });
        _rangeText.Foreground = ReportDesignSystem.MutedText;
        _rangeText.FontSize = 14;
        _rangeText.VerticalAlignment = VerticalAlignment.Center;
        _rangeText.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_rangeText, 1);
        row.Children.Add(_rangeText);
        return row;
    }

    private FrameworkElement BuildFilterBar()
    {
        var bar = new Grid { Padding = new Thickness(24, 13, 16, 13) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(FilterSummaryItem(ReportDesignSystem.Icon("\uE8FD", 20), _filterListText));
        summary.Children.Add(FilterDot());
        summary.Children.Add(FilterSummaryItem(LayerIcon(), _filterDepthText));
        summary.Children.Add(FilterDot());
        summary.Children.Add(FilterSummaryItem(ReportDesignSystem.Icon("\uE823", 20), _filterDateModeText));
        bar.Children.Add(summary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var clear = new Button { Content = ReportDesignSystem.IconText("\uE894", "清空筛选", 17, 16), MinWidth = 148 };
        ReportDesignSystem.ConfigureButton(clear, ReportButtonKind.Secondary);
        clear.Click += async (_, _) =>
        {
            try
            {
                _workspace.ClearFilter();
                await RefreshPreviewAsync();
            }
            catch (Exception exception) { ShowError(exception); }
        };
        actions.Children.Add(clear);
        var filter = new Button { Content = ReportDesignSystem.IconText("\uE71C", "调整筛选", 18, 17), MinWidth = 168 };
        ReportDesignSystem.ConfigureButton(filter, ReportButtonKind.Secondary);
        filter.Click += async (_, _) => await ShowFilterAsync();
        actions.Children.Add(filter);
        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);
        return ReportDesignSystem.Card(bar, new Thickness(0));
    }

    private static UIElement FilterDot() => new TextBlock { Text = "•", FontSize = 18, Foreground = ReportDesignSystem.MutedText, VerticalAlignment = VerticalAlignment.Center };

    private static UIElement FilterSummaryItem(UIElement icon, TextBlock text)
    {
        text.FontSize = 17;
        text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        text.Foreground = ReportDesignSystem.Text;
        text.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, text }
        };
    }

    private static UIElement LayerIcon()
    {
        var layers = new Grid { Width = 22, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        for (var index = 0; index < 3; index++)
        {
            layers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var layer = new Border
            {
                Height = 4,
                Margin = new Thickness(index == 1 ? 2 : 0, 1, index == 1 ? 2 : 0, 1),
                CornerRadius = new CornerRadius(2),
                Background = ReportDesignSystem.SecondaryText,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(layer, index);
            layers.Children.Add(layer);
        }
        return layers;
    }

    private Border BuildPreviewCard()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(SectionTitle("待办概览", "点击生成时重新读取并冻结待办快照。"));
        var counts = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 16, 0, 0) };
        counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        counts.Children.Add(CountCard(StatusIcon(true, ReportDesignSystem.Success), "已完成", _completedCount));
        var unfinished = CountCard(StatusIcon(false, ReportDesignSystem.Warning), "未完成", _unfinishedCount);
        Grid.SetColumn(unfinished, 1);
        counts.Children.Add(unfinished);
        Grid.SetRow(counts, 1);
        content.Children.Add(counts);
        var tableHeader = PreviewHeader();
        tableHeader.Margin = new Thickness(0, 16, 0, 0);
        Grid.SetRow(tableHeader, 2);
        content.Children.Add(tableHeader);
        _previewRows.Spacing = 0;
        Grid.SetRow(_previewRows, 3);
        content.Children.Add(_previewRows);
        return ReportDesignSystem.Card(content, new Thickness(24));
    }

    private Border BuildWritingCard()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(SectionTitle("写作要求", "风格和自定义要求会随待办快照发送给 AI。"));
        var styles = BuildStyleSegments();
        styles.Margin = new Thickness(0, 16, 0, 0);
        Grid.SetRow(styles, 1);
        content.Children.Add(styles);
        var requirementHeader = new StackPanel { Spacing = 4, Margin = new Thickness(0, 18, 0, 0) };
        requirementHeader.Children.Add(new TextBlock { Text = "自定义要求", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        requirementHeader.Children.Add(new TextBlock { Text = "可补充重点、禁用表达或格式要求。", FontSize = 14, Foreground = ReportDesignSystem.MutedText });
        Grid.SetRow(requirementHeader, 2);
        content.Children.Add(requirementHeader);
        _customRequirements.AcceptsReturn = true;
        _customRequirements.TextWrapping = TextWrapping.Wrap;
        _customRequirements.VerticalContentAlignment = VerticalAlignment.Top;
        _customRequirements.MaxLength = 500;
        _customRequirements.MinHeight = 210;
        _customRequirements.PlaceholderText = "例如：\n1. 重点突出本周完成的关键成果与影响。\n2. 未完成事项说明原因、风险及下一步计划。\n3. 表达结构清晰，适合向管理层汇报。";
        ReportDesignSystem.ConfigureTextBox(_customRequirements, 210);
        _customRequirements.TextChanged -= CustomRequirementsTextChanged;
        _customRequirements.TextChanged += CustomRequirementsTextChanged;
        _customRequirements.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_customRequirements, 3);
        content.Children.Add(_customRequirements);
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock { Text = "仅在本次生成中发送，不会作为报告历史保存。", FontSize = 13, Foreground = ReportDesignSystem.MutedText, VerticalAlignment = VerticalAlignment.Center });
        _customCounter.FontSize = 14;
        _customCounter.Foreground = ReportDesignSystem.SecondaryText;
        _customCounter.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_customCounter, 1);
        footer.Children.Add(_customCounter);
        Grid.SetRow(footer, 4);
        content.Children.Add(footer);
        UpdateCustomCounter();
        return ReportDesignSystem.Card(content, new Thickness(24));
    }

    private FrameworkElement BuildStyleSegments()
    {
        var segments = new Grid { Height = 48 };
        for (var index = 0; index < 3; index++) segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddSegment(segments, StyleButton("专业", ReportStyle.Professional), 0, true, false);
        AddSegment(segments, StyleButton("通俗", ReportStyle.Plain), 1, false, false);
        AddSegment(segments, StyleButton("简洁", ReportStyle.Concise), 2, false, true);
        return new Border
        {
            Height = 52,
            Background = ReportDesignSystem.SurfaceRaised,
            BorderBrush = ReportDesignSystem.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(3),
            Child = segments
        };
    }

    private Border BuildActionCard()
    {
        var content = new Grid { Padding = new Thickness(26, 18, 26, 14) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new Grid { ColumnSpacing = 14 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _templateAction.Content = ReportDesignSystem.IconText("\uE8A5", "提供模板与示例", 18, 17);
        _templateAction.MinWidth = 264;
        ReportDesignSystem.ConfigureButton(_templateAction, ReportButtonKind.Secondary);
        _templateAction.Click += (_, _) => NavigateTo(ReportPage.Preferences);
        Grid.SetColumn(_templateAction, 1);
        actions.Children.Add(_templateAction);
        _cancel.Content = ReportDesignSystem.IconText("\uE711", "取消生成", 17, 17);
        _cancel.MinWidth = 264;
        _cancel.Visibility = Visibility.Collapsed;
        ReportDesignSystem.ConfigureButton(_cancel, ReportButtonKind.Secondary);
        _cancel.Click += async (_, _) => await CancelAsync();
        Grid.SetColumn(_cancel, 1);
        actions.Children.Add(_cancel);
        _generate.Content = ReportDesignSystem.IconText("\uE735", "生成汇报", 20, 18);
        _generate.MinWidth = 320;
        _generate.MinHeight = 58;
        ReportDesignSystem.ConfigureButton(_generate, ReportButtonKind.Primary);
        _generate.Click += async (_, _) => await GenerateAsync();
        Grid.SetColumn(_generate, 2);
        actions.Children.Add(_generate);
        content.Children.Add(actions);
        var security = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        security.Children.Add(ReportDesignSystem.Icon("\uE72E", 15, ReportDesignSystem.MutedText));
        security.Children.Add(new TextBlock { Text = "发送所选待办详情前将请求确认", FontSize = 14, Foreground = ReportDesignSystem.MutedText });
        Grid.SetRow(security, 1);
        content.Children.Add(security);
        return ReportDesignSystem.Card(content, new Thickness(0));
    }

    private Border BuildResultCard()
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(SectionTitle("生成结果", "结果会保持模板的表格与富文本块结构，可继续编辑或复制文本；文件结果会写入新副本。"));
        content.Children.Add(_output);
        var copyActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var copyText = new Button { Content = ReportDesignSystem.IconText("\uE8C8", "复制文本", 17, 16) };
        ReportDesignSystem.ConfigureButton(copyText, ReportButtonKind.Secondary);
        copyText.Click += (_, _) => CopyOutput();
        copyActions.Children.Add(copyText);
        content.Children.Add(copyActions);
        _resultCard = ReportDesignSystem.Card(content, new Thickness(24));
        _resultCard.Visibility = Visibility.Collapsed;
        _resultCard.Margin = new Thickness(0, 16, 0, 0);
        return _resultCard;
    }

    private UIElement BuildRecordsPage()
    {
        var page = new StackPanel { Spacing = 16, Margin = new Thickness(64, 20, 64, 28), MaxWidth = 1440 };
        page.Children.Add(PageHeader("生成记录", "文本汇报可直接查看；文件汇报显示已保存的输出路径。不保存待办、模板、示例或自定义要求。"));

        ConfigureRecordFilter(_recordDateFilter, [(RecordTimeScope.All, "全部时间"), (RecordTimeScope.Today, "今天"), (RecordTimeScope.LastSevenDays, "最近 7 天")]);
        ConfigureRecordFilter(_recordStatusFilter, [(RecordStatusScope.All, "全部状态"), (RecordStatusScope.Generating, "生成中"), (RecordStatusScope.Completed, "已完成"), (RecordStatusScope.Failed, "失败"), (RecordStatusScope.Cancelled, "已取消")]);
        ConfigureRecordFilter(_recordRangeFilter, [(RecordRangeScope.All, "全部范围"), (RecordRangeScope.ThisWeek, "本周"), (RecordRangeScope.PreviousWeek, "上周"), (RecordRangeScope.ThisMonth, "本月"), (RecordRangeScope.Custom, "自定义区间")]);
        ConfigureRecordFilter(_recordModeFilter, [(RecordModeScope.All, "全部输出类型"), (RecordModeScope.Text, "文本"), (RecordModeScope.File, "Word / Excel 文件")]);

        var filterGrid = new Grid { ColumnSpacing = 12 };
        for (var index = 0; index < 4; index++) filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterGrid.Children.Add(Labeled("生成时间", _recordDateFilter));
        var statusField = Labeled("状态", _recordStatusFilter);
        var rangeField = Labeled("报告范围", _recordRangeFilter);
        var modeField = Labeled("输出类型", _recordModeFilter);
        Grid.SetColumn(statusField, 1);
        Grid.SetColumn(rangeField, 2);
        Grid.SetColumn(modeField, 3);
        filterGrid.Children.Add(statusField);
        filterGrid.Children.Add(rangeField);
        filterGrid.Children.Add(modeField);
        page.Children.Add(ReportDesignSystem.Card(filterGrid, new Thickness(24)));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var deleteFiltered = new Button { Content = ReportDesignSystem.IconText("\uE74D", "删除当前筛选结果", 17, 16) };
        ReportDesignSystem.ConfigureButton(deleteFiltered, ReportButtonKind.Secondary);
        deleteFiltered.Click += async (_, _) => await ConfirmDeleteRecordsAsync(FilteredRecords(_workspace.State).Select(record => record.Id).ToArray());
        actions.Children.Add(deleteFiltered);
        actions.Children.Add(new TextBlock { Text = "删除记录不会删除任何本地输出文件。", Foreground = ReportDesignSystem.MutedText, VerticalAlignment = VerticalAlignment.Center });
        page.Children.Add(actions);

        _recordRows.Spacing = 12;
        page.Children.Add(_recordRows);
        return page;
    }

    private void ConfigureRecordFilter<T>(ComboBox box, IReadOnlyList<(T Value, string Label)> values) where T : struct
    {
        foreach (var (value, label) in values) box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        box.SelectedIndex = 0;
        ReportDesignSystem.ConfigureComboBox(box);
        box.SelectionChanged += (_, _) => RenderRecords(_workspace.State);
    }

    private void RenderRecords(ReportWorkspaceSnapshot state)
    {
        _recordRows.Children.Clear();
        var records = FilteredRecords(state).ToArray();
        if (records.Length == 0)
        {
            _recordRows.Children.Add(ReportDesignSystem.Card(new TextBlock
            {
                Text = "当前筛选条件下没有生成记录。",
                Foreground = ReportDesignSystem.SecondaryText,
                Padding = new Thickness(4)
            }, new Thickness(24)));
            return;
        }
        foreach (var record in records) _recordRows.Children.Add(RecordRow(record));
    }

    private IEnumerable<ReportGenerationRecord> FilteredRecords(ReportWorkspaceSnapshot state)
    {
        var time = SelectedRecordValue(_recordDateFilter, RecordTimeScope.All);
        var status = SelectedRecordValue(_recordStatusFilter, RecordStatusScope.All);
        var range = SelectedRecordValue(_recordRangeFilter, RecordRangeScope.All);
        var mode = SelectedRecordValue(_recordModeFilter, RecordModeScope.All);
        var now = DateTimeOffset.Now;
        return state.Records.Where(record =>
            (time == RecordTimeScope.All || time == RecordTimeScope.Today && record.CreatedAt.LocalDateTime.Date == now.LocalDateTime.Date || time == RecordTimeScope.LastSevenDays && record.CreatedAt >= now.AddDays(-7)) &&
            (status == RecordStatusScope.All || (status == RecordStatusScope.Generating && record.Status == ReportGenerationRecordStatus.Generating) || (status == RecordStatusScope.Completed && record.Status == ReportGenerationRecordStatus.Completed) || (status == RecordStatusScope.Failed && record.Status == ReportGenerationRecordStatus.Failed) || (status == RecordStatusScope.Cancelled && record.Status == ReportGenerationRecordStatus.Cancelled)) &&
            (range == RecordRangeScope.All || (range == RecordRangeScope.ThisWeek && record.Range.Kind == ReportRangeKind.ThisWeek) || (range == RecordRangeScope.PreviousWeek && record.Range.Kind == ReportRangeKind.PreviousWeek) || (range == RecordRangeScope.ThisMonth && record.Range.Kind == ReportRangeKind.ThisMonth) || (range == RecordRangeScope.Custom && record.Range.Kind == ReportRangeKind.Custom)) &&
            (mode == RecordModeScope.All || (mode == RecordModeScope.Text && record.TemplateMode == ReportTemplateMode.Text) || (mode == RecordModeScope.File && record.TemplateMode == ReportTemplateMode.File)));
    }

    private static T SelectedRecordValue<T>(ComboBox box, T fallback) where T : struct =>
        (box.SelectedItem as ComboBoxItem)?.Tag is T value ? value : fallback;

    private UIElement RecordRow(ReportGenerationRecord record)
    {
        var content = new Grid { ColumnSpacing = 18 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var detail = new StackPanel { Spacing = 5 };
        detail.Children.Add(new TextBlock
        {
            Text = $"{RangeKindText(record.Range.Kind)} · {StyleText(record.Style)} · {(record.TemplateMode == ReportTemplateMode.File ? "文件" : "文本")}",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ReportDesignSystem.Text
        });
        detail.Children.Add(new TextBlock
        {
            Text = $"{record.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm}  ·  {record.Range.Label}  ·  已完成 {record.CompletedTaskCount} / 未完成 {record.UnfinishedTaskCount}",
            Foreground = ReportDesignSystem.SecondaryText
        });
        if (!string.IsNullOrWhiteSpace(record.OutputPath)) detail.Children.Add(new TextBlock { Text = $"输出文件：{record.OutputPath}", TextWrapping = TextWrapping.Wrap, Foreground = ReportDesignSystem.Success });
        else if (record.TemplateMode == ReportTemplateMode.File && record.FileOutputStatus != ReportFileOutputStatus.NotApplicable)
            detail.Children.Add(new TextBlock { Text = FileOutputText(record.FileOutputStatus), Foreground = ReportDesignSystem.MutedText });
        else if (record.TemplateMode == ReportTemplateMode.Text && record.Status == ReportGenerationRecordStatus.Completed && record.TextOutput is null)
            detail.Children.Add(new TextBlock { Text = "文本结果不可用。", Foreground = ReportDesignSystem.MutedText });
        detail.Children.Add(new TextBlock { Text = RecordStatusText(record.Status, record.ErrorCode), Foreground = RecordForeground(record.Status) });
        content.Children.Add(detail);
        var actionPanel = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        if (record.TemplateMode == ReportTemplateMode.Text && record.TextOutput is not null)
        {
            var view = new Button { Content = ReportDesignSystem.IconText("\uE890", "查看结果", 16, 15) };
            ReportDesignSystem.ConfigureButton(view, ReportButtonKind.Secondary);
            view.Click += async (_, _) => await ShowRecordTextOutputAsync(record);
            actionPanel.Children.Add(view);
        }
        var delete = new Button { Content = ReportDesignSystem.IconText("\uE74D", "删除", 16, 15) };
        ReportDesignSystem.ConfigureButton(delete, ReportButtonKind.Secondary);
        delete.Click += async (_, _) => await ConfirmDeleteRecordsAsync([record.Id]);
        actionPanel.Children.Add(delete);
        Grid.SetColumn(actionPanel, 1);
        content.Children.Add(actionPanel);
        return ReportDesignSystem.Card(content, new Thickness(20));
    }

    private async Task ShowRecordTextOutputAsync(ReportGenerationRecord record)
    {
        if (record.TextOutput is null) return;
        var viewer = new ScrollViewer
        {
            MaxHeight = 560,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildRecordTextOutputViewer(record.TextOutput)
        };
        var dialog = ReportDesignSystem.Dialog(_root.XamlRoot, "查看文本结果", viewer, "复制文本", "关闭");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _clipboard.SetText(ReportTextDocuments.ToPlainText(record.TextOutput));
        SetStatus("已复制生成记录中的文本结果。", ReportDesignSystem.Success);
    }

    private static UIElement BuildRecordTextOutputViewer(ReportTextDocument source)
    {
        var document = ReportTextDocuments.Normalize(source);
        var host = new StackPanel { Spacing = 10, Padding = new Thickness(4) };
        foreach (var block in document.Blocks)
        {
            if (block.Kind == ReportTextBlockKind.Divider)
            {
                host.Children.Add(new Border { Height = 1, Background = ReportDesignSystem.Stroke, Margin = new Thickness(0, 10, 0, 10) });
                continue;
            }
            if (block.Kind == ReportTextBlockKind.Table)
            {
                host.Children.Add(BuildRecordTextOutputTable(block.Table));
                continue;
            }

            var prefix = block.Kind switch
            {
                ReportTextBlockKind.BulletedList => "• ",
                ReportTextBlockKind.NumberedList => "1. ",
                ReportTextBlockKind.TodoList => block.IsChecked ? "☑ " : "☐ ",
                ReportTextBlockKind.Quote => "│ ",
                _ => string.Empty
            };
            var text = new TextBlock
            {
                Text = prefix + block.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = block.Link is null ? ReportDesignSystem.Text : ReportDesignSystem.AccentBorder,
                FontSize = block.Kind switch
                {
                    ReportTextBlockKind.Heading1 => 28,
                    ReportTextBlockKind.Heading2 => 23,
                    ReportTextBlockKind.Heading3 => 19,
                    ReportTextBlockKind.Code => 14,
                    _ => 16
                }
            };
            if (block.Bold || block.Kind is ReportTextBlockKind.Heading1 or ReportTextBlockKind.Heading2 or ReportTextBlockKind.Heading3)
                text.FontWeight = FontWeights.SemiBold;
            if (block.Italic || block.Kind == ReportTextBlockKind.Quote)
                text.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
            if (block.Kind == ReportTextBlockKind.Code)
            {
                text.FontFamily = new FontFamily("Cascadia Mono");
                text.Padding = new Thickness(10, 8, 10, 8);
                host.Children.Add(new Border { Background = ReportDesignSystem.InputSurface, CornerRadius = new CornerRadius(8), Child = text });
            }
            else host.Children.Add(text);
        }
        return host;
    }

    private static UIElement BuildRecordTextOutputTable(ReportTextTable? value)
    {
        var table = value ?? new ReportTextTable([new[] { string.Empty }]);
        var grid = new Grid { RowSpacing = 1, ColumnSpacing = 1 };
        for (var column = 0; column < table.ColumnCount; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 112 });
        for (var row = 0; row < table.RowCount; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var row = 0; row < table.RowCount; row++)
        for (var column = 0; column < table.ColumnCount; column++)
        {
            var cell = new Border
            {
                Background = ReportDesignSystem.InputSurface,
                BorderBrush = ReportDesignSystem.Stroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Child = new TextBlock
                {
                    Text = table.Cells[row][column],
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ReportDesignSystem.Text,
                    FontWeight = row == 0 ? FontWeights.SemiBold : FontWeights.Normal
                }
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        return new Border
        {
            Background = ReportDesignSystem.SurfaceRaised,
            BorderBrush = ReportDesignSystem.StrongStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = grid
        };
    }

    private async Task ConfirmDeleteRecordsAsync(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0) return;
        var content = new TextBlock
        {
            Text = $"将删除 {ids.Count} 条本地生成记录。报告正文和输出文件不会被删除。此操作无法撤销。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ReportDesignSystem.SecondaryText
        };
        var dialog = ReportDesignSystem.Dialog(_root.XamlRoot, "删除生成记录", content, "删除记录", "取消");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) _workspace.DeleteRecords(ids);
    }

    private static string RangeKindText(ReportRangeKind kind) => kind switch
    {
        ReportRangeKind.ThisWeek => "本周汇报",
        ReportRangeKind.PreviousWeek => "上周汇报",
        ReportRangeKind.ThisMonth => "本月汇报",
        _ => "自定义区间"
    };

    private static string StyleText(ReportStyle style) => style switch
    {
        ReportStyle.Plain => "通俗",
        ReportStyle.Concise => "简洁",
        _ => "专业"
    };

    private static string FileOutputText(ReportFileOutputStatus status) => status switch
    {
        ReportFileOutputStatus.Pending => "文件输出尚未保存。",
        ReportFileOutputStatus.Cancelled => "已取消选择输出位置。",
        ReportFileOutputStatus.Failed => "文件输出失败。",
        _ => string.Empty
    };

    private static string RecordStatusText(ReportGenerationRecordStatus status, string? errorCode) => status switch
    {
        ReportGenerationRecordStatus.Completed => "已完成",
        ReportGenerationRecordStatus.Cancelled => "已取消",
        ReportGenerationRecordStatus.Failed when errorCode == "interrupted" => "已中断",
        ReportGenerationRecordStatus.Failed => "生成失败",
        _ => "正在生成"
    };

    private static Brush RecordForeground(ReportGenerationRecordStatus status) => status switch
    {
        ReportGenerationRecordStatus.Completed => ReportDesignSystem.Success,
        ReportGenerationRecordStatus.Failed => ReportDesignSystem.Danger,
        ReportGenerationRecordStatus.Cancelled => ReportDesignSystem.Warning,
        _ => ReportDesignSystem.SecondaryText
    };

    private UIElement BuildPreferencesPage()
    {
        var page = new StackPanel { Spacing = 16, Margin = new Thickness(64, 20, 64, 28), MaxWidth = 1440 };
        page.Children.Add(PageHeader("模板偏好", "选择本次生成的文本或文件模板；只有主动保存的偏好会写入本机受控副本。"));
        var modeCard = new StackPanel { Spacing = 12 };
        modeCard.Children.Add(new TextBlock { Text = "模板模式", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        modeCard.Children.Add(BuildTemplateModeSegments());
        page.Children.Add(ReportDesignSystem.Card(modeCard, new Thickness(24)));

        var editor = new Grid();
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _textTemplateFields.Spacing = 12;
        _textTemplateFields.Children.Clear();
        _textTemplateFields.Children.Add(BlockEditorField("文本模板", _templateInput, "可为空；使用 / 或块左侧 + 添加标题、列表、引用、代码块、分隔线和表格。表格边框可直接增删行列或清空内容。"));
        _textTemplateFields.Children.Add(BlockEditorField("填写示例", _exampleInput, "可为空；与模板使用同一块编辑器，只学习表达方式，不作为待办事实来源。"));
        var textActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var saveTextPreference = new Button { Content = ReportDesignSystem.IconText("\uE74E", "保存文本偏好", 17, 16) };
        ReportDesignSystem.ConfigureButton(saveTextPreference, ReportButtonKind.Secondary);
        saveTextPreference.Click += (_, _) => SaveRichTextPreference();
        textActions.Children.Add(saveTextPreference);
        _textTemplateFields.Children.Add(textActions);
        _richTextPreferenceStatus.Foreground = ReportDesignSystem.MutedText;
        _richTextPreferenceStatus.TextWrapping = TextWrapping.Wrap;
        _textTemplateFields.Children.Add(_richTextPreferenceStatus);
        Grid.SetRow(_textTemplateFields, 0);
        editor.Children.Add(_textTemplateFields);

        _fileTemplateFields.Spacing = 14;
        _fileTemplateFields.Children.Clear();
        _fileTemplateFields.Children.Add(new TextBlock { Text = "文件模板", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        _fileTemplateFields.Children.Add(new TextBlock { Text = "支持 Word .docx 和 Excel .xlsx。暂不支持 .doc、.xls、.docm、.xlsm、.xlsb 或 PDF。模板不需要占位符；工具只会在输出副本中写入文字和表格数据，源文件不会被修改。", TextWrapping = TextWrapping.Wrap, Foreground = ReportDesignSystem.SecondaryText });
        var picks = new Grid { ColumnSpacing = 12 };
        picks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        picks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chooseTemplate = new Button { Content = ReportDesignSystem.IconText("\uE8A5", "选择文件模板", 17, 16), HorizontalAlignment = HorizontalAlignment.Stretch };
        ReportDesignSystem.ConfigureButton(chooseTemplate, ReportButtonKind.Secondary);
        chooseTemplate.Click += async (_, _) => await ChooseTemplateAsync(isExample: false);
        picks.Children.Add(chooseTemplate);
        var chooseExample = new Button { Content = ReportDesignSystem.IconText("\uE8A5", "选择文件示例", 17, 16), HorizontalAlignment = HorizontalAlignment.Stretch };
        ReportDesignSystem.ConfigureButton(chooseExample, ReportButtonKind.Secondary);
        chooseExample.Click += async (_, _) => await ChooseTemplateAsync(isExample: true);
        Grid.SetColumn(chooseExample, 1);
        picks.Children.Add(chooseExample);
        _fileTemplateFields.Children.Add(picks);
        _templateFile.Foreground = ReportDesignSystem.SecondaryText;
        _templateFile.TextWrapping = TextWrapping.Wrap;
        _exampleFile.Foreground = ReportDesignSystem.MutedText;
        _exampleFile.TextWrapping = TextWrapping.Wrap;
        _fileTemplateFields.Children.Add(_templateFile);
        _fileTemplateFields.Children.Add(_exampleFile);
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var savePreference = new Button { Content = ReportDesignSystem.IconText("\uE74E", "保存文件偏好", 17, 16) };
        ReportDesignSystem.ConfigureButton(savePreference, ReportButtonKind.Secondary);
        savePreference.Click += (_, _) => SaveFilePreference();
        fileActions.Children.Add(savePreference);
        _fileTemplateFields.Children.Add(fileActions);
        Grid.SetRow(_fileTemplateFields, 1);
        editor.Children.Add(_fileTemplateFields);
        page.Children.Add(ReportDesignSystem.Card(editor, new Thickness(24)));
        _preferenceStatus.TextWrapping = TextWrapping.Wrap;
        _preferenceStatus.Foreground = ReportDesignSystem.MutedText;
        page.Children.Add(_preferenceStatus);
        var back = new Button { Content = ReportDesignSystem.IconText("\uE72B", "返回生成汇报", 17, 16), HorizontalAlignment = HorizontalAlignment.Left };
        ReportDesignSystem.ConfigureButton(back, ReportButtonKind.Secondary);
        back.Click += (_, _) => NavigateTo(ReportPage.Generate);
        page.Children.Add(back);
        ConfigureTemplateInputs();
        UpdateTemplateModeControls();
        return page;
    }

    private UIElement BuildHelpPage()
    {
        var page = new StackPanel { Spacing = 16, Margin = new Thickness(64, 20, 64, 28), MaxWidth = 1120 };
        page.Children.Add(PageHeader("使用说明", "按范围筛选待办、补充写作要求，并在授权后生成可编辑的汇报。"));
        var steps = new StackPanel { Spacing = 16 };
        steps.Children.Add(HelpStep("1", "选择报告范围", "使用本周、上周、本月或自定义区间，并按待办工具相同语义调整筛选。"));
        steps.Children.Add(HelpStep("2", "核对待办快照", "概览同时显示已完成和未完成任务；生成时会重新读取并固定快照。"));
        steps.Children.Add(HelpStep("3", "补充写作要求", "选择专业、通俗或简洁风格，并按需提供模板、示例和自定义要求。"));
        steps.Children.Add(HelpStep("4", "确认并生成", "首次向模型端点发送任务详情、模板、示例和要求前会请求授权。"));
        page.Children.Add(ReportDesignSystem.Card(steps, new Thickness(24)));
        var safety = new StackPanel { Spacing = 8 };
        safety.Children.Add(new TextBlock { Text = "文件模板边界", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        safety.Children.Add(new TextBlock { Text = "仅支持 Word .docx 与 Excel .xlsx；不支持旧 Office、宏文件或 PDF。模板不要求占位符。汇报工具仅修改输出副本的正文/表格文字和 Excel 常量单元格，不执行宏、不改公式、不改图表或文件路径。", TextWrapping = TextWrapping.Wrap, Foreground = ReportDesignSystem.SecondaryText });
        page.Children.Add(ReportDesignSystem.Card(safety, new Thickness(24)));
        return page;
    }

    private static UIElement HelpStep(string number, string title, string description)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = ReportDesignSystem.Accent,
            Child = new TextBlock { Text = number, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = ReportDesignSystem.SecondaryText });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static UIElement PageHeader(string title, string description) => new StackPanel
    {
        Spacing = 7,
        Children =
        {
            new TextBlock { Text = title, FontSize = 40, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text },
            new TextBlock { Text = description, FontSize = 17, Foreground = ReportDesignSystem.SecondaryText, TextWrapping = TextWrapping.Wrap }
        }
    };

    private FrameworkElement BuildTemplateModeSegments()
    {
        var segments = new Grid { Height = 48 };
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddSegment(segments, TemplateModeButton("文本", ReportTemplateMode.Text), 0, true, false);
        AddSegment(segments, TemplateModeButton("Word / Excel 文件", ReportTemplateMode.File), 1, false, true);
        return new Border
        {
            Background = ReportDesignSystem.SurfaceRaised,
            BorderBrush = ReportDesignSystem.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(3),
            Child = segments
        };
    }

    private void ConfigureTemplateInputs()
    {
        _templateInput.DocumentChanged += (_, document) => _workspace.SetTextDocuments(document, _exampleInput.Document);
        _exampleInput.DocumentChanged += (_, document) => _workspace.SetTextDocuments(_templateInput.Document, document);
        _output.DocumentChanged += (_, document) => _workspace.SetOutputDocument(document);
    }

    private static FrameworkElement BlockEditorField(string label, ReportBlockEditor editor, string hint)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        panel.Children.Add(new TextBlock { Text = hint, FontSize = 13, Foreground = ReportDesignSystem.MutedText, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(editor);
        return panel;
    }

    private static UIElement StatusIcon(bool completed, Brush accent)
    {
        var icon = new Grid { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        icon.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Stroke = accent, StrokeThickness = 2.5, Width = 25, Height = 25, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (completed) icon.Children.Add(ReportDesignSystem.Icon("\uE73E", 14, accent));
        return icon;
    }

    private Border CountCard(UIElement statusIcon, string label, TextBlock value)
    {
        var grid = new Grid { Padding = new Thickness(18, 14, 18, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(statusIcon);
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = label, Foreground = ReportDesignSystem.SecondaryText, FontSize = 16 });
        value.FontSize = 30;
        value.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        value.Foreground = ReportDesignSystem.Text;
        text.Children.Add(value);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border { Background = ReportDesignSystem.SurfaceRaised, BorderBrush = ReportDesignSystem.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = grid };
    }

    private Grid PreviewHeader()
    {
        var grid = CreatePreviewGrid();
        grid.Margin = new Thickness(0, 2, 0, 0);
        AddCell(grid, "状态", 0, ReportDesignSystem.MutedText, true);
        AddCell(grid, "待办事项", 1, ReportDesignSystem.MutedText, true);
        AddCell(grid, "截止时间", 2, ReportDesignSystem.MutedText, true);
        AddCell(grid, "层级", 3, ReportDesignSystem.MutedText, true);
        AddCell(grid, "清单", 4, ReportDesignSystem.MutedText, true);
        return grid;
    }

    private UIElement PreviewRow(ReportTaskSnapshot task)
    {
        var grid = CreatePreviewGrid();
        grid.Padding = new Thickness(0, 8, 0, 8);
        var completed = task.Status == "completed";
        AddCell(grid, "●", 0, completed ? ReportDesignSystem.Success : ReportDesignSystem.Warning);
        var title = AddCell(grid, task.Title, 1, ReportDesignSystem.Text);
        ToolTipService.SetToolTip(title, task.Title);
        AddCell(grid, task.DueDate?.ToString("M月d日") ?? "—", 2, ReportDesignSystem.SecondaryText);
        AddCell(grid, task.Level <= 1 ? "●" : "└", 3, ReportDesignSystem.SecondaryText);
        var list = new Border
        {
            Background = completed ? ReportDesignSystem.Brush("#1C3C55") : ReportDesignSystem.Brush("#3A3030"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = task.ListName, FontSize = 12, Foreground = completed ? ReportDesignSystem.Brush("#8ACBFF") : ReportDesignSystem.Brush("#FFBD82"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 100 }
        };
        Grid.SetColumn(list, 4);
        grid.Children.Add(list);
        return new Border { BorderBrush = ReportDesignSystem.Brush("#23354D"), BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
    }

    private static Grid CreatePreviewGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        return grid;
    }

    private static TextBlock AddCell(Grid grid, string text, int column, Brush color, bool header = false)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = color,
            FontSize = header ? 13 : 14,
            FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        return block;
    }

    private Button StyleButton(string text, ReportStyle style)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        ReportDesignSystem.ConfigureButton(button, ReportButtonKind.Segment);
        _styleButtons[style] = button;
        button.Click += (_, _) =>
        {
            _style.SelectedItem = _style.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, style));
            UpdateStyleButtons(style);
        };
        return button;
    }

    private Button TemplateModeButton(string text, ReportTemplateMode mode)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        ReportDesignSystem.ConfigureButton(button, ReportButtonKind.Segment);
        _templateModeButtons[mode] = button;
        button.Click += (_, _) =>
        {
            _mode.SelectedItem = _mode.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, mode));
            UpdateTemplateModeControls();
        };
        return button;
    }

    private Button RangeButton(string text, ReportRangeKind kind)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        ReportDesignSystem.ConfigureButton(button, ReportButtonKind.Segment);
        _rangeButtons[kind] = button;
        button.Click += async (_, _) =>
        {
            try
            {
                if (kind == ReportRangeKind.Custom)
                {
                    await ShowFilterAsync();
                    return;
                }
                _workspace.SelectRange(kind);
                await RefreshPreviewAsync();
            }
            catch (Exception exception) { ShowError(exception); }
        };
        return button;
    }

    private static void AddSegment(Grid host, Button button, int column, bool first, bool last)
    {
        button.CornerRadius = new CornerRadius(first || last ? 8 : 0);
        button.BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0);
        button.BorderBrush = ReportDesignSystem.Stroke;
        Grid.SetColumn(button, column);
        host.Children.Add(button);
    }

    private void InitializeEditorControls()
    {
        if (_style.Items.Count == 0)
        {
            _style.Items.Add(StyleItem("专业", ReportStyle.Professional));
            _style.Items.Add(StyleItem("通俗", ReportStyle.Plain));
            _style.Items.Add(StyleItem("简洁", ReportStyle.Concise));
            _style.SelectedIndex = 0;
        }
        if (_mode.Items.Count == 0)
        {
            _mode.Items.Add(new ComboBoxItem { Content = "文本", Tag = ReportTemplateMode.Text });
            _mode.Items.Add(new ComboBoxItem { Content = "Word / Excel 文件", Tag = ReportTemplateMode.File });
            _mode.SelectedIndex = 0;
            _mode.SelectionChanged += (_, _) => UpdateTemplateModeControls();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _ai.ConnectAsync();
            var preference = _preferences.Load();
            var textPreference = _preferences.LoadText();
            if (textPreference.Template is not null) _templateInput.SetDocument(textPreference.Template, resetHistory: true);
            if (textPreference.Example is not null) _exampleInput.SetDocument(textPreference.Example, resetHistory: true);
            if (textPreference.Template is not null || textPreference.Example is not null)
            {
                _richTextPreferenceStatus.Foreground = ReportDesignSystem.Success;
                _richTextPreferenceStatus.Text = "已载入本机保存的块文档模板和示例偏好。";
            }
            _workspace.SetTextDocuments(_templateInput.Document, _exampleInput.Document);
            if (preference.TemplateFileName is not null)
            {
                _templatePath = preference.TemplateFileName;
                _templateInspection = OpenXmlReportTemplateService.Inspect(_templatePath);
                _templateFile.Text = $"已载入已保存模板：{Path.GetFileName(_templatePath)}";
            }
            if (preference.ExampleFileName is not null)
            {
                _examplePath = preference.ExampleFileName;
                _exampleFile.Text = $"已载入已保存示例：{Path.GetFileName(_examplePath)}";
            }
            await RefreshPreviewAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task InitializeVisualFixtureAsync()
    {
#if DEBUG
        _style.SelectedIndex = 0;
        _customRequirements.Text = string.Empty;
        _templateInput.SetDocument(ReportTextDocuments.FromMarkdown(ReportVisualFixture.Template), resetHistory: true);
        _exampleInput.SetDocument(ReportTextDocuments.FromMarkdown(ReportVisualFixture.Example), resetHistory: true);
        _workspace.SetCustomRequirements(string.Empty);
        _workspace.SetTextDocuments(_templateInput.Document, _exampleInput.Document);
        await _workspace.RefreshPreviewAsync();
#else
        throw new InvalidOperationException("Visual fixtures are available only in Debug builds.");
#endif
    }

    private async Task RefreshPreviewAsync()
    {
        try { await _workspace.RefreshPreviewAsync(); }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task GenerateAsync()
    {
        try
        {
            ApplyEditorState();
            SetStatus("正在读取并冻结待办快照…", ReportDesignSystem.SecondaryText);
            var input = await _workspace.BeginGenerationAsync();
            _activeGeneration = input;
            _generationAttempt = 1;
            await RequestCandidateAsync(input, _generationAttempt, null, null);
        }
        catch (Exception exception)
        {
            var state = _workspace.State;
            if (state.Lifecycle != ReportGenerationLifecycle.Failed)
            {
                _workspace.Fail(null, ToSafeMessage(exception), ToRecordErrorCode(exception));
                state = _workspace.State;
            }
            CompleteProgressDialog("生成失败", state.Error ?? ToSafeMessage(exception), ReportDesignSystem.Danger);
        }
    }

    private async Task RequestCandidateAsync(
        ReportGenerationInput input,
        int attempt,
        AiReportContentDocument? candidate,
        string? validationFeedback)
    {
        var result = await _ai.GenerateAsync(
            input,
            ConfirmReportDataSendingAsync,
            () => ShowProgressDialog(
                attempt == 1 ? "正在由 AI 生成" : $"正在请求 AI 修正（第 {attempt}/3 轮）",
                attempt == 1 ? "已固定待办快照，正在等待模型生成汇报。" : "正在依据本地结构校验结果调整完整报告内容。"),
            attempt,
            candidate,
            validationFeedback);
        if (!result.Executed || result.Value is null)
        {
            _workspace.Fail(null, "用户取消了向模型端点发送汇报数据。", "consent_declined");
            CompleteProgressDialog("已取消", "未向模型端点发送汇报数据。", ReportDesignSystem.Warning);
            return;
        }
        _workspace.AcceptInvocation(result.Value.InvocationId);
        UpdateProgressDialog(_workspace.State);
    }

    private async Task CancelAsync()
    {
        var state = _workspace.State;
        if (!_workspace.BeginCancellation() || string.IsNullOrWhiteSpace(state.InvocationId)) return;
        try { await _ai.CancelAsync(state.InvocationId); }
        catch (Exception exception) { _workspace.Fail(state.InvocationId, ToSafeMessage(exception)); }
    }

    private void ApplyEditorState()
    {
        _workspace.SetStyle((_style.SelectedItem as ComboBoxItem)?.Tag as ReportStyle? ?? ReportStyle.Professional);
        _workspace.SetCustomRequirements(_customRequirements.Text);
        var mode = (_mode.SelectedItem as ComboBoxItem)?.Tag as ReportTemplateMode? ?? ReportTemplateMode.Text;
        if (mode == ReportTemplateMode.Text)
        {
            _workspace.SetTextDocuments(_templateInput.Document, _exampleInput.Document);
            return;
        }
        if (_templatePath is null || _templateInspection is null)
            throw new InvalidOperationException("文件模式必须选择受支持的 .docx 或 .xlsx 模板。");
        if (_examplePath is not null && !string.Equals(Path.GetExtension(_examplePath), _templateInspection.Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("文件示例必须与模板使用相同文件类型。");
        var example = _examplePath is null ? null : OpenXmlReportTemplateService.Inspect(_examplePath).Document;
        _workspace.SetTemplate(new(ReportTemplateMode.File, string.Empty, string.Empty, _templatePath, _examplePath,
            _templateInspection.Document, example));
    }

    private void HandleNotification(AiCoreNotificationEventArgs notification)
    {
        try
        {
            if (notification.Method == AiProtocolNotifications.ReportCompleted)
            {
                var completed = notification.DeserializeParameters<AiReportCompleted>();
                if (_workspace.State.Template.Mode == ReportTemplateMode.Text)
                {
                    var output = ToReportGenerationOutput(completed);
                    if (!_workspace.Complete(completed.InvocationId, output)) return;
                    _workspace.SetOutputDocument(output.TextDocument ??
                        throw new AiCoreException("invalid_response", "文本汇报缺少完整内容。"));
                    CompleteProgressDialog("生成完成", "文本汇报已生成，可继续编辑或复制。", ReportDesignSystem.Success);
                }
                else _ = ProcessFileCandidateAsync(completed);
            }
            else if (notification.Method == AiProtocolNotifications.ReportCancelled)
            {
                var finished = notification.DeserializeParameters<AiReportFinished>();
                _workspace.Cancel(finished.InvocationId);
                CompleteProgressDialog("已取消", "未产生部分结果文件。", ReportDesignSystem.Warning);
            }
            else if (notification.Method == AiProtocolNotifications.ReportFailed)
            {
                var finished = notification.DeserializeParameters<AiReportFinished>();
                _workspace.Fail(finished.InvocationId, ErrorText(finished.ErrorCode), finished.ErrorCode);
                CompleteProgressDialog("生成失败", ErrorText(finished.ErrorCode), ReportDesignSystem.Danger);
            }
        }
        catch (Exception exception)
        {
            _workspace.Fail(null, ToSafeMessage(exception), ToRecordErrorCode(exception));
            CompleteProgressDialog("生成失败", ToSafeMessage(exception), ReportDesignSystem.Danger);
        }
    }

    /// <summary>Converts a complete provider candidate into the client-owned editable document model.</summary>
    internal static ReportGenerationOutput ToReportGenerationOutput(AiReportCompleted completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(completed.Output);
        var candidate = completed.Output.Document ?? throw new AiCoreException("invalid_response", "AI 未返回完整汇报内容。");
        return string.Equals(candidate.Format, "text", StringComparison.Ordinal)
            ? new(ReportAiContentMapper.FromWireText(candidate), null)
            : new(null, ReportAiContentMapper.FromWireFile(candidate));
    }

    private async Task ProcessFileCandidateAsync(AiReportCompleted completed)
    {
        try
        {
            var state = _workspace.State;
            if (!string.Equals(state.InvocationId, completed.InvocationId, StringComparison.Ordinal)) return;
            var output = ToReportGenerationOutput(completed);
            var candidate = output.FileDocument ?? throw new AiCoreException("invalid_response", "AI 未返回文件汇报内容。");
            var template = state.Template;
            if (template.TemplateFilePath is null) throw new InvalidOperationException("文件模板不存在。");
            ShowProgressDialog("正在校验候选内容", "正在将 AI 候选写入临时副本并执行结构校验。");
            var validation = await OpenXmlReportTemplateService.ValidateCandidateAsync(template.TemplateFilePath, candidate);
            if (validation.IsValid)
            {
                if (!_workspace.Complete(completed.InvocationId, output)) return;
                await SaveFileOutputAsync(candidate);
                return;
            }
            state = _workspace.State;
            if (state.Lifecycle != ReportGenerationLifecycle.Generating ||
                !string.Equals(state.InvocationId, completed.InvocationId, StringComparison.Ordinal)) return;
            if (_generationAttempt >= 3 || _activeGeneration is null)
            {
                _workspace.Fail(completed.InvocationId, validation.SafeDiagnostic ?? "AI 返回内容无法写入模板。", "candidate_validation_failed");
                CompleteProgressDialog("生成失败", validation.SafeDiagnostic ?? "AI 返回内容无法写入模板。", ReportDesignSystem.Danger);
                return;
            }
            if (!_workspace.BeginRepair(completed.InvocationId)) return;
            _generationAttempt++;
            await RequestCandidateAsync(_activeGeneration, _generationAttempt, completed.Output.Document, validation.SafeDiagnostic);
        }
        catch (Exception exception)
        {
            var state = _workspace.State;
            if (state.Lifecycle == ReportGenerationLifecycle.Generating &&
                string.Equals(state.InvocationId, completed.InvocationId, StringComparison.Ordinal))
            {
                _workspace.Fail(completed.InvocationId, ToSafeMessage(exception), ToRecordErrorCode(exception));
                CompleteProgressDialog("生成失败", ToSafeMessage(exception), ReportDesignSystem.Danger);
            }
        }
    }

    private async Task SaveFileOutputAsync(ReportFileContentDocument candidate)
    {
        var template = _workspace.State.Template;
        if (template.TemplateFilePath is null) return;
        var extension = Path.GetExtension(template.TemplateFilePath).ToLowerInvariant();
        ShowProgressDialog("请选择输出位置", "AI 已完成生成；请选择报告副本的保存位置。", canCancel: false);
        var targetPath = await _fileDialogs.PickSaveAsync(new(
            $"Fowan 汇报 {DateTime.Now:yyyyMMdd}",
            extension == ".docx" ? "Word 文档" : "Excel 工作簿",
            extension));
        if (targetPath is null)
        {
            _workspace.MarkFileOutputCancelled();
            CompleteProgressDialog("生成完成", "已取消选择输出位置，未写入文件副本。", ReportDesignSystem.Warning);
            return;
        }
        try
        {
            ShowProgressDialog("正在写入文件副本", "正在填充模板副本并验证输出文件。", canCancel: false);
            await OpenXmlReportTemplateService.WriteAsync(template.TemplateFilePath, targetPath, candidate);
            _workspace.MarkFileOutputSaved(targetPath);
            SetStatus($"已生成文件副本：{targetPath}", ReportDesignSystem.Success);
            CompleteProgressDialog("生成完成", $"已生成文件副本：{targetPath}", ReportDesignSystem.Success);
        }
        catch (Exception exception)
        {
            _workspace.MarkFileOutputFailed();
            ShowError(exception);
            CompleteProgressDialog("文件输出失败", ToSafeMessage(exception), ReportDesignSystem.Danger);
        }
    }

    private async Task ChooseTemplateAsync(bool isExample)
    {
        try
        {
            var path = await _fileDialogs.PickOpenAsync(new([".docx", ".xlsx"]));
            if (path is null) return;
            var inspection = OpenXmlReportTemplateService.Inspect(path);
            if (isExample)
            {
                if (_templateInspection is not null && !string.Equals(inspection.Extension, _templateInspection.Extension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("示例必须与模板为同一种文件类型。");
                _examplePath = path;
                _exampleFile.Text = $"文件示例：{Path.GetFileName(path)}";
            }
            else
            {
                if (_examplePath is not null && !string.Equals(Path.GetExtension(_examplePath), inspection.Extension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("模板必须与已选示例为同一种文件类型。");
                _templatePath = path;
                _templateInspection = inspection;
                _templateFile.Text = $"文件模板：{Path.GetFileName(path)}（已读取完整内容结构）";
            }
            _preferenceStatus.Foreground = ReportDesignSystem.SecondaryText;
            _preferenceStatus.Text = "文件仅在本次生成中使用；选择“保存文件偏好”后才会复制到本机受控目录。";
        }
        catch (Exception exception) { ShowError(exception, isExample ? "file-example-select" : "file-template-select"); }
    }

    private void SaveFilePreference()
    {
        try
        {
            if (_templatePath is null) throw new InvalidOperationException("请先选择文件模板。");
            _preferences.Save(_templatePath, _examplePath);
            var saved = _preferences.Load();
            _templatePath = saved.TemplateFileName;
            _examplePath = saved.ExampleFileName;
            _templateInspection = _templatePath is null ? null : OpenXmlReportTemplateService.Inspect(_templatePath);
            _templateFile.Text = _templatePath is null ? string.Empty : $"已保存模板偏好：{Path.GetFileName(_templatePath)}";
            _exampleFile.Text = _examplePath is null ? "未保存文件示例。" : $"已保存示例偏好：{Path.GetFileName(_examplePath)}";
            _preferenceStatus.Foreground = ReportDesignSystem.Success;
            _preferenceStatus.Text = "已保存为本机受控文件副本。";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void SaveRichTextPreference()
    {
        try
        {
            _preferences.SaveText(_templateInput.Document, _exampleInput.Document);
            _richTextPreferenceStatus.Foreground = ReportDesignSystem.Success;
            _richTextPreferenceStatus.Text = "已保存为本机受控块文档模板和示例偏好。";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task ShowFilterAsync()
    {
        var state = _workspace.State;
        var store = new TodoStore();
        var data = store.LoadData();
        var list = new ComboBox();
        list.Items.Add(new ComboBoxItem { Content = "全部清单", Tag = string.Empty });
        foreach (var entry in data.Lists) list.Items.Add(new ComboBoxItem { Content = entry.Name, Tag = entry.Id });
        list.SelectedItem = list.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, state.Filter.ListId)) ?? list.Items[0];
        var depth = new ComboBox();
        foreach (var option in new[] { (1, "仅一级"), (2, "一级和二级"), (TodoQuery.MaxTaskTreeDepth, "全部层级") })
            depth.Items.Add(new ComboBoxItem { Content = option.Item2, Tag = option.Item1 });
        depth.SelectedItem = depth.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, state.Filter.MaximumDepth));
        var dateMode = new ComboBox();
        dateMode.Items.Add(new ComboBoxItem { Content = "未选择", Tag = "none" });
        dateMode.Items.Add(new ComboBoxItem { Content = "执行周期", Tag = TodoDateFilterMode.ExecutionPeriod });
        dateMode.Items.Add(new ComboBoxItem { Content = "开始日期", Tag = TodoDateFilterMode.StartDate });
        var effectiveRange = state.Range;
        var selectedDateMode = state.Filter.DateRange?.Mode ?? (effectiveRange is null ? null : TodoDateFilterMode.ExecutionPeriod);
        dateMode.SelectedItem = dateMode.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, selectedDateMode) || (selectedDateMode is null && Equals(item.Tag, "none")));
        var initialStart = state.Filter.DateRange?.StartDate ?? effectiveRange?.Start ?? DateTime.Today;
        var initialEnd = state.Filter.DateRange?.EndDate ?? effectiveRange?.End ?? DateTime.Today;
        var start = new DatePicker { Date = new DateTimeOffset(initialStart) };
        var end = new DatePicker { Date = new DateTimeOffset(initialEnd) };
        ReportDesignSystem.ConfigureComboBox(list);
        ReportDesignSystem.ConfigureComboBox(depth);
        ReportDesignSystem.ConfigureComboBox(dateMode);
        ReportDesignSystem.ConfigureDatePicker(start);
        ReportDesignSystem.ConfigureDatePicker(end);
        var panel = new StackPanel { Spacing = 16, MinWidth = 620 };
        panel.Children.Add(Labeled("任务清单", list, "与待办工具使用相同的清单筛选。"));
        panel.Children.Add(Labeled("任务层级", depth, "决定是否包含子级任务。"));
        panel.Children.Add(Labeled("日期方式", dateMode, "未选择时不设汇报范围；选择日期后可按执行周期或开始日期筛选。"));
        panel.Children.Add(Labeled("开始日期", start));
        panel.Children.Add(Labeled("结束日期", end));
        void UpdateDateEnabled()
        {
            var enabled = !Equals((dateMode.SelectedItem as ComboBoxItem)?.Tag, "none");
            start.IsEnabled = enabled;
            end.IsEnabled = enabled;
        }
        dateMode.SelectionChanged += (_, _) => UpdateDateEnabled();
        UpdateDateEnabled();
        var dialog = ReportDesignSystem.Dialog(_root.XamlRoot, "调整筛选", panel, "应用筛选", "取消");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var selectedList = (list.SelectedItem as ComboBoxItem)?.Tag as string;
        var selectedDepth = (depth.SelectedItem as ComboBoxItem)?.Tag as int? ?? TodoQuery.MaxTaskTreeDepth;
        var selectedMode = (dateMode.SelectedItem as ComboBoxItem)?.Tag as TodoDateFilterMode?;
        if (selectedMode is null)
        {
            _workspace.SetFilter(new TodoFilterCriteria(selectedList, selectedDepth, null));
        }
        else
        {
            if (start.Date.Date > end.Date.Date) throw new InvalidOperationException("日期范围无效。");
            _workspace.SetFilter(new TodoFilterCriteria(selectedList, selectedDepth, null));
            _workspace.SetCustomRange(start.Date.DateTime.Date, end.Date.DateTime.Date, selectedMode.Value);
        }
        await RefreshPreviewAsync();
    }

    private Task<bool> ConfirmReportDataSendingAsync(string endpoint)
    {
        var content = new StackPanel { Spacing = 14, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = "确认发送汇报数据", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        content.Children.Add(new TextBlock { Text = "生成进度 · 等待授权", FontSize = 14, Foreground = ReportDesignSystem.MutedText });
        content.Children.Add(new TextBlock { Text = "即将发送的数据", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        content.Children.Add(new TextBlock
        {
            Text = "完整任务详情（含备注）、模板、填写示例和自定义要求仅用于本次生成。你可以取消，不会产生部分结果文件。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            Foreground = ReportDesignSystem.SecondaryText
        });
        content.Children.Add(new Border
        {
            Background = ReportDesignSystem.InputSurface,
            BorderBrush = ReportDesignSystem.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 48,
                MaxHeight = 96,
                Background = ReportDesignSystem.InputSurface,
                Content = new TextBlock
                {
                    Text = endpoint,
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(14, 10, 14, 10),
                    Foreground = ReportDesignSystem.Text
                }
            }
        });
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Grid? overlay = null;
        void Resolve(bool accepted)
        {
            if (overlay is not null) _root.Children.Remove(overlay);
            completion.TrySetResult(accepted);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = "取消", MinWidth = 126 };
        ReportDesignSystem.ConfigureButton(cancel, ReportButtonKind.Secondary);
        cancel.Click += (_, _) => Resolve(false);
        var authorize = new Button { Content = "授权并继续", MinWidth = 148 };
        ReportDesignSystem.ConfigureButton(authorize, ReportButtonKind.Primary);
        authorize.Click += (_, _) => Resolve(true);
        actions.Children.Add(cancel);
        actions.Children.Add(authorize);
        content.Children.Add(actions);
        var card = new Border
        {
            Background = ReportDesignSystem.Surface,
            BorderBrush = ReportDesignSystem.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(32, 28, 32, 26),
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = content
        };
        AutomationProperties.SetName(card, "确认发送汇报数据");
        overlay = CreateCenteredOverlay(card);
        _root.Children.Add(overlay);
        return completion.Task;
    }

    /// <summary>Hosts modal report surfaces in the root grid so their center is the report window's center.</summary>
    private static Grid CreateCenteredOverlay(UIElement card)
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(168, 4, 10, 20)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(32)
        };
        overlay.Children.Add(card);
        Grid.SetRowSpan(overlay, 2);
        return overlay;
    }

    private void ShowProgressDialog(string stage, string detail, bool canCancel = true)
    {
        if (_progressOverlay is null)
        {
            _progressStage = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text };
            _progressDetail = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480, Foreground = ReportDesignSystem.SecondaryText };
            _progressRing = new ProgressRing { IsActive = true, Width = 26, Height = 26, Foreground = ReportDesignSystem.Accent, HorizontalAlignment = HorizontalAlignment.Left };
            _progressTerminalIcon = ReportDesignSystem.Icon("\uE73E", 28, ReportDesignSystem.Success);
            _progressTerminalIcon.Visibility = Visibility.Collapsed;
            var indicator = new Grid { Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
            indicator.Children.Add(_progressRing);
            indicator.Children.Add(_progressTerminalIcon);
            var content = new StackPanel { Spacing = 14, MaxWidth = 480 };
            content.Children.Add(indicator);
            content.Children.Add(_progressStage);
            content.Children.Add(_progressDetail);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            _progressCancel = new Button { Content = "取消生成", MinWidth = 126 };
            ReportDesignSystem.ConfigureButton(_progressCancel, ReportButtonKind.Secondary);
            _progressCancel.Click += async (_, _) =>
            {
                if (_progressCancel is not null) _progressCancel.IsEnabled = false;
                await CancelAsync();
            };
            _progressDismiss = new Button { Content = "后台继续", MinWidth = 126 };
            ReportDesignSystem.ConfigureButton(_progressDismiss, ReportButtonKind.Secondary);
            _progressDismiss.Click += (_, _) => DismissProgressOverlay();
            actions.Children.Add(_progressCancel);
            actions.Children.Add(_progressDismiss);
            content.Children.Add(actions);

            var card = new Border
            {
                Background = ReportDesignSystem.Surface,
                BorderBrush = ReportDesignSystem.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(32, 28, 32, 26),
                MaxWidth = 620,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = content
            };
            AutomationProperties.SetName(card, "汇报生成进度");
            _progressOverlay = CreateCenteredOverlay(card);
            _root.Children.Add(_progressOverlay);
        }
        SetProgressIndicatorRunning();
        if (_progressStage is not null) _progressStage.Text = stage;
        if (_progressDetail is not null)
        {
            _progressDetail.Text = detail;
            _progressDetail.Foreground = ReportDesignSystem.SecondaryText;
        }
        if (_progressCancel is not null)
        {
            _progressCancel.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
            _progressCancel.IsEnabled = canCancel && _workspace.State.InvocationId is not null;
        }
        if (_progressDismiss is not null) _progressDismiss.Content = canCancel ? "后台继续" : "关闭";
    }

    private void DismissProgressOverlay()
    {
        if (_progressOverlay is not null) _root.Children.Remove(_progressOverlay);
        _progressOverlay = null;
        _progressStage = null;
        _progressDetail = null;
        _progressRing = null;
        _progressTerminalIcon = null;
        _progressCancel = null;
        _progressDismiss = null;
    }

    private void CompleteProgressDialog(string stage, string detail, Brush foreground)
    {
        ShowProgressDialog(stage, detail, canCancel: false);
        if (_progressDetail is not null) _progressDetail.Foreground = foreground;
        SetProgressIndicatorTerminal(ReferenceEquals(foreground, ReportDesignSystem.Success), foreground);
    }

    private void SetProgressIndicatorRunning()
    {
        if (_progressRing is not null)
        {
            _progressRing.Visibility = Visibility.Visible;
            _progressRing.IsActive = true;
        }
        if (_progressTerminalIcon is not null) _progressTerminalIcon.Visibility = Visibility.Collapsed;
    }

    private void SetProgressIndicatorTerminal(bool succeeded, Brush foreground)
    {
        if (_progressRing is not null)
        {
            _progressRing.IsActive = false;
            _progressRing.Visibility = Visibility.Collapsed;
        }
        if (_progressTerminalIcon is null) return;
        _progressTerminalIcon.Glyph = succeeded ? "\uE73E" : "\uE711";
        _progressTerminalIcon.Foreground = foreground;
        _progressTerminalIcon.Visibility = Visibility.Visible;
    }

    private void UpdateProgressDialog(ReportWorkspaceSnapshot state)
    {
        if (_progressOverlay is null) return;
        if (state.Lifecycle == ReportGenerationLifecycle.Generating)
        {
            ShowProgressDialog(
                state.InvocationId is null ? "正在提交生成请求" : "正在由 AI 生成",
                state.InvocationId is null ? "正在建立安全生成请求。" : "已固定待办快照，正在等待模型生成汇报。");
        }
        else if (state.Lifecycle == ReportGenerationLifecycle.Cancelling)
        {
            ShowProgressDialog("正在取消生成", "正在通知 AI 服务取消本次汇报。", canCancel: false);
        }
    }

    private void Render(ReportWorkspaceSnapshot state)
    {
        _rangeText.Text = state.Range?.Label ?? "未选择范围";
        _filterListText.Text = string.IsNullOrWhiteSpace(state.Filter.ListId) ? "未选择" : FilterListLabel(state.Filter.ListId);
        _filterDepthText.Text = state.Filter.MaximumDepth >= TodoQuery.MaxTaskTreeDepth ? "未选择" : state.Filter.MaximumDepth == 1 ? "仅一级" : "一级和二级";
        _filterDateModeText.Text = state.Filter.DateRange?.Mode == TodoDateFilterMode.StartDate ? "开始日期" : state.Range is null ? "未选择" : "执行周期";
        _completedCount.Text = state.Preview.Completed.Count.ToString(CultureInfo.InvariantCulture);
        _unfinishedCount.Text = state.Preview.Unfinished.Count.ToString(CultureInfo.InvariantCulture);
        _previewRows.Children.Clear();
        var visibleTasks = state.Preview.Completed.Take(3).Concat(state.Preview.Unfinished.Take(4)).ToList();
        if (visibleTasks.Count < 7)
        {
            visibleTasks.AddRange(state.Preview.Completed.Skip(3).Concat(state.Preview.Unfinished.Skip(4)).Take(7 - visibleTasks.Count));
        }
        foreach (var task in visibleTasks) _previewRows.Children.Add(PreviewRow(task));
        UpdateRangeButtons(state.Range?.Kind);
        UpdateStyleButtons(state.Style);
        UpdateCustomCounter();

        var working = state.Lifecycle is ReportGenerationLifecycle.Generating or ReportGenerationLifecycle.Cancelling;
        _generate.IsEnabled = !working && state.Range is not null;
        _generate.Content = ReportDesignSystem.IconText("\uE735", working ? "正在生成" : "生成汇报", 20, 18);
        ToolTipService.SetToolTip(_generate, state.Range is null ? "请先选择本周、上周、本月或自定义区间。" : "生成汇报");
        _templateAction.Visibility = working ? Visibility.Collapsed : Visibility.Visible;
        _cancel.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        _cancel.IsEnabled = state.Lifecycle == ReportGenerationLifecycle.Generating && state.InvocationId is not null;
        _templateInput.SetDocument(state.TemplateDocument);
        _exampleInput.SetDocument(state.ExampleDocument);
        _templateInput.IsEnabled = !working;
        _exampleInput.IsEnabled = !working;
        if (state.OutputDocument is not null)
        {
            _output.SetDocument(state.OutputDocument);
            if (_resultCard is not null) _resultCard.Visibility = Visibility.Visible;
        }
        var status = state.Lifecycle switch
        {
            ReportGenerationLifecycle.Generating => "正在生成汇报，已锁定当前待办快照…",
            ReportGenerationLifecycle.Cancelling => "正在取消生成…",
            ReportGenerationLifecycle.Cancelled => "已取消，未产生部分文件。",
            ReportGenerationLifecycle.Failed => state.Error ?? "生成失败。",
            ReportGenerationLifecycle.Completed when state.Template.Mode == ReportTemplateMode.File => "AI 候选已通过结构校验，可选择输出位置。",
            ReportGenerationLifecycle.Completed => "生成完成，可继续编辑或复制结果。",
            _ => string.Empty
        };
        SetStatus(status, state.Lifecycle == ReportGenerationLifecycle.Failed ? ReportDesignSystem.Danger : ReportDesignSystem.SecondaryText);
        RenderRecords(state);
        UpdateProgressDialog(state);
    }

    private string FilterListLabel(string? listId)
    {
        if (string.IsNullOrWhiteSpace(listId)) return "全部清单";
        try { return new TodoStore().LoadData().Lists.FirstOrDefault(list => list.Id == listId)?.Name ?? listId; }
        catch { return listId; }
    }

    private void UpdateRangeButtons(ReportRangeKind? selected)
    {
        foreach (var (kind, button) in _rangeButtons) ReportDesignSystem.SetActive(button, kind == selected);
    }

    private void UpdateStyleButtons(ReportStyle selected)
    {
        foreach (var (style, button) in _styleButtons) ReportDesignSystem.SetActive(button, style == selected);
    }

    private void UpdateTemplateModeControls()
    {
        var fileMode = (_mode.SelectedItem as ComboBoxItem)?.Tag as ReportTemplateMode? == ReportTemplateMode.File;
        _textTemplateFields.Visibility = fileMode ? Visibility.Collapsed : Visibility.Visible;
        _fileTemplateFields.Visibility = fileMode ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (mode, button) in _templateModeButtons) ReportDesignSystem.SetActive(button, (fileMode ? ReportTemplateMode.File : ReportTemplateMode.Text) == mode);
    }

    private void CustomRequirementsTextChanged(object sender, TextChangedEventArgs args) => UpdateCustomCounter();

    private void UpdateCustomCounter() => _customCounter.Text = $"{_customRequirements.Text?.Length ?? 0} / 500";

    private void SetStatus(string text, Brush foreground)
    {
        _statusText.Text = text;
        _statusText.Foreground = foreground;
        _statusSurface.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowError(Exception exception, string operation = "ui-operation")
    {
        ReportSafeDiagnostics.RecordHandled(operation, exception);
        var message = ToSafeMessage(exception);
        SetStatus(message, ReportDesignSystem.Danger);
        _preferenceStatus.Foreground = ReportDesignSystem.Danger;
        _preferenceStatus.Text = message;
    }

    private static UIElement SectionTitle(string title, string subtitle) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text },
            new TextBlock { Text = subtitle, FontSize = 14, Foreground = ReportDesignSystem.SecondaryText, TextWrapping = TextWrapping.Wrap }
        }
    };

    private static FrameworkElement Labeled(string label, UIElement control, string? hint = null)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ReportDesignSystem.Text });
        if (!string.IsNullOrWhiteSpace(hint)) panel.Children.Add(new TextBlock { Text = hint, FontSize = 13, Foreground = ReportDesignSystem.MutedText, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(control);
        return panel;
    }

    private void CopyOutput()
    {
        var text = ReportTextDocuments.ToPlainText(_output.Document);
        if (string.IsNullOrWhiteSpace(text)) return;
        _clipboard.SetText(text);
    }

    private void UpdateResponsiveLayout(Size size)
    {
        if (_generatorBody is null || _previewCard is null || _writingCard is null || _generatorPage is null) return;
        var narrow = size.Width < 1280;
        _generatorPage.Margin = new Thickness(narrow ? 32 : 64, 14, narrow ? 32 : 64, 20);
        _generatorBody.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        _generatorBody.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _generatorBody.RowDefinitions[0].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _generatorBody.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(_previewCard, 0);
        Grid.SetRow(_previewCard, 0);
        Grid.SetColumn(_writingCard, narrow ? 0 : 1);
        Grid.SetRow(_writingCard, narrow ? 1 : 0);
        _writingCard.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }

    private static ComboBoxItem StyleItem(string name, ReportStyle value) => new() { Content = name, Tag = value };

    private static string ToSafeMessage(Exception exception) => exception switch
    {
        AiCoreException ai => ErrorText(ai.Code),
        ReportTemplateValidationException template => template.SafeMessage,
        FileNotFoundException => "找不到所选文件。请确认文件仍存在且可读取后重试。",
        UnauthorizedAccessException => "无法读取所选文件。请关闭占用它的程序或选择有访问权限的文件。",
        COMException => "无法打开文件选择器。请确认汇报窗口已完全启动后重试。",
        _ => "操作失败，请检查汇报设置后重试。"
    };

    private static string ToRecordErrorCode(Exception exception) => exception is AiCoreException ai && !string.IsNullOrWhiteSpace(ai.Code)
        ? ai.Code
        : "generation_failed";

    private static string ErrorText(string? code) => code switch
    {
        "not_found" => "未配置“汇报”默认模型，请在 AI 配置中心完成绑定。",
        "consent_required" => "模型端点尚未授权。",
        "protocol_mismatch" => "汇报工具与 Fowan Core 协议不兼容。",
        "cancelled" => "生成已取消。",
        "provider_auth_failed" => "模型端点鉴权失败。",
        "provider_content_rejected" => "模型返回内容不符合安全的汇报输出格式。",
        "provider_unavailable" => "Fowan Core 或模型端点不可用。",
        _ => "生成失败，请检查 AI 配置、网络或模板后重试。"
    };

    private void ConfigureWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var dpiScale = Math.Max(1d, GetDpiForWindow(hwnd) / 96d);
            var desiredWidth = 1920d * dpiScale;
            var desiredHeight = 1080d * dpiScale;
            var fit = Math.Min(1d, Math.Min(area.Width / desiredWidth, area.Height / desiredHeight));
            var width = (int)Math.Floor(desiredWidth * fit);
            var height = (int)Math.Floor(desiredHeight * fit);
            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(area.X + Math.Max(0, (area.Width - width) / 2), area.Y + Math.Max(0, (area.Height - height) / 2)));
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_titleBarDragRegion);
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 226, 233, 245);
            appWindow.TitleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(160, 226, 233, 245);
            appWindow.TitleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(32, 226, 233, 245);
            appWindow.TitleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(52, 226, 233, 245);
            appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico"));
        }
        catch
        {
            // The report screen remains usable without non-client window APIs.
        }
    }

    private static Uri FileUri(string path) => new UriBuilder
    {
        Scheme = Uri.UriSchemeFile,
        Path = Path.GetFullPath(path)
    }.Uri;

    private sealed class NavigationItem(Button button, Border accent, TextBlock label)
    {
        public Button Button { get; } = button;
        public Border Accent { get; } = accent;
        public TextBlock Label { get; } = label;
    }

    private enum ReportPage
    {
        Generate,
        Records,
        Preferences,
        Help
    }

    private enum RecordTimeScope { All, Today, LastSevenDays }
    private enum RecordStatusScope { All, Generating, Completed, Failed, Cancelled }
    private enum RecordRangeScope { All, ThisWeek, PreviousWeek, ThisMonth, Custom }
    private enum RecordModeScope { All, Text, File }

    private sealed class FixtureTodoReader : IReportTodoReader
    {
        public Task<ReportTaskPreview> ReadAsync(TodoFilterCriteria filter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReportVisualFixture.Tasks);
        }
    }
}
