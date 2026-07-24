using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryComposerPresenter(
    Func<DiaryData> data,
    Func<DiarySettings> settings,
    IDiaryCommands commands,
    DiaryThemePalette theme,
    DiaryUiFactory ui,
    Func<DiaryEntry> ensureDraft,
    Func<DiaryEntry?> draftEntry,
    Func<bool> buildingShell,
    Action rebuild,
    Action ensureSelectedEntryVisible,
    Func<Task> acquireWeather,
    Func<Task> acquireLocation,
    Func<Task> showSettings,
    Func<Task> addImage,
    Func<DiaryEntry, Task> showTagPicker,
    Action<Button> showTemplateMenu,
    Func<string, Task> showSearch,
    Action saveDraft)
{
    private const int MaximumBodyLength = 5000;
    private const double MetricStripHeight = 68;
    private const double EditorTextRowHeight = 94;
    private const double EditorCardMinHeight = 184;
    private TextBox? _quickEditor;
    private TextBlock? _quickCharacterCount;
    private Button? _quickSaveButton;
    private string _composeMood = "愉快";
    private string _composeWeather = "多云";
    private string _composeLocation = "上海 · 静安区";

    public string? TagFilter { get; set; }

    public void FocusEditor() => _quickEditor?.Focus(FocusState.Programmatic);

    public FrameworkElement BuildMoodStrip()
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
        return ui.Card(grid, MetricStripHeight);
    }

    public FrameworkElement BuildEditorCard()
    {
        var layout = new Grid { Margin = new Thickness(24, 20, 24, 12) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(EditorTextRowHeight) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _quickEditor = new TextBox
        {
            Text = draftEntry()?.Body ?? string.Empty, PlaceholderText = "开始记录今天的想法...",
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxLength = MaximumBodyLength,
            BorderThickness = new Thickness(0), Background = DiaryUiFactory.TransparentBrush(),
            Foreground = theme.Brush("TextPrimary"), PlaceholderForeground = theme.Brush("TextMuted"), FontSize = 20
        };
        _quickEditor.TextChanged += (_, _) => OnQuickTextChanged();
        layout.Children.Add(_quickEditor);
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28 };
        actions.Children.Add(ToolbarAction("\uE91B", "图片", async _ => await addImage()));
        actions.Children.Add(ToolbarAction("\uE8EC", "标签", async _ => await showTagPicker(ensureDraft())));
        actions.Children.Add(ToolbarAction("\uE8A5", "模板", button => { showTemplateMenu(button); return Task.CompletedTask; }));
        actions.Children.Add(ToolbarAction("\uE721", "搜索", async _ => await showSearch(string.Empty)));
        toolbar.Children.Add(actions);
        _quickSaveButton = ui.SecondaryButton("保存日记");
        _quickSaveButton.Click += (_, _) => saveDraft();
        _quickCharacterCount = ui.Text(string.Empty, 14, "TextSecondary");
        var saveGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center,
            Children = { _quickCharacterCount, _quickSaveButton }
        };
        Grid.SetColumn(saveGroup, 1);
        toolbar.Children.Add(saveGroup);
        var toolbarBorder = new Border
        {
            BorderBrush = theme.Brush("InnerDivider"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 13, 0, 0), Child = toolbar
        };
        Grid.SetRow(toolbarBorder, 1);
        layout.Children.Add(toolbarBorder);
        UpdateQuickEditorState();
        return ui.Card(layout, EditorCardMinHeight);
    }

    public FrameworkElement BuildTagFilters()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(18, 13, 18, 13) };
        row.Children.Add(TagFilterButton("全部", null));
        foreach (var tag in DiaryTags.Names(data())) row.Children.Add(TagFilterButton(tag, tag));
        return ui.Card(row, null);
    }

    public void SetQuickMood(string mood)
    {
        var entry = ensureDraft();
        _composeMood = mood;
        commands.UpdateEntryMetadata(entry.Id, mood: mood);
        rebuild();
    }

    public void SetQuickWeather(string weather, DiaryWeatherDetails? details)
    {
        var entry = ensureDraft();
        _composeWeather = weather;
        commands.UpdateEntryMetadata(entry.Id, weather: weather, weatherDetails: details);
        rebuild();
    }

    public void SetQuickLocation(string location, DiaryLocationDetails? details)
    {
        var entry = ensureDraft();
        _composeLocation = location;
        commands.UpdateEntryMetadata(entry.Id, location: location, locationDetails: details);
        rebuild();
    }

    private FrameworkElement MetricCell(string label, string icon, string value, string? trailing, Action<Button> action, double leftPadding = 28, double rightPadding = 18)
    {
        var button = new Button
        {
            Height = MetricStripHeight, Padding = new Thickness(leftPadding, 0, rightPadding, 0), BorderThickness = new Thickness(0),
            Background = DiaryUiFactory.TransparentBrush(), HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(ui.Text(label, 14, "TextSecondary"));
        row.Children.Add(DiaryUiFactory.IsSegoeGlyph(icon) ? new FontIcon { Glyph = icon, FontSize = 18, Foreground = theme.Brush("TextSecondary") } : ui.EmojiText(icon, 20));
        row.Children.Add(ui.Text(value, 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        content.Children.Add(row);
        if (!string.IsNullOrEmpty(trailing))
        {
            var glyph = new FontIcon { Glyph = trailing, FontSize = 15, Foreground = theme.Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(glyph, 1);
            content.Children.Add(glyph);
        }
        button.Content = content;
        button.Click += (_, _) => action(button);
        return button;
    }

    private FrameworkElement MetricDivider() => new Border { Width = 1, Height = 31, Background = theme.Brush("SoftDivider"), VerticalAlignment = VerticalAlignment.Center };

    private void ShowMoodFlyout(Button anchor)
    {
        var flyout = new MenuFlyout();
        foreach (var mood in DiaryMetadata.MoodOptions)
        {
            var item = new MenuFlyoutItem { Text = mood, Icon = new FontIcon { Glyph = DiaryUiFactory.MoodGlyph(mood), FontSize = 15 } };
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
        var automatic = new MenuFlyoutItem
        {
            Text = "自动获取当前位置天气", Icon = new FontIcon { Glyph = "\uE81E", FontSize = 15 },
            IsEnabled = settings().LocationFeatureEnabled && settings().WeatherFeatureEnabled
        };
        automatic.Click += async (_, _) => await acquireWeather();
        flyout.Items.Add(automatic);
        if (!automatic.IsEnabled)
        {
            var configure = new MenuFlyoutItem { Text = "在设置中启用自动天气" };
            configure.Click += async (_, _) => await showSettings();
            flyout.Items.Add(configure);
        }
        flyout.ShowAt(anchor);
    }

    private void ShowLocationFlyout(Button anchor)
    {
        var locationBox = new TextBox { Text = ComposeLocation == "待补充" ? string.Empty : ComposeLocation, PlaceholderText = "输入地点", Width = 280 };
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(14), Width = 310 };
        panel.Children.Add(ui.Text("地点", 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        panel.Children.Add(locationBox);
        var recentLocations = data().Entries.Select(entry => entry.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location) && location != "待补充")
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Take(4).ToList();
        if (recentLocations.Count > 0)
        {
            var recent = new ComboBox { PlaceholderText = "最近使用", Width = 280 };
            foreach (var location in recentLocations) recent.Items.Add(new ComboBoxItem { Content = location });
            recent.SelectionChanged += (_, _) =>
            {
                if (recent.SelectedItem is ComboBoxItem selected) locationBox.Text = selected.Content?.ToString() ?? locationBox.Text;
            };
            panel.Children.Add(recent);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var useLocation = ui.SecondaryButton("使用此地点");
        var automatic = ui.OutlineButton("\uE81E", "获取当前位置", "TextPrimary");
        actions.Children.Add(useLocation);
        actions.Children.Add(automatic);
        panel.Children.Add(actions);
        panel.Children.Add(ui.Text("自动定位会先请求你的确认，并可在设置中关闭。", 12, "TextMuted"));
        var flyout = new Flyout { Content = panel };
        useLocation.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(locationBox.Text)) SetQuickLocation(locationBox.Text.Trim(), null);
            flyout.Hide();
        };
        automatic.Click += async (_, _) =>
        {
            await acquireLocation();
            flyout.Hide();
        };
        flyout.ShowAt(anchor);
    }

    private Button ToolbarAction(string glyph, string value, Func<Button, Task> action)
    {
        var button = new Button
        {
            Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = DiaryUiFactory.TransparentBrush(),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new FontIcon { Glyph = glyph, FontSize = 18, Foreground = theme.Brush("TextPrimary") }, ui.Text(value, 15, "TextPrimary") }
            }
        };
        button.Click += async (_, _) => await action(button);
        return button;
    }

    private void OnQuickTextChanged()
    {
        if (buildingShell() || _quickEditor is null) return;
        if (!string.IsNullOrWhiteSpace(_quickEditor.Text))
        {
            var draft = ensureDraft();
            commands.UpdateDraftText(draft.Id, _quickEditor.Text);
        }
        UpdateQuickEditorState();
    }

    private void UpdateQuickEditorState()
    {
        if (_quickEditor is null || _quickCharacterCount is null || _quickSaveButton is null) return;
        _quickCharacterCount.Text = $"{_quickEditor.Text.Length} / {MaximumBodyLength}";
        _quickSaveButton.IsEnabled = !string.IsNullOrWhiteSpace(_quickEditor.Text);
    }

    private Button TagFilterButton(string label, string? tag)
    {
        var selected = string.Equals(TagFilter, tag, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Padding = new Thickness(10, 4, 10, 4), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6),
            Background = selected ? theme.Brush("Accent") : theme.TagBackground(label),
            Content = new TextBlock
            {
                Text = label, FontSize = 13, Foreground = selected ? theme.Brush("OnAccent") : theme.TagForeground(label),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) =>
        {
            TagFilter = tag;
            ensureSelectedEntryVisible();
            rebuild();
        };
        return button;
    }

    public string ComposeMood => draftEntry()?.Mood ?? _composeMood;
    public string ComposeWeather => draftEntry()?.Weather ?? _composeWeather;
    public string ComposeLocation => draftEntry()?.Location ?? _composeLocation;
}
