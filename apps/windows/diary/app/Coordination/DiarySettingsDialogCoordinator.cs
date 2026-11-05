using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Fowan.Diary.Windows.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Diary.Windows.Coordination;

internal sealed class DiarySettingsDialogCoordinator(
    Func<XamlRoot> xamlRoot,
    Func<DiaryData> data,
    Func<DiarySettings> settings,
    DiaryWorkspace workspace,
    DiaryThemePalette theme,
    DiaryEnvironmentAcquisitionCoordinator environment,
    Func<DiaryData, bool> saveData,
    Action<DiarySettings> saveSettings,
    Action applyCaptionColors,
    Action rebuild,
    Func<string, string, Task> showMessage)
{
    public void ShowNotebookMenu(Button anchor)
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

    public async Task ShowCreateNotebookDialogAsync()
    {
        var name = new TextBox { PlaceholderText = "例如：旅行记录" };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "新建日记本", Content = Field("名称", name),
            PrimaryButtonText = "创建", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text)) return;
        var currentData = data();
        currentData.Notebooks.Add(new DiaryNotebook { Id = workspace.CreateNotebookId(), Name = name.Text.Trim(), AccentColor = "#2F80FF" });
        saveData(currentData);
        rebuild();
    }

    public async Task ShowManageNotebooksDialogAsync()
    {
        var currentData = data();
        var selector = new ComboBox { MinWidth = 260 };
        foreach (var notebook in currentData.Notebooks) selector.Items.Add(new ComboBoxItem { Content = notebook.Name, Tag = notebook.Id });
        selector.SelectedIndex = 0;
        var name = new TextBox { Text = currentData.Notebooks[0].Name };
        var target = new ComboBox { MinWidth = 260 };
        void RefreshTarget()
        {
            name.Text = (selector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            target.Items.Clear();
            var selectedId = (selector.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            foreach (var candidate in currentData.Notebooks.Where(candidate => candidate.Id != selectedId))
                target.Items.Add(new ComboBoxItem { Content = candidate.Name, Tag = candidate.Id });
            target.SelectedIndex = target.Items.Count > 0 ? 0 : -1;
        }
        selector.SelectionChanged += (_, _) => RefreshTarget();
        RefreshTarget();
        var delete = SecondaryButton("删除并迁移日记");
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "管理日记本",
            Content = new StackPanel { Spacing = 10, Children = { Field("日记本", selector), Field("名称", name), Field("迁移到", target), delete } },
            PrimaryButtonText = "保存名称", CloseButtonText = "关闭", DefaultButton = ContentDialogButton.Primary
        };
        delete.Click += (_, _) =>
        {
            if (currentData.Notebooks.Count <= 1 || selector.SelectedItem is not ComboBoxItem source || target.SelectedItem is not ComboBoxItem destination) return;
            var sourceId = source.Tag?.ToString();
            var targetId = destination.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId)) return;
            foreach (var entry in currentData.Entries.Where(entry => entry.NotebookId == sourceId))
            {
                entry.NotebookId = targetId;
                entry.UpdatedAt = DateTimeOffset.Now;
            }
            currentData.Notebooks.RemoveAll(candidate => candidate.Id == sourceId);
            var currentSettings = settings();
            var settingsChanged = false;
            if (string.Equals(currentSettings.TimelineNotebookId, sourceId, StringComparison.Ordinal))
            {
                currentSettings.TimelineNotebookId = DiaryTimeline.AllNotebooksId;
                settingsChanged = true;
            }
            if (currentSettings.CurrentViewId == DiaryViewIds.Notebook(sourceId))
            {
                currentSettings.CurrentViewId = DiaryViewIds.Notebook(targetId);
                settingsChanged = true;
            }
            if (!saveData(currentData)) return;
            if (settingsChanged) saveSettings(currentSettings);
            dialog.Hide();
            rebuild();
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || selector.SelectedItem is not ComboBoxItem item || string.IsNullOrWhiteSpace(name.Text)) return;
        var selectedNotebook = currentData.Notebooks.FirstOrDefault(candidate => candidate.Id == item.Tag?.ToString());
        if (selectedNotebook is null) return;
        selectedNotebook.Name = name.Text.Trim();
        saveData(currentData);
        rebuild();
    }

    public async Task ShowSettingsDialogAsync()
    {
        var current = settings();
        var themeBox = new ComboBox
        {
            MinWidth = 260,
            Items =
            {
                new ComboBoxItem { Content = "跟随系统", Tag = DiaryThemeIds.System },
                new ComboBoxItem { Content = "浅色主题", Tag = DiaryThemeIds.Light },
                new ComboBoxItem { Content = "深色主题", Tag = DiaryThemeIds.Dark }
            }
        };
        foreach (var item in themeBox.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == current.Theme) themeBox.SelectedItem = item;
        var locationToggle = new ToggleSwitch { Header = "自动获取当前位置", OffContent = "关闭", OnContent = "开启", IsOn = current.LocationFeatureEnabled };
        var weatherToggle = new ToggleSwitch { Header = "根据地点自动获取天气", OffContent = "关闭", OnContent = "开启", IsOn = current.WeatherFeatureEnabled, IsEnabled = locationToggle.IsOn };
        locationToggle.Toggled += (_, _) =>
        {
            weatherToggle.IsEnabled = locationToggle.IsOn;
            if (!locationToggle.IsOn) weatherToggle.IsOn = false;
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
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "日记设置",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Field("主题", themeBox),
                    new Border { BorderBrush = theme.Brush("InnerDivider"), BorderThickness = new Thickness(0, 1, 0, 0) },
                    privacy,
                    Text("日记内容保存在此设备的 Fowan Diary 数据目录中。", 13, "TextSecondary")
                }
            },
            PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || themeBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string selectedTheme) return;
        if (locationToggle.IsOn && !current.LocationFeatureEnabled && current.LocationConsentAcceptedAt is null && !await environment.ConfirmLocationConsentAsync()) return;
        if (locationToggle.IsOn && weatherToggle.IsOn && !current.WeatherFeatureEnabled && current.WeatherConsentAcceptedAt is null && !await environment.ConfirmWeatherConsentAsync()) return;
        current.Theme = selectedTheme;
        current.LocationFeatureEnabled = locationToggle.IsOn;
        current.WeatherFeatureEnabled = locationToggle.IsOn && weatherToggle.IsOn;
        if (current.LocationFeatureEnabled) current.LocationConsentAcceptedAt ??= DateTimeOffset.Now;
        if (current.WeatherFeatureEnabled) current.WeatherConsentAcceptedAt ??= DateTimeOffset.Now;
        saveSettings(current);
        applyCaptionColors();
        rebuild();
    }

    public Task ShowHelpDialogAsync() => showMessage(
        "Fowan 日记",
        "使用快速记录保存当下想法；选择时间线卡片可在右侧查看、编辑、关联待办或导出。所有内容仅保存在本机。");

    private TextBlock Text(string value, double size, string brushKey, global::Windows.UI.Text.FontWeight? weight = null) => new()
    {
        Text = value, FontSize = size, Foreground = theme.Brush(brushKey),
        FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement Field(string label, FrameworkElement input) => new StackPanel
    {
        Spacing = 5,
        Children = { new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, input }
    };

    private Button SecondaryButton(string text) => new()
    {
        Height = 36, Padding = new Thickness(14, 0, 14, 0), BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(7), Background = theme.Brush("ControlBackground"),
        Content = Text(text, 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold)
    };
}
