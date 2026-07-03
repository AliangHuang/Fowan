using Fowan.Windows.Models;
using Fowan.Windows.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Fowan.Windows;

public sealed class MainWindow : Window
{
    private const double SidebarExpandedWidth = 238;
    private const double SidebarCollapsedWidth = 76;

    private readonly SettingsStore _settingsStore = new();
    private readonly WorkspaceService _workspaceService = new();
    private readonly LocalizationService _loc = new();
    private readonly List<string> _captures = [];

    private ClientSettings _userSettings;
    private ClientSettings _settings;
    private Grid _root = new();
    private StackPanel _categoryPanel = new();
    private Grid _toolGrid = new();
    private Border _detailPanel = new();
    private Border _toastHost = new();
    private TextBlock _toastText = new();
    private TextBox _searchBox = new();
    private TextBlock _pageTitle = new();
    private TextBlock _resultSummary = new();
    private int _toastVersion;

    private string _selectedCategoryId = "all";
    private string _searchText = string.Empty;
    private bool _hasVisibleTools = true;
    private ToolViewMode _viewMode = ToolViewMode.Grid;
    private ToolSortMode _sortMode = ToolSortMode.Name;
    private ToolCard _selectedTool = ToolCatalog.Tools.First(tool => tool.Id == "settings");
    private readonly List<string> _pinnedToolIds = [];

    public MainWindow()
    {
        StartupTrace.Mark("MainWindow ctor begin");
        _userSettings = _settingsStore.Load();
        StartupTrace.Mark("Settings loaded");
        var settingsChanged = _workspaceService.EnsureInitialized(_userSettings);
        StartupTrace.Mark("Workspace initialized");
        if (settingsChanged)
        {
            _settingsStore.Save(_userSettings);
            StartupTrace.Mark("User settings migrated");
        }
        else
        {
            StartupTrace.Mark("User settings unchanged");
        }

        _settings = _workspaceService.LoadEffectiveSettings(_userSettings, ensureInitialized: false);
        StartupTrace.Mark("Effective settings loaded");
        _loc.SetLanguage(_settings.Language);
        StartupTrace.Mark("Localization loaded");
        ConfigureWindow();
        StartupTrace.Mark("Window configured");
        BuildShell();
        StartupTrace.Mark("Shell built");
    }

    private string L(string key) => _loc.Get(key);

