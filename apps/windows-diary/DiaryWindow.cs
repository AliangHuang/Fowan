using Fowan.Diary.Core.Models;
using Fowan.Diary.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Devices.Geolocation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Fowan.Diary.Windows;

public sealed class DiaryWindow : Window
{
    private const int DesignWindowWidth = 1680;
    private const int DesignWindowHeight = 940;
    private const int MaximumBodyLength = 5000;
    private const double SidebarWidth = 274;
    private const double DetailWidth = 526;
    private const double CompactSidebarWidth = 128;
    private const double CompactDetailWidth = 190;
    private const double CardCornerRadius = 8;
    private const double MetricStripHeight = 68;
    private const double EditorTextRowHeight = 94;
    private const double EditorCardMinHeight = 184;
    private const double TimelineColumnWidth = 104;
    private const double TimelineRowHeight = 110;
    private const double TimelineCardMinHeight = 108;
    private const double TimelineNavigatorWidth = 360;
    private const string TimelineRangeAll = "all";
    private const string TimelineRangeToday = "today";
    private const string TimelineRangeWeek = "week";
    private const string TimelineRangeMonth = "month";
    private const string TimelineRangeYear = "year";

    private readonly DiaryStore _store = new();
    private readonly DiarySettingsStore _settingsStore = new();
    private readonly IDiaryReverseGeocoder _reverseGeocoder = new NominatimReverseGeocoder();
    private readonly IDiaryWeatherProvider _weatherProvider = new OpenMeteoWeatherProvider();
    private readonly record struct TagVisual(string BackgroundKey, string ForegroundKey);

    private DiaryData _data = new();
    private DiarySettings _settings = new();
    private DiaryEntry? _selectedEntry;
    private DiaryEntry? _draftEntry;
    private IReadOnlyList<TodoCandidate> _todoCandidates = [];
    private DateTime _calendarMonth = new(DiaryRuntime.Today.Year, DiaryRuntime.Today.Month, 1);
    private DateTime? _calendarDate;
    private string _timelineRangeId = TimelineRangeAll;
    private DateTime _timelineAnchorDate = DiaryRuntime.Today;
    private DateTime _timelineNavigatorMonth = new(DiaryRuntime.Today.Year, DiaryRuntime.Today.Month, 1);
    private DateTime? _timelineDateFilter;
    private DateTime? _pendingTimelineScrollDate;
    private string? _tagFilter;
    private Grid _root = new();
    private readonly Dictionary<DateTime, FrameworkElement> _timelineDateAnchors = [];
    private TextBox? _quickEditor;
    private TextBlock? _quickCharacterCount;
    private Button? _quickSaveButton;
    private string _composeMood = "愉快";
    private string _composeWeather = "多云";
    private string _composeLocation = "上海 · 静安区";
    private (double Latitude, double Longitude, DateTimeOffset AcquiredAt)? _lastDeviceLocation;
    private bool _buildingShell;

    public DiaryWindow()
    {
        _data = _store.LoadData();
        _settings = _settingsStore.Load();
        NormalizeTimelineNotebookSelection();
        InitializeTimelineSessionState();
        _draftEntry = _data.Entries.Where(entry => entry.IsDraft).OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault();
        _selectedEntry = _data.Entries.FirstOrDefault(entry => entry.IsFavorite)
            ?? _data.Entries.Where(entry => !entry.IsDraft).OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault();
        _todoCandidates = TodoCandidateReader.LoadOpenCandidates(20);
        ConfigureWindow();
        BuildShell();
    }

    internal void ActivateInitialMode() => Activate();

    internal void RestoreFromExternalActivation() => Activate();

