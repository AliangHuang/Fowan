using Fowan.Windows.Models;
using Fowan.Windows.Application;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace Fowan.Windows.Presentation;

internal sealed record ToolboxShellElements(
    Grid Root, StackPanel CategoryPanel, Grid ToolGrid, Border DetailPanel,
    Border ToastHost, TextBlock ToastText, TextBox SearchBox, TextBlock PageTitle, TextBlock ResultSummary);

internal sealed class ToolboxShellBuilder(
    Window window,
    Func<ToolboxSnapshot> settings,
    Func<ElementTheme> resolveTheme,
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    ToolboxControlFactory controls,
    Func<string> selectedCategoryId,
    Func<string> searchText,
    Func<bool> isGridView,
    Func<string, string> categoryNameKey,
    Func<Border> buildDetailPanel,
    Func<Button> buildEngineStatus,
    Func<Button> buildSort,
    Func<double, string?, Ellipse> avatarView,
    Action<string> searchChanged,
    Action clearSearch,
    Action<string> selectCategory,
    Action toggleSidebar,
    Action showGridView,
    Action showListView,
    Func<Task> showSettings,
    Func<Task> showProfile)
{
    private const double SidebarExpandedWidth = 238;
    private const double SidebarCollapsedWidth = 76;

    public ToolboxShellElements Build()
    {
        var root = new Grid
        {
            RequestedTheme = resolveTheme(), Background = themeBrush("AppBackground"),
            KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var topBar = BuildTopBar(out var searchBox);
        root.Children.Add(topBar);
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(settings().IsSidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth)
        });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(398) });
        Grid.SetRow(content, 1);
        var categories = BuildCategoryPane(out var categoryPanel);
        content.Children.Add(categories);
        var toolArea = BuildToolArea(out var toolGrid, out var pageTitle, out var resultSummary);
        Grid.SetColumn(toolArea, 1);
        content.Children.Add(toolArea);
        var detail = buildDetailPanel();
        Grid.SetColumn(detail, 2);
        content.Children.Add(detail);
        root.Children.Add(content);
        var toast = BuildToast(out var toastText);
        root.Children.Add(toast);
        window.Content = root;
        return new ToolboxShellElements(root, categoryPanel, toolGrid, detail, toast, toastText,
            searchBox, pageTitle, resultSummary);
    }

    private Border BuildToast(out TextBlock toastText)
    {
        toastText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = themeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center, FontSize = 14
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE73E", FontSize = 15, Foreground = themeBrush("AccentTextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(toastText);
        var host = new Border
        {
            Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 360, Margin = new Thickness(0, 0, 28, 28),
            Padding = new Thickness(14, 10, 16, 10), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = themeBrush("CardStrokeColorDefaultBrush"),
            Background = themeBrush("ToastBackground"), Child = content
        };
        Grid.SetRow(host, 1);
        return host;
    }

    private FrameworkElement BuildTopBar(out TextBox searchBox)
    {
        var border = new Border
        {
            BorderBrush = themeBrush("DividerStrokeColorDefaultBrush"), BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 74, Padding = new Thickness(22, 12, 148, 12), Background = themeBrush("TopBarBackground")
        };
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var dragIcon = new Border
        {
            Width = 30, Height = 30, Background = new SolidColorBrush(Colors.Transparent),
            Child = new Image
            {
                Source = new BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-app-icon-256.png"))),
                Width = 30, Height = 30
            }
        };
        brand.Children.Add(dragIcon);
        window.SetTitleBar(dragIcon);
        brand.Children.Add(new TextBlock
        {
            Text = localize("App_Title"), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = themeBrush("TextFillColorPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center
        });
        grid.Children.Add(brand);
        var searchShell = BuildSearch(out searchBox);
        Grid.SetColumn(searchShell, 1);
        grid.Children.Add(searchShell);
        AddAt(grid, buildEngineStatus(), 2);
        AddAt(grid, buildSort(), 3);
        var settingsButton = controls.HeaderIconButton("\uE713", localize("Tool_Settings"));
        settingsButton.Click += async (_, _) => await showSettings();
        AddAt(grid, settingsButton, 4);
        var account = controls.HeaderIconButton(string.Empty, settings().UserDisplayName);
        account.Width = 46;
        account.Height = 46;
        account.Content = avatarView(42, null);
        account.Click += async (_, _) => await showProfile();
        AutomationProperties.SetName(account, settings().UserDisplayName);
        ToolTipService.SetToolTip(account, settings().UserDisplayName);
        AddAt(grid, account, 5);
        border.Child = grid;
        return border;
    }

    private Border BuildSearch(out TextBox searchBox)
    {
        var shell = new Border
        {
            Height = 42, MinWidth = 420, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            BorderBrush = themeBrush("CardStrokeColorDefaultBrush"), Background = themeBrush("ControlSurface"),
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(14, 0, 12, 0)
        };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon
        {
            Glyph = "\uE721", FontSize = 18, Foreground = themeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var layer = new Grid { Height = 42, VerticalAlignment = VerticalAlignment.Center };
        var placeholder = new TextBlock
        {
            Text = localize("Search_Placeholder"), Foreground = themeBrush("TextFillColorSecondaryBrush"),
            FontSize = 16, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1),
            Visibility = string.IsNullOrWhiteSpace(searchText()) ? Visibility.Visible : Visibility.Collapsed
        };
        var input = new TextBox
        {
            BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent),
            Foreground = themeBrush("TextFillColorPrimaryBrush"), PlaceholderForeground = themeBrush("TextFillColorSecondaryBrush"),
            Padding = new Thickness(0, 7, 0, 0), FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
            Height = 42, MinHeight = 42, Text = searchText()
        };
        searchBox = input;
        controls.ApplyFlatTextBoxStyle(input);
        var clear = controls.HeaderIconButton("\uE711", localize("Search_Clear"));
        clear.Width = 30;
        clear.Height = 30;
        clear.Visibility = string.IsNullOrWhiteSpace(searchText()) ? Visibility.Collapsed : Visibility.Visible;
        clear.Click += (_, _) => clearSearch();
        input.TextChanged += (_, _) =>
        {
            var empty = string.IsNullOrWhiteSpace(input.Text);
            placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            searchChanged(input.Text.Trim());
        };
        input.KeyDown += (_, args) =>
        {
            if (args.Key != global::Windows.System.VirtualKey.Escape || string.IsNullOrWhiteSpace(input.Text)) return;
            clearSearch();
            args.Handled = true;
        };
        layer.Children.Add(input);
        layer.Children.Add(placeholder);
        Grid.SetColumn(layer, 1);
        grid.Children.Add(layer);
        AddAt(grid, clear, 2);
        var shortcut = new TextBlock
        {
            Text = localize("Search_Shortcut"), Foreground = themeBrush("TextFillColorSecondaryBrush"),
            FontSize = 13, VerticalAlignment = VerticalAlignment.Center
        };
        AddAt(grid, shortcut, 3);
        shell.Child = grid;
        return shell;
    }

    private FrameworkElement BuildCategoryPane(out StackPanel categoryPanel)
    {
        var pane = new Border
        {
            BorderBrush = themeBrush("DividerStrokeColorDefaultBrush"), BorderThickness = new Thickness(0, 0, 1, 0),
            Background = themeBrush("SidebarBackground")
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        categoryPanel = new StackPanel
        {
            Spacing = 8, Padding = settings().IsSidebarCollapsed ? new Thickness(8, 26, 8, 12) : new Thickness(10, 26, 10, 12)
        };
        foreach (var category in ToolCatalog.Categories) categoryPanel.Children.Add(CategoryButton(category));
        layout.Children.Add(categoryPanel);
        var collapse = CategoryButton(new ToolCategory(
            "collapse", settings().IsSidebarCollapsed ? "Category_Expand" : "Category_Collapse",
            settings().IsSidebarCollapsed ? "\uE76C" : "\uE96F"));
        collapse.Margin = settings().IsSidebarCollapsed ? new Thickness(8, 0, 8, 22) : new Thickness(10, 0, 10, 22);
        collapse.Click += (_, _) => toggleSidebar();
        Grid.SetRow(collapse, 1);
        layout.Children.Add(collapse);
        pane.Child = layout;
        return pane;
    }

    public Button CategoryButton(ToolCategory category)
    {
        var selected = selectedCategoryId() == category.Id;
        var button = new Button
        {
            Tag = category.Id, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), MinHeight = 46,
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, localize(category.NameKey));
        AutomationProperties.SetName(button, localize(category.NameKey));
        var shell = new Border
        {
            Height = 46, CornerRadius = new CornerRadius(6),
            Background = selected ? themeBrush("SelectedNavigationBackground") : new SolidColorBrush(Colors.Transparent)
        };
        var grid = new Grid { ColumnSpacing = settings().IsSidebarCollapsed ? 0 : 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = settings().IsSidebarCollapsed ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
        });
        if (!settings().IsSidebarCollapsed) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 4, Height = 30, CornerRadius = new CornerRadius(2),
            Background = selected ? themeBrush("AccentFillColorDefaultBrush") : new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center
        });
        var icon = new FontIcon
        {
            Glyph = category.IconGlyph, FontSize = 20,
            Foreground = themeBrush(selected ? "AccentTextFillColorPrimaryBrush" : "NavigationIconBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = settings().IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        if (!settings().IsSidebarCollapsed)
        {
            var label = new TextBlock
            {
                Text = localize(category.NameKey), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 15,
                Foreground = themeBrush(selected ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush")
            };
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);
        }
        shell.Child = grid;
        button.Content = shell;
        if (category.Id != "collapse") button.Click += (_, _) => selectCategory(category.Id);
        return button;
    }

    private FrameworkElement BuildToolArea(out Grid toolGrid, out TextBlock pageTitle, out TextBlock resultSummary)
    {
        var root = new Grid { Padding = new Thickness(26, 28, 26, 24), RowSpacing = 18 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        pageTitle = new TextBlock
        {
            Text = localize(categoryNameKey(selectedCategoryId())), FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = themeBrush("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titles.Children.Add(pageTitle);
        resultSummary = new TextBlock { FontSize = 13, Foreground = themeBrush("TextFillColorSecondaryBrush") };
        titles.Children.Add(resultSummary);
        header.Children.Add(titles);
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var gridButton = controls.SmallToolbarButton("\uECA5", localize("View_Grid"), isGridView());
        gridButton.Click += (_, _) => showGridView();
        toggles.Children.Add(gridButton);
        var listButton = controls.SmallToolbarButton("\uE8FD", localize("View_List"), !isGridView());
        listButton.Click += (_, _) => showListView();
        toggles.Children.Add(listButton);
        AddAt(header, toggles, 1);
        root.Children.Add(header);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, -10, 0)
        };
        toolGrid = new Grid { ColumnSpacing = 14, RowSpacing = 12 };
        scroll.Content = new Border { Padding = new Thickness(0, 0, 18, 0), Child = toolGrid };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        return root;
    }

    private static void AddAt(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static Uri FileUri(string path) => new UriBuilder
    {
        Scheme = Uri.UriSchemeFile, Path = Path.GetFullPath(path)
    }.Uri;
}