    private void ConfigureWindow()
    {
        Title = "Fowan";

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var scale = Math.Clamp(GetDpiForWindow(hwnd) / 96.0, 1.0, 3.0);
            var width = (int)Math.Round(1440 * scale);
            var height = (int)Math.Round(880 * scale);
            appWindow.Resize(new SizeInt32(width, height));

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

            ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Window decoration APIs can fail in design-time or restricted hosts; the app shell still works.
        }
    }

    private void BuildShell()
    {
        _root = new Grid
        {
            RequestedTheme = ResolveTheme(),
            Background = ThemeBrush("AppBackground"),
            KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _root.Children.Add(BuildTopBar());

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(_settings.IsSidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth)
        });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(398) });
        Grid.SetRow(contentGrid, 1);

        contentGrid.Children.Add(BuildCategoryPane());

        var toolArea = BuildToolArea();
        Grid.SetColumn(toolArea, 1);
        contentGrid.Children.Add(toolArea);

        _detailPanel = BuildDetailPanel();
        Grid.SetColumn(_detailPanel, 2);
        contentGrid.Children.Add(_detailPanel);

        _root.Children.Add(contentGrid);

        _root.Children.Add(BuildToastHost());
        Content = _root;
        RegisterShellKeyboardAccelerators();
    }

    private Border BuildToastHost()
    {
        _toastText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };

        var toastContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        toastContent.Children.Add(new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 15,
            Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        toastContent.Children.Add(_toastText);

        _toastHost = new Border
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 360,
            Margin = new Thickness(0, 0, 28, 28),
            Padding = new Thickness(14, 10, 16, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("ToastBackground"),
            Child = toastContent
        };
        Grid.SetRow(_toastHost, 1);
        return _toastHost;
    }

    private FrameworkElement BuildTopBar()
    {
        var border = new Border
        {
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 74,
            Padding = new Thickness(22, 12, 148, 12),
            Background = ThemeBrush("TopBarBackground")
        };

        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(178) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleBarDragIcon = new Border
        {
            Width = 30,
            Height = 30,
            Background = new SolidColorBrush(Colors.Transparent),
            Child = new Image
            {
                Source = new BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-app-icon-256.png"))),
                Width = 30,
                Height = 30
            }
        };
        brand.Children.Add(titleBarDragIcon);
        SetTitleBar(titleBarDragIcon);
        brand.Children.Add(new TextBlock
        {
            Text = L("App_Title"),
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        grid.Children.Add(brand);

        var workspace = BuildWorkspaceButton();
        Grid.SetColumn(workspace, 1);
        grid.Children.Add(workspace);

        var searchShell = new Border
        {
            Height = 42,
            MinWidth = 420,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("ControlSurface"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(14, 0, 12, 0)
        };
        var searchGrid = new Grid { ColumnSpacing = 10 };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.Children.Add(new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 18,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var searchTextLayer = new Grid
        {
            Height = 42,
            VerticalAlignment = VerticalAlignment.Center
        };
        var searchPlaceholder = new TextBlock
        {
            Text = L("Search_Placeholder"),
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            FontSize = 16,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1),
            Visibility = string.IsNullOrWhiteSpace(_searchText) ? Visibility.Visible : Visibility.Collapsed
        };

        _searchBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            PlaceholderForeground = ThemeBrush("TextFillColorSecondaryBrush"),
            Padding = new Thickness(0, 7, 0, 0),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 42,
            MinHeight = 42
        };
        ApplyFlatTextBoxStyle(_searchBox);
        _searchBox.Text = _searchText;
        _searchBox.TextChanged += (_, _) =>
        {
            _searchText = _searchBox.Text.Trim();
            searchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_searchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            RefreshToolGrid();
        };
        _searchBox.KeyDown += (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Escape && !string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                ClearSearch();
                args.Handled = true;
            }
        };
        searchTextLayer.Children.Add(_searchBox);
        searchTextLayer.Children.Add(searchPlaceholder);
        Grid.SetColumn(searchTextLayer, 1);
        searchGrid.Children.Add(searchTextLayer);

        var clearSearchButton = HeaderIconButton("\uE711", L("Search_Clear"));
        clearSearchButton.Width = 30;
        clearSearchButton.Height = 30;
        clearSearchButton.Visibility = string.IsNullOrWhiteSpace(_searchText) ? Visibility.Collapsed : Visibility.Visible;
        clearSearchButton.Click += (_, _) => ClearSearch();
        _searchBox.TextChanged += (_, _) =>
        {
            clearSearchButton.Visibility = string.IsNullOrWhiteSpace(_searchBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        };
        Grid.SetColumn(clearSearchButton, 2);
        searchGrid.Children.Add(clearSearchButton);

        var shortcut = new TextBlock
        {
            Text = L("Search_Shortcut"),
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(shortcut, 3);
        searchGrid.Children.Add(shortcut);
        searchShell.Child = searchGrid;
        Grid.SetColumn(searchShell, 2);
        grid.Children.Add(searchShell);

        var engineStatus = BuildEngineStatusButton();
        Grid.SetColumn(engineStatus, 3);
        grid.Children.Add(engineStatus);

        var settingsButton = HeaderIconButton("\uE713", L("Tool_Settings"));
        settingsButton.Click += async (_, _) => await ShowSettingsDialogAsync();
        Grid.SetColumn(settingsButton, 4);
        grid.Children.Add(settingsButton);

        var account = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = ThemeBrush("AvatarBackground"),
            Child = new TextBlock
            {
                Text = L("Account_Initial"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 18,
                Foreground = ThemeBrush("TextFillColorPrimaryBrush")
            }
        };
        Grid.SetColumn(account, 5);
        grid.Children.Add(account);

        border.Child = grid;
        return border;
    }

    private FrameworkElement BuildCategoryPane()
    {
        var pane = new Border
        {
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background = ThemeBrush("SidebarBackground")
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _categoryPanel = new StackPanel
        {
            Spacing = 8,
            Padding = _settings.IsSidebarCollapsed
                ? new Thickness(8, 26, 8, 12)
                : new Thickness(10, 26, 10, 12)
        };

        foreach (var category in ToolCatalog.Categories)
        {
            _categoryPanel.Children.Add(CategoryButton(category));
        }

        layout.Children.Add(_categoryPanel);

        var collapse = CategoryButton(new ToolCategory(
            "collapse",
            _settings.IsSidebarCollapsed ? "Category_Expand" : "Category_Collapse",
            _settings.IsSidebarCollapsed ? "\uE76C" : "\uE96F"));
        collapse.Margin = _settings.IsSidebarCollapsed
            ? new Thickness(8, 0, 8, 22)
            : new Thickness(10, 0, 10, 22);
        Grid.SetRow(collapse, 1);
        layout.Children.Add(collapse);

        pane.Child = layout;
        return pane;
    }

    private Button CategoryButton(ToolCategory category)
    {
        var selected = _selectedCategoryId == category.Id;
        var button = new Button
        {
            Tag = category.Id,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = 46,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, L(category.NameKey));
        AutomationProperties.SetName(button, L(category.NameKey));

        var navShell = new Border
        {
            Height = 46,
            CornerRadius = new CornerRadius(6),
            Background = selected ? ThemeBrush("SelectedNavigationBackground") : new SolidColorBrush(Colors.Transparent)
        };
        var navGrid = new Grid
        {
            ColumnSpacing = _settings.IsSidebarCollapsed ? 0 : 12
        };
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = _settings.IsSidebarCollapsed
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto
        });
        if (!_settings.IsSidebarCollapsed)
        {
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        navGrid.Children.Add(new Border
        {
            Width = 4,
            Height = 30,
            CornerRadius = new CornerRadius(2),
            Background = selected ? ThemeBrush("AccentFillColorDefaultBrush") : new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center
        });
        var icon = new FontIcon
        {
            Glyph = category.IconGlyph,
            FontSize = 20,
            Foreground = selected ? ThemeBrush("AccentTextFillColorPrimaryBrush") : ThemeBrush("NavigationIconBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = _settings.IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };
        Grid.SetColumn(icon, 1);
        navGrid.Children.Add(icon);
        if (!_settings.IsSidebarCollapsed)
        {
            var label = new TextBlock
            {
                Text = L(category.NameKey),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 15,
                Foreground = selected ? ThemeBrush("AccentTextFillColorPrimaryBrush") : ThemeBrush("TextFillColorPrimaryBrush")
            };
            Grid.SetColumn(label, 2);
            navGrid.Children.Add(label);
        }
        navShell.Child = navGrid;
        button.Content = navShell;

        button.Click += (_, _) =>
        {
            if (category.Id is "collapse")
            {
                ToggleSidebar();
                return;
            }

            if (category.Id is "settings")
            {
                SelectTool(ToolCatalog.Tools.First(tool => tool.Id == "settings"));
                return;
            }

            if (category.Id is "diagnostics")
            {
                SelectTool(ToolCatalog.Tools.First(tool => tool.Id == "diagnostics"));
                return;
            }

            _selectedCategoryId = category.Id;
            RefreshCategories();
            RefreshToolGrid();
        };

        return button;
    }

    private FrameworkElement BuildToolArea()
    {
        var root = new Grid
        {
            Padding = new Thickness(26, 28, 26, 24),
            RowSpacing = 18
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };

        _pageTitle = new TextBlock
        {
            Text = L(CategoryNameKey(_selectedCategoryId)),
            FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_pageTitle);
        _resultSummary = new TextBlock
        {
            Text = string.Empty,
            FontSize = 13,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        };
        titleStack.Children.Add(_resultSummary);
        header.Children.Add(titleStack);

        var viewToggle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        var gridViewButton = SmallToolbarButton("\uECA5", L("View_Grid"), _viewMode == ToolViewMode.Grid);
        gridViewButton.Click += (_, _) => SetViewMode(ToolViewMode.Grid);
        viewToggle.Children.Add(gridViewButton);
        var listViewButton = SmallToolbarButton("\uE8FD", L("View_List"), _viewMode == ToolViewMode.List);
        listViewButton.Click += (_, _) => SetViewMode(ToolViewMode.List);
        viewToggle.Children.Add(listViewButton);
        Grid.SetColumn(viewToggle, 1);
        header.Children.Add(viewToggle);

        var sort = BuildSortButton();
        sort.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(sort, 2);
        header.Children.Add(sort);

        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, -10, 0)
        };

        _toolGrid = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 12
        };
        scroll.Content = new Border
        {
            Padding = new Thickness(0, 0, 18, 0),
            Child = _toolGrid
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        RefreshToolGrid();
        return root;
    }

    private Border BuildDetailPanel()
    {
        var panel = new Border
        {
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Background = ThemeBrush("DetailBackground"),
            Padding = new Thickness(28, 62, 28, 28)
        };

        panel.Child = BuildCurrentDetailContent();
        return panel;
    }

    private UIElement BuildCurrentDetailContent()
    {
        return _hasVisibleTools ? BuildDetailContent() : BuildEmptyDetailContent();
    }

    private UIElement BuildEmptyDetailContent()
    {
        var stack = new StackPanel
        {
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Top
        };

        stack.Children.Add(new Border
        {
            Width = 82,
            Height = 82,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("IconTileBackground"),
            Child = new FontIcon
            {
                Glyph = "\uE721",
                FontSize = 34,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        stack.Children.Add(new TextBlock
        {
            Text = L("Search_NoResultsTitle"),
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            Margin = new Thickness(0, 6, 0, 0)
        });

        stack.Children.Add(new TextBlock
        {
            Text = L("Search_NoResultsDescription"),
            FontSize = 14,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.WrapWholeWords,
            LineHeight = 21
        });

        var clear = new Button
        {
            Content = L("Search_Clear"),
            IsEnabled = !string.IsNullOrWhiteSpace(_searchText),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 12, 16, 12),
            Background = ThemeBrush("ControlSurface"),
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 16, 0, 0)
        };
        clear.Click += (_, _) => ClearSearch();
        stack.Children.Add(clear);

        return stack;
    }

    private UIElement BuildDetailContent()
    {
        var tool = _selectedTool;
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(IconTile(tool, 82, 38));

        stack.Children.Add(new TextBlock
        {
            Text = L(tool.NameKey),
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            Margin = new Thickness(0, 6, 0, 0)
        });

        stack.Children.Add(StatusPill(tool.Status));

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = ThemeBrush("DividerStrokeColorDefaultBrush"),
            Margin = new Thickness(0, 2, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = L(tool.DescriptionKey),
            FontSize = 14,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.WrapWholeWords,
            LineHeight = 21
        });

        var primary = new Button
        {
            Content = L(tool.PrimaryAction.LabelKey),
            IsEnabled = tool.PrimaryAction.Enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 12, 16, 12),
            Background = tool.Status == ToolStatus.Available ? ThemeBrush("AccentFillColorDefaultBrush") : ThemeBrush("DisabledButtonBackground"),
            Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 20, 0, 0)
        };
        primary.Click += async (_, _) => await ExecutePrimaryActionAsync(tool);
        stack.Children.Add(primary);

        var secondary = new Button
        {
            Content = PinActionLabel(tool),
            IsEnabled = CanPinTool(tool),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 10, 16, 10),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        secondary.Click += (_, _) => TogglePinnedTool(tool);
        stack.Children.Add(secondary);

        stack.Children.Add(BuildDetailRows(tool));

        return stack;
    }

    private UIElement BuildDetailRows(ToolCard tool)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 18, 0, 0),
            RowSpacing = 12
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddDetailRow(grid, 0, L("Detail_Category"), L(CategoryNameKey(tool.CategoryId)));
        AddDetailRow(grid, 1, L("Detail_Version"), "1.0.0");
        AddDetailRow(grid, 2, L("Detail_Updated"), DateTime.Now.ToString("yyyy-MM-dd"));
        AddDetailRow(grid, 3, L("Detail_Publisher"), "Fowan");
        AddDetailRow(grid, 4, L("Detail_RequiredCapabilities"),
            tool.RequiredCapabilities.Count == 0 ? "-" : string.Join(", ", tool.RequiredCapabilities));
        AddDetailRow(grid, 5, L("Detail_RecentActivity"),
            _captures.Count == 0 ? L("Detail_NoRecentActivity") : $"{_captures.Count} capture(s)");

        return grid;
    }

    private static void AddDetailRow(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
    }

    private void RefreshCategories()
    {
        _categoryPanel.Children.Clear();
        foreach (var category in ToolCatalog.Categories)
        {
            _categoryPanel.Children.Add(CategoryButton(category));
        }
        _pageTitle.Text = L(CategoryNameKey(_selectedCategoryId));
    }

    private void RefreshToolGrid()
    {
        if (_toolGrid is null)
        {
            return;
        }

        _toolGrid.Children.Clear();
        _toolGrid.ColumnDefinitions.Clear();
        _toolGrid.RowDefinitions.Clear();

        var tools = CurrentTools().ToList();
        _hasVisibleTools = tools.Count > 0;
        if (_resultSummary is not null)
        {
            _resultSummary.Text = string.Format(L("Search_ResultCount"), tools.Count);
        }

        if (tools.Count == 0)
        {
            _toolGrid.Children.Add(new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(4, 20, 0, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = L("Empty_NoTools"),
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = ThemeBrush("TextFillColorPrimaryBrush")
                    },
                    new TextBlock
                    {
                        Text = L("Search_NoResultsDescription"),
                        FontSize = 14,
                        Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                        TextWrapping = TextWrapping.WrapWholeWords
                    }
                }
            });
            if (_detailPanel.Child is not null)
            {
                _detailPanel.Child = BuildCurrentDetailContent();
            }
            return;
        }

        if (tools.All(tool => tool.Id != _selectedTool.Id))
        {
            _selectedTool = tools[0];
        }

        if (_detailPanel.Child is not null)
        {
            _detailPanel.Child = BuildCurrentDetailContent();
        }

        var columns = _viewMode == ToolViewMode.Grid ? 3 : 1;
        for (var i = 0; i < columns; i++)
        {
            _toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var rows = (int)Math.Ceiling(tools.Count / (double)columns);
        for (var i = 0; i < rows; i++)
        {
            _toolGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < tools.Count; index++)
        {
            var card = _viewMode == ToolViewMode.Grid
                ? BuildToolCard(tools[index])
                : BuildToolListItem(tools[index]);
            Grid.SetColumn(card, index % columns);
            Grid.SetRow(card, index / columns);
            _toolGrid.Children.Add(card);
        }
    }

    private IEnumerable<ToolCard> CurrentTools()
    {
        var query = _searchText;
        var tools = ToolCatalog.Tools
            .Where(tool => tool.Id != "toolbox-home")
            .Where(tool => _selectedCategoryId == "all" || tool.CategoryId == _selectedCategoryId)
            .Where(tool =>
                string.IsNullOrWhiteSpace(query) ||
                L(tool.NameKey).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                L(tool.DescriptionKey).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                L(CategoryNameKey(tool.CategoryId)).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                StatusText(tool.Status).Contains(query, StringComparison.CurrentCultureIgnoreCase));

        return _sortMode switch
        {
            ToolSortMode.Status => tools
                .OrderBy(AvailabilitySortKey)
                .ThenBy(PinnedSortKey)
                .ThenBy(tool => tool.Status)
                .ThenBy(tool => L(tool.NameKey), StringComparer.CurrentCultureIgnoreCase),
            ToolSortMode.Category => tools
                .OrderBy(AvailabilitySortKey)
                .ThenBy(PinnedSortKey)
                .ThenBy(tool => L(CategoryNameKey(tool.CategoryId)), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(tool => L(tool.NameKey), StringComparer.CurrentCultureIgnoreCase),
            _ => tools
                .OrderBy(AvailabilitySortKey)
                .ThenBy(PinnedSortKey)
                .ThenBy(tool => L(tool.NameKey), StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private FrameworkElement BuildToolCard(ToolCard tool)
    {
        var selected = tool.Id == _selectedTool.Id;
        var button = new Button
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            MinHeight = 140,
            UseSystemFocusVisuals = false
        };
        ConfigureToolCardButton(button);
        button.Click += (_, _) => SelectTool(tool);
        button.KeyDown += async (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Enter && tool.Status == ToolStatus.Available)
            {
                args.Handled = true;
                await ExecutePrimaryActionAsync(tool);
            }
        };

        AutomationProperties.SetName(button, string.Format(
            L("Accessibility_ToolCard"),
            L(tool.NameKey),
            StatusText(tool.Status),
            L(tool.DescriptionKey)));

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = selected ? new Thickness(2) : new Thickness(1),
            BorderBrush = selected ? ThemeBrush("AccentStrokeColorDefaultBrush") : ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 0)
        };

        var grid = new Grid();

        var stack = new StackPanel { Spacing = 11 };
        stack.Children.Add(IconTile(tool, 58, 28));
        stack.Children.Add(new TextBlock
        {
            Text = L(tool.NameKey),
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        stack.Children.Add(StatusPill(tool.Status));
        grid.Children.Add(stack);

        border.Child = grid;
        ApplyToolCardHoverBorder(button, border, selected);
        button.Content = border;

        var host = new Grid();
        host.Children.Add(button);
        if (CanPinTool(tool))
        {
            host.Children.Add(BuildToolPinButton(tool, new Thickness(0, 10, 10, 0)));
        }

        return host;
    }

    private FrameworkElement BuildToolListItem(ToolCard tool)
    {
        var selected = tool.Id == _selectedTool.Id;
        var button = new Button
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            MinHeight = 86,
            UseSystemFocusVisuals = false
        };
        ConfigureToolCardButton(button);
        button.Click += (_, _) => SelectTool(tool);
        button.KeyDown += async (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Enter && tool.Status == ToolStatus.Available)
            {
                args.Handled = true;
                await ExecutePrimaryActionAsync(tool);
            }
        };

        AutomationProperties.SetName(button, string.Format(
            L("Accessibility_ToolCard"),
            L(tool.NameKey),
            StatusText(tool.Status),
            L(tool.DescriptionKey)));

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = selected ? new Thickness(2) : new Thickness(1),
            BorderBrush = selected ? ThemeBrush("AccentStrokeColorDefaultBrush") : ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            Padding = new Thickness(14)
        };

        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(IconTile(tool, 48, 23));

        var textStack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        textStack.Children.Add(new TextBlock
        {
            Text = L(tool.NameKey),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush")
        });
        textStack.Children.Add(new TextBlock
        {
            Text = L(tool.DescriptionKey),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var rightStack = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = CanPinTool(tool) ? new Thickness(0, 0, 40, 0) : new Thickness(0)
        };
        rightStack.Children.Add(StatusPill(tool.Status));
        if (selected)
        {
            rightStack.Children.Add(new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 13,
                Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right
            });
        }
        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);

        border.Child = grid;
        ApplyToolCardHoverBorder(button, border, selected);
        button.Content = border;

        var host = new Grid();
        host.Children.Add(button);
        if (CanPinTool(tool))
        {
            host.Children.Add(BuildToolPinButton(tool, new Thickness(0, 12, 12, 0)));
        }

        return host;
    }

    private Button BuildToolPinButton(ToolCard tool, Thickness margin)
    {
        var pinned = IsPinnedTool(tool);
        var label = PinActionLabel(tool);
        var button = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = pinned ? ThemeBrush("AccentStrokeColorDefaultBrush") : ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = pinned ? ThemeBrush("SelectedNavigationBackground") : ThemeBrush("ControlSurface"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = margin,
            Content = new FontIcon
            {
                Glyph = "\uE840",
                FontSize = 14,
                Foreground = pinned ? ThemeBrush("AccentTextFillColorPrimaryBrush") : ThemeBrush("TextFillColorSecondaryBrush")
            }
        };
        ConfigurePinButton(button);
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, $"{label}: {L(tool.NameKey)}");
        button.Click += (_, _) => TogglePinnedTool(tool);
        return button;
    }

    private static int AvailabilitySortKey(ToolCard tool)
    {
        return tool.Status == ToolStatus.Available ? 0 : 1;
    }

    private int PinnedSortKey(ToolCard tool)
    {
        if (!CanPinTool(tool))
        {
            return int.MaxValue;
        }

        var index = _pinnedToolIds.IndexOf(tool.Id);
        return index >= 0 ? index : int.MaxValue;
    }

    private bool CanPinTool(ToolCard tool)
    {
        return tool.Status == ToolStatus.Available;
    }

    private bool IsPinnedTool(ToolCard tool)
    {
        return CanPinTool(tool) && _pinnedToolIds.Contains(tool.Id);
    }

    private string PinActionLabel(ToolCard tool)
    {
        if (!CanPinTool(tool))
        {
            return L("Action_PinUnavailable");
        }

        return IsPinnedTool(tool) ? L("Action_UnpinFromTop") : L("Action_PinToTop");
    }

    private void TogglePinnedTool(ToolCard tool)
    {
        if (!CanPinTool(tool))
        {
            return;
        }

        if (!_pinnedToolIds.Remove(tool.Id))
        {
            _pinnedToolIds.Insert(0, tool.Id);
        }

        RefreshToolGrid();
    }

    private void ConfigureToolCardButton(Button button)
    {
        var transparent = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBackground"] = transparent;
        button.Resources["ButtonBackgroundPointerOver"] = transparent;
        button.Resources["ButtonBackgroundPressed"] = transparent;
        button.Resources["ButtonBackgroundDisabled"] = transparent;
        button.Resources["ButtonBorderBrush"] = transparent;
        button.Resources["ButtonBorderBrushPointerOver"] = transparent;
        button.Resources["ButtonBorderBrushPressed"] = transparent;
        button.Resources["ButtonBorderBrushDisabled"] = transparent;
        button.Resources["FocusVisualPrimaryBrush"] = transparent;
        button.Resources["FocusVisualSecondaryBrush"] = transparent;
    }

    private void ConfigurePinButton(Button button)
    {
        button.Resources["ButtonBackground"] = button.Background;
        button.Resources["ButtonBackgroundPointerOver"] = ThemeBrush("ToolCardHoverBackgroundBrush");
        button.Resources["ButtonBackgroundPressed"] = ThemeBrush("SelectedNavigationBackground");
        button.Resources["ButtonBorderBrush"] = button.BorderBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = ThemeBrush("ToolCardHoverStrokeBrush");
        button.Resources["ButtonBorderBrushPressed"] = ThemeBrush("AccentStrokeColorDefaultBrush");
        button.Resources["FocusVisualPrimaryBrush"] = ThemeBrush("ToolCardHoverStrokeBrush");
        button.Resources["FocusVisualSecondaryBrush"] = new SolidColorBrush(Colors.Transparent);
    }

    private void ApplyToolCardHoverBorder(Button button, Border border, bool selected)
    {
        var defaultBrush = selected ? ThemeBrush("AccentStrokeColorDefaultBrush") : ThemeBrush("CardStrokeColorDefaultBrush");
        var hoverBrush = ThemeBrush("ToolCardHoverStrokeBrush");
        var defaultBackground = ThemeBrush("CardBackgroundFillColorDefaultBrush");
        var hoverBackground = ThemeBrush("ToolCardHoverBackgroundBrush");
        border.BorderBrush = defaultBrush;
        border.Background = defaultBackground;

        void SetHoverBorder()
        {
            border.BorderBrush = hoverBrush;
            border.Background = hoverBackground;
        }

        void SetDefaultBorder()
        {
            border.BorderBrush = defaultBrush;
            border.Background = defaultBackground;
        }

        var hoverHandler = new PointerEventHandler((_, _) => SetHoverBorder());
        var exitHandler = new PointerEventHandler((_, _) => SetDefaultBorder());
        button.AddHandler(UIElement.PointerEnteredEvent, hoverHandler, true);
        button.AddHandler(UIElement.PointerMovedEvent, hoverHandler, true);
        button.AddHandler(UIElement.PointerExitedEvent, exitHandler, true);
        border.AddHandler(UIElement.PointerEnteredEvent, hoverHandler, true);
        border.AddHandler(UIElement.PointerMovedEvent, hoverHandler, true);
        border.AddHandler(UIElement.PointerExitedEvent, exitHandler, true);
    }

    private FrameworkElement StatusPill(ToolStatus status)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = StatusBrush(status),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = StatusText(status),
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14
        });
        return stack;
    }

    private SolidColorBrush StatusBrush(ToolStatus status) => status switch
    {
        ToolStatus.Available => new SolidColorBrush(Colors.LimeGreen),
        ToolStatus.Disabled => new SolidColorBrush(Colors.DimGray),
        ToolStatus.RequiresEngine => new SolidColorBrush(Colors.Orange),
        ToolStatus.RequiresSignIn => new SolidColorBrush(Colors.DodgerBlue),
        _ => new SolidColorBrush(Colors.DarkGray)
    };

    private string StatusText(ToolStatus status) => status switch
    {
        ToolStatus.Available => L("Status_Available"),
        ToolStatus.Disabled => L("Status_Disabled"),
        ToolStatus.RequiresEngine => L("Status_RequiresEngine"),
        ToolStatus.RequiresSignIn => L("Status_RequiresSignIn"),
        _ => L("Status_Planned")
    };

    private void SelectTool(ToolCard tool)
    {
        _selectedTool = tool;
        RefreshToolGrid();
        _detailPanel.Child = BuildDetailContent();
    }

    private async Task ExecutePrimaryActionAsync(ToolCard tool)
    {
        if (tool.Status != ToolStatus.Available)
        {
            return;
        }

        switch (tool.Id)
        {
            case "quick-capture":
                await ShowQuickCaptureDialogAsync();
                break;
            case "settings":
                await ShowSettingsDialogAsync();
                break;
            case "diagnostics":
                SelectTool(tool);
                break;
            case "toolbox-home":
                _selectedCategoryId = "all";
                RefreshCategories();
                RefreshToolGrid();
                break;
        }
    }

    private void ToggleSidebar()
    {
        _userSettings.IsSidebarCollapsed = !_settings.IsSidebarCollapsed;
        SaveUserSettingsAndRebuild();
    }

    private void ClearSearch()
    {
        _searchText = string.Empty;
        if (!string.IsNullOrEmpty(_searchBox.Text))
        {
            _searchBox.Text = string.Empty;
        }
        else
        {
            RefreshToolGrid();
        }

        _searchBox.Focus(FocusState.Programmatic);
    }

    private void SetViewMode(ToolViewMode viewMode)
    {
        if (_viewMode == viewMode)
        {
            return;
        }

        _viewMode = viewMode;
        BuildShell();
    }

    private void SetSortMode(ToolSortMode sortMode)
    {
        if (_sortMode == sortMode)
        {
            return;
        }

        _sortMode = sortMode;
        BuildShell();
    }

    private void SetWorkspace(string workspaceId)
    {
        var workspace = _userSettings.Workspaces.FirstOrDefault(item => item.Id == workspaceId);
        if (workspace is null || _userSettings.WorkspaceId == workspace.Id)
        {
            return;
        }

        _userSettings.WorkspaceId = workspace.Id;
        var message = string.Format(L("Workspace_Switched"), WorkspaceName(workspace));
        SaveUserSettingsAndRebuild();
        ShowInfo(message, InfoBarSeverity.Success);
    }

    private void SaveUserSettingsAndRebuild()
    {
        _workspaceService.EnsureInitialized(_userSettings);
        _settingsStore.Save(_userSettings);
        _settings = _workspaceService.LoadEffectiveSettings(_userSettings, ensureInitialized: false);
        _loc.SetLanguage(_settings.Language);
        BuildShell();
    }

    private void RegisterShellKeyboardAccelerators()
    {
        var focusSearch = new KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.K,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control
        };
        focusSearch.Invoked += (_, args) =>
        {
            _searchBox.Focus(FocusState.Programmatic);
            _searchBox.SelectAll();
            args.Handled = true;
        };
        _root.KeyboardAccelerators.Add(focusSearch);

        var clearSearch = new KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.Escape
        };
        clearSearch.Invoked += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                ClearSearch();
                args.Handled = true;
            }
        };
        _root.KeyboardAccelerators.Add(clearSearch);
    }

    private async Task ShowCreateWorkspaceDialogAsync()
    {
        var nameBox = new TextBox
        {
            PlaceholderText = L("Workspace_NamePlaceholder"),
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 48,
            FontSize = 18,
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(7),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("ControlSurface")
        };

        var createDirectoryMode = new RadioButton
        {
            Content = L("Workspace_CreateDirectoryModeShort"),
            GroupName = "WorkspaceCreateMode",
            IsChecked = true,
            FontSize = 16,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush")
        };
        var existingDirectoryMode = new RadioButton
        {
            Content = L("Workspace_ExistingDirectoryModeShort"),
            GroupName = "WorkspaceCreateMode",
            FontSize = 16,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush")
        };

        string? selectedDirectory = null;
        var updatingDirectoryText = false;
        var modeCards = new List<Border>();

        var pathLabel = new TextBlock
        {
            Text = L("Workspace_DefaultRootLabel"),
            FontSize = 13,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        };
        var selectedDirectoryBox = new TextBox
        {
            Text = _workspaceService.PreviewNewWorkspaceDisplayPath(nameBox.Text),
            PlaceholderText = L("Workspace_NoFolderSelected"),
            FontSize = 17,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0, 5, 0, 0),
            Height = 40,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyFlatTextBoxStyle(selectedDirectoryBox);
        AutomationProperties.SetName(selectedDirectoryBox, L("Workspace_SelectedFolderLabel"));

        void SetDirectoryText(string value)
        {
            if (selectedDirectoryBox.Text == value)
            {
                return;
            }

            updatingDirectoryText = true;
            selectedDirectoryBox.Text = value;
            updatingDirectoryText = false;
        }

        var chooseFolder = new Button
        {
            Content = L("Workspace_ChooseFolder"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 10, 18, 10),
            CornerRadius = new CornerRadius(6),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("ControlSurface"),
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            Opacity = 0.55
        };
        ToolTipService.SetToolTip(chooseFolder, L("Workspace_ChooseFolder"));
        AutomationProperties.SetName(chooseFolder, L("Workspace_ChooseFolder"));
        chooseFolder.Click += async (_, _) =>
        {
            var folder = await PickWorkspaceFolderAsync();
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            selectedDirectory = folder;
            SetDirectoryText(folder);
            pathLabel.Text = L("Workspace_SelectedFolderLabel");
            existingDirectoryMode.IsChecked = true;
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                nameBox.Text = new DirectoryInfo(folder).Name;
            }
        };

        var pathPreview = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("ControlSurface"),
            Padding = new Thickness(14, 5, 14, 5)
        };
        var pathGrid = new Grid { ColumnSpacing = 14 };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.Children.Add(new FontIcon
        {
            Glyph = "\uE8B7",
            FontSize = 22,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(selectedDirectoryBox, 1);
        pathGrid.Children.Add(selectedDirectoryBox);
        pathPreview.Child = pathGrid;

        var errorText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.WrapWholeWords
        };

        Grid? overlay = null;
        WorkspaceRegistration? createdWorkspace = null;
        var dialogCompletion = new TaskCompletionSource<WorkspaceRegistration?>();

        void CloseOverlay(WorkspaceRegistration? workspace = null)
        {
            if (overlay is not null)
            {
                _root.Children.Remove(overlay);
            }

            if (!dialogCompletion.Task.IsCompleted)
            {
                dialogCompletion.SetResult(workspace);
            }
        }

        var closeButton = HeaderIconButton("\uE711", L("Action_Close"));
        closeButton.Width = 34;
        closeButton.Height = 34;
        closeButton.Click += (_, _) => CloseOverlay();

        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel { Spacing = 8 };
        titleStack.Children.Add(new TextBlock
        {
            Text = L("Workspace_NewTitle"),
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush")
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = L("Workspace_DialogSubtitle"),
            FontSize = 16,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        });
        titleGrid.Children.Add(titleStack);
        Grid.SetColumn(closeButton, 1);
        titleGrid.Children.Add(closeButton);

        var modeGrid = new Grid { ColumnSpacing = 14 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var createModeCard = WorkspaceModeCard(createDirectoryMode);
        var existingModeCard = WorkspaceModeCard(existingDirectoryMode);
        modeCards.Add(createModeCard);
        modeCards.Add(existingModeCard);
        modeGrid.Children.Add(createModeCard);
        Grid.SetColumn(existingModeCard, 1);
        modeGrid.Children.Add(existingModeCard);

        var chooseFolderRow = new Grid();
        chooseFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chooseFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chooseFolder, 1);
        chooseFolderRow.Children.Add(chooseFolder);

        var formStack = new StackPanel { Spacing = 18 };
        formStack.Children.Add(titleGrid);
        formStack.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = L("Workspace_Name"),
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = ThemeBrush("TextFillColorPrimaryBrush")
                },
                nameBox
            }
        });
        formStack.Children.Add(modeGrid);
        formStack.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                pathLabel,
                pathPreview
            }
        });
        formStack.Children.Add(chooseFolderRow);
        formStack.Children.Add(errorText);

        var cancelButton = new Button
        {
            Content = L("Action_Cancel"),
            Padding = new Thickness(26, 12, 26, 12),
            CornerRadius = new CornerRadius(7),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("ControlSurface"),
            Foreground = ThemeBrush("TextFillColorPrimaryBrush")
        };
        cancelButton.Click += (_, _) => CloseOverlay();

        var createButton = new Button
        {
            Content = L("Workspace_Create"),
            Padding = new Thickness(30, 12, 30, 12),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(0),
            Background = ThemeBrush("AccentFillColorDefaultBrush"),
            Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush")
        };

        var footerGrid = new Grid
        {
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(0, 18, 0, 0)
        };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var footerDivider = new Border
        {
            Height = 1,
            Background = ThemeBrush("DividerStrokeColorDefaultBrush"),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumnSpan(footerDivider, 2);
        footerGrid.Children.Add(footerDivider);
        var footerHint = new TextBlock
        {
            Text = L("Workspace_ManifestHint"),
            FontSize = 14,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 18, 12, 0),
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(footerHint, 1);
        footerGrid.Children.Add(footerHint);
        cancelButton.Margin = new Thickness(0, 18, 12, 0);
        Grid.SetRow(cancelButton, 1);
        Grid.SetColumn(cancelButton, 1);
        footerGrid.Children.Add(cancelButton);
        createButton.Margin = new Thickness(0, 18, 0, 0);
        Grid.SetRow(createButton, 1);
        Grid.SetColumn(createButton, 2);
        footerGrid.Children.Add(createButton);

        var dialogContent = new Border
        {
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Padding = new Thickness(28, 26, 28, 24),
            Background = ThemeBrush("DetailBackground"),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    formStack,
                    footerGrid
                }
            }
        };

        void RefreshWorkspaceDialog()
        {
            var useExistingDirectory = existingDirectoryMode.IsChecked == true;
            chooseFolder.Opacity = useExistingDirectory ? 1 : 0.55;
            chooseFolder.Foreground = useExistingDirectory
                ? ThemeBrush("TextFillColorPrimaryBrush")
                : ThemeBrush("TextFillColorSecondaryBrush");

            if (useExistingDirectory)
            {
                pathLabel.Text = L("Workspace_SelectedFolderLabel");
                SetDirectoryText(selectedDirectory ?? string.Empty);
            }
            else
            {
                pathLabel.Text = L("Workspace_DefaultRootLabel");
                SetDirectoryText(_workspaceService.PreviewNewWorkspaceDisplayPath(nameBox.Text));
            }

            createButton.IsEnabled = !useExistingDirectory || !string.IsNullOrWhiteSpace(selectedDirectory);

            foreach (var card in modeCards)
            {
                if (card.Child is RadioButton radio)
                {
                    card.BorderBrush = radio.IsChecked == true
                        ? ThemeBrush("AccentStrokeColorDefaultBrush")
                        : ThemeBrush("CardStrokeColorDefaultBrush");
                    card.Background = radio.IsChecked == true
                        ? ThemeBrush("SelectedNavigationBackground")
                        : ThemeBrush("ControlSurface");
                }
            }
        }

        createDirectoryMode.Checked += (_, _) => RefreshWorkspaceDialog();
        existingDirectoryMode.Checked += (_, _) => RefreshWorkspaceDialog();
        nameBox.TextChanged += (_, _) => RefreshWorkspaceDialog();
        selectedDirectoryBox.TextChanged += (_, _) =>
        {
            if (updatingDirectoryText)
            {
                return;
            }

            selectedDirectory = string.IsNullOrWhiteSpace(selectedDirectoryBox.Text)
                ? null
                : selectedDirectoryBox.Text.Trim().Trim('"');
            if (createDirectoryMode.IsChecked == true)
            {
                existingDirectoryMode.IsChecked = true;
            }

            RefreshWorkspaceDialog();
        };

        createButton.Click += (_, _) =>
        {
            errorText.Visibility = Visibility.Collapsed;
            var workspaceName = nameBox.Text.Trim();
            var useExistingDirectory = existingDirectoryMode.IsChecked == true;

            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                errorText.Text = L("Workspace_NameRequired");
                errorText.Visibility = Visibility.Visible;
                return;
            }

            if (useExistingDirectory && string.IsNullOrWhiteSpace(selectedDirectory))
            {
                errorText.Text = L("Workspace_FolderRequired");
                errorText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                createdWorkspace = _workspaceService.CreateWorkspace(
                    workspaceName,
                    useExistingDirectory ? selectedDirectory : null);
                _workspaceService.RegisterWorkspace(_userSettings, createdWorkspace);
                _settingsStore.Save(_userSettings);
                CloseOverlay(createdWorkspace);
            }
            catch (Exception exception)
            {
                errorText.Text = string.Format(L("Workspace_CreateFailed"), exception.Message);
                errorText.Visibility = Visibility.Visible;
            }
        };

        RefreshWorkspaceDialog();

        overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(118, 0, 0, 0))
        };
        Grid.SetRowSpan(overlay, 2);
        overlay.Children.Add(dialogContent);
        overlay.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Escape)
            {
                CloseOverlay();
                args.Handled = true;
            }
        }), true);

        _root.Children.Add(overlay);
        nameBox.Focus(FocusState.Programmatic);

        createdWorkspace = await dialogCompletion.Task;
        if (createdWorkspace is not null)
        {
            _settings = _workspaceService.LoadEffectiveSettings(_userSettings, ensureInitialized: false);
            _loc.SetLanguage(_settings.Language);
            var message = string.Format(L("Workspace_Created"), WorkspaceName(createdWorkspace));
            BuildShell();
            ShowInfo(message, InfoBarSeverity.Success);
        }

        static Border WorkspaceModeCard(RadioButton radioButton)
        {
            return new Border
            {
                MinHeight = 76,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 8, 18, 8),
                Child = radioButton
            };
        }
    }

    private async Task<string?> PickWorkspaceFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task ShowQuickCaptureDialogAsync()
    {
        var input = new TextBox
        {
            PlaceholderText = L("QuickCapture_Placeholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 130
        };

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(input);
        stack.Children.Add(new TextBlock
        {
            Text = L("QuickCapture_Destination"),
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        });

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = L("QuickCapture_Title"),
            Content = stack,
            PrimaryButtonText = L("Action_Capture"),
            CloseButtonText = L("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            _captures.Add(input.Text.Trim());
            ShowInfo(L("QuickCapture_Saved"), InfoBarSeverity.Success);
            _detailPanel.Child = BuildDetailContent();
        }
    }

    private async Task ShowSettingsDialogAsync()
    {
        var themeBox = new ComboBox { Header = L("Settings_Theme"), MinWidth = 260 };
        themeBox.Items.Add(new ComboBoxItem { Content = L("Settings_Theme_System"), Tag = "system" });
        themeBox.Items.Add(new ComboBoxItem { Content = L("Settings_Theme_Light"), Tag = "light" });
        themeBox.Items.Add(new ComboBoxItem { Content = L("Settings_Theme_Dark"), Tag = "dark" });
        themeBox.SelectedIndex = _settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };

        var languageBox = new ComboBox { Header = L("Settings_Language"), MinWidth = 260 };
        languageBox.Items.Add(new ComboBoxItem { Content = L("Settings_Language_System"), Tag = "system" });
        languageBox.Items.Add(new ComboBoxItem { Content = L("Settings_Language_Chinese"), Tag = "zh-CN" });
        languageBox.Items.Add(new ComboBoxItem { Content = L("Settings_Language_English"), Tag = "en-US" });
        languageBox.SelectedIndex = _settings.Language switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };

        var stack = new StackPanel { Spacing = 18 };
        stack.Children.Add(themeBox);
        stack.Children.Add(languageBox);
        stack.Children.Add(new TextBlock { Text = L("Settings_Startup"), Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
        stack.Children.Add(new TextBlock { Text = L("Settings_Privacy"), Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
        stack.Children.Add(new TextBlock { Text = L("Settings_About"), Foreground = ThemeBrush("TextFillColorSecondaryBrush") });

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = L("Settings_Title"),
            Content = stack,
            PrimaryButtonText = L("Action_Save"),
            CloseButtonText = L("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _userSettings.Theme = ((ComboBoxItem)themeBox.SelectedItem).Tag?.ToString() ?? "system";
        _userSettings.Language = ((ComboBoxItem)languageBox.SelectedItem).Tag?.ToString() ?? "system";
        SaveUserSettingsAndRebuild();
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        _toastText.Text = message;
        _toastHost.Visibility = Visibility.Visible;
        _toastHost.Background = severity switch
        {
            InfoBarSeverity.Error => new SolidColorBrush(ThemeColor("ToastErrorBackground")),
            InfoBarSeverity.Warning => new SolidColorBrush(ThemeColor("ToastWarningBackground")),
            _ => new SolidColorBrush(ThemeColor("ToastBackground"))
        };

        var currentToast = ++_toastVersion;
        _ = Task.Run(async () =>
        {
            await Task.Delay(2800);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (currentToast == _toastVersion)
                {
                    _toastHost.Visibility = Visibility.Collapsed;
                }
            });
        });
    }

    private ElementTheme ResolveTheme() => _settings.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private string CategoryNameKey(string categoryId)
    {
        if (categoryId == "all")
        {
            return "Category_AllTools";
        }

        return ToolCatalog.Categories.FirstOrDefault(category => category.Id == categoryId)?.NameKey ?? "Category_AllTools";
    }

    private static Button IconButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 18 }
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private Button BuildWorkspaceButton()
    {
        var workspace = SelectedWorkspace();
        var button = HeaderPillButton(
            WorkspaceName(workspace),
            "\uE821",
            false,
            "\uE70D",
            L("Workspace_Select"));
        button.MinWidth = 162;

        var flyout = new MenuFlyout();
        foreach (var option in _userSettings.Workspaces)
        {
            var item = new MenuFlyoutItem
            {
                Text = WorkspaceName(option),
                Icon = option.Id == workspace.Id
                    ? new FontIcon { Glyph = "\uE73E" }
                    : new FontIcon { Glyph = "\uE8B7" }
            };
            item.Click += (_, _) => SetWorkspace(option.Id);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var createItem = new MenuFlyoutItem
        {
            Text = L("Workspace_New"),
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        createItem.Click += async (_, _) => await ShowCreateWorkspaceDialogAsync();
        flyout.Items.Add(createItem);

        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private Button BuildEngineStatusButton()
    {
        var button = HeaderPillButton(
            L("Engine_Online"),
            null,
            true,
            "\uE70D",
            L("Engine_StatusDetails"));
        button.MinWidth = 142;

        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Width = 300,
                Spacing = 12,
                Padding = new Thickness(2),
                Children =
                {
                    new TextBlock
                    {
                        Text = L("Engine_StatusTitle"),
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = ThemeBrush("TextFillColorPrimaryBrush")
                    },
                    new TextBlock
                    {
                        Text = L("Engine_MockDescription"),
                        TextWrapping = TextWrapping.WrapWholeWords,
                        Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                        LineHeight = 20
                    },
                    BuildStatusRow(L("Engine_State"), L("Engine_Online")),
                    BuildStatusRow(L("Diagnostics_ProtocolVersion"), L("Mock_ProtocolVersion")),
                    BuildStatusRow(L("Diagnostics_Capabilities"), L("Mock_Capabilities"))
                }
            }
        };

        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private Button BuildSortButton()
    {
        var button = HeaderPillButton(SortLabel(), null, false, "\uE70D", L("Sort_Tooltip"));
        button.MinWidth = 132;

        var flyout = new MenuFlyout();
        AddSortItem(flyout, ToolSortMode.Name, "Sort_Name");
        AddSortItem(flyout, ToolSortMode.Status, "Sort_Status");
        AddSortItem(flyout, ToolSortMode.Category, "Sort_Category");

        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private void AddSortItem(MenuFlyout flyout, ToolSortMode mode, string labelKey)
    {
        var item = new MenuFlyoutItem
        {
            Text = L(labelKey),
            Icon = _sortMode == mode ? new FontIcon { Glyph = "\uE73E" } : null
        };
        item.Click += (_, _) => SetSortMode(mode);
        flyout.Items.Add(item);
    }

    private UIElement BuildStatusRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        });

        var valueBlock = new TextBlock
        {
            Text = value,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush")
        };
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);

        return grid;
    }

    private WorkspaceRegistration SelectedWorkspace()
    {
        return _workspaceService.SelectedWorkspace(_userSettings, ensureInitialized: false);
    }

    private string WorkspaceName(WorkspaceRegistration workspace)
    {
        var name = _workspaceService.WorkspaceDisplayName(workspace);
        if (workspace.Id == WorkspaceService.DefaultWorkspaceId &&
            (string.IsNullOrWhiteSpace(name) ||
             string.Equals(name, "Default Workspace", StringComparison.OrdinalIgnoreCase)))
        {
            return L("Workspace_Default");
        }

        return string.IsNullOrWhiteSpace(name) ? L("Workspace_Default") : name;
    }

    private string SortLabel() => _sortMode switch
    {
        ToolSortMode.Status => L("Sort_Label_Status"),
        ToolSortMode.Category => L("Sort_Label_Category"),
        _ => L("Sort_Label_Name")
    };

    private Button HeaderPillButton(
        string text,
        string? leadingGlyph = null,
        bool showStatusDot = false,
        string? trailingGlyph = null,
        string? automationName = null)
    {
        var button = new Button
        {
            Height = 42,
            MinWidth = 132,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("ControlSurface"),
            Padding = new Thickness(14, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = HeaderPillContent(text, leadingGlyph, showStatusDot, trailingGlyph)
        };
        ToolTipService.SetToolTip(button, automationName ?? text);
        AutomationProperties.SetName(button, automationName ?? text);
        return button;
    }

    private Border HeaderPill(string text, string? leadingGlyph = null, bool showStatusDot = false, string? trailingGlyph = null)
    {
        var border = new Border
        {
            Height = 42,
            MinWidth = 132,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("ControlSurface"),
            Padding = new Thickness(14, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        border.Child = HeaderPillContent(text, leadingGlyph, showStatusDot, trailingGlyph);
        return border;
    }

    private StackPanel HeaderPillContent(string text, string? leadingGlyph, bool showStatusDot, string? trailingGlyph)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (showStatusDot)
        {
            stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = StatusBrush(ToolStatus.Available),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        if (!string.IsNullOrEmpty(leadingGlyph))
        {
            stack.Children.Add(new FontIcon
            {
                Glyph = leadingGlyph,
                FontSize = 15,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (!string.IsNullOrEmpty(trailingGlyph))
        {
            stack.Children.Add(new FontIcon
            {
                Glyph = trailingGlyph,
                FontSize = 12,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            });
        }

        return stack;
    }

    private Button HeaderIconButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 20,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            }
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private Button SmallToolbarButton(string glyph, string label, bool selected)
    {
        var button = new Button
        {
            Width = 42,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = selected ? ThemeBrush("SelectedNavigationBackground") : ThemeBrush("ControlSurface"),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 17,
                Foreground = selected ? ThemeBrush("AccentTextFillColorPrimaryBrush") : ThemeBrush("TextFillColorSecondaryBrush")
            }
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void ApplyFlatTextBoxStyle(TextBox textBox)
    {
        var transparent = new SolidColorBrush(Colors.Transparent);
        textBox.Resources["TextControlBackground"] = transparent;
        textBox.Resources["TextControlBackgroundPointerOver"] = transparent;
        textBox.Resources["TextControlBackgroundFocused"] = transparent;
        textBox.Resources["TextControlBorderBrush"] = transparent;
        textBox.Resources["TextControlBorderBrushPointerOver"] = transparent;
        textBox.Resources["TextControlBorderBrushFocused"] = transparent;
        textBox.Resources["TextControlForeground"] = ThemeBrush("TextFillColorPrimaryBrush");
        textBox.Resources["TextControlForegroundFocused"] = ThemeBrush("TextFillColorPrimaryBrush");
        textBox.Resources["TextControlPlaceholderForeground"] = ThemeBrush("TextFillColorSecondaryBrush");
        textBox.Resources["TextControlPlaceholderForegroundFocused"] = ThemeBrush("TextFillColorSecondaryBrush");
    }

    private Border IconTile(ToolCard tool, double size, double glyphSize)
    {
        return new Border
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("IconTileBackground"),
            Child = new FontIcon
            {
                Glyph = tool.IconGlyph,
                FontSize = glyphSize,
                Foreground = ToolAccentBrush(tool),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Brush ToolAccentBrush(ToolCard tool)
    {
        var color = tool.Id switch
        {
            "todo" => ColorHelper.FromArgb(255, 127, 92, 255),
            "notes" => ColorHelper.FromArgb(255, 255, 174, 36),
            "knowledge" => ColorHelper.FromArgb(255, 45, 194, 154),
            "files" => ColorHelper.FromArgb(255, 47, 140, 255),
            "global-search" => ColorHelper.FromArgb(255, 156, 92, 255),
            "workflows" => ColorHelper.FromArgb(255, 123, 202, 91),
            "ai" => ColorHelper.FromArgb(255, 177, 123, 226),
            "plugins" => ColorHelper.FromArgb(255, 255, 125, 72),
            "settings" => ColorHelper.FromArgb(255, 86, 145, 255),
            "diagnostics" => ColorHelper.FromArgb(255, 42, 190, 178),
            _ => ColorHelper.FromArgb(255, 38, 128, 235)
        };

        return new SolidColorBrush(color);
    }

    private static Uri FileUri(string path)
    {
        return new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Path = Path.GetFullPath(path)
        }.Uri;
    }

    private static Style? ThemeStyle(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out var resource)
            ? resource as Style
            : null;
    }

    private Brush ThemeBrush(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush &&
            !CustomThemeKeys.Contains(resourceKey))
        {
            return brush;
        }

        return new SolidColorBrush(ThemeColor(resourceKey));
    }

    private static readonly HashSet<string> CustomThemeKeys = new(StringComparer.Ordinal)
    {
        "AppBackground",
        "TopBarBackground",
        "SidebarBackground",
        "DetailBackground",
        "ControlSurface",
        "SelectedNavigationBackground",
        "NavigationIconBrush",
        "IconTileBackground",
        "AvatarBackground",
        "DisabledButtonBackground",
        "ToastBackground",
        "ToastWarningBackground",
        "ToastErrorBackground",
        "ApplicationPageBackgroundThemeBrush",
        "LayerFillColorDefaultBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush",
        "ToolCardHoverStrokeBrush",
        "ToolCardHoverBackgroundBrush",
        "DividerStrokeColorDefaultBrush",
        "AccentFillColorDefaultBrush",
        "AccentStrokeColorDefaultBrush",
        "AccentTextFillColorPrimaryBrush",
        "TextOnAccentFillColorPrimaryBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "ControlFillColorSecondaryBrush"
    };

    private Color ThemeColor(string resourceKey)
    {
        var dark = IsDarkTheme();
        return resourceKey switch
        {
            "AppBackground" or "ApplicationPageBackgroundThemeBrush" => dark ? C(0x0F141B) : C(0xF8F9FB),
            "TopBarBackground" => dark ? C(0x10161D) : C(0xFFFFFF),
            "SidebarBackground" or "LayerFillColorDefaultBrush" => dark ? C(0x10171F) : C(0xFAFBFD),
            "DetailBackground" => dark ? C(0x161D26) : C(0xFFFFFF),
            "ControlSurface" => dark ? C(0x151C25) : C(0xFFFFFF),
            "SelectedNavigationBackground" => dark ? C(0x1A2635) : C(0xECF5FF),
            "NavigationIconBrush" => dark ? C(0xC5CEDA) : C(0x4D5563),
            "IconTileBackground" => dark ? C(0x1A222D) : C(0xFBFCFE),
            "AvatarBackground" or "ControlFillColorSecondaryBrush" => dark ? C(0x2A3442) : C(0xE8ECF2),
            "DisabledButtonBackground" => dark ? C(0x25303C) : C(0xD8DEE7),
            "ToastBackground" => dark ? C(0x17251F) : C(0xEEF8F2),
            "ToastWarningBackground" => dark ? C(0x2A2415) : C(0xFFF7E2),
            "ToastErrorBackground" => dark ? C(0x2A181B) : C(0xFDEEEE),
            "CardBackgroundFillColorDefaultBrush" => dark ? C(0x171E27) : C(0xFFFFFF),
            "CardStrokeColorDefaultBrush" => dark ? C(0x303945) : C(0xDDE3EA),
            "ToolCardHoverStrokeBrush" => dark ? C(150, 0xB8D9FF) : C(160, 0x70B6F2),
            "ToolCardHoverBackgroundBrush" => dark ? C(28, 0x58A6FF) : C(22, 0xD6ECFF),
            "DividerStrokeColorDefaultBrush" => dark ? C(0x29323D) : C(0xE1E6EE),
            "AccentFillColorDefaultBrush" or "AccentStrokeColorDefaultBrush" or "AccentTextFillColorPrimaryBrush" => C(0x2A82F3),
            "TextOnAccentFillColorPrimaryBrush" => C(0xFFFFFF),
            "TextFillColorPrimaryBrush" => dark ? C(0xF5F7FA) : C(0x151A21),
            "TextFillColorSecondaryBrush" => dark ? C(0xA7B0BE) : C(0x606A78),
            _ => dark ? C(0x303945) : C(0xE2E7EF)
        };
    }

    private bool IsDarkTheme()
    {
        return _settings.Theme switch
        {
            "dark" => true,
            "light" => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
        };
    }

    private static Color C(uint rgb)
    {
        return ColorHelper.FromArgb(
            255,
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }

    private static Color C(byte alpha, uint rgb)
    {
        return ColorHelper.FromArgb(
            alpha,
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }

    private enum ToolViewMode
    {
        Grid,
        List
    }

    private enum ToolSortMode
    {
        Name,
        Status,
        Category
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
