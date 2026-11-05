using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoSidebarParts(
    UIElement Root,
    FrameworkElement BrandArea,
    StackPanel NavigationPanel,
    StackPanel ListPanel,
    Button HelpButton);

internal sealed record TodoTaskAreaParts(
    FrameworkElement Root,
    TextBlock Title,
    TextBlock Summary,
    Button UndoButton,
    Button RedoButton,
    Button FilterButton,
    Button StickyModeButton,
    TextBox AddTaskBox,
    ScrollViewer Scroll,
    StackPanel Content);

internal sealed record TodoShellPalette(
    Brush SidebarBackground,
    Brush SidebarBorder,
    Brush BrandBackground,
    Brush BrandText,
    Brush SidebarDivider,
    Brush SidebarText,
    Brush ContentBackground,
    Brush ContentBorder,
    Brush Text,
    Brush SecondaryText,
    Brush Accent,
    Brush Transparent);

internal sealed record TodoShellControls(
    Func<string, string, Button> SidebarIconButton,
    Func<string, string, Button> PillButton,
    Func<string, string, Button> IconOnlyButton,
    Action<TextBox> StyleTextBox);

internal sealed record TodoShellActions(
    Func<Task> AddList,
    Func<Task> ShowSettings,
    Func<Task> ShowHelp,
    Func<Task> ShowFilter,
    Action Undo,
    Action Redo,
    Action OpenSticky,
    Func<Task> AddTask);

internal sealed class TodoShellView(
    TodoShellPalette palette,
    TodoShellControls controls,
    TodoShellActions actions,
    Uri iconUri)
{
    public TodoSidebarParts BuildSidebar()
    {
        var border = new Border
        {
            Background = palette.SidebarBackground, BorderBrush = palette.SidebarBorder,
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Padding = new Thickness(20, 26, 18, 24),
            VerticalAlignment = VerticalAlignment.Center
        };
        brand.Children.Add(new Image
        {
            Width = 28, Height = 28, Margin = new Thickness(6),
            Source = new BitmapImage(iconUri)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "Fowan", FontSize = 21, FontWeight = FontWeights.SemiBold,
            Foreground = palette.BrandText, VerticalAlignment = VerticalAlignment.Center
        });
        layout.Children.Add(brand);
        var content = new StackPanel { Padding = new Thickness(12, 0, 12, 18), Spacing = 18 };
        var navigation = new StackPanel { Spacing = 6 };
        content.Children.Add(navigation);
        content.Children.Add(new Border
        {
            Height = 1, Background = palette.SidebarDivider, Margin = new Thickness(10, 8, 10, 2)
        });
        var listHeader = new Grid { Margin = new Thickness(10, 0, 8, 0) };
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listHeader.Children.Add(new TextBlock
        {
            Text = "任务清单", FontSize = 14, Foreground = palette.SidebarText,
            VerticalAlignment = VerticalAlignment.Center
        });
        var addList = controls.SidebarIconButton("\uE710", "新建清单");
        addList.Width = 32;
        addList.Height = 32;
        addList.Click += async (_, _) => await actions.AddList();
        Grid.SetColumn(addList, 1);
        listHeader.Children.Add(addList);
        content.Children.Add(listHeader);
        var lists = new StackPanel { Spacing = 6 };
        content.Children.Add(lists);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        var bottom = new Grid { Margin = new Thickness(18, 0, 18, 24) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var settings = controls.SidebarIconButton("\uE713", "设置");
        settings.Click += async (_, _) => await actions.ShowSettings();
        bottom.Children.Add(settings);
        var help = controls.SidebarIconButton("\uE897", "帮助");
        help.Click += async (_, _) => await actions.ShowHelp();
        Grid.SetColumn(help, 2);
        bottom.Children.Add(help);
        Grid.SetRow(bottom, 2);
        layout.Children.Add(bottom);
        border.Child = layout;
        return new(border, brand, navigation, lists, help);
    }

    public TodoTaskAreaParts BuildTaskArea()
    {
        var border = new Border { Background = palette.ContentBackground };
        var grid = new Grid { Padding = new Thickness(32, 68, 32, 26), RowSpacing = 16 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            FontSize = 32, FontWeight = FontWeights.Bold, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        var summary = new TextBlock
        {
            FontSize = 15, Foreground = palette.SecondaryText,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new StackPanel { Spacing = 8, Children = { title, summary } });
        var undo = controls.IconOnlyButton("\uE7A7", "撤销");
        undo.Click += (_, _) => actions.Undo();
        Grid.SetColumn(undo, 1);
        header.Children.Add(undo);
        var redo = controls.IconOnlyButton("\uE7A6", "重做");
        redo.Click += (_, _) => actions.Redo();
        Grid.SetColumn(redo, 2);
        header.Children.Add(redo);
        var filter = controls.PillButton("筛选", "\uE71C");
        filter.ClearValue(Control.BackgroundProperty);
        filter.ClearValue(Control.BorderBrushProperty);
        filter.MinWidth = 92;
        filter.Click += async (_, _) => await actions.ShowFilter();
        Grid.SetColumn(filter, 3);
        header.Children.Add(filter);
        var sticky = controls.IconOnlyButton("\uE8A7", "切换便签模式");
        sticky.Click += (_, _) => actions.OpenSticky();
        Grid.SetColumn(sticky, 4);
        header.Children.Add(sticky);
        grid.Children.Add(header);
        var addShell = new Border
        {
            Height = 48, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = palette.ContentBorder, Background = palette.ContentBackground,
            Padding = new Thickness(12, 0, 10, 0)
        };
        var addGrid = new Grid { ColumnSpacing = 10 };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var quickAdd = controls.IconOnlyButton("\uE710", "添加任务");
        quickAdd.Foreground = palette.Accent;
        quickAdd.Click += async (_, _) => await actions.AddTask();
        addGrid.Children.Add(quickAdd);
        var addTask = new TextBox
        {
            BorderThickness = new Thickness(0), Background = palette.Transparent,
            PlaceholderText = "添加任务", FontSize = 15, Foreground = palette.Text,
            Padding = new Thickness(0, 5, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        controls.StyleTextBox(addTask);
        addTask.KeyDown += async (_, args) =>
        {
            if (args.Key != VirtualKey.Enter) return;
            args.Handled = true;
            await actions.AddTask();
        };
        Grid.SetColumn(addTask, 1);
        addGrid.Children.Add(addTask);
        var date = controls.IconOnlyButton("\uE787", "截止日期");
        Grid.SetColumn(date, 2);
        addGrid.Children.Add(date);
        var important = controls.IconOnlyButton("\uE735", "重要");
        Grid.SetColumn(important, 3);
        addGrid.Children.Add(important);
        addShell.Child = addGrid;
        Grid.SetRow(addShell, 1);
        grid.Children.Add(addShell);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel { Spacing = 8 };
        scroll.Content = content;
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);
        border.Child = grid;
        return new(border, title, summary, undo, redo, filter, sticky, addTask, scroll, content);
    }
}