    private void ConfigureWindow()
    {
        Title = "Fowan Diary";
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            var workArea = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd), DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min(workArea.Width, DesignWindowWidth);
            var height = Math.Min(workArea.Height, DesignWindowHeight);
            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(workArea.X + Math.Max(0, (workArea.Width - width) / 2), workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
            ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            ApplyCaptionButtonColors(appWindow);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // The diary remains usable if window decoration APIs are unavailable.
        }
    }

    private void ApplyCaptionButtonColors(AppWindow appWindow)
    {
        var foreground = IsDarkTheme() ? Colors.White : Colors.Black;
        appWindow.TitleBar.ButtonForegroundColor = foreground;
        appWindow.TitleBar.ButtonInactiveForegroundColor = IsDarkTheme() ? ColorHelper.FromArgb(180, 255, 255, 255) : ColorHelper.FromArgb(180, 0, 0, 0);
        appWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        appWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        appWindow.TitleBar.ButtonHoverBackgroundColor = IsDarkTheme() ? ColorHelper.FromArgb(45, 255, 255, 255) : ColorHelper.FromArgb(28, 0, 0, 0);
        appWindow.TitleBar.ButtonPressedBackgroundColor = IsDarkTheme() ? ColorHelper.FromArgb(70, 255, 255, 255) : ColorHelper.FromArgb(45, 0, 0, 0);
    }

    private void ApplyCaptionButtonColorsToCurrentWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ApplyCaptionButtonColors(AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd)));
        }
        catch
        {
            // Theme changes do not depend on caption button support.
        }
    }

    private void BuildShell()
    {
        _buildingShell = true;
        var isTimeline = IsTimelineView;
        _timelineDateAnchors.Clear();
        _root = new Grid { RequestedTheme = ResolveElementTheme(), Background = Brush("AppBackground") };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!isTimeline)
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DetailWidth) });
        }
        _root.Loaded += (_, _) => ApplyResponsiveColumns(_root.ActualWidth);
        _root.SizeChanged += (_, args) => ApplyResponsiveColumns(args.NewSize.Width);
        _root.Children.Add(BuildSidebar());
        if (isTimeline)
        {
            var timeline = BuildTimelineWorkspace();
            Grid.SetColumn(timeline, 1);
            _root.Children.Add(timeline);
        }
        else
        {
            var main = BuildMainColumn();
            Grid.SetColumn(main, 1);
            _root.Children.Add(main);
            var detail = BuildDetailColumn();
            Grid.SetColumn(detail, 2);
            _root.Children.Add(detail);
        }
        Content = _root;
        _buildingShell = false;
        QueuePendingTimelineScroll();
    }

    private void ApplyResponsiveColumns(double width)
    {
        if (width <= 0 || _root.ColumnDefinitions.Count < 2)
        {
            return;
        }
        var layoutWidth = width / CurrentDpiScale();
        var sidebar = layoutWidth < 760 ? CompactSidebarWidth : layoutWidth < 1200 ? 206 : SidebarWidth;
        _root.ColumnDefinitions[0].Width = new GridLength(sidebar);
        if (IsTimelineView)
        {
            return;
        }
        var detail = layoutWidth < 760 ? CompactDetailWidth : layoutWidth < 1200 ? 332 : DetailWidth;
        var main = Math.Max(220, layoutWidth - sidebar - detail);
        if (sidebar + detail + main > layoutWidth)
        {
            var fixedBudget = Math.Max(0, layoutWidth - main);
            sidebar = Math.Max(120, Math.Min(sidebar, fixedBudget * 0.42));
            detail = Math.Max(180, Math.Min(detail, fixedBudget - sidebar));
        }
        _root.ColumnDefinitions[2].Width = new GridLength(detail);
    }

    private double CurrentDpiScale()
    {
        try
        {
            var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            return dpi > 0 ? dpi / 96.0 : 1;
        }
        catch
        {
            return 1;
        }
    }

    private FrameworkElement BuildSidebar()
    {
        var border = new Border
        {
            Background = SidebarBackgroundBrush(),
            BorderBrush = Brush("Divider"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(13, 24, 10, 24)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13, Margin = new Thickness(0, 0, 0, 30) };
        var brandIcon = new Border
        {
            Width = 48,
            Height = 48,
            Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-app-icon-256.png"))),
                Width = 48,
                Height = 48,
                Stretch = Stretch.UniformToFill
            }
        };
        SetTitleBar(brandIcon);
        brand.Children.Add(brandIcon);
        var brandText = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(Text("Fowan", 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        brandText.Children.Add(Text("日记", 14, "TextSecondary"));
        brand.Children.Add(brandText);
        layout.Children.Add(brand);

        var nav = new StackPanel { Spacing = 10 };
        nav.Children.Add(NavButton(DiaryViewIds.Today, "\uE706", "今天"));
        nav.Children.Add(NavButton(DiaryViewIds.Timeline, "\uE916", "时间线"));
        nav.Children.Add(NavButton(DiaryViewIds.Calendar, "\uE787", "日历"));
        nav.Children.Add(NavButton(DiaryViewIds.Tags, "\uE8EC", "标签"));
        nav.Children.Add(NavButton(DiaryViewIds.Favorites, "\uE734", "收藏"));
        nav.Children.Add(NavButton(DiaryViewIds.Drafts, "\uE70B", "草稿"));
        Grid.SetRow(nav, 1);
        layout.Children.Add(nav);

        var notebooks = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var notebookHeader = new Grid
        {
            BorderBrush = Brush("Divider"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 14, 0, 6)
        };
        notebookHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notebookHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notebookHeader.Children.Add(Text("我的日记本", 14, "TextSecondary"));
        var addNotebook = TextButton("+", "管理日记本", 20, "TextSecondary");
        addNotebook.Click += (_, _) => ShowNotebookMenu(addNotebook);
        Grid.SetColumn(addNotebook, 1);
        notebookHeader.Children.Add(addNotebook);
        notebooks.Children.Add(notebookHeader);
        foreach (var notebook in _data.Notebooks)
        {
            notebooks.Children.Add(NotebookButton(notebook));
        }
        Grid.SetRow(notebooks, 2);
        layout.Children.Add(notebooks);

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var settings = BottomButton("\uE713", "设置");
        settings.Click += async (_, _) => await ShowSettingsDialogAsync();
        bottom.Children.Add(settings);
        var help = BottomButton("\uE897", "帮助");
        help.Click += async (_, _) => await ShowHelpDialogAsync();
        Grid.SetColumn(help, 1);
        bottom.Children.Add(help);
        var footer = new Border { BorderBrush = Brush("Divider"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 20, 0, 0), Child = bottom };
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        border.Child = layout;
        return border;
    }

    private Button NavButton(string viewId, string glyph, string label)
    {
        var selected = string.Equals(_settings.CurrentViewId, viewId, StringComparison.Ordinal);
        var button = new Button
        {
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = selected ? Brush("SelectedNav") : TransparentBrush(),
            Content = NavContent(glyph, label, selected)
        };
        button.Click += (_, _) => SelectView(viewId);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private UIElement NavContent(string glyph, string label, bool selected)
    {
        var grid = new Grid { ColumnSpacing = 13, Margin = new Thickness(0, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border { Width = 3, Height = 28, CornerRadius = new CornerRadius(2), Background = selected ? Brush("Accent") : TransparentBrush(), VerticalAlignment = VerticalAlignment.Center });
        var icon = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = selected ? Brush("Accent") : Brush("TextPrimary"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var text = Text(label, 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        return grid;
    }

    private Button NotebookButton(DiaryNotebook notebook)
    {
        var viewId = DiaryViewIds.Notebook(notebook.Id);
        var selected = string.Equals(_settings.CurrentViewId, viewId, StringComparison.Ordinal);
        var button = new Button { Height = 32, Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7), Background = selected ? Brush("SelectedNav") : TransparentBrush(), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var grid = new Grid { ColumnSpacing = 10, Margin = new Thickness(12, 0, 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = HexBrush(notebook.AccentColor), VerticalAlignment = VerticalAlignment.Center });
        var label = Text(notebook.Name, 13, "TextPrimary");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        button.Content = grid;
        button.Click += (_, _) => SelectView(viewId);
        return button;
    }

    private Button BottomButton(string glyph, string label)
    {
        return new Button
        {
            Height = 36,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = glyph, FontSize = 17, Foreground = Brush("TextPrimary") }, Text(label, 13, "TextPrimary") } }
        };
    }

    private FrameworkElement BuildTimelineWorkspace()
    {
        var layout = new Grid { Background = Brush("AppBackground") };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(BuildTimelineHeader());

        var content = new Grid { Margin = new Thickness(34, 20, 34, 24) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimelineNavigatorWidth) });
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.Children.Add(BuildTimelineFilterBar());
        var stream = new ScrollViewer
        {
            Padding = new Thickness(0, 24, 28, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            Content = BuildTimelineStream()
        };
        Grid.SetRow(stream, 1);
        main.Children.Add(stream);
        content.Children.Add(main);
        var navigator = new Border
        {
            BorderBrush = Brush("Divider"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(24, 0, 0, 0),
            Child = BuildTimelineNavigator()
        };
        Grid.SetColumn(navigator, 1);
        content.Children.Add(navigator);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        return layout;
    }

    private FrameworkElement BuildTimelineHeader()
    {
        var header = new Grid { Margin = new Thickness(34, 58, 34, 0), ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 5 };
        title.Children.Add(Text("时间线", 30, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        title.Children.Add(Text(TimelineDateRangeLabel(), 14, "TextSecondary"));
        header.Children.Add(title);

        var notebook = BuildTimelineNotebookSelector();
        Grid.SetColumn(notebook, 1);
        header.Children.Add(notebook);

        var search = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var searchBox = new TextBox
        {
            Width = 236,
            Height = 36,
            PlaceholderText = "搜索日记标题、内容或标签",
            Padding = new Thickness(11, 0, 8, 0),
            Background = Brush("ControlBackground"),
            BorderBrush = Brush("CardStroke"),
            BorderThickness = new Thickness(1),
            Foreground = Brush("TextPrimary"),
            PlaceholderForeground = Brush("TextMuted")
        };
        searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Enter)
            {
                await ShowSearchDialogAsync(searchBox.Text);
            }
        };
        search.Children.Add(searchBox);
        var searchButton = IconButton("\uE721", "搜索日记");
        searchButton.Click += async (_, _) => await ShowSearchDialogAsync(searchBox.Text);
        search.Children.Add(searchButton);
        Grid.SetColumn(search, 2);
        header.Children.Add(search);

        var create = PrimaryButton("\uE710", "新建日记");
        create.Click += async (_, _) => await CreateTimelineEntryAsync();
        Grid.SetColumn(create, 3);
        header.Children.Add(create);
        return header;
    }

    private FrameworkElement BuildTimelineFilterBar()
    {
        var content = new Grid { Margin = new Thickness(18, 0, 18, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ranges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        ranges.Children.Add(TimelineRangeButton("全部", TimelineRangeAll));
        ranges.Children.Add(TimelineRangeButton("今天", TimelineRangeToday));
        ranges.Children.Add(TimelineRangeButton("本周", TimelineRangeWeek));
        ranges.Children.Add(TimelineRangeButton("本月", TimelineRangeMonth));
        ranges.Children.Add(TimelineRangeButton("本年", TimelineRangeYear));
        content.Children.Add(ranges);
        var navigator = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var previous = TimelineIconButton("\uE76B", "上一个时间范围");
        previous.Click += (_, _) => MoveTimelineRange(-1);
        navigator.Children.Add(previous);
        navigator.Children.Add(Text(TimelineNavigatorTitle(), 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var next = TimelineIconButton("\uE76C", "下一个时间范围");
        next.Click += (_, _) => MoveTimelineRange(1);
        navigator.Children.Add(next);
        Grid.SetColumn(navigator, 1);
        content.Children.Add(navigator);
        return Card(content, 58);
    }

    private Button TimelineRangeButton(string label, string rangeId)
    {
        var selected = string.Equals(_timelineRangeId, rangeId, StringComparison.Ordinal) && _timelineDateFilter is null;
        var button = new Button
        {
            Height = 42,
            Padding = new Thickness(16, 0, 16, 0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = selected ? Brush("SelectedNav") : TransparentBrush(),
            Content = Text(label, 14, selected ? "OnAccent" : "TextSecondary", Microsoft.UI.Text.FontWeights.SemiBold)
        };
        button.Click += (_, _) => SelectTimelineRange(rangeId);
        return button;
    }

    private FrameworkElement BuildTimelineStream()
    {
        var entries = TimelineEntries();
        var stack = new StackPanel { Spacing = 14 };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Text($"{PageListTitle()} · {entries.Count} 篇日记", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var sort = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new FontIcon { Glyph = "\uE8CB", FontSize = 15, Foreground = Brush("TextSecondary") }, Text("从新到旧", 13, "TextSecondary") } };
        Grid.SetColumn(sort, 1);
        header.Children.Add(sort);
        stack.Children.Add(header);
        stack.Children.Add(entries.Count == 0 ? EmptyCard("当前时间范围没有日记。") : BuildTimeline(entries));
        return stack;
    }

    private FrameworkElement BuildTimelineNavigator()
    {
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(Text("活动日历", 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        stack.Children.Add(BuildTimelineActivityCalendar());
        stack.Children.Add(new Border { Height = 1, Background = Brush("Divider"), Margin = new Thickness(0, 2, 0, 0) });
        stack.Children.Add(Text("时间导航", 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        stack.Children.Add(BuildTimelineNavigationRows());
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, Content = stack };
    }

    private FrameworkElement BuildTimelineActivityCalendar()
    {
        var stack = new StackPanel { Spacing = 10 };
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var monthControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var previous = TextButton("‹", "上个月", 20, "TextSecondary");
        previous.Click += (_, _) => MoveTimelineNavigatorMonth(-1);
        monthControls.Children.Add(previous);
        monthControls.Children.Add(Text(_timelineNavigatorMonth.ToString("yyyy年M月"), 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var next = TextButton("›", "下个月", 20, "TextSecondary");
        next.Click += (_, _) => MoveTimelineNavigatorMonth(1);
        monthControls.Children.Add(next);
        Grid.SetColumn(monthControls, 1);
        title.Children.Add(monthControls);
        stack.Children.Add(title);

        var calendar = new Grid { RowSpacing = 5, ColumnSpacing = 5 };
        for (var column = 0; column < 7; column++) calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 7; row++) calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labels = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (var column = 0; column < labels.Length; column++)
        {
            var label = Text(labels[column], 12, "TextMuted");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(label, column);
            calendar.Children.Add(label);
        }
        var start = _timelineNavigatorMonth.AddDays(-((int)_timelineNavigatorMonth.DayOfWeek + 6) % 7);
        for (var index = 0; index < 42; index++)
        {
            var cell = TimelineActivityCell(start.AddDays(index));
            Grid.SetRow(cell, index / 7 + 1);
            Grid.SetColumn(cell, index % 7);
            calendar.Children.Add(cell);
        }
        stack.Children.Add(calendar);
        return stack;
    }

    private Button TimelineActivityCell(DateTime date)
    {
        var inMonth = date.Month == _timelineNavigatorMonth.Month && date.Year == _timelineNavigatorMonth.Year;
        var selected = _timelineDateFilter?.Date == date.Date;
        var hasEntries = TimelineSourceEntries().Any(entry => entry.CreatedAt.LocalDateTime.Date == date.Date);
        var button = new Button
        {
            Height = 28,
            Padding = new Thickness(0),
            BorderThickness = hasEntries && !selected ? new Thickness(1) : new Thickness(0),
            BorderBrush = hasEntries && !selected ? Brush("Accent") : TransparentBrush(),
            CornerRadius = new CornerRadius(14),
            Background = selected ? Brush("Accent") : TransparentBrush(),
            Content = new TextBlock { Text = date.Day.ToString(), FontSize = 12, Foreground = selected ? Brush("OnAccent") : inMonth ? Brush("TextPrimary") : Brush("TextMuted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        button.Click += (_, _) => SelectTimelineDate(date);
        return button;
    }

    private FrameworkElement BuildTimelineNavigationRows()
    {
        var source = TimelineSourceEntries();
        if (source.Count == 0)
        {
            return Text("当前日记本还没有可导航的记录。", 13, "TextSecondary");
        }

        var stack = new StackPanel { Spacing = 5 };
        var groups = source
            .GroupBy(entry => new DateTime(entry.CreatedAt.LocalDateTime.Year, entry.CreatedAt.LocalDateTime.Month, 1))
            .OrderByDescending(group => group.Key);
        foreach (var month in groups)
        {
            var monthContent = new Grid { Width = 300 };
            monthContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            monthContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            monthContent.Children.Add(Text($"{month.Key:yyyy年M月}", 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
            var monthCount = new TextBlock { Text = $"{month.Count()} 篇", FontSize = 13, Foreground = Brush("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(monthCount, 1);
            monthContent.Children.Add(monthCount);
            var monthButton = new Button
            {
                Height = 34,
                Padding = new Thickness(6, 0, 6, 0),
                BorderThickness = new Thickness(0),
                Background = month.Key == _timelineNavigatorMonth ? Brush("ControlBackground") : TransparentBrush(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = monthContent
            };
            var targetMonth = month.Key;
            monthButton.Click += (_, _) => NavigateTimelineToDate(targetMonth, month.First().CreatedAt.LocalDateTime.Date);
            stack.Children.Add(monthButton);
            if (month.Key == _timelineNavigatorMonth)
            {
                foreach (var day in month.GroupBy(entry => entry.CreatedAt.LocalDateTime.Date).OrderByDescending(group => group.Key))
                {
                    var dayContent = new Grid { Width = 290 };
                    dayContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dayContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    dayContent.Children.Add(Text(day.Key.ToString("M月d日"), 13, "TextSecondary"));
                    var dayCount = new TextBlock { Text = $"{day.Count()} 篇", FontSize = 12, Foreground = Brush("TextMuted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(dayCount, 1);
                    dayContent.Children.Add(dayCount);
                    var dayButton = new Button
                    {
                        Height = 30,
                        Padding = new Thickness(16, 0, 6, 0),
                        BorderThickness = new Thickness(0),
                        Background = _timelineDateFilter?.Date == day.Key ? Brush("SelectedNav") : TransparentBrush(),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Content = dayContent
                    };
                    var targetDate = day.Key;
                    dayButton.Click += (_, _) => NavigateTimelineToDate(targetMonth, targetDate);
                    stack.Children.Add(dayButton);
                }
            }
        }
        return stack;
    }

    private void SelectTimelineRange(string rangeId)
    {
        _timelineRangeId = rangeId;
        _timelineDateFilter = null;
        _timelineNavigatorMonth = new DateTime(_timelineAnchorDate.Year, _timelineAnchorDate.Month, 1);
        EnsureSelectedEntryVisible();
        BuildShell();
    }

    private void MoveTimelineRange(int offset)
    {
        switch (_timelineRangeId)
        {
            case TimelineRangeToday:
                _timelineAnchorDate = _timelineAnchorDate.AddDays(offset);
                break;
            case TimelineRangeWeek:
                _timelineAnchorDate = _timelineAnchorDate.AddDays(7 * offset);
                break;
            case TimelineRangeMonth:
                _timelineAnchorDate = _timelineAnchorDate.AddMonths(offset);
                break;
            case TimelineRangeYear:
                _timelineAnchorDate = _timelineAnchorDate.AddYears(offset);
                break;
            default:
                _timelineNavigatorMonth = _timelineNavigatorMonth.AddMonths(offset);
                BuildShell();
                return;
        }
        _timelineDateFilter = null;
        _timelineNavigatorMonth = new DateTime(_timelineAnchorDate.Year, _timelineAnchorDate.Month, 1);
        EnsureSelectedEntryVisible();
        BuildShell();
    }

    private void MoveTimelineNavigatorMonth(int offset)
    {
        _timelineNavigatorMonth = _timelineNavigatorMonth.AddMonths(offset);
        BuildShell();
    }

    private void SelectTimelineDate(DateTime date)
    {
        if (_timelineDateFilter?.Date == date.Date)
        {
            _timelineDateFilter = null;
            _timelineRangeId = TimelineRangeAll;
        }
        else
        {
            _timelineDateFilter = date.Date;
            _timelineRangeId = TimelineRangeAll;
            _timelineAnchorDate = date.Date;
        }
        _timelineNavigatorMonth = new DateTime(date.Year, date.Month, 1);
        EnsureSelectedEntryVisible();
        BuildShell();
    }

    private void NavigateTimelineToDate(DateTime month, DateTime date)
    {
        _timelineRangeId = TimelineRangeAll;
        _timelineDateFilter = null;
        _timelineAnchorDate = date.Date;
        _timelineNavigatorMonth = new DateTime(month.Year, month.Month, 1);
        _pendingTimelineScrollDate = date.Date;
        BuildShell();
    }

    private void NavigateTimelineToEntry(DiaryEntry entry)
    {
        _selectedEntry = entry;
        _settings.TimelineNotebookId = DiaryTimeline.AllNotebooksId;
        SaveSettings();
        NavigateTimelineToDate(entry.CreatedAt.LocalDateTime.Date, entry.CreatedAt.LocalDateTime.Date);
    }

    private void InitializeTimelineSessionState()
    {
        var range = Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_RANGE");
        if (range is TimelineRangeAll or TimelineRangeToday or TimelineRangeWeek or TimelineRangeMonth or TimelineRangeYear)
        {
            _timelineRangeId = range;
        }
        if (TryParseTimelineDate(Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_ANCHOR"), out var anchor))
        {
            _timelineAnchorDate = anchor;
            _timelineNavigatorMonth = new DateTime(anchor.Year, anchor.Month, 1);
        }
        if (TryParseTimelineDate(Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_DATE"), out var selectedDate))
        {
            _timelineDateFilter = selectedDate;
            _timelineRangeId = TimelineRangeAll;
            _timelineNavigatorMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        }
        if (TryParseTimelineDate(Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_NAVIGATOR_MONTH"), out var navigatorMonth))
        {
            _timelineNavigatorMonth = new DateTime(navigatorMonth.Year, navigatorMonth.Month, 1);
        }
    }

    private static bool TryParseTimelineDate(string? value, out DateTime date) => DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);

    private IReadOnlyList<DiaryEntry> TimelineSourceEntries() => DiaryTimeline.Query(_data, TimelineNotebookId);

    private IReadOnlyList<DiaryEntry> TimelineEntries()
    {
        var window = TimelineDateWindow();
        return DiaryTimeline.Query(_data, TimelineNotebookId, window.Start, window.End);
    }

    private (DateTime? Start, DateTime? End) TimelineDateWindow()
    {
        if (_timelineDateFilter is not null)
        {
            return (_timelineDateFilter.Value.Date, _timelineDateFilter.Value.Date);
        }

        var anchor = _timelineAnchorDate.Date;
        return _timelineRangeId switch
        {
            TimelineRangeToday => (anchor, anchor),
            TimelineRangeWeek => (anchor.AddDays(-((int)anchor.DayOfWeek + 6) % 7), anchor.AddDays(-((int)anchor.DayOfWeek + 6) % 7).AddDays(6)),
            TimelineRangeMonth => (new DateTime(anchor.Year, anchor.Month, 1), new DateTime(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1)),
            TimelineRangeYear => (new DateTime(anchor.Year, 1, 1), new DateTime(anchor.Year, 12, 31)),
            _ => (null, null)
        };
    }

    private string TimelineDateRangeLabel()
    {
        var entries = TimelineEntries();
        if (entries.Count == 0)
        {
            return "当前时间范围还没有记录";
        }
        var window = TimelineDateWindow();
        var newest = entries[0].CreatedAt.LocalDateTime.Date;
        var oldest = entries[^1].CreatedAt.LocalDateTime.Date;
        var start = window.Start ?? oldest;
        var end = window.End ?? newest;
        var range = start == end ? start.ToString("yyyy年M月d日") : $"{start:yyyy年M月d日} – {end:yyyy年M月d日}";
        return $"{range}（{entries.Count} 篇日记）";
    }

    private string TimelineNavigatorTitle()
    {
        if (_timelineDateFilter is not null)
        {
            return _timelineDateFilter.Value.ToString("yyyy年M月d日");
        }
        return _timelineRangeId == TimelineRangeAll
            ? _timelineNavigatorMonth.ToString("yyyy年M月")
            : _timelineRangeId switch
            {
                TimelineRangeToday => "按天浏览",
                TimelineRangeWeek => "按周浏览",
                TimelineRangeMonth => "按月浏览",
                TimelineRangeYear => "按年浏览",
                _ => _timelineNavigatorMonth.ToString("yyyy年M月")
            };
    }

    private string TimelineDateHeading(DateTime date)
    {
        var prefix = date.Date == DiaryRuntime.Today ? "今天 · " : date.Date == DiaryRuntime.Today.AddDays(-1) ? "昨天 · " : string.Empty;
        return $"{prefix}{date.ToString("M月d日 ddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"))}";
    }

    private async Task CreateTimelineEntryAsync()
    {
        var draft = EnsureDraft();
        _selectedEntry = draft;
        await ShowEntryEditorAsync(draft);
    }

    private void QueuePendingTimelineScroll()
    {
        if (!IsTimelineView || _pendingTimelineScrollDate is null)
        {
            return;
        }
        _root.Loaded += (_, _) => DispatcherQueue.TryEnqueue(ScrollPendingTimelineAnchor);
        if (_root.IsLoaded)
        {
            DispatcherQueue.TryEnqueue(ScrollPendingTimelineAnchor);
        }
    }

    private void ScrollPendingTimelineAnchor()
    {
        if (_pendingTimelineScrollDate is not DateTime date || !_timelineDateAnchors.TryGetValue(date, out var anchor))
        {
            return;
        }
        _pendingTimelineScrollDate = null;
        anchor.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.12 });
    }

    private bool IsTimelineView => string.Equals(_settings.CurrentViewId, DiaryViewIds.Timeline, StringComparison.Ordinal);

    private FrameworkElement BuildMainColumn()
    {
        return new Border
        {
            Background = Brush("AppBackground"),
            BorderBrush = Brush("Divider"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Padding = new Thickness(30, 58, 30, 24), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, Content = BuildMainContent() }
        };
    }

    private UIElement BuildMainContent()
    {
        if (string.Equals(_settings.CurrentViewId, DiaryViewIds.Tags, StringComparison.Ordinal))
        {
            return BuildTagManagementContent();
        }
        var stack = new StackPanel { Spacing = 16 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
        title.Children.Add(Text(PageTitle(), 28, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        title.Children.Add(Text(DiaryRuntime.Today.ToString("yyyy年M月d日　dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), 16, "TextSecondary"));
        header.Children.Add(title);
        if (string.Equals(_settings.CurrentViewId, DiaryViewIds.Timeline, StringComparison.Ordinal))
        {
            var timelineNotebook = BuildTimelineNotebookSelector();
            timelineNotebook.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(timelineNotebook, 1);
            header.Children.Add(timelineNotebook);
        }
        var create = PrimaryButton("\uE710", "新建日记");
        create.Click += (_, _) => BeginDraft(focusEditor: true);
        Grid.SetColumn(create, 2);
        header.Children.Add(create);
        var more = IconButton("\uE712", "更多");
        more.Margin = new Thickness(0, 0, 12, 0);
        more.Click += (_, _) => ShowHeaderMenu(more);
        Grid.SetColumn(more, 3);
        header.Children.Add(more);
        stack.Children.Add(header);
        stack.Children.Add(BuildMoodStrip());
        stack.Children.Add(BuildEditorCard());
        if (string.Equals(_settings.CurrentViewId, DiaryViewIds.Calendar, StringComparison.Ordinal))
        {
            stack.Children.Add(BuildCalendarCard(large: true));
        }
        var entries = FilteredEntries().ToList();
        var listHeader = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listHeader.Children.Add(Text($"{PageListTitle()} · {entries.Count} 篇日记", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var sort = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "\uE8CB", FontSize = 15, Foreground = Brush("TextSecondary") }, Text("按时间排序", 14, "TextSecondary") } };
        Grid.SetColumn(sort, 1);
        listHeader.Children.Add(sort);
        stack.Children.Add(listHeader);
        stack.Children.Add(entries.Count == 0 ? EmptyCard("当前视图还没有日记。") : BuildTimeline(entries));
        return stack;
    }

    private UIElement BuildTagManagementContent()
    {
        var stack = new StackPanel { Spacing = 16 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
        title.Children.Add(Text("标签", 28, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        title.Children.Add(Text("维护标签、配色与日记筛选", 16, "TextSecondary"));
        header.Children.Add(title);
        var create = PrimaryButton("\uE710", "新建标签");
        create.Click += async (_, _) => await ShowCreateTagDialogAsync();
        Grid.SetColumn(create, 1);
        header.Children.Add(create);
        stack.Children.Add(header);
        stack.Children.Add(BuildTagFilters());

        var catalog = new StackPanel { Spacing = 0, Margin = new Thickness(18, 14, 18, 14) };
        catalog.Children.Add(Text($"标签表 · {_data.TagCatalog.Count} 个", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        catalog.Children.Add(Text("删除标签定义不会删除历史日记中的标签文字。", 12, "TextMuted"));
        foreach (var tag in _data.TagCatalog.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            catalog.Children.Add(BuildTagCatalogRow(tag));
        }
        stack.Children.Add(Card(catalog, null));
        var entries = FilteredEntries().ToList();
        stack.Children.Add(Text($"{(string.IsNullOrWhiteSpace(_tagFilter) ? "全部标签日记" : _tagFilter)} · {entries.Count} 篇日记", 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        stack.Children.Add(entries.Count == 0 ? EmptyCard("当前标签下还没有日记。") : BuildTimeline(entries));
        return stack;
    }

    private FrameworkElement BuildTagCatalogRow(DiaryTagDefinition tag)
    {
        var row = new Grid { Height = 48, Margin = new Thickness(0, 10, 0, 0), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var color = DiaryMetadata.TagColor(tag.ColorId);
        row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = HexBrush(color.Hex), VerticalAlignment = VerticalAlignment.Center });
        var pill = MetaTagPill(tag.Name);
        pill.VerticalAlignment = VerticalAlignment.Center;
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(pill, 1);
        row.Children.Add(pill);
        var count = _data.Entries.Count(entry => entry.Tags.Any(name => string.Equals(name, tag.Name, StringComparison.OrdinalIgnoreCase)));
        var usage = Text($"{count} 篇", 13, "TextSecondary");
        Grid.SetColumn(usage, 2);
        row.Children.Add(usage);
        var edit = TextButton("编辑", "编辑标签", 13, "Accent");
        edit.Click += async (_, _) => await ShowEditTagDialogAsync(tag);
        Grid.SetColumn(edit, 3);
        row.Children.Add(edit);
        return row;
    }

    private FrameworkElement BuildMoodStrip()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        grid.Children.Add(MetricCell("心情", "🙂", ComposeMood, "\uE70D", ShowMoodFlyout));
        var firstDivider = MetricDivider();
        Grid.SetColumn(firstDivider, 1);
        grid.Children.Add(firstDivider);
        var weather = MetricCell("天气", "⛅", ComposeWeather, "\uE70D", ShowWeatherFlyout, 42);
        Grid.SetColumn(weather, 2);
        grid.Children.Add(weather);
        var secondDivider = MetricDivider();
        Grid.SetColumn(secondDivider, 3);
        grid.Children.Add(secondDivider);
        var location = MetricCell("地点", "\uE707", ComposeLocation, "\uE712", ShowLocationFlyout, 28, 14);
        Grid.SetColumn(location, 4);
        grid.Children.Add(location);
        return Card(grid, MetricStripHeight);
    }

    private FrameworkElement MetricCell(string label, string icon, string value, string? trailing, Action<Button> action, double leftPadding = 28, double rightPadding = 18)
    {
        var button = new Button { Height = MetricStripHeight, Padding = new Thickness(leftPadding, 0, rightPadding, 0), BorderThickness = new Thickness(0), Background = TransparentBrush(), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Text(label, 14, "TextSecondary"));
        row.Children.Add(IsSegoeGlyph(icon) ? new FontIcon { Glyph = icon, FontSize = 18, Foreground = Brush("TextSecondary") } : EmojiText(icon, 20));
        row.Children.Add(Text(value, 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        content.Children.Add(row);
        if (!string.IsNullOrEmpty(trailing))
        {
            var glyph = new FontIcon { Glyph = trailing, FontSize = 15, Foreground = Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(glyph, 1);
            content.Children.Add(glyph);
        }
        button.Content = content;
        button.Click += (_, _) => action(button);
        return button;
    }

    private FrameworkElement MetricDivider() => new Border { Width = 1, Height = 31, Background = Brush("SoftDivider"), VerticalAlignment = VerticalAlignment.Center };

    private void ShowMoodFlyout(Button anchor)
    {
        var flyout = new MenuFlyout();
        foreach (var mood in DiaryMetadata.MoodOptions)
        {
            var item = new MenuFlyoutItem { Text = mood, Icon = new FontIcon { Glyph = MoodGlyph(mood), FontSize = 15 } };
            item.Click += (_, _) => SetQuickMood(mood);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(anchor);
    }

    private void ShowWeatherFlyout(Button anchor)
    {
        var flyout = new MenuFlyout();
        foreach (var weather in DiaryMetadata.WeatherOptions)
        {
            var item = new MenuFlyoutItem { Text = weather };
            item.Click += (_, _) => SetQuickWeather(weather, null);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var automatic = new MenuFlyoutItem { Text = "自动获取当前位置天气", Icon = new FontIcon { Glyph = "\uE81E", FontSize = 15 }, IsEnabled = _settings.LocationFeatureEnabled && _settings.WeatherFeatureEnabled };
        automatic.Click += async (_, _) => await AcquireWeatherAsync();
        flyout.Items.Add(automatic);
        if (!automatic.IsEnabled)
        {
            var settings = new MenuFlyoutItem { Text = "在设置中启用自动天气" };
            settings.Click += async (_, _) => await ShowSettingsDialogAsync();
            flyout.Items.Add(settings);
        }
        flyout.ShowAt(anchor);
    }

    private void ShowLocationFlyout(Button anchor)
    {
        var locationBox = new TextBox { Text = ComposeLocation == "待补充" ? string.Empty : ComposeLocation, PlaceholderText = "输入地点", Width = 280 };
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(14), Width = 310 };
        panel.Children.Add(Text("地点", 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        panel.Children.Add(locationBox);
        var recentLocations = _data.Entries.Select(entry => entry.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location) && location != "待补充")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(4)
            .ToList();
        if (recentLocations.Count > 0)
        {
            var recent = new ComboBox { PlaceholderText = "最近使用", Width = 280 };
            foreach (var location in recentLocations)
            {
                recent.Items.Add(new ComboBoxItem { Content = location });
            }
            recent.SelectionChanged += (_, _) =>
            {
                if (recent.SelectedItem is ComboBoxItem selected)
                {
                    locationBox.Text = selected.Content?.ToString() ?? locationBox.Text;
                }
            };
            panel.Children.Add(recent);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = SecondaryButton("使用此地点");
        var automatic = OutlineButton("\uE81E", "获取当前位置", "TextPrimary");
        actions.Children.Add(save);
        actions.Children.Add(automatic);
        panel.Children.Add(actions);
        panel.Children.Add(Text("自动定位会先请求你的确认，并可在设置中关闭。", 12, "TextMuted"));
        var flyout = new Flyout { Content = panel };
        save.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(locationBox.Text))
            {
                SetQuickLocation(locationBox.Text.Trim(), null);
            }
            flyout.Hide();
        };
        automatic.Click += async (_, _) =>
        {
            await AcquireLocationAsync();
            flyout.Hide();
        };
        flyout.ShowAt(anchor);
    }

    private void SetQuickMood(string mood)
    {
        var entry = EnsureDraft();
        entry.Mood = mood;
        entry.UpdatedAt = DateTimeOffset.Now;
        _composeMood = mood;
        SaveData(silent: true);
        BuildShell();
    }

    private void SetQuickWeather(string weather, DiaryWeatherDetails? details)
    {
        var entry = EnsureDraft();
        entry.Weather = weather;
        entry.WeatherDetails = details;
        entry.UpdatedAt = DateTimeOffset.Now;
        _composeWeather = weather;
        SaveData(silent: true);
        BuildShell();
    }

    private void SetQuickLocation(string location, DiaryLocationDetails? details)
    {
        var entry = EnsureDraft();
        entry.Location = location;
        entry.LocationDetails = details;
        entry.UpdatedAt = DateTimeOffset.Now;
        _composeLocation = location;
        SaveData(silent: true);
        BuildShell();
    }

    private FrameworkElement BuildEditorCard()
    {
        var layout = new Grid { Margin = new Thickness(24, 20, 24, 12) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(EditorTextRowHeight) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _quickEditor = new TextBox
        {
            Text = _draftEntry?.Body ?? string.Empty,
            PlaceholderText = "开始记录今天的想法...",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxLength = MaximumBodyLength,
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Foreground = Brush("TextPrimary"),
            PlaceholderForeground = Brush("TextMuted"),
            FontSize = 20
        };
        _quickEditor.TextChanged += (_, _) => OnQuickTextChanged();
        layout.Children.Add(_quickEditor);
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28 };
        actions.Children.Add(ToolbarAction("\uE91B", "图片", async _ => await AddImageAttachmentAsync()));
        actions.Children.Add(ToolbarAction("\uE8EC", "标签", async _ => await ShowTagPickerAsync(EnsureDraft())));
        actions.Children.Add(ToolbarAction("\uE8A5", "模板", button => { ShowTemplateMenu(button); return Task.CompletedTask; }));
        actions.Children.Add(ToolbarAction("\uE721", "搜索", async _ => await ShowSearchDialogAsync()));
        toolbar.Children.Add(actions);
        _quickSaveButton = SecondaryButton("保存日记");
        _quickSaveButton.Click += (_, _) => SaveDraft();
        _quickCharacterCount = Text(string.Empty, 14, "TextSecondary");
        var saveGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center, Children = { _quickCharacterCount, _quickSaveButton } };
        Grid.SetColumn(saveGroup, 1);
        toolbar.Children.Add(saveGroup);
        var toolbarBorder = new Border { BorderBrush = Brush("InnerDivider"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 13, 0, 0), Child = toolbar };
        Grid.SetRow(toolbarBorder, 1);
        layout.Children.Add(toolbarBorder);
        UpdateQuickEditorState();
        return Card(layout, EditorCardMinHeight);
    }

    private Button ToolbarAction(string glyph, string text, Func<Button, Task> action)
    {
        var button = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = TransparentBrush(), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new FontIcon { Glyph = glyph, FontSize = 18, Foreground = Brush("TextPrimary") }, Text(text, 15, "TextPrimary") } } };
        button.Click += async (_, _) => await action(button);
        return button;
    }

    private void OnQuickTextChanged()
    {
        if (_buildingShell || _quickEditor is null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(_quickEditor.Text))
        {
            var draft = EnsureDraft();
            draft.Body = _quickEditor.Text;
            draft.Title = DiaryText.InferTitle(draft.Body);
            draft.UpdatedAt = DateTimeOffset.Now;
            SaveData(silent: true);
        }
        UpdateQuickEditorState();
    }

    private void UpdateQuickEditorState()
    {
        if (_quickEditor is null || _quickCharacterCount is null || _quickSaveButton is null)
        {
            return;
        }
        _quickCharacterCount.Text = $"{_quickEditor.Text.Length} / {MaximumBodyLength}";
        _quickSaveButton.IsEnabled = !string.IsNullOrWhiteSpace(_quickEditor.Text);
    }

    private FrameworkElement BuildTagFilters()
    {
        var tags = DiaryTags.Names(_data);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(18, 13, 18, 13) };
        row.Children.Add(TagFilterButton("全部", null));
        foreach (var tag in tags)
        {
            row.Children.Add(TagFilterButton(tag, tag));
        }
        return Card(row, null);
    }

    private Button TagFilterButton(string label, string? tag)
    {
        var selected = string.Equals(_tagFilter, tag, StringComparison.OrdinalIgnoreCase);
        var button = new Button { Padding = new Thickness(10, 4, 10, 4), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6), Background = selected ? Brush("Accent") : TagBackgroundBrush(label), Content = new TextBlock { Text = label, FontSize = 13, Foreground = selected ? Brush("OnAccent") : TagForegroundBrush(label), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
        button.Click += (_, _) => { _tagFilter = tag; EnsureSelectedEntryVisible(); BuildShell(); };
        return button;
    }

    private FrameworkElement BuildTimeline(IReadOnlyList<DiaryEntry> entries)
    {
        var timelineView = IsTimelineView;
        var rowHeight = timelineView ? 92 : TimelineRowHeight;
        var grid = new Grid { Margin = new Thickness(0, timelineView ? 2 : 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimelineColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Background = Brush("TimelineLine"), Margin = new Thickness(22, timelineView ? 26 : 30, 0, 80) });
        var times = new StackPanel { Spacing = timelineView ? 10 : 16 };
        var cards = new StackPanel { Spacing = timelineView ? 10 : 13 };
        DateTime? previousDate = null;
        var notebookMode = TimelineNotebookId;
        foreach (var entry in entries)
        {
            var selected = _selectedEntry?.Id == entry.Id;
            var entryDate = entry.CreatedAt.LocalDateTime.Date;
            var showDate = timelineView && previousDate != entryDate;
            previousDate = entry.CreatedAt.LocalDateTime.Date;
            var dateHeaderHeight = showDate ? 28 : 0;
            var timeMargin = new Thickness(44, (timelineView ? 16 : 19) + dateHeaderHeight, 0, 0);
            var timeRow = new Grid
            {
                Height = rowHeight + dateHeaderHeight,
                Children =
                {
                    new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(17, (timelineView ? 22 : 25) + dateHeaderHeight, 0, 0), Fill = selected ? Brush("Accent") : Brush("TimelineDot") },
                    new TextBlock { Text = entry.CreatedAt.ToString("HH:mm"), Margin = timeMargin, Foreground = selected ? Brush("Accent") : Brush("TextSecondary"), FontSize = 14 }
                }
            };
            times.Children.Add(timeRow);
            var card = TimelineCard(entry, selected, timelineView && string.Equals(notebookMode, DiaryTimeline.AllNotebooksId, StringComparison.Ordinal), timelineView);
            if (showDate && timelineView)
            {
                var cardBlock = new Grid();
                cardBlock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(dateHeaderHeight) });
                cardBlock.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cardBlock.Children.Add(new TextBlock { Text = TimelineDateHeading(entryDate), Foreground = Brush("TextSecondary"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetRow(card, 1);
                cardBlock.Children.Add(card);
                _timelineDateAnchors[entryDate] = cardBlock;
                cards.Children.Add(cardBlock);
            }
            else
            {
                cards.Children.Add(card);
            }
        }
        grid.Children.Add(times);
        Grid.SetColumn(cards, 1);
        grid.Children.Add(cards);
        return grid;
    }

    private FrameworkElement TimelineCard(DiaryEntry entry, bool selected, bool showNotebook, bool compact = false)
    {
        var border = new Border { MinHeight = compact ? 82 : TimelineCardMinHeight, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(selected ? 1.5 : 1), BorderBrush = selected ? Brush("Accent") : Brush("CardStroke"), Background = selected ? Brush("SelectedCard") : CardBackgroundBrush(), Padding = compact ? new Thickness(18, 10, 14, 10) : new Thickness(22, 14, 18, 15) };
        border.Tapped += async (_, _) =>
        {
            _selectedEntry = entry;
            if (IsTimelineView)
            {
                await ShowEntryEditorAsync(entry);
            }
            else
            {
                BuildShell();
            }
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(Text(entry.Title, compact ? 17 : 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var favorite = TimelineIconButton("\uE734", entry.IsFavorite ? "取消收藏" : "收藏", entry.IsFavorite ? "Favorite" : "TextSecondary");
        favorite.Click += (_, _) => ToggleFavorite(entry.Id);
        icons.Children.Add(favorite);
        var more = TimelineIconButton("\uE712", "更多");
        more.Click += (_, _) => ShowEntryMenu(more, entry);
        icons.Children.Add(more);
        Grid.SetColumn(icons, 1);
        grid.Children.Add(icons);
        var snippet = Text(Snippet(entry.Body), compact ? 14 : 15, "TextSecondary");
        snippet.Margin = new Thickness(0, compact ? 4 : 6, 0, 0);
        Grid.SetRow(snippet, 1);
        grid.Children.Add(snippet);
        var tags = TagRow(entry.Tags, showNotebook ? _data.Notebooks.FirstOrDefault(notebook => notebook.Id == entry.NotebookId) : null);
        tags.Margin = new Thickness(0, compact ? 5 : 7, 0, 0);
        Grid.SetRow(tags, 2);
        grid.Children.Add(tags);
        border.Child = grid;
        return border;
    }

    private FrameworkElement BuildDetailColumn()
    {
        return new Border
        {
            Background = Brush("DetailBackground"),
            Padding = new Thickness(34, 62, 34, 28),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, Content = BuildDetailContent() }
        };
    }

    private UIElement BuildDetailContent()
    {
        if (string.Equals(_settings.CurrentViewId, DiaryViewIds.Tags, StringComparison.Ordinal))
        {
            return BuildTagDetailContent();
        }
        if (_selectedEntry is null)
        {
            return EmptyCard("请选择一篇日记。");
        }
        var entry = _selectedEntry;
        var stack = new StackPanel { Spacing = 14 };
        var title = new Grid { RowSpacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        title.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        title.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(Text(entry.Title, 30, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        var date = Text(entry.CreatedAt.ToString("yyyy年M月d日 HH:mm"), 14, "TextSecondary");
        Grid.SetRow(date, 1);
        title.Children.Add(date);
        var titleActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var favorite = IconButton("\uE734", entry.IsFavorite ? "取消收藏" : "收藏", entry.IsFavorite ? "Favorite" : "TextSecondary");
        favorite.Click += (_, _) => ToggleFavorite(entry.Id);
        titleActions.Children.Add(favorite);
        var more = IconButton("\uE712", "更多");
        more.Click += (_, _) => ShowEntryMenu(more, entry);
        titleActions.Children.Add(more);
        var close = IconButton("\uE711", "关闭");
        close.Click += (_, _) => { _selectedEntry = null; BuildShell(); };
        titleActions.Children.Add(close);
        Grid.SetColumn(titleActions, 1);
        title.Children.Add(titleActions);
        var edit = TextButton("\uE70F  编辑", "编辑日记", 14, "TextSecondary");
        edit.Click += async (_, _) => await ShowEntryEditorAsync(entry);
        Grid.SetColumn(edit, 1);
        Grid.SetRow(edit, 1);
        title.Children.Add(edit);
        stack.Children.Add(title);
        stack.Children.Add(BuildMetaCard(entry));
        stack.Children.Add(BuildCalendarCard(large: false));
        stack.Children.Add(BuildTodoLinksCard(entry));
        stack.Children.Add(BuildDetailActions(entry));
        return stack;
    }

    private UIElement BuildTagDetailContent()
    {
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(Text("标签管理", 30, "TextPrimary", Microsoft.UI.Text.FontWeights.Bold));
        stack.Children.Add(Text("为日记建立可复用的主题与颜色。", 14, "TextSecondary"));
        var guide = new StackPanel { Spacing = 10, Margin = new Thickness(16, 14, 16, 14) };
        guide.Children.Add(Text("使用说明", 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        guide.Children.Add(Text("新建标签后，可在快速记录工具栏和日记详情中选择；标签页顶部可按标签筛选日记。", 13, "TextSecondary"));
        guide.Children.Add(Text("删除定义不会删除旧日记中的标签文本。", 13, "TextSecondary"));
        stack.Children.Add(Card(guide, null));
        var palette = new StackPanel { Spacing = 10, Margin = new Thickness(16, 14, 16, 14) };
        palette.Children.Add(Text("第一版配色 · 12 色", 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var colors = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var column = 0; column < 3; column++) colors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 4; row++) colors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < DiaryMetadata.TagColors.Count; index++)
        {
            var color = DiaryMetadata.TagColors[index];
            var swatch = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            swatch.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = HexBrush(color.Hex), VerticalAlignment = VerticalAlignment.Center });
            swatch.Children.Add(Text(color.Name, 12, "TextSecondary"));
            Grid.SetColumn(swatch, index % 3);
            Grid.SetRow(swatch, index / 3);
            colors.Children.Add(swatch);
        }
        palette.Children.Add(colors);
        stack.Children.Add(Card(palette, null));
        return stack;
    }

    private FrameworkElement BuildMetaCard(DiaryEntry entry)
    {
        var stack = new StackPanel { Spacing = 0, Margin = new Thickness(16, 10, 16, 10) };
        stack.Children.Add(MetaRow("\uE76E", "心情", entry.Mood, "🙂"));
        stack.Children.Add(MetaRow("\uE787", "天气", entry.Weather, "⛅"));
        stack.Children.Add(MetaRow("\uE707", "地点", entry.Location, "\uE707"));
        stack.Children.Add(MetaTagRow(entry));
        return Card(stack, null);
    }

    private FrameworkElement MetaRow(string glyph, string label, string value, string valueIcon)
    {
        var layout = new Grid { Height = 44 };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 17, Foreground = Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center });
        var labelBlock = Text(label, 14, "TextSecondary");
        Grid.SetColumn(labelBlock, 1);
        grid.Children.Add(labelBlock);
        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        valueRow.Children.Add(IsSegoeGlyph(valueIcon) ? new FontIcon { Glyph = valueIcon, FontSize = 16, Foreground = Brush("TextSecondary") } : EmojiText(valueIcon, 18));
        valueRow.Children.Add(Text(value, 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        Grid.SetColumn(valueRow, 2);
        grid.Children.Add(valueRow);
        layout.Children.Add(grid);
        var divider = new Border { Height = 1, Background = Brush("InnerDivider"), Margin = new Thickness(30, 0, 0, 0) };
        Grid.SetRow(divider, 1);
        layout.Children.Add(divider);
        return layout;
    }

    private FrameworkElement MetaTagRow(DiaryEntry entry)
    {
        var grid = new Grid { Height = 44, ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = "\uE8EC", FontSize = 17, Foreground = Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center });
        var label = Text("标签", 14, "TextSecondary");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tag in entry.Tags)
        {
            row.Children.Add(MetaTagPill(tag));
        }
        var add = TextButton("+", "编辑标签", 18, "TextPrimary");
        add.Click += async (_, _) => await ShowTagPickerAsync(entry);
        row.Children.Add(add);
        Grid.SetColumn(row, 2);
        grid.Children.Add(row);
        return grid;
    }

    private FrameworkElement BuildCalendarCard(bool large)
    {
        var stack = new StackPanel { Spacing = large ? 13 : 10, Margin = new Thickness(18, 14, 18, 16) };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Text("日历", large ? 18 : 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var month = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var previous = TextButton("‹", "上个月", 20, "TextSecondary");
        previous.Click += (_, _) => MoveCalendarMonth(-1);
        month.Children.Add(previous);
        month.Children.Add(Text(_calendarMonth.ToString("yyyy年M月"), 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var next = TextButton("›", "下个月", 20, "TextSecondary");
        next.Click += (_, _) => MoveCalendarMonth(1);
        month.Children.Add(next);
        Grid.SetColumn(month, 1);
        header.Children.Add(month);
        var view = TimelineIconButton("\uE787", "日历视图");
        view.Click += (_, _) => SelectView(DiaryViewIds.Calendar);
        Grid.SetColumn(view, 2);
        header.Children.Add(view);
        stack.Children.Add(header);
        var calendar = new Grid { RowSpacing = 5, ColumnSpacing = 6 };
        for (var i = 0; i < 7; i++) calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++) calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labels = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (var col = 0; col < labels.Length; col++)
        {
            var text = Text(labels[col], 13, "TextMuted");
            text.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(text, col);
            calendar.Children.Add(text);
        }
        var start = _calendarMonth.AddDays(-((int)_calendarMonth.DayOfWeek + 6) % 7);
        for (var index = 0; index < 42; index++)
        {
            var date = start.AddDays(index);
            var cell = CalendarCell(date);
            Grid.SetRow(cell, index / 7 + 1);
            Grid.SetColumn(cell, index % 7);
            calendar.Children.Add(cell);
        }
        stack.Children.Add(calendar);
        return Card(stack, null);
    }

    private Button CalendarCell(DateTime date)
    {
        var inMonth = date.Month == _calendarMonth.Month && date.Year == _calendarMonth.Year;
        var selected = _calendarDate?.Date == date.Date;
        var hasDot = _data.Entries.Any(entry => entry.CreatedAt.LocalDateTime.Date == date.Date);
        var button = new Button { Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = selected ? Brush("Accent") : TransparentBrush(), CornerRadius = new CornerRadius(14), Content = new TextBlock { Text = date.Day.ToString(), FontSize = 13, Foreground = selected ? Brush("OnAccent") : inMonth ? Brush("TextPrimary") : Brush("TextMuted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        if (hasDot && !selected) button.BorderBrush = Brush("Accent");
        if (hasDot && !selected) button.BorderThickness = new Thickness(1);
        button.Click += (_, _) => SelectCalendarDate(date);
        return button;
    }

    private FrameworkElement BuildTodoLinksCard(DiaryEntry entry)
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(18, 16, 18, 16) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Text("关联的待办", 18, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        var add = TextButton("+ 添加待办", "添加待办", 14, "Accent");
        add.Click += async (_, _) => await ShowTodoPickerAsync(entry);
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        stack.Children.Add(header);
        if (entry.TodoLinks.Count == 0)
        {
            stack.Children.Add(Text("没有已关联的待办。", 14, "TextSecondary"));
        }
        else
        {
            foreach (var link in entry.TodoLinks.Take(4)) stack.Children.Add(TodoLinkRow(link));
        }
        return Card(stack, null);
    }

    private FrameworkElement TodoLinkRow(DiaryTodoLink link)
    {
        var grid = new Grid { Height = 42 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 16, Height = 16, Stroke = Brush("TextSecondary"), StrokeThickness = 1.4, VerticalAlignment = VerticalAlignment.Center });
        var title = Text(link.TitleSnapshot, 15, "TextPrimary");
        title.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var visual = TagVisualFor(link.ListNameSnapshot);
        var pill = TodoStatusPill(link.ListNameSnapshot, visual.BackgroundKey, visual.ForegroundKey);
        Grid.SetColumn(pill, 2);
        grid.Children.Add(pill);
        return new Border { BorderBrush = Brush("InnerDivider"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private FrameworkElement BuildDetailActions(DiaryEntry entry)
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 18, 0, 0) };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var favorite = OutlineButton("\uE734", entry.IsFavorite ? "已收藏" : "收藏", "Favorite");
        favorite.Click += (_, _) => ToggleFavorite(entry.Id);
        grid.Children.Add(favorite);
        var export = OutlineButton("\uE898", "导出", "TextPrimary");
        export.Click += async (_, _) => await ExportEntryAsync(entry);
        Grid.SetColumn(export, 1);
        grid.Children.Add(export);
        var delete = OutlineButton("\uE74D", "删除", "Danger");
        delete.Click += async (_, _) => await DeleteEntryAsync(entry);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        return grid;
    }

    private StackPanel TagRow(IReadOnlyList<string> tags, DiaryNotebook? notebook = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (notebook is not null)
        {
            row.Children.Add(NotebookPill(notebook));
        }
        foreach (var tag in tags.Where(tag => notebook is null || !string.Equals(tag, notebook.Name, StringComparison.OrdinalIgnoreCase))) row.Children.Add(Pill(tag));
        return row;
    }

    private Border NotebookPill(DiaryNotebook notebook)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = HexBrush(notebook.AccentColor),
            Padding = new Thickness(10, 5, 10, 5),
            Child = new TextBlock { Text = notebook.Name, FontSize = 13, Foreground = Brush("OnAccent"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
        };
    }

    private Border MetaTagPill(string tag)
    {
        return new Border { Height = 28, MinWidth = 50, CornerRadius = new CornerRadius(6), Background = TagBackgroundBrush(tag), Padding = new Thickness(10, 0, 10, 0), Child = new TextBlock { Text = tag, FontSize = 13, Foreground = TagForegroundBrush(tag), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
    }

    private Border Pill(string tag)
    {
        return new Border { CornerRadius = new CornerRadius(6), Background = TagBackgroundBrush(tag), Padding = new Thickness(10, 5, 10, 5), Child = new TextBlock { Text = tag, FontSize = 13, Foreground = TagForegroundBrush(tag), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
    }

    private Border TodoStatusPill(string text, string brushKey, string foregroundKey) => new() { CornerRadius = new CornerRadius(6), Background = Brush(brushKey), Padding = new Thickness(9, 5, 9, 5), Child = Text(text, 12, foregroundKey, Microsoft.UI.Text.FontWeights.SemiBold) };

    private Button PrimaryButton(string glyph, string text) => new()
    {
        Height = 40, Padding = new Thickness(14, 0, 14, 0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7), Background = Brush("Accent"),
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = glyph, FontSize = 15, Foreground = Brush("OnAccent") }, Text(text, 14, "OnAccent", Microsoft.UI.Text.FontWeights.SemiBold) } }
    };

    private Button SecondaryButton(string text) => new() { Height = 36, Padding = new Thickness(14, 0, 14, 0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7), Background = Brush("ControlBackground"), Content = Text(text, 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold) };

    private Button OutlineButton(string glyph, string text, string foregroundKey) => new()
    {
        Height = 44, Padding = new Thickness(9, 0, 9, 0), BorderBrush = Brush("CardStroke"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Background = TransparentBrush(),
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { new FontIcon { Glyph = glyph, FontSize = 16, Foreground = Brush(foregroundKey) }, Text(text, 14, foregroundKey, Microsoft.UI.Text.FontWeights.SemiBold) } }
    };

    private Button IconButton(string glyph, string label, string foregroundKey = "TextSecondary")
    {
        var foreground = Brush(foregroundKey);
        var button = new Button { Width = 32, Height = 32, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = TransparentBrush(), Content = new FontIcon { Glyph = glyph, FontSize = 16, Foreground = foreground } };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private Button TimelineIconButton(string glyph, string label, string foregroundKey = "TextSecondary")
    {
        var foreground = Brush(foregroundKey);
        var button = new Button { Width = 28, Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = TransparentBrush(), Content = new FontIcon { Glyph = glyph, FontSize = 15, Foreground = foreground } };
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private Button TextButton(string text, string label, double size, string foregroundKey)
    {
        var button = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = TransparentBrush(), Content = Text(text, size, foregroundKey) };
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private Border Card(UIElement child, double? minHeight) => new() { MinHeight = minHeight ?? 0, CornerRadius = new CornerRadius(CardCornerRadius), BorderThickness = new Thickness(1), BorderBrush = Brush("CardStroke"), Background = CardBackgroundBrush(), Child = child };

    private FrameworkElement EmptyCard(string text) => Card(new TextBlock { Text = text, Foreground = Brush("TextSecondary"), FontSize = 14, Margin = new Thickness(18) }, 64);

    private TextBlock Text(string text, double size, string brushKey, global::Windows.UI.Text.FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = Brush(brushKey), FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

    private TextBlock EmojiText(string text, double size) => new() { Text = text, FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji"), Foreground = Brush("TextPrimary"), VerticalAlignment = VerticalAlignment.Center };

    private string ComposeMood => _draftEntry?.Mood ?? _composeMood;
    private string ComposeWeather => _draftEntry?.Weather ?? _composeWeather;
    private string ComposeLocation => _draftEntry?.Location ?? _composeLocation;

    private void BeginDraft(bool focusEditor)
    {
        EnsureDraft();
        BuildShell();
        if (focusEditor)
        {
            _quickEditor?.Focus(FocusState.Programmatic);
        }
    }

    private DiaryEntry EnsureDraft()
    {
        if (_draftEntry is not null)
        {
            return _draftEntry;
        }
        var now = DateTimeOffset.Now;
        _draftEntry = new DiaryEntry
        {
            Id = DiaryStore.NewId("entry"),
            Title = "未命名日记",
            NotebookId = DiaryViewIds.IsNotebook(_settings.CurrentViewId)
                ? DiaryViewIds.NotebookId(_settings.CurrentViewId)
                : IsTimelineView && !string.Equals(TimelineNotebookId, DiaryTimeline.AllNotebooksId, StringComparison.Ordinal)
                    ? TimelineNotebookId
                    : _data.Notebooks[0].Id,
            Mood = _composeMood,
            Weather = _composeWeather,
            Location = _composeLocation,
            IsDraft = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _data.Entries.Insert(0, _draftEntry);
        SaveData(silent: true);
        return _draftEntry;
    }

    private void SaveDraft()
    {
        if (_draftEntry is null || string.IsNullOrWhiteSpace(_draftEntry.Body))
        {
            return;
        }
        _draftEntry.Title = DiaryText.InferTitle(_draftEntry.Body);
        _draftEntry.IsDraft = false;
        _draftEntry.UpdatedAt = DateTimeOffset.Now;
        _selectedEntry = _draftEntry;
        _draftEntry = null;
        _settings.CurrentViewId = DiaryViewIds.Today;
        SaveSettings();
        SaveData(silent: true);
        BuildShell();
    }

    private void SelectView(string viewId)
    {
        _settings.CurrentViewId = viewId;
        if (!string.Equals(viewId, DiaryViewIds.Tags, StringComparison.Ordinal)) _tagFilter = null;
        SaveSettings();
        EnsureSelectedEntryVisible();
        BuildShell();
    }

    private ComboBox BuildTimelineNotebookSelector()
    {
        var selector = new ComboBox { Width = 176, Height = 36, Padding = new Thickness(10, 0, 8, 0) };
        var all = new ComboBoxItem { Content = "全部日记本", Tag = DiaryTimeline.AllNotebooksId };
        selector.Items.Add(all);
        foreach (var notebook in _data.Notebooks)
        {
            var itemContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            itemContent.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = HexBrush(notebook.AccentColor), VerticalAlignment = VerticalAlignment.Center });
            itemContent.Children.Add(Text(notebook.Name, 14, "TextPrimary"));
            selector.Items.Add(new ComboBoxItem { Content = itemContent, Tag = notebook.Id });
        }
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), TimelineNotebookId, StringComparison.Ordinal))
            ?? all;
        selector.SelectionChanged += (_, _) =>
        {
            if (_buildingShell || selector.SelectedItem is not ComboBoxItem item || item.Tag is not string notebookId)
            {
                return;
            }
            _settings.TimelineNotebookId = notebookId;
            SaveSettings();
            EnsureSelectedEntryVisible();
            BuildShell();
        };
        return selector;
    }

    private void SelectEntry(string entryId)
    {
        _selectedEntry = _data.Entries.FirstOrDefault(entry => string.Equals(entry.Id, entryId, StringComparison.Ordinal));
        if (_selectedEntry?.IsDraft == true) _draftEntry = _selectedEntry;
        BuildShell();
    }

    private void ToggleFavorite(string entryId)
    {
        var entry = _data.Entries.FirstOrDefault(candidate => string.Equals(candidate.Id, entryId, StringComparison.Ordinal));
        if (entry is null) return;
        entry.IsFavorite = !entry.IsFavorite;
        entry.UpdatedAt = DateTimeOffset.Now;
        _selectedEntry = entry;
        SaveData(silent: true);
        BuildShell();
    }

    private void MoveCalendarMonth(int offset)
    {
        _calendarMonth = _calendarMonth.AddMonths(offset);
        _calendarDate = null;
        BuildShell();
    }

    private void SelectCalendarDate(DateTime date)
    {
        _calendarMonth = new DateTime(date.Year, date.Month, 1);
        _calendarDate = date.Date;
        _settings.CurrentViewId = DiaryViewIds.Calendar;
        SaveSettings();
        EnsureSelectedEntryVisible();
        BuildShell();
    }

    private void EnsureSelectedEntryVisible()
    {
        var visible = FilteredEntries().ToList();
        if (_selectedEntry is null || visible.All(entry => entry.Id != _selectedEntry.Id))
        {
            _selectedEntry = visible.FirstOrDefault() ?? _data.Entries.Where(entry => !entry.IsDraft).OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault();
        }
    }

    private IEnumerable<DiaryEntry> FilteredEntries()
    {
        IEnumerable<DiaryEntry> entries = _data.Entries;
        var view = _settings.CurrentViewId;
        if (string.Equals(view, DiaryViewIds.Timeline, StringComparison.Ordinal))
        {
            return TimelineEntries();
        }
        entries = view switch
        {
            DiaryViewIds.Today => entries.Where(entry => entry.CreatedAt.LocalDateTime.Date == DiaryRuntime.Today),
            DiaryViewIds.Favorites => entries.Where(entry => entry.IsFavorite),
            DiaryViewIds.Drafts => entries.Where(entry => entry.IsDraft),
            DiaryViewIds.Calendar when _calendarDate is not null => entries.Where(entry => entry.CreatedAt.LocalDateTime.Date == _calendarDate.Value.Date),
            DiaryViewIds.Calendar => entries.Where(entry => entry.CreatedAt.Year == _calendarMonth.Year && entry.CreatedAt.Month == _calendarMonth.Month),
            DiaryViewIds.Tags when !string.IsNullOrWhiteSpace(_tagFilter) => entries.Where(entry => entry.Tags.Any(tag => string.Equals(tag, _tagFilter, StringComparison.OrdinalIgnoreCase))),
            DiaryViewIds.Tags => entries.Where(entry => entry.Tags.Count > 0),
            _ when DiaryViewIds.IsNotebook(view) => entries.Where(entry => string.Equals(entry.NotebookId, DiaryViewIds.NotebookId(view), StringComparison.Ordinal)),
            _ => entries
        };
        return entries.OrderBy(entry => entry.CreatedAt);
    }

    private string PageTitle()
    {
        var view = _settings.CurrentViewId;
        if (DiaryViewIds.IsNotebook(view)) return NotebookName(DiaryViewIds.NotebookId(view));
        return view switch
        {
            DiaryViewIds.Timeline => "时间线",
            DiaryViewIds.Calendar => "日历",
            DiaryViewIds.Tags => "标签",
            DiaryViewIds.Favorites => "收藏",
            DiaryViewIds.Drafts => "草稿",
            _ => "今天的日记"
        };
    }

    private string PageListTitle()
    {
        if (string.Equals(_settings.CurrentViewId, DiaryViewIds.Today, StringComparison.Ordinal))
        {
            return "今天";
        }
        if (string.Equals(_settings.CurrentViewId, DiaryViewIds.Timeline, StringComparison.Ordinal))
        {
            return string.Equals(TimelineNotebookId, DiaryTimeline.AllNotebooksId, StringComparison.Ordinal)
                ? "全部日记本"
                : NotebookName(TimelineNotebookId);
        }
        return PageTitle();
    }

    private string TimelineNotebookId => string.IsNullOrWhiteSpace(_settings.TimelineNotebookId) ? DiaryTimeline.AllNotebooksId : _settings.TimelineNotebookId;

    private void NormalizeTimelineNotebookSelection()
    {
        var normalizedNotebookId = DiaryTimeline.ResolveNotebookId(_data, TimelineNotebookId);
        if (string.Equals(_settings.TimelineNotebookId, normalizedNotebookId, StringComparison.Ordinal))
        {
            return;
        }
        _settings.TimelineNotebookId = normalizedNotebookId;
        SaveSettings();
    }

    private string NotebookName(string id) => _data.Notebooks.FirstOrDefault(notebook => string.Equals(notebook.Id, id, StringComparison.Ordinal))?.Name ?? DiaryStore.DefaultNotebookName;

    private async Task ShowEntryEditorAsync(DiaryEntry entry)
    {
        var titleBox = new TextBox { Text = entry.Title == "未命名日记" ? string.Empty : entry.Title, PlaceholderText = "标题" };
        var bodyBox = new TextBox { Text = entry.Body, PlaceholderText = "正文", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxLength = MaximumBodyLength, MinHeight = 150 };
        var notebookBox = new ComboBox { MinWidth = 260 };
        foreach (var notebook in _data.Notebooks)
        {
            var item = new ComboBoxItem { Content = notebook.Name, Tag = notebook.Id };
            notebookBox.Items.Add(item);
            if (notebook.Id == entry.NotebookId) notebookBox.SelectedItem = item;
        }
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var tag in entry.Tags) tags.Children.Add(MetaTagPill(tag));
        var content = new ScrollViewer { MaxHeight = 560, Content = new StackPanel { Spacing = 10, Children = { Field("标题", titleBox), Field("正文", bodyBox), Field("日记本", notebookBox), Field("标签", new StackPanel { Spacing = 8, Children = { tags, Text("请从日记详情或快速记录工具栏管理标签。", 12, "TextMuted") } }) } } };
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "编辑日记", Content = content, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (notebookBox.SelectedItem is ComboBoxItem selectedNotebook) entry.NotebookId = selectedNotebook.Tag?.ToString() ?? entry.NotebookId;
        entry.Body = bodyBox.Text;
        entry.Title = string.IsNullOrWhiteSpace(titleBox.Text) ? DiaryText.InferTitle(entry.Body) : titleBox.Text.Trim();
        entry.UpdatedAt = DateTimeOffset.Now;
        SaveData(silent: true);
        BuildShell();
    }

    private async Task ShowTagPickerAsync(DiaryEntry entry)
    {
        var selected = entry.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel { Spacing = 8 };
        foreach (var tag in _data.TagCatalog.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var check = new CheckBox { Content = tag.Name, IsChecked = selected.Contains(tag.Name) };
            checks[tag.Name] = check;
            list.Children.Add(check);
        }
        var name = new TextBox { PlaceholderText = "新标签名称" };
        var color = BuildTagColorSelector();
        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                list.Children.Count == 0 ? Text("还没有标签。新建一个标签后即可使用。", 13, "TextSecondary") : new ScrollViewer { MaxHeight = 260, Content = list },
                new Border { BorderBrush = Brush("InnerDivider"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0) },
                Field("新建标签", name),
                Field("配色", color)
            }
        };
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "选择标签", Content = content, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!string.IsNullOrWhiteSpace(name.Text))
        {
            var colorId = (color.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            try
            {
                var created = DiaryTags.Ensure(_data, name.Text, colorId);
                selected.Add(created.Name);
            }
            catch (ArgumentException)
            {
                await ShowMessageAsync("标签名称无效", "请输入非空的标签名称。");
                return;
            }
        }
        foreach (var pair in checks)
        {
            if (pair.Value.IsChecked == true) selected.Add(pair.Key); else selected.Remove(pair.Key);
        }
        DiaryTags.Apply(_data, entry, selected);
        entry.UpdatedAt = DateTimeOffset.Now;
        SaveData(silent: true);
        BuildShell();
    }

    private ComboBox BuildTagColorSelector(string? selectedColorId = null)
    {
        var selector = new ComboBox { MinWidth = 240 };
        foreach (var color in DiaryMetadata.TagColors)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = HexBrush(color.Hex), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(Text(color.Name, 14, "TextPrimary"));
            var item = new ComboBoxItem { Content = content, Tag = color.Id };
            selector.Items.Add(item);
            if (string.Equals(color.Id, selectedColorId ?? DiaryMetadata.DefaultTagColorId, StringComparison.OrdinalIgnoreCase))
            {
                selector.SelectedItem = item;
            }
        }
        selector.SelectedIndex = selector.SelectedIndex < 0 ? 0 : selector.SelectedIndex;
        return selector;
    }

    private async Task ShowCreateTagDialogAsync()
    {
        var name = new TextBox { PlaceholderText = "例如：阅读" };
        var color = BuildTagColorSelector();
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "新建标签",
            Content = new StackPanel { Spacing = 10, Children = { Field("名称", name), Field("配色（12 色）", color) } },
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        try
        {
            DiaryTags.Ensure(_data, name.Text, (color.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        }
        catch (ArgumentException)
        {
            await ShowMessageAsync("标签名称无效", "请输入非空的标签名称。");
            return;
        }
        SaveData(silent: true);
        BuildShell();
    }

    private async Task ShowEditTagDialogAsync(DiaryTagDefinition tag)
    {
        var name = new TextBox { Text = tag.Name };
        var color = BuildTagColorSelector(tag.ColorId);
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "编辑标签",
            Content = new StackPanel { Spacing = 10, Children = { Field("名称", name), Field("配色（12 色）", color) } },
            PrimaryButtonText = "保存",
            SecondaryButtonText = "删除标签",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (!DiaryTags.Rename(_data, tag.Id, name.Text))
                {
                    await ShowMessageAsync("标签名称已存在", "请使用不同的标签名称。");
                    return;
                }
                tag.ColorId = DiaryMetadata.TagColor((color.SelectedItem as ComboBoxItem)?.Tag?.ToString()).Id;
                SaveData(silent: true);
                BuildShell();
            }
            catch (ArgumentException)
            {
                await ShowMessageAsync("标签名称无效", "请输入非空的标签名称。");
            }
            return;
        }
        if (result != ContentDialogResult.Secondary)
        {
            return;
        }
        var confirm = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = $"删除标签“{tag.Name}”？",
            Content = "标签定义将被移除；已有日记中的历史标签文字会保留。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            DiaryTags.RemoveDefinition(_data, tag.Id);
            if (string.Equals(_tagFilter, tag.Name, StringComparison.OrdinalIgnoreCase))
            {
                _tagFilter = null;
            }
            SaveData(silent: true);
            BuildShell();
        }
    }

    private async Task AcquireLocationAsync()
    {
        if (!await EnsureLocationFeatureEnabledAsync())
        {
            return;
        }
        var coordinates = await GetDeviceLocationAsync();
        if (coordinates is null)
        {
            return;
        }
        try
        {
            var resolved = await _reverseGeocoder.ReverseAsync(coordinates.Value.Latitude, coordinates.Value.Longitude, _settings.ReverseGeocoderEndpoint);
            var label = resolved is null ? CoordinateLabel(coordinates.Value.Latitude, coordinates.Value.Longitude) : ShortLocationLabel(resolved.DisplayName);
            SetQuickLocation(label, new DiaryLocationDetails
            {
                Source = "nominatim",
                Latitude = coordinates.Value.Latitude,
                Longitude = coordinates.Value.Longitude,
                ResolvedAt = DateTimeOffset.Now
            });
        }
        catch
        {
            SetQuickLocation(CoordinateLabel(coordinates.Value.Latitude, coordinates.Value.Longitude), new DiaryLocationDetails
            {
                Source = "device",
                Latitude = coordinates.Value.Latitude,
                Longitude = coordinates.Value.Longitude,
                ResolvedAt = DateTimeOffset.Now
            });
            await ShowMessageAsync("地点名称获取失败", "已保存当前位置坐标。请检查网络后重试，或手动输入地点。");
        }
    }

    private async Task AcquireWeatherAsync()
    {
        if (!await EnsureWeatherFeatureEnabledAsync())
        {
            return;
        }
        var coordinates = await GetDeviceLocationAsync();
        if (coordinates is null)
        {
            return;
        }
        try
        {
            var weather = await _weatherProvider.GetCurrentAsync(coordinates.Value.Latitude, coordinates.Value.Longitude, _settings.WeatherEndpoint);
            SetQuickWeather($"{weather.Condition} {Math.Round(weather.TemperatureCelsius):0}℃", new DiaryWeatherDetails
            {
                Source = "open-meteo",
                TemperatureCelsius = weather.TemperatureCelsius,
                WeatherCode = weather.WeatherCode,
                Latitude = weather.Latitude,
                Longitude = weather.Longitude,
                FetchedAt = weather.FetchedAt
            });
        }
        catch
        {
            await ShowMessageAsync("天气获取失败", "现有天气信息未修改。请检查网络或稍后重试。");
        }
    }

    private async Task<bool> EnsureLocationFeatureEnabledAsync()
    {
        if (_settings.LocationFeatureEnabled)
        {
            return true;
        }
        if (_settings.LocationConsentAcceptedAt is null && !await ConfirmLocationConsentAsync())
        {
            return false;
        }
        _settings.LocationFeatureEnabled = true;
        _settings.LocationConsentAcceptedAt ??= DateTimeOffset.Now;
        SaveSettings();
        return true;
    }

    private async Task<bool> EnsureWeatherFeatureEnabledAsync()
    {
        if (!_settings.LocationFeatureEnabled)
        {
            await ShowMessageAsync("请先启用自动定位", "自动天气需要先取得一次由你主动触发的位置坐标。请在设置中启用自动定位。");
            return false;
        }
        if (_settings.WeatherFeatureEnabled)
        {
            return true;
        }
        if (_settings.WeatherConsentAcceptedAt is null && !await ConfirmWeatherConsentAsync())
        {
            return false;
        }
        _settings.WeatherFeatureEnabled = true;
        _settings.WeatherConsentAcceptedAt ??= DateTimeOffset.Now;
        SaveSettings();
        return true;
    }

    private async Task<bool> ConfirmLocationConsentAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "启用自动定位？",
            Content = "仅在你点击“获取当前位置”时，Fowan 才会请求 Windows 定位权限。取得的经纬度会发送给 Nominatim 用于解析可读地点名称；不会后台定位，也可以随时在设置中关闭。",
            PrimaryButtonText = "同意并继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmWeatherConsentAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "启用自动天气？",
            Content = "仅在你点击“自动获取当前位置天气”时，Fowan 才会把本次位置坐标发送给 Open-Meteo 查询当前天气。不会后台更新，也可以随时在设置中关闭。",
            PrimaryButtonText = "同意并继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<(double Latitude, double Longitude)?> GetDeviceLocationAsync()
    {
        if (_lastDeviceLocation is { } cached && DateTimeOffset.Now - cached.AcquiredAt < TimeSpan.FromMinutes(5))
        {
            return (cached.Latitude, cached.Longitude);
        }
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
            {
                await ShowMessageAsync("定位权限未授予", "请在 Windows 设置中允许 Fowan 使用位置，或继续手动输入地点。");
                return null;
            }
            var locator = new Geolocator { DesiredAccuracyInMeters = 150 };
            var position = await locator.GetGeopositionAsync();
            var point = position.Coordinate.Point.Position;
            _lastDeviceLocation = (point.Latitude, point.Longitude, DateTimeOffset.Now);
            return (point.Latitude, point.Longitude);
        }
        catch
        {
            await ShowMessageAsync("无法获取当前位置", "请检查 Windows 定位服务和网络连接，或继续手动输入地点。");
            return null;
        }
    }

    private static string CoordinateLabel(double latitude, double longitude) => $"{latitude:F4}, {longitude:F4}";

    private static string ShortLocationLabel(string displayName)
    {
        var parts = displayName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(3);
        var label = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(label) ? displayName : label;
    }

    private async Task AddImageAttachmentAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg"); picker.FileTypeFilter.Add(".gif"); picker.FileTypeFilter.Add(".webp"); picker.FileTypeFilter.Add(".bmp");
        try
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var draft = EnsureDraft();
            var attachment = _store.ImportAttachment(draft.Id, file.Path);
            draft.Attachments.Add(attachment);
            draft.UpdatedAt = DateTimeOffset.Now;
            if (!SaveData(silent: true))
            {
                _store.DeleteAttachment(attachment);
                draft.Attachments.Remove(attachment);
            }
            BuildShell();
        }
        catch
        {
            await ShowMessageAsync("添加图片失败", "请选择受支持的图片文件，并确认日记数据目录可写。");
        }
    }

    private void ShowTemplateMenu(Button anchor)
    {
        var flyout = new MenuFlyout();
        foreach (var template in DiaryText.Templates)
        {
            var item = new MenuFlyoutItem { Text = template.Name };
            item.Click += (_, _) => ApplyTemplate(template);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(anchor);
    }

    private void ApplyTemplate(DiaryTemplate template)
    {
        var draft = EnsureDraft();
        draft.Body = string.IsNullOrWhiteSpace(draft.Body) ? template.Body : $"{draft.Body.TrimEnd()}\n\n{template.Body}";
        draft.Title = DiaryText.InferTitle(draft.Body);
        draft.UpdatedAt = DateTimeOffset.Now;
        SaveData(silent: true);
        BuildShell();
    }

    private async Task ShowSearchDialogAsync(string initialQuery = "")
    {
        var queryBox = new TextBox { Text = initialQuery, PlaceholderText = "搜索标题、正文或标签" };
        var results = new StackPanel { Spacing = 6 };
        ContentDialog? dialog = null;
        void Refresh()
        {
            results.Children.Clear();
            foreach (var entry in DiaryText.Search(_data, queryBox.Text).Take(8))
            {
                var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Spacing = 2, Children = { Text(entry.Title, 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold), Text(Snippet(entry.Body), 13, "TextSecondary") } } };
                button.Click += (_, _) =>
                {
                    dialog?.Hide();
                    if (IsTimelineView)
                    {
                        NavigateTimelineToEntry(entry);
                    }
                    else
                    {
                        _selectedEntry = entry;
                        BuildShell();
                    }
                };
                results.Children.Add(button);
            }
        }
        queryBox.TextChanged += (_, _) => Refresh();
        dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "搜索日记", Content = new StackPanel { Spacing = 10, Children = { queryBox, new ScrollViewer { MaxHeight = 330, Content = results } } }, CloseButtonText = "关闭" };
        Refresh();
        await dialog.ShowAsync();
    }

    private async Task ShowTodoPickerAsync(DiaryEntry entry)
    {
        _todoCandidates = TodoCandidateReader.LoadOpenCandidates(50);
        var selected = entry.TodoLinks.Select(link => link.TaskId).ToHashSet(StringComparer.Ordinal);
        var checks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        var list = new StackPanel { Spacing = 8 };
        foreach (var candidate in _todoCandidates)
        {
            var check = new CheckBox { Content = $"{candidate.Title} · {candidate.ListName}", IsChecked = selected.Contains(candidate.Id) };
            checks[candidate.Id] = check;
            list.Children.Add(check);
        }
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "关联待办", Content = _todoCandidates.Count == 0 ? Text("没有可关联的未完成待办。", 14, "TextSecondary") : new ScrollViewer { MaxHeight = 360, Content = list }, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        entry.TodoLinks = _todoCandidates.Where(candidate => checks[candidate.Id].IsChecked == true).Select(candidate => new DiaryTodoLink { TaskId = candidate.Id, TitleSnapshot = candidate.Title, ListNameSnapshot = candidate.ListName, StartDate = candidate.StartDate }).ToList();
        entry.UpdatedAt = DateTimeOffset.Now;
        SaveData(silent: true);
        BuildShell();
    }

    private async Task DeleteEntryAsync(DiaryEntry entry)
    {
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "删除这篇日记？", Content = "删除后无法恢复。", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _data.Entries.RemoveAll(candidate => candidate.Id == entry.Id);
        if (SaveData(silent: true)) _store.DeleteAttachmentDirectory(entry.Id);
        if (_draftEntry?.Id == entry.Id) _draftEntry = null;
        _selectedEntry = FilteredEntries().FirstOrDefault();
        BuildShell();
    }

    private async Task ExportEntryAsync(DiaryEntry entry)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = SafeFileName(entry.Title) };
        picker.FileTypeChoices.Add("Markdown 文档", new List<string> { ".md" });
        try
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is not null) await FileIO.WriteTextAsync(file, ToMarkdown(entry));
        }
        catch
        {
            await ShowMessageAsync("导出失败", "请检查目标位置是否可写。");
        }
    }

    private void ShowHeaderMenu(Button anchor)
    {
        var flyout = new MenuFlyout();
        var search = new MenuFlyoutItem { Text = "搜索日记" };
        search.Click += async (_, _) => await ShowSearchDialogAsync();
        flyout.Items.Add(search);
        var attachments = new MenuFlyoutItem { Text = "查看当前附件" };
        attachments.Click += async (_, _) => await ShowAttachmentsDialogAsync(_selectedEntry);
        flyout.Items.Add(attachments);
        flyout.ShowAt(anchor);
    }

    private void ShowEntryMenu(Button anchor, DiaryEntry entry)
    {
        var flyout = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "编辑日记" };
        edit.Click += async (_, _) => await ShowEntryEditorAsync(entry);
        flyout.Items.Add(edit);
        var attachments = new MenuFlyoutItem { Text = "管理图片" };
        attachments.Click += async (_, _) => await ShowAttachmentsDialogAsync(entry);
        flyout.Items.Add(attachments);
        var delete = new MenuFlyoutItem { Text = "删除", Foreground = Brush("Danger") };
        delete.Click += async (_, _) => await DeleteEntryAsync(entry);
        flyout.Items.Add(delete);
        flyout.ShowAt(anchor);
    }

    private async Task ShowAttachmentsDialogAsync(DiaryEntry? entry)
    {
        if (entry is null)
        {
            await ShowMessageAsync("图片附件", "请选择或新建一篇日记后再管理图片。");
            return;
        }
        var list = new StackPanel { Spacing = 8 };
        if (entry.Attachments.Count == 0) list.Children.Add(Text("当前日记没有图片附件。", 14, "TextSecondary"));
        foreach (var attachment in entry.Attachments.ToList())
        {
            var remove = SecondaryButton($"移除 {attachment.FileName}");
            remove.Click += (_, _) =>
            {
                entry.Attachments.Remove(attachment);
                if (SaveData(silent: true))
                {
                    _store.DeleteAttachment(attachment);
                }
                else
                {
                    entry.Attachments.Add(attachment);
                }
            };
            list.Children.Add(remove);
        }
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "图片附件", Content = list, CloseButtonText = "关闭" };
        await dialog.ShowAsync();
        BuildShell();
    }

    private void ShowNotebookMenu(Button anchor)
    {
        var flyout = new MenuFlyout();
        var create = new MenuFlyoutItem { Text = "新建日记本" };
        create.Click += async (_, _) => await ShowCreateNotebookDialogAsync();
        flyout.Items.Add(create);
        var manage = new MenuFlyoutItem { Text = "管理日记本" };
        manage.Click += async (_, _) => await ShowManageNotebooksDialogAsync();
        flyout.Items.Add(manage);
        flyout.ShowAt(anchor);
    }

    private async Task ShowCreateNotebookDialogAsync()
    {
        var name = new TextBox { PlaceholderText = "例如：旅行记录" };
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "新建日记本", Content = Field("名称", name), PrimaryButtonText = "创建", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text)) return;
        _data.Notebooks.Add(new DiaryNotebook { Id = DiaryStore.NewId("notebook"), Name = name.Text.Trim(), AccentColor = "#2F80FF" });
        SaveData(silent: true);
        BuildShell();
    }

    private async Task ShowManageNotebooksDialogAsync()
    {
        var selector = new ComboBox { MinWidth = 260 };
        foreach (var notebook in _data.Notebooks) selector.Items.Add(new ComboBoxItem { Content = notebook.Name, Tag = notebook.Id });
        selector.SelectedIndex = 0;
        var name = new TextBox { Text = _data.Notebooks[0].Name };
        var target = new ComboBox { MinWidth = 260 };
        void RefreshTarget()
        {
            name.Text = (selector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            target.Items.Clear();
            var selectedId = (selector.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            foreach (var candidate in _data.Notebooks.Where(candidate => candidate.Id != selectedId))
            {
                target.Items.Add(new ComboBoxItem { Content = candidate.Name, Tag = candidate.Id });
            }
            target.SelectedIndex = target.Items.Count > 0 ? 0 : -1;
        }
        selector.SelectionChanged += (_, _) => RefreshTarget();
        RefreshTarget();
        var delete = SecondaryButton("删除并迁移日记");
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "管理日记本", Content = new StackPanel { Spacing = 10, Children = { Field("日记本", selector), Field("名称", name), Field("迁移到", target), delete } }, PrimaryButtonText = "保存名称", CloseButtonText = "关闭", DefaultButton = ContentDialogButton.Primary };
        delete.Click += (_, _) =>
        {
            if (_data.Notebooks.Count <= 1 || selector.SelectedItem is not ComboBoxItem source || target.SelectedItem is not ComboBoxItem destination)
            {
                return;
            }
            var sourceId = source.Tag?.ToString();
            var targetId = destination.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
            {
                return;
            }
            foreach (var entry in _data.Entries.Where(entry => entry.NotebookId == sourceId))
            {
                entry.NotebookId = targetId;
                entry.UpdatedAt = DateTimeOffset.Now;
            }
            _data.Notebooks.RemoveAll(candidate => candidate.Id == sourceId);
            var settingsChanged = false;
            if (string.Equals(_settings.TimelineNotebookId, sourceId, StringComparison.Ordinal))
            {
                _settings.TimelineNotebookId = DiaryTimeline.AllNotebooksId;
                settingsChanged = true;
            }
            if (_settings.CurrentViewId == DiaryViewIds.Notebook(sourceId))
            {
                _settings.CurrentViewId = DiaryViewIds.Notebook(targetId);
                settingsChanged = true;
            }
            if (SaveData(silent: true))
            {
                if (settingsChanged) SaveSettings();
                dialog.Hide();
                BuildShell();
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || selector.SelectedItem is not ComboBoxItem item || string.IsNullOrWhiteSpace(name.Text)) return;
        var selectedNotebookModel = _data.Notebooks.FirstOrDefault(candidate => candidate.Id == item.Tag?.ToString());
        if (selectedNotebookModel is null) return;
        selectedNotebookModel.Name = name.Text.Trim();
        SaveData(silent: true);
        BuildShell();
    }

    private async Task ShowSettingsDialogAsync()
    {
        var themeBox = new ComboBox { MinWidth = 260, Items = { new ComboBoxItem { Content = "跟随系统", Tag = DiaryThemeIds.System }, new ComboBoxItem { Content = "浅色主题", Tag = DiaryThemeIds.Light }, new ComboBoxItem { Content = "深色主题", Tag = DiaryThemeIds.Dark } } };
        foreach (var item in themeBox.Items.OfType<ComboBoxItem>()) if (item.Tag?.ToString() == _settings.Theme) themeBox.SelectedItem = item;
        var locationToggle = new ToggleSwitch { Header = "自动获取当前位置", OffContent = "关闭", OnContent = "开启", IsOn = _settings.LocationFeatureEnabled };
        var weatherToggle = new ToggleSwitch { Header = "根据地点自动获取天气", OffContent = "关闭", OnContent = "开启", IsOn = _settings.WeatherFeatureEnabled, IsEnabled = locationToggle.IsOn };
        locationToggle.Toggled += (_, _) =>
        {
            weatherToggle.IsEnabled = locationToggle.IsOn;
            if (!locationToggle.IsOn)
            {
                weatherToggle.IsOn = false;
            }
        };
        var privacy = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Text("隐私与自动填充", 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold),
                locationToggle,
                Text("仅在你主动点击获取时请求 Windows 定位，并将坐标发送至 Nominatim 解析地点。", 12, "TextMuted"),
                weatherToggle,
                Text("仅在你主动获取天气时，将本次坐标发送至 Open-Meteo 查询当前天气。", 12, "TextMuted")
            }
        };
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "日记设置", Content = new StackPanel { Spacing = 12, Children = { Field("主题", themeBox), new Border { BorderBrush = Brush("InnerDivider"), BorderThickness = new Thickness(0, 1, 0, 0) }, privacy, Text("日记内容保存在此设备的 Fowan Diary 数据目录中。", 13, "TextSecondary") } }, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || themeBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string theme) return;
        if (locationToggle.IsOn && !_settings.LocationFeatureEnabled && _settings.LocationConsentAcceptedAt is null && !await ConfirmLocationConsentAsync())
        {
            return;
        }
        if (locationToggle.IsOn && weatherToggle.IsOn && !_settings.WeatherFeatureEnabled && _settings.WeatherConsentAcceptedAt is null && !await ConfirmWeatherConsentAsync())
        {
            return;
        }
        _settings.Theme = theme;
        _settings.LocationFeatureEnabled = locationToggle.IsOn;
        _settings.WeatherFeatureEnabled = locationToggle.IsOn && weatherToggle.IsOn;
        if (_settings.LocationFeatureEnabled) _settings.LocationConsentAcceptedAt ??= DateTimeOffset.Now;
        if (_settings.WeatherFeatureEnabled) _settings.WeatherConsentAcceptedAt ??= DateTimeOffset.Now;
        SaveSettings();
        ApplyCaptionButtonColorsToCurrentWindow();
        BuildShell();
    }

    private Task ShowHelpDialogAsync() => ShowMessageAsync("Fowan 日记", "使用快速记录保存当下想法；选择时间线卡片可在右侧查看、编辑、关联待办或导出。所有内容仅保存在本机。");

    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = title, Content = content, CloseButtonText = "知道了" };
        await dialog.ShowAsync();
    }

    private bool SaveData(bool silent)
    {
        try { _store.SaveData(_data); return true; }
        catch
        {
            if (!silent) _ = ShowMessageAsync("保存失败", "日记仍保留在当前窗口中，请检查本地数据目录。");
            return false;
        }
    }

    private void SaveSettings()
    {
        try { _settingsStore.Save(_settings); }
        catch { }
    }

    private static FrameworkElement Field(string label, FrameworkElement input) => new StackPanel { Spacing = 5, Children = { new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, input } };

    private static List<string> ParseTags(string value) => value.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string EmptyToDefault(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Snippet(string body)
    {
        var line = body.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(line) ? "还没有正文内容。" : line.Length <= 52 ? line : $"{line[..52]}...";
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "日记" : safe;
    }

    private static string ToMarkdown(DiaryEntry entry)
    {
        var lines = new List<string> { $"# {entry.Title}", string.Empty, $"- 创建时间：{entry.CreatedAt:yyyy-MM-dd HH:mm}", $"- 更新时间：{entry.UpdatedAt:yyyy-MM-dd HH:mm}", $"- 心情：{entry.Mood}", $"- 天气：{entry.Weather}", $"- 地点：{entry.Location}", $"- 标签：{(entry.Tags.Count == 0 ? "无" : string.Join("、", entry.Tags))}", string.Empty, "## 正文", string.Empty, entry.Body };
        if (entry.TodoLinks.Count > 0) { lines.Add(string.Empty); lines.Add("## 关联待办"); lines.Add(string.Empty); lines.AddRange(entry.TodoLinks.Select(link => $"- {link.TitleSnapshot}（{link.ListNameSnapshot}）")); }
        return string.Join(Environment.NewLine, lines);
    }

    private ElementTheme ResolveElementTheme() => _settings.Theme switch { DiaryThemeIds.Light => ElementTheme.Light, DiaryThemeIds.Dark => ElementTheme.Dark, _ => ElementTheme.Default };

    private bool IsDarkTheme() => _settings.Theme switch { DiaryThemeIds.Dark => true, DiaryThemeIds.Light => false, _ => Application.Current.RequestedTheme == ApplicationTheme.Dark };

    private SolidColorBrush Brush(string key)
    {
        var dark = IsDarkTheme();
        var color = key switch
        {
            "AppBackground" => dark ? C(0x111820) : C(0xFBFCFE), "SidebarBackground" => dark ? C(0x111C28) : C(0xF4F8FD), "DetailBackground" => dark ? C(0x121820) : C(0xFFFFFF), "CardBackground" => dark ? C(0x182028) : C(0xFFFFFF), "ControlBackground" => dark ? C(0x1A232C) : C(0xFFFFFF),
            "SelectedNav" => dark ? C(0x14325A) : C(0xE7F0FF), "SelectedCard" => dark ? C(0x142235) : C(0xFBFDFF), "Divider" => dark ? ColorHelper.FromArgb(150, 55, 64, 74) : C(0xE2E7EF), "InnerDivider" => dark ? ColorHelper.FromArgb(78, 72, 82, 92) : ColorHelper.FromArgb(130, 214, 222, 232), "SoftDivider" => dark ? ColorHelper.FromArgb(88, 82, 92, 104) : ColorHelper.FromArgb(150, 214, 222, 232),
            "CardStroke" => dark ? ColorHelper.FromArgb(145, 66, 76, 88) : C(0xDCE3EC), "TimelineLine" => dark ? ColorHelper.FromArgb(145, 60, 72, 86) : C(0xCBD4DF), "TimelineDot" => dark ? C(0x93A0AF) : C(0xC7D0DB), "TextPrimary" => dark ? C(0xF2F5F8) : C(0x17202B), "TextSecondary" => dark ? C(0xAAB3BE) : C(0x5E6977), "TextMuted" => dark ? C(0x7A8796) : C(0x96A0AD),
            "Accent" => C(0x2F80FF), "OnAccent" => C(0xFFFFFF), "Favorite" => C(0xF2A900), "Danger" => C(0xE5484D), "TagBlue" => dark ? C(0x1F3D68) : C(0xE6F0FF), "TagGreen" => dark ? C(0x1C4937) : C(0xE4F7EE), "TagPurple" => dark ? C(0x3C2B5A) : C(0xF0E6FF), "TagCyan" => dark ? C(0x1B4653) : C(0xE2F7FB), "TagYellow" => dark ? C(0x5C491B) : C(0xFFF3D8),
            "TagBlueText" => dark ? C(0x8DBEFF) : C(0x1B5FB8), "TagGreenText" => dark ? C(0x86E0B0) : C(0x1F7A4C), "TagPurpleText" => dark ? C(0xD3B7FF) : C(0x6B3FB0), "TagCyanText" => dark ? C(0x8BE4F2) : C(0x217A88), "TagYellowText" => dark ? C(0xFFD56A) : C(0x8A6500),
            _ => dark ? C(0x303B47) : C(0xDCE3EC)
        };
        return new SolidColorBrush(color);
    }

    private Microsoft.UI.Xaml.Media.Brush CardBackgroundBrush()
    {
        if (!IsDarkTheme()) return Brush("CardBackground");
        return new LinearGradientBrush { StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(0, 1), GradientStops = { new GradientStop { Color = ColorHelper.FromArgb(255, 28, 37, 47), Offset = 0 }, new GradientStop { Color = ColorHelper.FromArgb(255, 24, 32, 40), Offset = 0.55 }, new GradientStop { Color = ColorHelper.FromArgb(255, 21, 28, 36), Offset = 1 } } };
    }

    private Microsoft.UI.Xaml.Media.Brush SidebarBackgroundBrush()
    {
        if (!IsDarkTheme()) return Brush("SidebarBackground");
        return new LinearGradientBrush { StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(0.9, 1), GradientStops = { new GradientStop { Color = ColorHelper.FromArgb(255, 18, 29, 42), Offset = 0 }, new GradientStop { Color = ColorHelper.FromArgb(255, 15, 27, 39), Offset = 0.55 }, new GradientStop { Color = ColorHelper.FromArgb(255, 12, 24, 36), Offset = 1 } } };
    }

    private SolidColorBrush HexBrush(string value)
    {
        if (value.Length == 7 && byte.TryParse(value[1..3], System.Globalization.NumberStyles.HexNumber, null, out var red) && byte.TryParse(value[3..5], System.Globalization.NumberStyles.HexNumber, null, out var green) && byte.TryParse(value[5..7], System.Globalization.NumberStyles.HexNumber, null, out var blue)) return new SolidColorBrush(ColorHelper.FromArgb(255, red, green, blue));
        return Brush("Accent");
    }

    private DiaryTagColor TagColorFor(string tag) => DiaryMetadata.TagColor(DiaryTags.Find(_data, tag)?.ColorId);

    private SolidColorBrush TagBackgroundBrush(string tag)
    {
        var color = HexBrush(TagColorFor(tag).Hex).Color;
        return new SolidColorBrush(ColorHelper.FromArgb(IsDarkTheme() ? (byte)68 : (byte)32, color.R, color.G, color.B));
    }

    private SolidColorBrush TagForegroundBrush(string tag)
    {
        var color = HexBrush(TagColorFor(tag).Hex).Color;
        return new SolidColorBrush(IsDarkTheme()
            ? ColorHelper.FromArgb(255, (byte)Math.Min(255, color.R + 55), (byte)Math.Min(255, color.G + 55), (byte)Math.Min(255, color.B + 55))
            : ColorHelper.FromArgb(255, (byte)(color.R * 0.72), (byte)(color.G * 0.72), (byte)(color.B * 0.72)));
    }

    private static TagVisual TagVisualFor(string tag) => tag switch { "复盘" or "进行中" => new TagVisual("TagGreen", "TagGreenText"), "灵感" => new TagVisual("TagPurple", "TagPurpleText"), "思考" => new TagVisual("TagCyan", "TagCyanText"), "项目进展" => new TagVisual("TagYellow", "TagYellowText"), _ => new TagVisual("TagBlue", "TagBlueText") };

    private static string MoodGlyph(string mood) => mood switch
    {
        "愉快" => "\uE76E",
        "平静" => "\uE7BA",
        "专注" => "\uE8D4",
        "满足" => "\uE8FB",
        "放松" => "\uE706",
        "疲惫" => "\uE708",
        "低落" => "\uE7F4",
        "兴奋" => "\uE7F3",
        _ => "\uE76E"
    };

    private static bool IsSegoeGlyph(string text) => text.Length == 1 && text[0] >= '\uE000' && text[0] <= '\uF8FF';

    private static SolidColorBrush TransparentBrush() => new(Colors.Transparent);

    private static Color C(uint rgb) => ColorHelper.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    private static Uri FileUri(string path) => new UriBuilder { Scheme = Uri.UriSchemeFile, Path = Path.GetFullPath(path) }.Uri;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
