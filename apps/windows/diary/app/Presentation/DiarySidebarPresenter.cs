using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiarySidebarPresenter(
    Func<DiaryData> data,
    Func<DiarySettings> settings,
    DiaryThemePalette theme,
    DiaryUiFactory ui,
    Action<UIElement> setTitleBar,
    Action<string> selectView,
    Action<Button> showNotebookMenu,
    Func<Task> showSettings,
    Func<Task> showHelp)
{
    public FrameworkElement Build()
    {
        var border = new Border
        {
            Background = theme.SidebarBackground(), BorderBrush = theme.Brush("Divider"),
            BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(13, 24, 10, 24)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13, Margin = new Thickness(0, 0, 0, 30) };
        var brandIcon = new Border
        {
            Width = 48, Height = 48,
            Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-app-icon-256.png"))),
                Width = 48, Height = 48, Stretch = Stretch.UniformToFill
            }
        };
        setTitleBar(brandIcon);
        brand.Children.Add(brandIcon);
        var brandText = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(ui.Text("Fowan", 17, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold));
        brandText.Children.Add(ui.Text("日记", 14, "TextSecondary"));
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
            BorderBrush = theme.Brush("Divider"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 14, 0, 6)
        };
        notebookHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notebookHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notebookHeader.Children.Add(ui.Text("我的日记本", 14, "TextSecondary"));
        var addNotebook = ui.TextButton("+", "管理日记本", 20, "TextSecondary");
        addNotebook.Click += (_, _) => showNotebookMenu(addNotebook);
        Grid.SetColumn(addNotebook, 1);
        notebookHeader.Children.Add(addNotebook);
        notebooks.Children.Add(notebookHeader);
        foreach (var notebook in data().Notebooks) notebooks.Children.Add(NotebookButton(notebook));
        Grid.SetRow(notebooks, 2);
        layout.Children.Add(notebooks);
        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var configure = BottomButton("\uE713", "设置");
        configure.Click += async (_, _) => await showSettings();
        bottom.Children.Add(configure);
        var help = BottomButton("\uE897", "帮助");
        help.Click += async (_, _) => await showHelp();
        Grid.SetColumn(help, 1);
        bottom.Children.Add(help);
        var footer = new Border { BorderBrush = theme.Brush("Divider"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 20, 0, 0), Child = bottom };
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        border.Child = layout;
        return border;
    }

    private Button NavButton(string viewId, string glyph, string label)
    {
        var selected = string.Equals(settings().CurrentViewId, viewId, StringComparison.Ordinal);
        var button = new Button
        {
            Height = 50, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8),
            Background = selected ? theme.Brush("SelectedNav") : DiaryUiFactory.TransparentBrush(),
            Content = NavContent(glyph, label, selected)
        };
        button.Click += (_, _) => selectView(viewId);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private UIElement NavContent(string glyph, string label, bool selected)
    {
        var grid = new Grid { ColumnSpacing = 13, Margin = new Thickness(0, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border { Width = 3, Height = 28, CornerRadius = new CornerRadius(2), Background = selected ? theme.Brush("Accent") : DiaryUiFactory.TransparentBrush(), VerticalAlignment = VerticalAlignment.Center });
        var icon = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = selected ? theme.Brush("Accent") : theme.Brush("TextPrimary"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var text = ui.Text(label, 16, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        return grid;
    }

    private Button NotebookButton(DiaryNotebook notebook)
    {
        var viewId = DiaryViewIds.Notebook(notebook.Id);
        var selected = string.Equals(settings().CurrentViewId, viewId, StringComparison.Ordinal);
        var button = new Button
        {
            Height = 32, Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7),
            Background = selected ? theme.Brush("SelectedNav") : DiaryUiFactory.TransparentBrush(),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var grid = new Grid { ColumnSpacing = 10, Margin = new Thickness(12, 0, 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = theme.HexBrush(notebook.AccentColor), VerticalAlignment = VerticalAlignment.Center });
        var label = ui.Text(notebook.Name, 13, "TextPrimary");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        button.Content = grid;
        button.Click += (_, _) => selectView(viewId);
        return button;
    }

    private Button BottomButton(string glyph, string label) => new()
    {
        Height = 36, Padding = new Thickness(0), BorderThickness = new Thickness(0),
        Background = DiaryUiFactory.TransparentBrush(), HorizontalAlignment = HorizontalAlignment.Left,
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new FontIcon { Glyph = glyph, FontSize = 17, Foreground = theme.Brush("TextPrimary") }, ui.Text(label, 13, "TextPrimary") }
        }
    };

    private static Uri FileUri(string path) => new UriBuilder { Scheme = Uri.UriSchemeFile, Path = Path.GetFullPath(path) }.Uri;
}
