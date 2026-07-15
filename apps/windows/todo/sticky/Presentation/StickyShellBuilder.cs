using Fowan.Todo.Shared.Models;
using Fowan.Todo.Sticky.Windows.Application;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed record StickyViewElements(
    Grid Root, Border Shell, Border DismissOverlay, Border AddRow, Border TaskDivider,
    FrameworkElement DragHandle, FrameworkElement BrandIcon, Button SettingsButton,
    Button AddTaskButton, TextBlock TitleText, TextBlock CountText, TextBox AddBox,
    TextBlock AddPlaceholder, ScrollViewer TaskScroll, StackPanel ActiveTasks,
    StackPanel CompletedTasks, StackPanel CompletedTaskSection, Button CompletedToggle);

internal sealed class StickyShellBuilder(
    Window window,
    StickyWindowCommands commands,
    Func<TodoSettings> settings,
    StickyThemePalette palette,
    StickyControlFactory controls,
    ScaleTransform scaleTransform,
    MouseButtonEventHandler onHeaderDown,
    MouseButtonEventHandler onWindowDown,
    Action returnToMain,
    Action toggleAdjustment,
    Action closeChildWindows,
    Action synchronizeChildWindows,
    Func<bool> addTaskFromInput,
    Action showAddTask,
    Action focusInlineAdd,
    Action updateAddPlaceholder,
    Action refreshTasks)
{
    private const double FloatingWindowSize = 52;

    public StickyViewElements Build()
    {
        if (settings().IsStickyFloatingModeEnabled) return BuildFloating();
        var root = new Grid { LayoutTransform = scaleTransform, Background = Brushes.Transparent };
        var shell = new Border
        {
            CornerRadius = new CornerRadius(8), Background = palette.Surface,
            BorderBrush = palette.Brush(0xDCE7EA), BorderThickness = new Thickness(1)
        };
        root.Children.Add(shell);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Child = layout;
        var header = BuildHeader(out var dragHandle, out var brandIcon, out var settingsButton);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);
        var divider = new Border { Background = palette.Brush(0xDCE7EA) };
        Grid.SetRow(divider, 1);
        layout.Children.Add(divider);
        var titleArea = BuildTitleArea(out var titleText, out var countText);
        Grid.SetRow(titleArea, 2);
        layout.Children.Add(titleArea);
        var addRow = BuildAddRow(out var addTaskButton, out var addBox, out var addPlaceholder);
        Grid.SetRow(addRow, 3);
        layout.Children.Add(addRow);
        var taskScroll = BuildTaskScroll(out var activeTasks, out var completedTasks,
            out var completedSection, out var completedToggle, out var taskDivider);
        Grid.SetRow(taskScroll, 4);
        layout.Children.Add(taskScroll);
        var overlay = new Border { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        overlay.MouseLeftButtonDown += (_, args) => { closeChildWindows(); args.Handled = true; };
        Panel.SetZIndex(overlay, 20);
        root.Children.Add(overlay);
        window.Content = root;
        return new StickyViewElements(root, shell, overlay, addRow, taskDivider, dragHandle, brandIcon,
            settingsButton, addTaskButton, titleText, countText, addBox, addPlaceholder, taskScroll,
            activeTasks, completedTasks, completedSection, completedToggle);
    }

    private StickyViewElements BuildFloating()
    {
        var root = new Grid { Background = Brushes.Transparent };
        var icon = new Image
        {
            Width = 30, Height = 30, Source = StickyControlFactory.LoadIconImage(), Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        System.Windows.Automation.AutomationProperties.SetName(icon, "悬浮待办图标");
        var shell = new Border
        {
            Width = FloatingWindowSize, Height = FloatingWindowSize, CornerRadius = new CornerRadius(13),
            Background = palette.Surface, BorderBrush = palette.Brush(0xDCE7EA), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Child = icon
        };
        ToolTipService.SetToolTip(shell, "单击恢复便签模式");
        shell.MouseLeftButtonDown += onWindowDown;
        root.Children.Add(shell);
        window.Content = root;
        return new StickyViewElements(root, shell, new Border(), new Border(), new Border(), new Grid(), icon,
            new Button(), new Button(), new TextBlock(), new TextBlock(), new TextBox(), new TextBlock(),
            new ScrollViewer(), new StackPanel(), new StackPanel(), new StackPanel(), new Button());
    }

    private UIElement BuildHeader(out FrameworkElement dragHandle, out FrameworkElement brandIcon, out Button settingsButton)
    {
        var header = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        dragHandle = header;
        header.MouseLeftButtonDown += onHeaderDown;
        var grid = new Grid { Margin = new Thickness(18, 0, 16, 0), VerticalAlignment = VerticalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var brandImage = new Image
        {
            Width = 16, Height = 16, Source = StickyControlFactory.LoadIconImage(),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        brandIcon = brandImage;
        System.Windows.Automation.AutomationProperties.SetName(brandImage, "便签品牌图标");
        brand.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(6),
            Background = palette.Brush(0x001B3D), Child = brandImage
        });
        brand.Children.Add(new TextBlock
        {
            Text = "Fowan", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = palette.Text,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        });
        grid.Children.Add(brand);
        var topmost = controls.HeaderPillButton(settings().IsStickyTopmost ? "\uE840" : "\uE718", "置顶");
        topmost.Click += (_, _) =>
        {
            var topmostEnabled = commands.ToggleTopmost();
            window.Topmost = topmostEnabled;
            topmost.Content = StickyControlFactory.HeaderButtonContent(topmostEnabled ? "\uE840" : "\uE718", "置顶", palette.Accent);
            synchronizeChildWindows();
        };
        AddHeaderButton(grid, topmost, 1);
        var restore = controls.HeaderIconButton("\uE72B", "回到大界面");
        restore.Click += (_, _) => returnToMain();
        AddHeaderButton(grid, restore, 2);
        settingsButton = controls.HeaderIconButton("\uE713", "透明度和缩放");
        settingsButton.Click += (_, _) => toggleAdjustment();
        AddHeaderButton(grid, settingsButton, 3);
        var minimize = controls.HeaderIconButton("\uE921", "最小化便签");
        minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
        AddHeaderButton(grid, minimize, 4);
        var close = controls.HeaderIconButton("\uE711", "关闭便签");
        close.Click += (_, _) => window.Close();
        AddHeaderButton(grid, close, 5);
        header.Child = grid;
        return header;
    }

    private UIElement BuildTitleArea(out TextBlock title, out TextBlock count)
    {
        var stack = new StackPanel { Margin = new Thickness(20, 20, 20, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title = new TextBlock
        {
            FontSize = 26, FontWeight = FontWeights.Bold, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(title);
        count = new TextBlock
        {
            FontSize = 12, Foreground = palette.SecondaryText, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 4, 0, 0)
        };
        Grid.SetColumn(count, 1);
        row.Children.Add(count);
        stack.Children.Add(row);
        return stack;
    }

    private Border BuildAddRow(out Button addButton, out TextBox addBox, out TextBlock placeholder)
    {
        var border = new Border
        {
            Height = 42, Margin = new Thickness(20, 14, 20, 0), CornerRadius = new CornerRadius(8),
            Background = palette.Panel(0xF5FAFB), BorderBrush = palette.Brush(0xDCE7EA),
            BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 10, 0)
        };
        border.MouseLeftButtonDown += (_, _) => focusInlineAdd();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addButton = controls.CircleIconButton("\uE710", "添加任务", palette.Accent, Brushes.White);
        addButton.Click += (_, _) => { if (!addTaskFromInput()) showAddTask(); };
        grid.Children.Add(addButton);
        var inputHost = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        inputHost.PreviewMouseLeftButtonDown += (_, _) => focusInlineAdd();
        addBox = new TextBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = palette.Text,
            CaretBrush = palette.Accent, Cursor = Cursors.IBeam, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 30,
            FocusVisualStyle = null
        };
        addBox.TextChanged += (_, _) => updateAddPlaceholder();
        addBox.KeyDown += (_, args) => { if (args.Key == Key.Enter) { addTaskFromInput(); args.Handled = true; } };
        inputHost.Children.Add(addBox);
        placeholder = new TextBlock
        {
            Text = "添加任务", IsHitTestVisible = false, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = palette.Accent, VerticalAlignment = VerticalAlignment.Center
        };
        inputHost.Children.Add(placeholder);
        Grid.SetColumn(inputHost, 1);
        grid.Children.Add(inputHost);
        border.Child = grid;
        return border;
    }

    private ScrollViewer BuildTaskScroll(out StackPanel active, out StackPanel completed,
        out StackPanel completedSection, out Button completedToggle, out Border divider)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(20, 16, 20, 18)
        };
        ApplyScrollTheme(scroll);
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        active = new StackPanel { Orientation = Orientation.Vertical };
        completedToggle = controls.CompletedToggleButton();
        completedToggle.Click += (_, _) =>
        {
            commands.ToggleCompletedExpanded();
            refreshTasks();
        };
        completed = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 8, 0, 0) };
        completedSection = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(active);
        divider = new Border { Height = 1, Background = palette.Brush(0xE7EEF0), Margin = new Thickness(0, 10, 0, 14) };
        completedSection.Children.Add(divider);
        completedSection.Children.Add(completedToggle);
        completedSection.Children.Add(completed);
        stack.Children.Add(completedSection);
        scroll.Content = stack;
        return scroll;
    }

    private void ApplyScrollTheme(ScrollViewer scroll)
    {
        scroll.Resources[SystemColors.ControlBrushKey] = palette.Brush(0xF5FAFB);
        scroll.Resources[SystemColors.ControlLightBrushKey] = palette.Brush(0xF5FAFB);
        scroll.Resources[SystemColors.ControlDarkBrushKey] = palette.Brush(0x9BB2BC);
        scroll.Resources[SystemColors.WindowBrushKey] = palette.Brush(0xFFFFFF);
        var track = palette.HexColor(0xF5FAFB, Math.Clamp(settings().StickyOpacity, 0.0, 1.0));
        var thumb = palette.HexColor(palette.IsDark ? 0x536677u : 0x9DBEC7u);
        var hover = palette.HexColor(palette.IsDark ? 0x667B8Eu : 0x87AFBAu);
        var xaml = $$"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ScrollBar}">
              <Setter Property="Width" Value="8"/><Setter Property="MinWidth" Value="8"/><Setter Property="Background" Value="{{track}}"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ScrollBar}"><Grid Background="{TemplateBinding Background}" Width="8">
                <Track x:Name="PART_Track" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb Background="{{thumb}}"><Thumb.Template><ControlTemplate TargetType="{x:Type Thumb}"><Border x:Name="ThumbChrome" Margin="2" CornerRadius="3" Background="{TemplateBinding Background}"/>
                  <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ThumbChrome" Property="Background" Value="{{hover}}"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter>
            </Style>
            """;
        scroll.Resources[typeof(ScrollBar)] = (Style)XamlReader.Parse(xaml);
    }

    private static void AddHeaderButton(Grid grid, Button button, int column)
    {
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }
}
