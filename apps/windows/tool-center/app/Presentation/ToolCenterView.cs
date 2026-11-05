using Fowan.ToolCenter.Windows.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Fowan.ToolCenter.Windows.Presentation;

internal sealed class ToolCenterView : Grid
{
    private readonly ToolCenterSession _session;
    private readonly Grid _cards = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly StackPanel _detail = new() { Spacing = 16 };
    private readonly StackPanel _empty = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _count;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyDescription;
    private readonly Button _clearSearch;
    private readonly Grid _catalogContent;
    private readonly Border _detailsHost;
    private readonly Dictionary<string, RadioButton> _categories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _applicationButtons = new(StringComparer.Ordinal);
    private string _categoryId = "all";
    private string? _selectedId;
    private int _columnCount = 2;
    private bool _compactDetails;
    private bool _attached;
    private ToolCenterManagementPanel? _managementPanel;
    private TextBlock? _yymCardStatus;
    private readonly Func<Task<string?>> _pickInstallParent;

    public ToolCenterView(ToolCenterSession session, Func<Task<string?>> pickInstallParent)
    {
        _session = session;
        _pickInstallParent = pickInstallParent;
        RequestedTheme = session.Preferences.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        Background = Brush("ApplicationPageBackgroundThemeBrush");
        ColumnDefinitions.Add(new() { Width = new GridLength(188) });
        ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });

        var navigation = new StackPanel { Spacing = 12, Margin = new Thickness(16, 28, 16, 20) };
        navigation.Children.Add(Label("Fowan", 25, strong: true));
        navigation.Children.Add(Label(T("Center_Title"), 13, secondary: true));
        navigation.Children.Add(new Border { Height = 14 });
        foreach (var id in new[] { "all" }.Concat(session.Applications.Select(app => app.CategoryId).Distinct()))
        {
            var button = new RadioButton
            {
                Content = T($"Category_{id}"), GroupName = "ToolCenterCategories",
                HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40
            };
            AutomationProperties.SetName(button, T($"Category_{id}"));
            button.Checked += (_, _) => { _categoryId = id; Refresh(); };
            _categories.Add(id, button);
            navigation.Children.Add(button);
        }
        Children.Add(navigation);

        var body = new Grid { Margin = new Thickness(28, 28, 28, 24), RowSpacing = 24 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        SetColumn(body, 1);
        Children.Add(body);

        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(Label(T("Center_Title"), 28, strong: true));
        heading.Children.Add(Label(T("Center_Subtitle"), 16));
        heading.Children.Add(Label(T("Center_Introduction"), 13, secondary: true));
        body.Children.Add(heading);

        var toolbar = new Grid { ColumnSpacing = 20 };
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var titles = new StackPanel { Spacing = 5 };
        titles.Children.Add(Label(T("Center_Applications"), 18, strong: true));
        _count = Label(string.Empty, 12, secondary: true);
        AutomationProperties.SetLiveSetting(_count, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        titles.Children.Add(_count);
        toolbar.Children.Add(titles);
        _search.PlaceholderText = T("Center_Search");
        _search.IsEnabled = !session.Applications.IsEmpty;
        _search.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(_search, T("Center_Search"));
        _search.TextChanged += (_, _) => Refresh();
        SetColumn(_search, 1);
        toolbar.Children.Add(_search);
        SetRow(toolbar, 1);
        body.Children.Add(toolbar);

        var content = _catalogContent = new Grid { ColumnSpacing = 24, RowSpacing = 16 };
        content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = new GridLength(360) });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var applications = new Grid();
        var scroll = new ScrollViewer
        {
            Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        applications.Children.Add(scroll);
        _empty.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 30, Foreground = Brush("TextFillColorSecondaryBrush") });
        _emptyTitle = Label(string.Empty, 18, strong: true);
        _emptyDescription = Label(string.Empty, 13, secondary: true);
        _emptyTitle.TextAlignment = TextAlignment.Center;
        _emptyDescription.TextAlignment = TextAlignment.Center;
        _empty.Children.Add(_emptyTitle);
        _empty.Children.Add(_emptyDescription);
        var clear = _clearSearch = new Button { Content = T("Center_ClearSearch"), HorizontalAlignment = HorizontalAlignment.Center };
        clear.Click += (_, _) => { _search.Text = string.Empty; _search.Focus(FocusState.Programmatic); };
        _empty.Children.Add(clear);
        applications.Children.Add(_empty);
        content.Children.Add(applications);
        var details = _detailsHost = new Border
        {
            Background = Brush("LayerFillColorDefaultBrush"), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20), BorderBrush = Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        SetColumn(details, 1);
        content.Children.Add(details);
        SetRow(content, 2);
        body.Children.Add(content);
        content.SizeChanged += (_, args) =>
        {
            _compactDetails = args.NewSize.Width < 900;
            UpdateDetailLayout();
        };
        applications.SizeChanged += (_, args) =>
        {
            var columns = args.NewSize.Width < 450 ? 1 : 2;
            if (_columnCount == columns) return;
            _columnCount = columns;
            Refresh();
        };
        AddKeyboardShortcuts();
        _categories["all"].IsChecked = true;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_attached) return;
        _attached = true;
        _session.StateChanged += OnStateChanged;
        await _session.InitializeAsync();
    }

    private void OnStateChanged(object? sender, ToolCenterManagementSnapshot snapshot)
    {
        void RefreshManagement()
        {
            if (!_attached) return;
            if (_yymCardStatus is not null) _yymCardStatus.Text = ManagementStatus();
            _managementPanel?.Refresh();
        }
        if (DispatcherQueue.HasThreadAccess) RefreshManagement();
        else DispatcherQueue.TryEnqueue(RefreshManagement);
    }

    internal void Detach()
    {
        _attached = false;
        Loaded -= OnLoaded;
        _session.StateChanged -= OnStateChanged;
    }

    private void Refresh()
    {
        var apps = _session.Search(_search.Text, _categoryId);
        if (!apps.Any(app => app.Id == _selectedId)) _selectedId = apps.FirstOrDefault()?.Id;
        _count.Text = string.Format(T("Center_Count"), apps.Length);
        _cards.Children.Clear();
        _cards.RowDefinitions.Clear();
        _cards.ColumnDefinitions.Clear();
        _applicationButtons.Clear();
        _yymCardStatus = null;
        var columns = Math.Max(1, Math.Min(_columnCount, apps.Length));
        for (var column = 0; column < columns; column++)
            _cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < apps.Length; index++)
        {
            if (index % columns == 0) _cards.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var app = apps[index];
            var card = BuildCard(app);
            SetColumn(card, index % columns);
            SetRow(card, index / columns);
            _cards.Children.Add(card);
            _applicationButtons.Add(app.Id, card);
        }
        _empty.Visibility = apps.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        _emptyTitle.Text = T(_session.Applications.IsEmpty ? "Center_EmptyTitle" : "Center_NoResults");
        _emptyDescription.Text = T(_session.Applications.IsEmpty ? "Center_EmptyDescription" : "Center_NoResultsDescription");
        _clearSearch.Visibility = string.IsNullOrWhiteSpace(_search.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdateDetailLayout();
        ShowSelection();
    }

    private void UpdateDetailLayout()
    {
        var hasSelection = _applicationButtons.Count > 0;
        _detailsHost.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        _catalogContent.ColumnSpacing = !_compactDetails && hasSelection ? 24 : 0;
        _catalogContent.RowSpacing = _compactDetails && hasSelection ? 16 : 0;
        _catalogContent.ColumnDefinitions[1].Width = !_compactDetails && hasSelection ? new GridLength(360) : new GridLength(0);
        _catalogContent.RowDefinitions[0].Height = _compactDetails && hasSelection ? new GridLength(190) : new GridLength(1, GridUnitType.Star);
        _catalogContent.RowDefinitions[1].Height = _compactDetails && hasSelection ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        SetColumn(_detailsHost, _compactDetails ? 0 : 1);
        SetRow(_detailsHost, _compactDetails ? 1 : 0);
        _detailsHost.MaxHeight = double.PositiveInfinity;
    }

    private Button BuildCard(ToolCenterApplicationSnapshot app)
    {
        var body = new StackPanel { Spacing = 12, MinHeight = 154 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Icon(app.IconGlyph));
        var statusText = Label(app.Id == "yym" ? ManagementStatus() : T("Center_Available"), 12, secondary: true);
        if (app.Id == "yym") _yymCardStatus = statusText;
        var status = new Border
        {
            Background = Brush("ControlFillColorSecondaryBrush"), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Top,
            Child = statusText
        };
        SetColumn(status, 1);
        header.Children.Add(status);
        body.Children.Add(header);
        body.Children.Add(Label(app.Name, 17, strong: true));
        body.Children.Add(Label(app.Description, 13, secondary: true));
        body.Children.Add(Label(app.CategoryName, 12, secondary: true));
        var button = new Button
        {
            Content = body, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(18), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Background = Brush("CardBackgroundFillColorDefaultBrush")
        };
        AutomationProperties.SetName(button, $"{app.Name}, {T("Center_Available")}. {app.Description}");
        button.Click += (_, _) => { _selectedId = app.Id; ShowSelection(); };
        return button;
    }

    private void ShowSelection()
    {
        foreach (var pair in _applicationButtons)
            pair.Value.BorderBrush = Brush(pair.Key == _selectedId ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
        _detail.Children.Clear();
        _managementPanel = null;
        _detail.Children.Add(Label(T("Center_Detail"), 13, secondary: true));
        var app = _session.Applications.FirstOrDefault(item => item.Id == _selectedId);
        if (app is null)
        {
            _detail.Children.Add(Label(T("Center_SelectApplication"), 15));
            return;
        }
        _detail.Children.Add(Icon(app.IconGlyph));
        _detail.Children.Add(Label(app.Name, 23, strong: true));
        _detail.Children.Add(Label(app.Description, 14));
        _detail.Children.Add(Label($"{T("Center_Category")} · {app.CategoryName}", 12, secondary: true));
        if (app.Id == "yym")
        {
            _managementPanel = new ToolCenterManagementPanel(_session, _pickInstallParent);
            _detail.Children.Add(_managementPanel);
        }
    }

    private string ManagementStatus()
    {
        var installation = _session.Management.Installation;
        return T(installation.IsRunning ? "Center_Running" : !installation.IsInstalled ? "Center_NotInstalled" :
            installation.IsHealthy ? "Center_Installed" : "Center_RepairNeeded");
    }

    private void AddKeyboardShortcuts()
    {
        var search = new KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.K,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control
        };
        search.Invoked += (_, args) =>
        {
            if (!_search.IsEnabled) return;
            _search.Focus(FocusState.Keyboard);
            _search.SelectAll();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(search);
        var clear = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
        clear.Invoked += (_, args) =>
        {
            if (_search.Text.Length == 0) return;
            _search.Text = string.Empty;
            args.Handled = true;
        };
        KeyboardAccelerators.Add(clear);
    }

    private string T(string key) => _session.Text(key);
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)global::Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static Border Icon(string glyph) => new()
    {
        Width = 42, Height = 42, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left,
        Background = Brush("AccentFillColorDefaultBrush"),
        Child = new FontIcon { Glyph = glyph, FontSize = 21, Foreground = Brush("TextOnAccentFillColorPrimaryBrush") }
    };
    private static TextBlock Label(string text, double size, bool strong = false, bool secondary = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Foreground = Brush(secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush")
    };
}
