using Fowan.Todo.Core.Models;
using Fowan.Todo.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using MuxFontWeights = Microsoft.UI.Text.FontWeights;
using WinTextDecorations = Windows.UI.Text.TextDecorations;

namespace Fowan.Todo.Windows;

public sealed class TodoWindow : Window
{
    private const double SidebarWidth = 248;
    private const double CompletedTimestampEditorWidth = 236;
    // The detail host has a 1 DIP left border. 433 DIP leaves exactly 236 DIP
    // for the timestamp editor after its 28/88 DIP columns, spacing and padding.
    private const double DetailWidth = 433;
    private const int TaskDragLongPressMilliseconds = 350;
    private const double TaskDragMoveCancelDistance = 6;
    private const double TaskDropEdgeRatio = 0.28;
    private const double TaskAutoScrollActivationDistance = 96;
    private const double TaskAutoScrollMinimumSpeed = 120;
    private const double TaskAutoScrollMaximumSpeed = 720;
    private const double TaskAutoScrollSectionOverlap = 50;
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const int ShowWindowRestore = 9;

    private readonly TodoStore _store = new();
    private TodoData _data;
    private TodoSettings _settings;

    private Grid _root = new();
    private StackPanel _navigationPanel = new();
    private StackPanel _listPanel = new();
    private StackPanel _taskContent = new();
    private ScrollViewer _taskScroll = new();
    private FrameworkElement? _activeTaskSection;
    private FrameworkElement? _completedTaskSection;
    private Border _detailHost = new();
    private FrameworkElement _brandArea = new Grid();
    private TextBox _addTaskBox = new();
    private TextBlock _taskTitle = new();
    private TextBlock _taskSummary = new();
    private Button _filterButton = new();
    private readonly List<FrameworkElement> _titleBarInteractiveElements = [];
    private readonly List<Button> _taskRows = [];

    private string _currentViewId;
    private string? _selectedTaskId;
    private System.Diagnostics.Process? _stickyProcess;
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _taskDragTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _taskAutoScrollTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _recycleBinMaintenanceTimer;
    private TodoTask? _dragCandidateTask;
    private Button? _dragCandidateRow;
    private FrameworkElement? _dropPreview;
    private Panel? _dropPreviewHost;
    private TodoDropTarget? _currentDropTarget;
    private Point _taskDragStartPoint;
    private Point _taskDragCurrentPoint;
    private bool _isTaskDragging;
    private string? _suppressTaskClickId;
    private TodoDateRangeFilter? _dateRangeFilter;
    private string? _listIdFilter;
    private int _maximumVisibleTaskDepth = TodoQuery.MaxTaskTreeDepth;

    public TodoWindow()
    {
        _data = _store.LoadData();
        _settings = _store.LoadSettings();
        PurgeExpiredRecycleBin();
        _currentViewId = IsKnownView(_settings.CurrentViewId) ? _settings.CurrentViewId : TodoViewIds.Today;
        _selectedTaskId = _settings.SelectedTaskId;
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            if (_settings.Theme != TodoThemeIds.System)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyCaptionButtonColorsToCurrentWindow();
                BuildShell();
                RefreshAll();
            });
        };

        ConfigureWindow();
        BuildShell();
        RefreshAll();
        StartRecycleBinMaintenanceTimer();
    }

    internal void ActivateInitialMode()
    {
        if (_settings.IsStickyModeEnabled)
        {
            OpenStickyMode(closeMainWindow: true);
            return;
        }

        Activate();
        QueueStickyPrewarm();
    }

    private void ConfigureWindow()
    {
        Title = "Fowan Todo";
        Closed += (_, _) => StickyLauncher.TryShutdown();

        try
        {
            var hwnd = WindowHandle();
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var scale = Math.Clamp(GetDpiForWindow(hwnd) / 96.0, 1.0, 3.0);
            var width = (int)Math.Round(1280 * scale);
            var height = (int)Math.Round(820 * scale);
            appWindow.Resize(new SizeInt32(width, height));

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

            ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            ApplyCaptionButtonColors(appWindow);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Window decoration APIs can fail in restricted hosts; the UI still renders.
        }
    }

    private void BuildShell()
    {
        _titleBarInteractiveElements.Clear();
        _root = new Grid
        {
            RequestedTheme = ResolveElementTheme(),
            Background = Brush(0xFFFFFF)
        };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DetailWidth) });

        _root.Children.Add(BuildSidebar());

        var taskArea = BuildTaskArea();
        Grid.SetColumn(taskArea, 1);
        _root.Children.Add(taskArea);

        _detailHost = new Border
        {
            Background = Brush(0xFFFFFF),
            BorderBrush = Brush(0xDCE7EA),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };
        Grid.SetColumn(_detailHost, 2);
        _root.Children.Add(_detailHost);
        Content = _root;
        ConfigureVirtualTitleBarRegions();
    }

    private void ConfigureVirtualTitleBarRegions()
    {
        _root.Loaded += (_, _) => UpdateVirtualTitleBarRegions();
        _root.SizeChanged += (_, _) => UpdateVirtualTitleBarRegions();
        UpdateVirtualTitleBarRegions();
    }

    private void UpdateVirtualTitleBarRegions()
    {
        if (_root.ActualWidth <= 0 || _root.XamlRoot is null)
        {
            return;
        }

        try
        {
            var hwnd = WindowHandle();
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var scale = Math.Clamp(GetDpiForWindow(hwnd) / 96.0, 1.0, 3.0);
            var clientWidth = Math.Max(0, (int)Math.Ceiling(_root.ActualWidth * scale));
            var clientHeight = Math.Max(0, (int)Math.Ceiling(_root.ActualHeight * scale));
            var systemTitleBarHeight = Math.Max(0, appWindow.TitleBar.Height);
            var virtualTitleBarHeight = Math.Max(
                systemTitleBarHeight,
                (int)Math.Ceiling(_brandArea.ActualHeight * scale));
            var captionWidth = Math.Max(0, clientWidth - appWindow.TitleBar.RightInset);
            var inputSource = InputNonClientPointerSource.GetForWindowId(windowId);

            var captionRects = new List<RectInt32>();
            if (captionWidth > 0 && systemTitleBarHeight > 0)
            {
                captionRects.Add(new RectInt32(0, 0, captionWidth, systemTitleBarHeight));
            }

            if (virtualTitleBarHeight > systemTitleBarHeight)
            {
                captionRects.Add(new RectInt32(
                    0,
                    systemTitleBarHeight,
                    clientWidth,
                    virtualTitleBarHeight - systemTitleBarHeight));
            }

            inputSource.SetRegionRects(NonClientRegionKind.Caption, captionRects.ToArray());

            var passThroughRects = new List<RectInt32>();
            foreach (var element in _titleBarInteractiveElements.Where(element => element.IsHitTestVisible))
            {
                if (!TryGetElementBounds(element, out var bounds) ||
                    bounds.Bottom <= 0 ||
                    bounds.Top >= virtualTitleBarHeight / scale)
                {
                    continue;
                }

                var left = Math.Max(0, (int)Math.Floor(bounds.Left * scale));
                var top = Math.Max(0, (int)Math.Floor(bounds.Top * scale));
                var right = Math.Min(clientWidth, (int)Math.Ceiling(bounds.Right * scale));
                // A control which overlaps the caption boundary must remain a full
                // client-input rectangle. Clipping it at the boundary causes its
                // hover state to alternate with the native title bar.
                var bottom = Math.Min(clientHeight, (int)Math.Ceiling(bounds.Bottom * scale));
                if (right > left && bottom > top)
                {
                    passThroughRects.Add(new RectInt32(left, top, right - left, bottom - top));
                }
            }

            inputSource.SetRegionRects(NonClientRegionKind.Passthrough, passThroughRects.ToArray());
        }
        catch
        {
            // A native title-bar API failure must not prevent the Todo window from rendering.
        }
    }

    private void RegisterTitleBarInteractiveElement(FrameworkElement element)
    {
        _titleBarInteractiveElements.Add(element);
    }

    private UIElement BuildSidebar()
    {
        var border = new Border
        {
            Background = Brush(0x001B3D),
            BorderBrush = Brush(0x0D2E5F),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Padding = new Thickness(20, 26, 18, 24),
            VerticalAlignment = VerticalAlignment.Center
        };
        _brandArea = brand;
        var brandIcon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            Background = Brush(0x082B62),
            Child = new Image
            {
                Width = 28,
                Height = 28,
                Source = new BitmapImage(FileUri(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo-app-icon-256.png")))
            }
        };
        brand.Children.Add(brandIcon);
        brand.Children.Add(new TextBlock
        {
            Text = "Fowan",
            FontSize = 21,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = PureWhiteBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });
        layout.Children.Add(brand);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);

        var content = new StackPanel
        {
            Padding = new Thickness(12, 0, 12, 18),
            Spacing = 18
        };

        _navigationPanel = new StackPanel { Spacing = 6 };
        content.Children.Add(_navigationPanel);

        content.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(0x173B70),
            Margin = new Thickness(10, 8, 10, 2)
        });

        var listHeader = new Grid { Margin = new Thickness(10, 0, 8, 0) };
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listHeader.Children.Add(new TextBlock
        {
            Text = "任务清单",
            FontSize = 14,
            Foreground = Brush(0xD6E3F5),
            VerticalAlignment = VerticalAlignment.Center
        });
        var addList = SidebarIconButton("\uE710", "新建清单");
        addList.Width = 32;
        addList.Height = 32;
        addList.Click += async (_, _) => await ShowAddListDialogAsync();
        Grid.SetColumn(addList, 1);
        listHeader.Children.Add(addList);
        content.Children.Add(listHeader);

        _listPanel = new StackPanel { Spacing = 6 };
        content.Children.Add(_listPanel);

        scroll.Content = content;
        layout.Children.Add(scroll);

        var bottom = new Grid
        {
            Margin = new Thickness(18, 0, 18, 24)
        };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var settingsButton = SidebarIconButton("\uE713", "设置");
        settingsButton.Click += async (_, _) => await ShowSettingsDialogAsync();
        bottom.Children.Add(settingsButton);
        var help = SidebarIconButton("\uE897", "帮助");
        help.Click += async (_, _) => await ShowHelpDialogAsync();
        Grid.SetColumn(help, 2);
        bottom.Children.Add(help);
        Grid.SetRow(bottom, 2);
        layout.Children.Add(bottom);

        border.Child = layout;
        return border;
    }

    private FrameworkElement BuildTaskArea()
    {
        var border = new Border
        {
            Background = Brush(0xFFFFFF)
        };

        var grid = new Grid
        {
            Padding = new Thickness(32, 68, 32, 26),
            RowSpacing = 16
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel { Spacing = 8 };
        _taskTitle = new TextBlock
        {
            FontSize = 32,
            FontWeight = MuxFontWeights.Bold,
            Foreground = TextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_taskTitle);
        _taskSummary = new TextBlock
        {
            FontSize = 15,
            Foreground = SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_taskSummary);
        header.Children.Add(titleStack);

        _filterButton = PillButton("筛选", "\uE71C");
        _filterButton.ClearValue(Control.BackgroundProperty);
        _filterButton.ClearValue(Control.BorderBrushProperty);
        _filterButton.MinWidth = 92;
        _filterButton.Click += async (_, _) => await ShowFilterDialogAsync();
        Grid.SetColumn(_filterButton, 1);
        header.Children.Add(_filterButton);
        RegisterTitleBarInteractiveElement(_filterButton);
        RefreshFilterButtonState();

        var stickyMode = IconOnlyButton("\uE8A7", "切换便签模式");
        stickyMode.Click += (_, _) => OpenStickyMode();
        Grid.SetColumn(stickyMode, 2);
        header.Children.Add(stickyMode);
        RegisterTitleBarInteractiveElement(stickyMode);
        grid.Children.Add(header);

        var addShell = new Border
        {
            Height = 48,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(0xDCE7EA),
            Background = Brush(0xFFFFFF),
            Padding = new Thickness(12, 0, 10, 0)
        };
        var addGrid = new Grid { ColumnSpacing = 10 };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var quickAdd = IconOnlyButton("\uE710", "添加任务");
        quickAdd.Foreground = AccentBrush();
        quickAdd.Click += async (_, _) => await AddTaskFromInputAsync();
        addGrid.Children.Add(quickAdd);
        _addTaskBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            PlaceholderText = "添加任务",
            FontSize = 15,
            Foreground = TextBrush(),
            Padding = new Thickness(0, 5, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyFlatTextBoxStyle(_addTaskBox);
        _addTaskBox.KeyDown += async (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                await AddTaskFromInputAsync();
            }
        };
        Grid.SetColumn(_addTaskBox, 1);
        addGrid.Children.Add(_addTaskBox);
        var dateHint = IconOnlyButton("\uE787", "截止日期");
        Grid.SetColumn(dateHint, 2);
        addGrid.Children.Add(dateHint);
        var importantHint = IconOnlyButton("\uE735", "重要");
        Grid.SetColumn(importantHint, 3);
        addGrid.Children.Add(importantHint);
        addShell.Child = addGrid;
        Grid.SetRow(addShell, 1);
        grid.Children.Add(addShell);

        _taskScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _taskContent = new StackPanel { Spacing = 8 };
        _taskScroll.Content = _taskContent;
        Grid.SetRow(_taskScroll, 2);
        grid.Children.Add(_taskScroll);

        border.Child = grid;
        return border;
    }

    private void RefreshAll()
    {
        if (!IsKnownView(_currentViewId))
        {
            _currentViewId = TodoViewIds.Today;
        }

        RefreshNavigation();
        RefreshFilterButtonState();
        RefreshTaskContent();
        RefreshDetail();
    }

    private void RefreshNavigation()
    {
        _navigationPanel.Children.Clear();
        _navigationPanel.Children.Add(NavigationButton(TodoViewIds.Today, "今日任务", "\uE787", ActiveTasksForView(TodoViewIds.Today).Count()));
        _navigationPanel.Children.Add(NavigationButton(TodoViewIds.Planned, "计划任务", "\uE163", ActiveTasksForView(TodoViewIds.Planned).Count()));
        _navigationPanel.Children.Add(NavigationButton(TodoViewIds.Important, "重要任务", "\uE735", ActiveTasksForView(TodoViewIds.Important).Count()));
        _navigationPanel.Children.Add(NavigationButton(TodoViewIds.All, "全部任务", "\uE8FD", ActiveTasksForView(TodoViewIds.All).Count()));
        _navigationPanel.Children.Add(NavigationButton(TodoViewIds.Completed, "已完成", "\uE73E", CompletedTasksForView(TodoViewIds.Completed).Count()));
        if (_settings.IsRecycleBinEnabled)
        {
            _navigationPanel.Children.Add(NavigationButton(TodoViewIds.RecycleBin, "回收站", "\uE74D", TodoQuery.RecycleBinTasks(_data).Count()));
        }

        _listPanel.Children.Clear();
        foreach (var list in OrderedLists())
        {
            _listPanel.Children.Add(ListNavigationItem(list));
        }
    }

    private Button NavigationButton(string viewId, string label, string glyph, int count)
    {
        var selected = string.Equals(_currentViewId, viewId, StringComparison.Ordinal);
        var isList = TodoViewIds.IsList(viewId);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = 44,
            BorderThickness = new Thickness(0),
            Background = TransparentBrush()
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);

        var shell = new Border
        {
            Height = 46,
            CornerRadius = new CornerRadius(7),
            Background = selected ? Brush(0x0B3A7A) : TransparentBrush()
        };
        var grid = new Grid
        {
            ColumnSpacing = 11,
            Padding = new Thickness(isList ? 14 : 12, 0, 12, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (isList)
        {
            grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = ListColorBrush(TodoViewIds.ListId(viewId)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            grid.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 20,
                Foreground = selected ? PureWhiteBrush() : Brush(0xAFC1D8),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        var text = new TextBlock
        {
            Text = label,
            FontSize = 15,
            Foreground = selected ? PureWhiteBrush() : Brush(0xE5EDF8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (count > 0)
        {
            var badge = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 12,
                Foreground = selected ? Brush(0xDDEBFF) : Brush(0xD6E3F5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        shell.Child = grid;
        button.Content = shell;
        button.Click += (_, _) =>
        {
            NavigateToView(viewId);
        };
        return button;
    }

    private UIElement ListNavigationItem(TodoList list)
    {
        var viewId = TodoViewIds.List(list.Id);
        var selected = string.Equals(_currentViewId, viewId, StringComparison.Ordinal);
        var count = TasksForList(list.Id).Count(task => !task.IsCompleted);

        var shell = new Border
        {
            Height = 46,
            CornerRadius = new CornerRadius(7),
            Background = selected ? Brush(0x0B3A7A) : TransparentBrush()
        };

        var grid = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(14, 0, 8, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = ListColorBrush(list.Id),
            VerticalAlignment = VerticalAlignment.Center
        });

        var text = new TextBlock
        {
            Text = list.Name,
            FontSize = 15,
            Foreground = selected ? PureWhiteBrush() : Brush(0xE5EDF8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (count > 0)
        {
            var badge = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 12,
                Foreground = selected ? Brush(0xDDEBFF) : Brush(0xD6E3F5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var manage = SidebarIconButton("\uE712", "管理清单");
        manage.Width = 30;
        manage.Height = 30;
        manage.Tapped += (_, args) => args.Handled = true;
        manage.Flyout = BuildListMenu(list);
        Grid.SetColumn(manage, 3);
        grid.Children.Add(manage);

        grid.Tapped += (_, _) =>
        {
            NavigateToView(viewId);
        };

        shell.Child = grid;
        AutomationProperties.SetName(shell, list.Name);
        ToolTipService.SetToolTip(shell, list.Name);
        return shell;
    }

    private MenuFlyout BuildListMenu(TodoList list)
    {
        var menu = new MenuFlyout();

        var changeColor = new MenuFlyoutItem { Text = "更改配色" };
        changeColor.Click += async (_, _) => await ShowListColorDialogAsync(list);
        menu.Items.Add(changeColor);

        var rename = new MenuFlyoutItem { Text = "重命名" };
        rename.Click += async (_, _) => await ShowRenameListDialogAsync(list);
        menu.Items.Add(rename);

        var delete = new MenuFlyoutItem
        {
            Text = "删除",
            IsEnabled = !IsDefaultList(list.Id) && _data.Lists.Count > 1
        };
        delete.Click += async (_, _) => await ShowDeleteListDialogAsync(list);
        menu.Items.Add(delete);

        return menu;
    }

    private void RefreshTaskContent()
    {
        CancelTaskDrag();
        _taskRows.Clear();
        _taskContent.Children.Clear();
        _activeTaskSection = null;
        _completedTaskSection = null;
        _taskTitle.Text = ViewTitle(_currentViewId);

        if (_currentViewId == TodoViewIds.RecycleBin)
        {
            var deletedTasks = TodoQuery.RecycleBinTaskNodes(
                _data,
                CollapsedTaskIds(),
                _maximumVisibleTaskDepth).ToList();
            _taskSummary.Text = $"{deletedTasks.Count} 项";
            if (deletedTasks.Count == 0)
            {
                _taskContent.Children.Add(EmptyState("回收站为空"));
                return;
            }

            foreach (var node in deletedTasks)
            {
                _taskContent.Children.Add(TaskRow(node.Task, node.Task.IsCompleted, node.Depth, recycleBin: true));
            }

            return;
        }

        var activeTasks = ActiveTaskNodesForView(_currentViewId).ToList();
        var completedTasks = CompletedTaskNodesForView(_currentViewId).ToList();

        if (_currentViewId == TodoViewIds.Completed)
        {
            _taskSummary.Text = $"{completedTasks.Count} 项";
            if (completedTasks.Count == 0)
            {
                _taskContent.Children.Add(EmptyState("还没有已完成任务"));
                return;
            }

            foreach (var node in completedTasks)
            {
                _taskContent.Children.Add(TaskRow(node.Task, completed: true, depth: node.Depth));
            }

            return;
        }

        _taskSummary.Text = $"{activeTasks.Count} 项";
        var activeSection = new StackPanel { Spacing = 8 };
        _activeTaskSection = activeSection;
        if (activeTasks.Count == 0)
        {
            activeSection.Children.Add(EmptyState("当前没有待办任务"));
        }
        else
        {
            foreach (var node in activeTasks)
            {
                activeSection.Children.Add(TaskRow(node.Task, completed: false, depth: node.Depth));
            }
        }

        _taskContent.Children.Add(activeSection);
        _completedTaskSection = (FrameworkElement)CompletedSection(completedTasks);
        _taskContent.Children.Add(_completedTaskSection);
    }

    private UIElement CompletedSection(IReadOnlyList<TodoTaskNode> completedTasks)
    {
        var section = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0)
        };
        section.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(0xDCE7EA),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var header = new Grid
        {
            Height = 38,
            ColumnSpacing = 12
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chevronButton = RowIconButton(_settings.IsCompletedExpanded ? "\uE70D" : "\uE76C", _settings.IsCompletedExpanded ? "收起已完成" : "展开已完成", SecondaryTextBrush());
        chevronButton.Click += (_, _) =>
        {
            _settings.IsCompletedExpanded = !_settings.IsCompletedExpanded;
            SaveSettings();
            RefreshTaskContent();
        };
        header.Children.Add(chevronButton);

        var label = new TextBlock
        {
            Text = $"已完成 · {completedTasks.Count}",
            FontSize = 16,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = TextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        header.Children.Add(label);

        var restoreAll = HeaderActionButton("\uE777", "恢复");
        restoreAll.IsEnabled = completedTasks.Count > 0;
        restoreAll.Click += (_, _) => RestoreCompletedForCurrentView();
        Grid.SetColumn(restoreAll, 2);
        header.Children.Add(restoreAll);

        var clearAll = HeaderActionButton("\uE74D", "清除");
        clearAll.IsEnabled = completedTasks.Count > 0;
        clearAll.Click += (_, _) => ClearCompletedForCurrentView();
        Grid.SetColumn(clearAll, 3);
        header.Children.Add(clearAll);

        section.Children.Add(header);

        if (_settings.IsCompletedExpanded)
        {
            foreach (var node in completedTasks)
            {
                section.Children.Add(TaskRow(node.Task, completed: true, depth: node.Depth));
            }
        }

        return section;
    }

    private UIElement TaskRow(TodoTask task, bool completed, int depth = 0, bool recycleBin = false)
    {
        var selected = string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal);
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = 54,
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            CornerRadius = new CornerRadius(8),
            Tag = task.Id,
            Margin = new Thickness(Math.Clamp(depth, 0, TodoQuery.MaxTaskTreeDepth - 1) * 22, 0, 0, 0)
        };
        ConfigureTaskRowButtonChrome(button);
        _taskRows.Add(button);
        if (!recycleBin)
        {
            AttachTaskDragHandlers(button, task);
            button.ContextFlyout = BuildTaskContextMenu(task);
        }
        AutomationProperties.SetName(button, task.Title);

        var shell = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = selected ? (completed ? Brush(0xBFDCCB) : AccentBrush()) : Brush(0xE1EAED),
            Background = selected ? (completed ? Brush(0xEEF8F1) : Brush(0xEEF9FA)) : Brush(completed ? 0xFBFCFCu : 0xFFFFFFu),
            Padding = new Thickness(14, 8, 10, 8)
        };
        var isPointerOver = false;
        void RefreshTaskCardVisual()
        {
            var isSelected = string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal);
            shell.BorderBrush = isSelected
                ? (completed ? Brush(0xBFDCCB) : AccentBrush())
                : isPointerOver
                    ? TaskHoverBorderBrush(completed)
                    : Brush(0xE1EAED);
            shell.Background = isSelected
                ? (completed ? Brush(0xEEF8F1) : Brush(0xEEF9FA))
                : isPointerOver
                    ? TaskHoverBackgroundBrush(completed)
                    : Brush(completed ? 0xFBFCFCu : 0xFFFFFFu);
        }
        button.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) =>
        {
            isPointerOver = true;
            RefreshTaskCardVisual();
        }), true);
        button.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) =>
        {
            isPointerOver = false;
            RefreshTaskCardVisual();
        }), true);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leadingActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (recycleBin)
        {
            leadingActions.Children.Add(new Border { Width = 24, Height = 24 });
            leadingActions.Children.Add(new Border { Width = 24, Height = 24 });
        }
        else if (TodoQuery.DirectChildCount(_data, task.Id) > 0)
        {
            leadingActions.Children.Add(TreeToggleButton(task));
        }
        else
        {
            leadingActions.Children.Add(new Border { Width = 24, Height = 24 });
        }

        leadingActions.Children.Add(TaskCheckButton(task));
        grid.Children.Add(leadingActions);
        var title = new TextBlock
        {
            Text = task.Title,
            FontSize = 15,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = completed ? MutedBrush() : TextBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = completed ? WinTextDecorations.Strikethrough : WinTextDecorations.None,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var listPill = TaskListPill(task.ListId);
        Grid.SetColumn(listPill, 2);
        grid.Children.Add(listPill);

        var time = new TextBlock
        {
            Text = TaskTimeText(task),
            FontSize = 13,
            Foreground = task.DueDate?.Date == DateTime.Today && !completed ? Brush(0xF06423) : SecondaryTextBrush(),
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(time, 3);
        grid.Children.Add(time);

        if (recycleBin)
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };
            var restore = RowIconButton("\uE777", "恢复任务树", SecondaryTextBrush());
            restore.Click += (_, _) => RestoreTaskTree(task);
            actions.Children.Add(restore);
            var deletePermanently = RowIconButton("\uE74D", "永久删除任务树", Brush(0xB42318));
            deletePermanently.Click += async (_, _) => await PermanentlyDeleteTaskTreeAsync(task);
            actions.Children.Add(deletePermanently);
            Grid.SetColumn(actions, 4);
            grid.Children.Add(actions);
        }
        else if (completed)
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };
            var restore = RowIconButton("\uE777", "恢复任务", SecondaryTextBrush());
            restore.Click += (_, _) => SetTaskCompleted(task, false);
            actions.Children.Add(restore);
            var delete = RowIconButton("\uE74D", "删除任务", SecondaryTextBrush());
            delete.Click += async (_, _) => await DeleteTaskAsync(task);
            actions.Children.Add(delete);
            Grid.SetColumn(actions, 4);
            grid.Children.Add(actions);
        }
        else
        {
            var star = RowIconButton(task.IsImportant ? "\uE735" : "\uE734", task.IsImportant ? "取消重要" : "标为重要", task.IsImportant ? AccentBrush() : SecondaryTextBrush());
            star.Click += (_, _) => ToggleImportant(task);
            Grid.SetColumn(star, 4);
            grid.Children.Add(star);
        }

        shell.Child = grid;
        button.Content = shell;
        button.Click += (_, _) =>
        {
            if (string.Equals(_suppressTaskClickId, task.Id, StringComparison.Ordinal))
            {
                _suppressTaskClickId = null;
                return;
            }

            _selectedTaskId = task.Id;
            SaveSettings();
            RefreshTaskContent();
            RefreshDetail();
        };

        return button;
    }

    private MenuFlyout BuildTaskContextMenu(TodoTask task)
    {
        var menu = new MenuFlyout();
        var createChild = new MenuFlyoutItem
        {
            Text = "创建子任务",
            IsEnabled = TodoQuery.CanAddChild(_data, task)
        };
        if (!createChild.IsEnabled)
        {
            ToolTipService.SetToolTip(createChild, TodoQuery.AddChildBlockedReason(_data, task));
        }

        createChild.Click += async (_, _) => await ShowAddSubtaskDialogAsync(task);
        menu.Items.Add(createChild);

        var delete = new MenuFlyoutItem { Text = "删除任务" };
        delete.Click += async (_, _) => await DeleteTaskAsync(task);
        menu.Items.Add(delete);
        return menu;
    }

    private void ConfigureTaskRowButtonChrome(Button button)
    {
        button.Resources["ButtonBackground"] = TransparentBrush();
        button.Resources["ButtonBackgroundPointerOver"] = TransparentBrush();
        button.Resources["ButtonBackgroundPressed"] = TransparentBrush();
        button.Resources["ButtonBorderBrush"] = TransparentBrush();
        button.Resources["ButtonBorderBrushPointerOver"] = TransparentBrush();
        button.Resources["ButtonBorderBrushPressed"] = TransparentBrush();
        button.Resources["ButtonBorderThickness"] = new Thickness(0);
        button.Resources["ButtonBorderThicknessPointerOver"] = new Thickness(0);
        button.Resources["ButtonBorderThicknessPressed"] = new Thickness(0);
        button.Resources["ButtonCornerRadius"] = new CornerRadius(8);
    }

    private void AttachTaskDragHandlers(Button row, TodoTask task)
    {
        row.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => StartTaskDragCandidate(row, task, args)), true);
        row.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) => UpdateTaskDrag(args)), true);
        row.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) => FinishTaskDrag(args)), true);
        row.PointerCaptureLost += (_, _) =>
        {
            if (ReferenceEquals(_dragCandidateRow, row))
            {
                if (_isTaskDragging)
                {
                    CompleteTaskDrag(applyDrop: true);
                    return;
                }

                CancelTaskDrag();
            }
        };
    }

    private void StartTaskDragCandidate(Button row, TodoTask task, PointerRoutedEventArgs args)
    {
        var pointerPoint = args.GetCurrentPoint(_root);
        if (!pointerPoint.Properties.IsLeftButtonPressed ||
            IsTaskDragIgnoredSource(args.OriginalSource as DependencyObject, row))
        {
            return;
        }

        CancelTaskDrag();
        _dragCandidateTask = task;
        _dragCandidateRow = row;
        _taskDragStartPoint = pointerPoint.Position;
        _taskDragCurrentPoint = _taskDragStartPoint;
        _taskDragTimer = DispatcherQueue.CreateTimer();
        _taskDragTimer.Interval = TimeSpan.FromMilliseconds(TaskDragLongPressMilliseconds);
        _taskDragTimer.Tick += (_, _) => BeginTaskDrag();
        _taskDragTimer.Start();
        row.CapturePointer(args.Pointer);
    }

    private void UpdateTaskDrag(PointerRoutedEventArgs args)
    {
        if (_dragCandidateTask is null)
        {
            return;
        }

        var point = args.GetCurrentPoint(_root).Position;
        _taskDragCurrentPoint = point;
        if (!_isTaskDragging)
        {
            if (Distance(_taskDragStartPoint, point) > TaskDragMoveCancelDistance)
            {
                CancelTaskDrag();
            }

            return;
        }

        var nextTarget = DropTargetFromPoint(point);
        if (nextTarget is null && _currentDropTarget is not null && IsPointInsideDropPreview(point))
        {
            nextTarget = _currentDropTarget;
        }

        if (!DropTargetsEqual(_currentDropTarget, nextTarget))
        {
            _currentDropTarget = nextTarget;
            ApplyDropTargetVisual(_currentDropTarget);
        }

        args.Handled = true;
    }

    private void FinishTaskDrag(PointerRoutedEventArgs args)
    {
        if (_dragCandidateTask is null)
        {
            return;
        }

        var handled = _isTaskDragging;
        CompleteTaskDrag(applyDrop: true);
        args.Handled = handled;
    }

    private void CompleteTaskDrag(bool applyDrop)
    {
        if (_dragCandidateTask is null)
        {
            return;
        }

        var draggedTask = _dragCandidateTask;
        var wasTaskDragging = _isTaskDragging;
        var dropTarget = _currentDropTarget;
        if (wasTaskDragging)
        {
            SuppressTaskClickFromDrag(draggedTask.Id);
        }

        CancelTaskDrag();
        if (applyDrop && wasTaskDragging && dropTarget is TodoDropTarget target)
        {
            ApplyTaskDrop(draggedTask, target);
        }
    }

    private void BeginTaskDrag()
    {
        _taskDragTimer?.Stop();
        if (_dragCandidateTask is null || _dragCandidateRow is null)
        {
            CancelTaskDrag();
            return;
        }

        _isTaskDragging = true;
        _dragCandidateRow.Opacity = 0.58;
        _currentDropTarget = DropTargetFromPoint(_taskDragCurrentPoint);
        ApplyDropTargetVisual(_currentDropTarget);
        StartTaskAutoScroll();
    }

    private void CancelTaskDrag()
    {
        _taskDragTimer?.Stop();
        _taskDragTimer = null;
        StopTaskAutoScroll();
        RemoveDropPreview();

        foreach (var row in _taskRows)
        {
            ResetTaskRowChrome(row);
        }

        var capturedRow = _dragCandidateRow;
        _dragCandidateTask = null;
        _dragCandidateRow = null;
        _currentDropTarget = null;
        _isTaskDragging = false;
        capturedRow?.ReleasePointerCaptures();
    }

    private void StartTaskAutoScroll()
    {
        _taskAutoScrollTimer ??= DispatcherQueue.CreateTimer();
        _taskAutoScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _taskAutoScrollTimer.Tick -= OnTaskAutoScrollTick;
        _taskAutoScrollTimer.Tick += OnTaskAutoScrollTick;
        _taskAutoScrollTimer.Start();
    }

    private void StopTaskAutoScroll()
    {
        _taskAutoScrollTimer?.Stop();
    }

    private void OnTaskAutoScrollTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_isTaskDragging || _taskScroll.ActualHeight <= 0)
        {
            StopTaskAutoScroll();
            return;
        }

        var origin = _taskScroll.TransformToVisual(_root).TransformPoint(new Point(0, 0));
        var top = origin.Y;
        var bottom = top + _taskScroll.ActualHeight;
        var distance = _taskDragCurrentPoint.Y < top
            ? _taskDragCurrentPoint.Y - top
            : _taskDragCurrentPoint.Y > bottom
                ? _taskDragCurrentPoint.Y - bottom
                : 0;
        if (Math.Abs(distance) < double.Epsilon)
        {
            return;
        }

        var ratio = Math.Clamp(Math.Abs(distance) / TaskAutoScrollActivationDistance, 0, 1);
        var speed = TaskAutoScrollMinimumSpeed + (TaskAutoScrollMaximumSpeed - TaskAutoScrollMinimumSpeed) * ratio;
        var scrollRange = TaskAutoScrollRange();
        var nextOffset = Math.Clamp(
            _taskScroll.VerticalOffset + Math.Sign(distance) * speed * 0.016,
            scrollRange.Minimum,
            scrollRange.Maximum);
        if (Math.Abs(nextOffset - _taskScroll.VerticalOffset) < 0.01)
        {
            return;
        }

        _taskScroll.ChangeView(null, nextOffset, null, disableAnimation: true);
        var nextTarget = DropTargetFromPoint(_taskDragCurrentPoint);
        if (nextTarget is null && _currentDropTarget is not null && IsPointInsideDropPreview(_taskDragCurrentPoint))
        {
            nextTarget = _currentDropTarget;
        }

        if (!DropTargetsEqual(_currentDropTarget, nextTarget))
        {
            _currentDropTarget = nextTarget;
            ApplyDropTargetVisual(_currentDropTarget);
        }
    }

    private (double Minimum, double Maximum) TaskAutoScrollRange()
    {
        var maximum = Math.Max(0, _taskScroll.ScrollableHeight);
        if (_dragCandidateTask is null ||
            _activeTaskSection is null ||
            _completedTaskSection is null ||
            !TryGetElementBounds(_completedTaskSection, out var completedBounds))
        {
            return (0, maximum);
        }

        var scrollOrigin = _taskScroll.TransformToVisual(_root).TransformPoint(new Point(0, 0));
        var completedTop = Math.Clamp(
            completedBounds.Top - scrollOrigin.Y + _taskScroll.VerticalOffset,
            0,
            maximum);
        if (_dragCandidateTask.IsCompleted)
        {
            return (Math.Clamp(completedTop - TaskAutoScrollSectionOverlap, 0, maximum), maximum);
        }

        var activeMaximum = Math.Clamp(
            completedTop - _taskScroll.ActualHeight + TaskAutoScrollSectionOverlap,
            0,
            maximum);
        return (0, activeMaximum);
    }

    private void SuppressTaskClickFromDrag(string taskId)
    {
        _suppressTaskClickId = taskId;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (string.Equals(_suppressTaskClickId, taskId, StringComparison.Ordinal))
            {
                _suppressTaskClickId = null;
            }
        });
    }

    private TodoDropTarget? DropTargetFromPoint(Point point)
    {
        if (_dragCandidateTask is null)
        {
            return null;
        }

        foreach (var row in _taskRows)
        {
            if (ReferenceEquals(row, _dragCandidateRow) ||
                row.Tag is not string taskId ||
                !TryGetTaskRowBounds(row, out var bounds) ||
                point.Y < bounds.Top ||
                point.Y > bounds.Bottom)
            {
                continue;
            }

            var targetTask = FindTask(taskId);
            if (targetTask is null || targetTask.IsCompleted != _dragCandidateTask.IsCompleted)
            {
                continue;
            }

            var ratio = bounds.Height <= 0 ? 0.5 : (point.Y - bounds.Top) / bounds.Height;
            var placement = ratio < TaskDropEdgeRatio
                ? TodoDropPlacement.Before
                : ratio > 1 - TaskDropEdgeRatio
                    ? TodoDropPlacement.After
                    : TodoDropPlacement.Child;
            var target = placement == TodoDropPlacement.After
                ? DropTargetAfterVisibleRow(row, targetTask) ?? new TodoDropTarget(taskId, placement)
                : new TodoDropTarget(taskId, placement);
            return IsValidTaskDrop(_dragCandidateTask, target) ? target : null;
        }

        return DropTargetBetweenRows(point);
    }

    private TodoDropTarget? DropTargetBetweenRows(Point point)
    {
        if (_dragCandidateTask is null)
        {
            return null;
        }

        var rowHits = new List<(Button Row, Panel Host, TodoTask Task, Rect Bounds)>();
        foreach (var row in _taskRows)
        {
            if (row.Tag is not string taskId ||
                row.Parent is not Panel host ||
                !TryGetTaskRowBounds(row, out var bounds))
            {
                continue;
            }

            var task = FindTask(taskId);
            if (task is not null)
            {
                rowHits.Add((row, host, task, bounds));
            }
        }

        foreach (var group in rowHits.GroupBy(hit => hit.Host))
        {
            var orderedRows = group
                .OrderBy(hit => hit.Bounds.Top)
                .ToList();
            for (var index = 1; index < orderedRows.Count; index++)
            {
                var upper = orderedRows[index - 1];
                var lower = orderedRows[index];
                var gapTop = Math.Min(upper.Bounds.Bottom, lower.Bounds.Top);
                var gapBottom = Math.Max(upper.Bounds.Bottom, lower.Bounds.Top);
                if (gapBottom - gapTop < 2 ||
                    point.Y < gapTop ||
                    point.Y > gapBottom)
                {
                    continue;
                }

                if (ReferenceEquals(lower.Row, _dragCandidateRow))
                {
                    if (lower.Task.IsCompleted != _dragCandidateTask.IsCompleted)
                    {
                        continue;
                    }

                    var beforeDragged = new TodoDropTarget(lower.Task.Id, TodoDropPlacement.Before);
                    return IsValidTaskDrop(_dragCandidateTask, beforeDragged) ? beforeDragged : null;
                }

                if (lower.Task.IsCompleted != _dragCandidateTask.IsCompleted)
                {
                    continue;
                }

                var target = new TodoDropTarget(lower.Task.Id, TodoDropPlacement.Before);
                return IsValidTaskDrop(_dragCandidateTask, target) ? target : null;
            }

            var bottomTarget = DropTargetAtHostBottom(point, orderedRows);
            if (bottomTarget is not null)
            {
                return bottomTarget;
            }
        }

        return null;
    }

    private TodoDropTarget? DropTargetAtHostBottom(
        Point point,
        IReadOnlyList<(Button Row, Panel Host, TodoTask Task, Rect Bounds)> orderedRows)
    {
        if (_dragCandidateTask is null || orderedRows.Count == 0)
        {
            return null;
        }

        var last = orderedRows[^1];
        if (last.Task.IsCompleted != _dragCandidateTask.IsCompleted ||
            !TryGetBottomDropBoundary(last.Host, last.Row, last.Bounds, out var bottomBoundary) ||
            bottomBoundary - last.Bounds.Bottom < 2 ||
            point.Y < last.Bounds.Bottom ||
            point.Y > bottomBoundary)
        {
            return null;
        }

        var target = new TodoDropTarget(last.Task.Id, TodoDropPlacement.TopLevelEnd);
        return IsValidTaskDrop(_dragCandidateTask, target) ? target : null;
    }

    private bool TryGetBottomDropBoundary(Panel host, UIElement lastRow, Rect lastBounds, out double bottomBoundary)
    {
        bottomBoundary = lastBounds.Bottom;
        var lastIndex = host.Children.IndexOf(lastRow);
        if (TryGetNextVisibleSiblingTop(host, lastIndex, lastBounds.Bottom, out bottomBoundary))
        {
            return true;
        }

        if (host.Parent is Panel parent)
        {
            var hostIndex = parent.Children.IndexOf(host);
            if (TryGetNextVisibleSiblingTop(parent, hostIndex, lastBounds.Bottom, out bottomBoundary))
            {
                return true;
            }
        }

        bottomBoundary = lastBounds.Bottom + Math.Max(16, lastBounds.Height * TaskDropEdgeRatio);
        return true;
    }

    private bool TryGetNextVisibleSiblingTop(Panel host, int startIndex, double afterY, out double top)
    {
        top = afterY;
        if (startIndex < 0)
        {
            return false;
        }

        for (var index = startIndex + 1; index < host.Children.Count; index++)
        {
            if (ReferenceEquals(host.Children[index], _dropPreview) ||
                host.Children[index] is not FrameworkElement next ||
                next.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (TryGetElementBounds(next, out var nextBounds) && nextBounds.Top > afterY)
            {
                top = nextBounds.Top;
                return true;
            }
        }

        return false;
    }

    private TodoDropTarget? DropTargetAfterVisibleRow(Button upperRow, TodoTask upperTask)
    {
        if (upperRow.Parent is not Panel host ||
            _dragCandidateTask is null)
        {
            return null;
        }

        var upperIndex = host.Children.IndexOf(upperRow);
        if (upperIndex < 0)
        {
            return null;
        }

        var upperDepth = TodoQuery.TaskIndentDepth(_data, upperTask);
        for (var index = upperIndex + 1; index < host.Children.Count; index++)
        {
            if (ReferenceEquals(host.Children[index], _dropPreview))
            {
                continue;
            }

            if (host.Children[index] is not Button lowerRow ||
                lowerRow.Tag is not string lowerTaskId)
            {
                continue;
            }

            var lowerTask = FindTask(lowerTaskId);
            if (lowerTask is null)
            {
                continue;
            }

            if (lowerTask.IsCompleted != _dragCandidateTask.IsCompleted)
            {
                return null;
            }

            var lowerDepth = TodoQuery.TaskIndentDepth(_data, lowerTask);
            return lowerDepth > upperDepth
                ? new TodoDropTarget(lowerTask.Id, TodoDropPlacement.Before)
                : null;
        }

        return null;
    }

    private void ApplyDropTargetVisual(TodoDropTarget? target)
    {
        RemoveDropPreview();
        if (target is null)
        {
            return;
        }

        var row = RowForTask(target.TaskId);
        if (row is null)
        {
            return;
        }

        InsertDropPreview(target, row);
    }

    private void InsertDropPreview(TodoDropTarget target, Button targetRow)
    {
        if (targetRow.Parent is not Panel host ||
            targetRow.Tag is not string targetTaskId)
        {
            return;
        }

        var targetTask = FindTask(targetTaskId);
        if (targetTask is null)
        {
            return;
        }

        var targetDepth = TodoQuery.TaskIndentDepth(_data, targetTask);
        var previewDepth = target.Placement == TodoDropPlacement.TopLevelEnd
            ? 0
            : target.Placement == TodoDropPlacement.Child
                ? Math.Clamp(targetDepth + 1, 0, TodoQuery.MaxTaskTreeDepth - 1)
                : targetDepth;
        var insertIndex = InsertIndexForDrop(host, targetRow, target, targetDepth);
        var preview = CreateDropPreview(target, targetTask, previewDepth);

        _dropPreview = preview;
        _dropPreviewHost = host;
        host.Children.Insert(insertIndex, preview);
    }

    private int InsertIndexForDrop(Panel host, Button targetRow, TodoDropTarget target, int targetDepth)
    {
        var index = host.Children.IndexOf(targetRow);
        if (index < 0)
        {
            return host.Children.Count;
        }

        if (target.Placement is TodoDropPlacement.TopLevelEnd or TodoDropPlacement.Child)
        {
            return Math.Min(index + 1, host.Children.Count);
        }

        if (target.Placement == TodoDropPlacement.Before)
        {
            return index;
        }

        return IndexAfterVisibleSubtree(host, index, targetDepth);
    }

    private int IndexAfterVisibleSubtree(Panel host, int startIndex, int targetDepth)
    {
        var insertIndex = startIndex + 1;
        for (var index = startIndex + 1; index < host.Children.Count; index++)
        {
            if (host.Children[index] is not Button row ||
                row.Tag is not string taskId)
            {
                break;
            }

            var task = FindTask(taskId);
            if (task is null || TodoQuery.TaskIndentDepth(_data, task) <= targetDepth)
            {
                break;
            }

            if (IsDraggedTaskOrDescendant(task))
            {
                continue;
            }

            insertIndex = index + 1;
        }

        return insertIndex;
    }

    private bool IsDraggedTaskOrDescendant(TodoTask task)
    {
        if (_dragCandidateTask is null)
        {
            return false;
        }

        var current = task;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(current.Id))
        {
            if (string.Equals(current.Id, _dragCandidateTask.Id, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(current.ParentTaskId))
            {
                return false;
            }

            var parent = FindTask(current.ParentTaskId);
            if (parent is null)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private FrameworkElement CreateDropPreview(TodoDropTarget target, TodoTask targetTask, int previewDepth)
    {
        var completed = _dragCandidateTask?.IsCompleted == true;
        var preview = new Grid
        {
            Height = completed ? 34 : 44,
            Margin = new Thickness(previewDepth * 22, 0, 0, completed ? 8 : 10),
            IsHitTestVisible = false
        };

        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            Stroke = AccentBrush(),
            StrokeThickness = 1.6,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = Brush(0xEEF9FA)
        });
        preview.Children.Add(new TextBlock
        {
            Text = DropPreviewText(target, targetTask),
            Margin = new Thickness(12, 0, 12, 0),
            FontSize = 12,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = AccentDarkBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        return preview;
    }

    private string DropPreviewText(TodoDropTarget target, TodoTask targetTask)
    {
        if (target.Placement == TodoDropPlacement.Child)
        {
            return $"松手后作为“{targetTask.Title}”的子任务";
        }

        if (target.Placement == TodoDropPlacement.TopLevelEnd)
        {
            return "松手后放到顶层任务末尾";
        }

        var parent = string.IsNullOrWhiteSpace(targetTask.ParentTaskId)
            ? null
            : FindTask(targetTask.ParentTaskId);
        var parentName = parent?.Title ?? "顶层任务";
        return target.Placement == TodoDropPlacement.Before
            ? $"松手后放在“{parentName}”下，位于目标之前"
            : $"松手后放在“{parentName}”下，位于目标之后";
    }

    private bool IsPointInsideDropPreview(Point point)
    {
        return _dropPreview is not null &&
            _dropPreview.Visibility == Visibility.Visible &&
            TryGetElementBounds(_dropPreview, out var bounds) &&
            bounds.Contains(point);
    }

    private void RemoveDropPreview()
    {
        if (_dropPreviewHost is not null && _dropPreview is not null)
        {
            _dropPreviewHost.Children.Remove(_dropPreview);
        }

        _dropPreview = null;
        _dropPreviewHost = null;
    }

    private static bool DropTargetsEqual(TodoDropTarget? first, TodoDropTarget? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return first.Placement == second.Placement &&
            string.Equals(first.TaskId, second.TaskId, StringComparison.Ordinal);
    }

    private static void ResetTaskRowChrome(Button row)
    {
        row.Opacity = 1;
    }

    private void ApplyTaskDrop(TodoTask draggedTask, TodoDropTarget target)
    {
        if (!IsValidTaskDrop(draggedTask, target))
        {
            return;
        }

        var targetTask = FindTask(target.TaskId);
        if (targetTask is null)
        {
            return;
        }

        var oldParentId = draggedTask.ParentTaskId;
        var newParentId = ParentIdForDropTarget(targetTask, target.Placement);
        var parentChanged = !ParentIdsEqual(oldParentId, newParentId);

        draggedTask.ParentTaskId = string.IsNullOrWhiteSpace(newParentId) ? null : newParentId;
        if (parentChanged)
        {
            MoveSubtreeToList(draggedTask.Id, ResolveDropListId(targetTask, newParentId));
        }

        if (target.Placement == TodoDropPlacement.TopLevelEnd)
        {
            if (parentChanged)
            {
                ReassignSiblingSortOrders(oldParentId, draggedTask.IsCompleted);
            }

            ReorderTaskAsLastChild(draggedTask, newParentId);
        }
        else if (target.Placement == TodoDropPlacement.Child)
        {
            _settings.CollapsedTaskIds.RemoveAll(id => string.Equals(id, targetTask.Id, StringComparison.Ordinal));
            ReorderTaskAsFirstChild(draggedTask, newParentId);
        }
        else
        {
            ReorderTaskAroundTarget(draggedTask, targetTask, target.Placement, oldParentId, newParentId);
        }

        Touch(draggedTask);
        _selectedTaskId = draggedTask.Id;
        SaveDataAndRefresh();
    }

    private bool IsValidTaskDrop(TodoTask draggedTask, TodoDropTarget target)
    {
        var targetTask = FindTask(target.TaskId);
        if (targetTask is null ||
            draggedTask.IsCompleted != targetTask.IsCompleted)
        {
            return false;
        }

        if (target.Placement != TodoDropPlacement.TopLevelEnd &&
            string.Equals(draggedTask.Id, targetTask.Id, StringComparison.Ordinal))
        {
            return false;
        }

        var descendantIds = TodoQuery.DescendantIds(_data, draggedTask.Id).ToHashSet(StringComparer.Ordinal);
        if (target.Placement != TodoDropPlacement.TopLevelEnd &&
            descendantIds.Contains(targetTask.Id))
        {
            return false;
        }

        var newParentId = ParentIdForDropTarget(targetTask, target.Placement);
        if (string.Equals(newParentId, draggedTask.Id, StringComparison.Ordinal) ||
            (newParentId is not null && descendantIds.Contains(newParentId)))
        {
            return false;
        }

        if (IsOriginalDropPosition(draggedTask, targetTask, target.Placement, newParentId))
        {
            return false;
        }

        return CanMoveTaskToParent(draggedTask, newParentId);
    }

    private static string? ParentIdForDropTarget(TodoTask targetTask, TodoDropPlacement placement)
    {
        return placement switch
        {
            TodoDropPlacement.Child => targetTask.Id,
            TodoDropPlacement.TopLevelEnd => null,
            _ => targetTask.ParentTaskId
        };
    }

    private bool IsOriginalDropPosition(
        TodoTask draggedTask,
        TodoTask targetTask,
        TodoDropPlacement placement,
        string? newParentId)
    {
        if (placement == TodoDropPlacement.TopLevelEnd)
        {
            if (!ParentIdsEqual(draggedTask.ParentTaskId, null))
            {
                return false;
            }

            var rootSiblings = OrderedSiblings(null, draggedTask.IsCompleted);
            return rootSiblings.Count > 0 &&
                string.Equals(rootSiblings[^1].Id, draggedTask.Id, StringComparison.Ordinal);
        }

        if (placement == TodoDropPlacement.Child)
        {
            if (!ParentIdsEqual(draggedTask.ParentTaskId, targetTask.Id))
            {
                return false;
            }

            var childSiblings = OrderedSiblings(targetTask.Id, draggedTask.IsCompleted);
            return childSiblings.Count > 0 &&
                string.Equals(childSiblings[0].Id, draggedTask.Id, StringComparison.Ordinal);
        }

        if (!ParentIdsEqual(draggedTask.ParentTaskId, newParentId))
        {
            return false;
        }

        var siblings = OrderedSiblings(newParentId, draggedTask.IsCompleted);
        var draggedIndex = siblings.FindIndex(task => string.Equals(task.Id, draggedTask.Id, StringComparison.Ordinal));
        var targetIndex = siblings.FindIndex(task => string.Equals(task.Id, targetTask.Id, StringComparison.Ordinal));
        if (draggedIndex < 0 || targetIndex < 0)
        {
            return false;
        }

        return placement == TodoDropPlacement.Before
            ? draggedIndex + 1 == targetIndex
            : draggedIndex - 1 == targetIndex;
    }

    private bool CanMoveTaskToParent(TodoTask draggedTask, string? newParentId)
    {
        var newRootDepth = 1;
        if (!string.IsNullOrWhiteSpace(newParentId))
        {
            var parent = FindTask(newParentId);
            if (parent is null)
            {
                return false;
            }

            newRootDepth = TodoQuery.TaskDepth(_data, parent) + 1;
            var childCount = _data.Tasks.Count(task =>
                !string.Equals(task.Id, draggedTask.Id, StringComparison.Ordinal) &&
                string.Equals(task.ParentTaskId, newParentId, StringComparison.Ordinal));
            if (childCount >= TodoQuery.MaxChildTasksPerTask)
            {
                return false;
            }
        }

        return newRootDepth + MaxDescendantOffset(draggedTask.Id) <= TodoQuery.MaxTaskTreeDepth;
    }

    private int MaxDescendantOffset(string taskId)
    {
        var childrenByParent = _data.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        return MaxDescendantOffset(taskId, childrenByParent, new HashSet<string>(StringComparer.Ordinal));
    }

    private static int MaxDescendantOffset(
        string taskId,
        IReadOnlyDictionary<string, List<TodoTask>> childrenByParent,
        HashSet<string> seen)
    {
        if (!seen.Add(taskId) || !childrenByParent.TryGetValue(taskId, out var children))
        {
            return 0;
        }

        var max = 0;
        foreach (var child in children)
        {
            max = Math.Max(max, 1 + MaxDescendantOffset(child.Id, childrenByParent, seen));
        }

        return max;
    }

    private void ReorderTaskAroundTarget(
        TodoTask draggedTask,
        TodoTask targetTask,
        TodoDropPlacement placement,
        string? oldParentId,
        string? newParentId)
    {
        if (!ParentIdsEqual(oldParentId, newParentId))
        {
            ReassignSiblingSortOrders(oldParentId, draggedTask.IsCompleted);
        }

        var siblings = OrderedSiblings(newParentId, draggedTask.IsCompleted)
            .Where(task => !string.Equals(task.Id, draggedTask.Id, StringComparison.Ordinal))
            .ToList();
        var targetIndex = siblings.FindIndex(task => string.Equals(task.Id, targetTask.Id, StringComparison.Ordinal));
        if (targetIndex < 0)
        {
            siblings.Add(draggedTask);
        }
        else
        {
            siblings.Insert(placement == TodoDropPlacement.After ? targetIndex + 1 : targetIndex, draggedTask);
        }

        ReassignSiblingSortOrders(siblings);
    }

    private void ReorderTaskAsLastChild(TodoTask draggedTask, string? newParentId)
    {
        var siblings = OrderedSiblings(newParentId, draggedTask.IsCompleted)
            .Where(task => !string.Equals(task.Id, draggedTask.Id, StringComparison.Ordinal))
            .Append(draggedTask)
            .ToList();
        ReassignSiblingSortOrders(siblings);
    }

    private void ReorderTaskAsFirstChild(TodoTask draggedTask, string? newParentId)
    {
        var siblings = OrderedSiblings(newParentId, draggedTask.IsCompleted)
            .Where(task => !string.Equals(task.Id, draggedTask.Id, StringComparison.Ordinal))
            .Prepend(draggedTask)
            .ToList();
        ReassignSiblingSortOrders(siblings);
    }

    private void ReassignSiblingSortOrders(string? parentTaskId, bool completed)
    {
        ReassignSiblingSortOrders(OrderedSiblings(parentTaskId, completed));
    }

    private static void ReassignSiblingSortOrders(IReadOnlyList<TodoTask> siblings)
    {
        var order = 1000.0;
        foreach (var task in siblings)
        {
            task.SortOrder = order;
            order += 1000.0;
        }
    }

    private List<TodoTask> OrderedSiblings(string? parentTaskId, bool completed)
    {
        return _data.Tasks
            .Where(task => task.IsCompleted == completed && ParentIdsEqual(task.ParentTaskId, parentTaskId))
            .OrderBy(task => task.SortOrder <= 0 ? double.MaxValue : task.SortOrder)
            .ThenBy(task => task.CreatedAt)
            .ToList();
    }

    private void MoveSubtreeToList(string taskId, string listId)
    {
        var moveIds = TodoQuery.DescendantIds(_data, taskId)
            .Append(taskId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var task in _data.Tasks.Where(task => moveIds.Contains(task.Id)))
        {
            task.ListId = listId;
            Touch(task);
        }
    }

    private string ResolveDropListId(TodoTask targetTask, string? newParentId)
    {
        if (!string.IsNullOrWhiteSpace(newParentId))
        {
            return FindTask(newParentId)?.ListId ?? targetTask.ListId;
        }

        return targetTask.ListId;
    }

    private TodoTask? FindTask(string taskId)
    {
        return _data.Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
    }

    private Button? RowForTask(string taskId)
    {
        return _taskRows.FirstOrDefault(row => row.Tag is string rowTaskId &&
            string.Equals(rowTaskId, taskId, StringComparison.Ordinal));
    }

    private bool TryGetTaskRowBounds(Button row, out Rect bounds)
    {
        return TryGetElementBounds(row, out bounds);
    }

    private bool TryGetElementBounds(FrameworkElement element, out Rect bounds)
    {
        try
        {
            var topLeft = element.TransformToVisual(_root).TransformPoint(new Point(0, 0));
            bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (Exception)
        {
            bounds = Rect.Empty;
            return false;
        }
    }

    private static bool IsTaskDragIgnoredSource(DependencyObject? source, DependencyObject stopAt)
    {
        while (source is not null && !ReferenceEquals(source, stopAt))
        {
            if (source is ButtonBase or TextBox or Slider or Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool ParentIdsEqual(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
    }

    private static double Distance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void RefreshDetail()
    {
        if (_currentViewId == TodoViewIds.RecycleBin)
        {
            _detailHost.Child = EmptyDetail();
            return;
        }

        var task = SelectedTask();
        _detailHost.Child = task is null ? EmptyDetail() : DetailContent(task);
    }

    private UIElement EmptyDetail()
    {
        return new Grid
        {
            Padding = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = "选择一个任务查看详情",
                    FontSize = 16,
                    Foreground = SecondaryTextBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private UIElement DetailContent(TodoTask task)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var panel = new StackPanel
        {
            Padding = new Thickness(28, 78, 28, 24),
            Spacing = 18
        };

        var titleGrid = new Grid { ColumnSpacing = 8 };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBox = new TextBox
        {
            Text = task.Title,
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Padding = new Thickness(0),
            FontSize = 25,
            FontWeight = MuxFontWeights.SemiBold,
            MinHeight = 42,
            TextWrapping = TextWrapping.Wrap
        };
        ApplyFlatTextBoxStyle(titleBox);
        titleBox.LostFocus += (_, _) => UpdateTaskTitle(task, titleBox.Text);
        titleBox.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                UpdateTaskTitle(task, titleBox.Text);
                args.Handled = true;
            }
        };
        titleGrid.Children.Add(titleBox);
        var important = RowIconButton(task.IsImportant ? "\uE735" : "\uE734", task.IsImportant ? "取消重要" : "标为重要", task.IsImportant ? Brush(0xF2B01E) : SecondaryTextBrush());
        important.Click += (_, _) => ToggleImportant(task);
        Grid.SetColumn(important, 1);
        titleGrid.Children.Add(important);
        var more = RowIconButton("\uE712", "更多", SecondaryTextBrush());
        Grid.SetColumn(more, 2);
        titleGrid.Children.Add(more);
        var close = RowIconButton("\uE711", "关闭详情", SecondaryTextBrush());
        close.Click += (_, _) =>
        {
            _selectedTaskId = null;
            SaveSettings();
            RefreshTaskContent();
            RefreshDetail();
        };
        Grid.SetColumn(close, 3);
        titleGrid.Children.Add(close);
        panel.Children.Add(titleGrid);

        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9
        };
        status.Children.Add(new FontIcon
        {
            Glyph = task.IsCompleted ? "\uE73E" : "\uE73A",
            FontSize = 16,
            Foreground = task.IsCompleted ? Brush(0x138A43) : SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });
        status.Children.Add(new TextBlock
        {
            Text = task.IsCompleted ? "已完成" : "未完成",
            FontSize = 14,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = task.IsCompleted ? Brush(0x138A43) : SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (task.CompletedAt.HasValue)
        {
            var completedLocal = task.CompletedAt.Value.ToLocalTime();
            status.Children.Add(new TextBlock
            {
                Text = completedLocal.Date == DateTimeOffset.Now.Date
                    ? $"今天 {completedLocal:HH:mm} 完成"
                    : $"{completedLocal:MM-dd HH:mm} 完成",
                FontSize = 13,
                Foreground = SecondaryTextBrush(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            });
        }
        panel.Children.Add(status);

        if (task.IsCompleted)
        {
            var originalCompletedAt = task.CompletedAt ?? DateTimeOffset.Now;
            var completedLocal = originalCompletedAt.ToLocalTime();
            var completedDatePicker = new CalendarDatePicker
            {
                Date = new DateTimeOffset(completedLocal.Date),
                PlaceholderText = "选择完成日期",
                Width = CompletedTimestampEditorWidth,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var completedTimePicker = new TimePicker
            {
                Time = completedLocal.TimeOfDay,
                ClockIdentifier = "24HourClock",
                Width = CompletedTimestampEditorWidth,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var completionActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Visibility = Visibility.Collapsed
            };
            var discardCompletionChange = PillButton("撤销修改", "\uE711");
            discardCompletionChange.MinWidth = 112;
            var applyCompletionChange = PrimaryButton("应用修改", "\uE73E");
            applyCompletionChange.MinWidth = 112;
            completionActions.Children.Add(discardCompletionChange);
            completionActions.Children.Add(applyCompletionChange);
            var isSynchronizingCompletionDraft = false;
            void DiscardCompletionDraft()
            {
                isSynchronizingCompletionDraft = true;
                completedDatePicker.Date = new DateTimeOffset(originalCompletedAt.ToLocalTime().Date);
                completedTimePicker.Time = originalCompletedAt.ToLocalTime().TimeOfDay;
                isSynchronizingCompletionDraft = false;
                completionActions.Visibility = Visibility.Collapsed;
            }
            void MarkCompletionDraftChanged()
            {
                if (!isSynchronizingCompletionDraft)
                {
                    completionActions.Visibility = Visibility.Visible;
                }
            }
            void ApplyCompletionDraft()
            {
                if (!task.IsCompleted || completedDatePicker.Date is not { } completedDate)
                {
                    return;
                }

                var localCompletedAt = completedDate.DateTime.Date.Add(completedTimePicker.Time);
                task.CompletedAt = new DateTimeOffset(
                    localCompletedAt,
                    TimeZoneInfo.Local.GetUtcOffset(localCompletedAt));
                Touch(task);
                SaveDataAndRefresh();
            }

            completedDatePicker.DateChanged += (_, _) => MarkCompletionDraftChanged();
            completedTimePicker.TimeChanged += (_, _) => MarkCompletionDraftChanged();
            discardCompletionChange.Click += (_, _) => DiscardCompletionDraft();
            applyCompletionChange.Click += (_, _) => ApplyCompletionDraft();
            panel.Children.Add(DetailField("\uE787", "完成日期", completedDatePicker, CompletedTimestampEditorWidth));
            panel.Children.Add(DetailField("\uE916", "完成时间", completedTimePicker, CompletedTimestampEditorWidth));
            panel.Children.Add(completionActions);
        }

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(0xDCE7EA),
            Margin = new Thickness(0, 22, 0, 4)
        });

        var listBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var list in OrderedLists())
        {
            var item = new ComboBoxItem
            {
                Content = list.Name,
                Tag = list.Id
            };
            listBox.Items.Add(item);
            if (string.Equals(task.ListId, list.Id, StringComparison.Ordinal))
            {
                listBox.SelectedItem = item;
            }
        }
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is ComboBoxItem { Tag: string listId } &&
                !string.Equals(task.ListId, listId, StringComparison.Ordinal))
            {
                task.ListId = listId;
                Touch(task);
                SaveDataAndRefresh();
            }
        };
        panel.Children.Add(DetailField("\uE8B7", "所属清单", listBox));

        var startPicker = new CalendarDatePicker
        {
            Date = new DateTimeOffset(task.StartDate.Date),
            PlaceholderText = "选择开始时间",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        startPicker.DateChanged += (_, args) =>
        {
            task.StartDate = args.NewDate?.DateTime.Date ?? DateTime.Today;
            Touch(task);
            SaveDataAndRefresh();
        };
        panel.Children.Add(DetailField("\uE163", "开始时间", startPicker));

        var dateGrid = new Grid { ColumnSpacing = 8 };
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var datePicker = new CalendarDatePicker
        {
            Date = task.DueDate.HasValue ? new DateTimeOffset(task.DueDate.Value) : null,
            PlaceholderText = "选择日期",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        datePicker.DateChanged += (_, args) =>
        {
            task.DueDate = args.NewDate?.DateTime.Date;
            Touch(task);
            SaveDataAndRefresh();
        };
        dateGrid.Children.Add(datePicker);
        var clearDate = IconOnlyButton("\uE711", "清除日期");
        clearDate.Click += (_, _) =>
        {
            task.DueDate = null;
            Touch(task);
            SaveDataAndRefresh();
        };
        Grid.SetColumn(clearDate, 1);
        dateGrid.Children.Add(clearDate);
        panel.Children.Add(DetailField("\uE787", "截止日期", dateGrid));

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(0xE7EEF0),
            Margin = new Thickness(0, 2, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "备注",
            FontSize = 14,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = TextBrush()
        });
        var notes = new TextBox
        {
            Text = task.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            PlaceholderText = "添加备注"
        };
        notes.LostFocus += (_, _) =>
        {
            task.Notes = notes.Text.Trim();
            Touch(task);
            _store.SaveData(_data);
        };
        panel.Children.Add(notes);

        scroll.Content = panel;
        root.Children.Add(scroll);

        var actions = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(28, 16, 28, 26),
            Background = Brush(0xFFFFFF)
        };
        var addChild = PrimaryButton("添加子任务", "\uE710");
        var childBlockedReason = TodoQuery.AddChildBlockedReason(_data, task);
        addChild.IsEnabled = string.IsNullOrWhiteSpace(childBlockedReason);
        if (!addChild.IsEnabled)
        {
            ToolTipService.SetToolTip(addChild, childBlockedReason);
        }

        addChild.Click += async (_, _) => await ShowAddSubtaskDialogAsync(task);
        actions.Children.Add(addChild);

        if (task.IsCompleted)
        {
            var restore = PrimaryButton("恢复任务", "\uE777");
            restore.Click += (_, _) => SetTaskCompleted(task, false);
            actions.Children.Add(restore);
        }
        else
        {
            var complete = PrimaryButton("完成任务", "\uE73E");
            complete.Click += async (_, _) => await ToggleTaskCompletedAsync(task);
            actions.Children.Add(complete);
        }

        var delete = DangerButton("删除任务", "\uE74D");
        delete.Click += async (_, _) => await DeleteTaskAsync(task);
        actions.Children.Add(delete);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        return root;
    }

    private async Task AddTaskFromInputAsync()
    {
        var title = _addTaskBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await ShowAddTaskDialogAsync();
            return;
        }

        AddTask(
            title,
            DefaultListIdForNewTask(),
            _currentViewId == TodoViewIds.Important,
            DateTime.Today,
            null);
        _addTaskBox.Text = string.Empty;
    }

    private void AddTask(
        string title,
        string listId,
        bool isImportant,
        DateTime startDate,
        DateTime? dueDate,
        string? parentTaskId = null,
        string? notes = null)
    {
        var now = DateTimeOffset.Now;
        parentTaskId = NormalizeParentTaskId(parentTaskId);
        var task = new TodoTask
        {
            Id = TodoStore.NewId("task"),
            Title = title,
            ListId = ListExists(listId) ? listId : DefaultListId(),
            ParentTaskId = parentTaskId,
            IsImportant = isImportant,
            StartDate = startDate.Date,
            DueDate = dueDate?.Date,
            Notes = notes?.Trim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        _data.Tasks.Add(task);
        InsertNewTaskAsFirstChild(task);
        _selectedTaskId = task.Id;
        if (_currentViewId == TodoViewIds.Completed)
        {
            _currentViewId = TodoViewIds.All;
        }

        SaveDataAndRefresh();
    }

    private void InsertNewTaskAsFirstChild(TodoTask task)
    {
        if (string.IsNullOrWhiteSpace(task.ParentTaskId))
        {
            return;
        }

        var siblingIndex = 2;
        foreach (var sibling in _data.Tasks
                     .Where(candidate => candidate.Id != task.Id &&
                         string.Equals(candidate.ParentTaskId, task.ParentTaskId, StringComparison.Ordinal))
                     .OrderBy(candidate => candidate.SortOrder <= 0 ? double.MaxValue : candidate.SortOrder)
                     .ThenBy(candidate => candidate.CreatedAt))
        {
            sibling.SortOrder = siblingIndex * 1000;
            siblingIndex++;
        }

        task.SortOrder = 1000;
        _settings.CollapsedTaskIds.RemoveAll(id => string.Equals(id, task.ParentTaskId, StringComparison.Ordinal));
    }

    private string? NormalizeParentTaskId(string? parentTaskId)
    {
        if (string.IsNullOrWhiteSpace(parentTaskId))
        {
            return null;
        }

        var parent = _data.Tasks.FirstOrDefault(task => string.Equals(task.Id, parentTaskId, StringComparison.Ordinal));
        return parent is not null && TodoQuery.CanAddChild(_data, parent)
            ? parent.Id
            : null;
    }

    private async Task ShowAddTaskDialogAsync()
    {
        var titleBox = new TextBox
        {
            PlaceholderText = "任务名称",
            MinWidth = 340,
            FontSize = 15
        };
        var notesBox = new TextBox
        {
            PlaceholderText = "备注（可选）",
            MinWidth = 340,
            MinHeight = 96,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };

        var listBox = BuildListComboBox(DefaultListIdForNewTask());
        var startPicker = new CalendarDatePicker
        {
            Date = new DateTimeOffset(DateTime.Today),
            PlaceholderText = "选择开始时间",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var duePicker = new CalendarDatePicker
        {
            Date = null,
            PlaceholderText = "选择截止日期",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var importantBox = new CheckBox
        {
            Content = "标为重要",
            IsChecked = _currentViewId == TodoViewIds.Important
        };

        var layout = new StackPanel
        {
            Spacing = 12,
            MinWidth = 360
        };
        layout.Children.Add(FieldLabel("任务名称"));
        layout.Children.Add(titleBox);
        layout.Children.Add(FieldLabel("备注"));
        layout.Children.Add(notesBox);
        layout.Children.Add(FieldLabel("所属清单"));
        layout.Children.Add(listBox);
        layout.Children.Add(FieldLabel("开始时间"));
        layout.Children.Add(startPicker);
        layout.Children.Add(FieldLabel("截止日期"));
        layout.Children.Add(duePicker);
        layout.Children.Add(importantBox);

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "添加任务",
            Content = layout,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                args.Cancel = true;
                titleBox.Focus(FocusState.Programmatic);
            }
        };

        var result = await dialog.ShowAsync();
        var title = titleBox.Text.Trim();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var listId = listBox.SelectedItem is ComboBoxItem { Tag: string selectedListId }
            ? selectedListId
            : DefaultListId();
        AddTask(
            title,
            listId,
            importantBox.IsChecked == true,
            startPicker.Date?.DateTime.Date ?? DateTime.Today,
            duePicker.Date?.DateTime.Date,
            notes: notesBox.Text);
    }

    private async Task ShowAddSubtaskDialogAsync(TodoTask parent)
    {
        if (!TodoQuery.CanAddChild(_data, parent))
        {
            return;
        }

        var titleBox = new TextBox
        {
            PlaceholderText = "子任务名称",
            MinWidth = 340,
            FontSize = 15
        };
        var notesBox = new TextBox
        {
            PlaceholderText = "备注（可选）",
            MinWidth = 340,
            MinHeight = 96,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };

        var layout = new StackPanel
        {
            Spacing = 12,
            MinWidth = 360
        };
        layout.Children.Add(FieldLabel("父任务"));
        layout.Children.Add(new TextBlock
        {
            Text = parent.Title,
            FontSize = 14,
            Foreground = SecondaryTextBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        layout.Children.Add(FieldLabel("子任务名称"));
        layout.Children.Add(titleBox);
        layout.Children.Add(FieldLabel("备注"));
        layout.Children.Add(notesBox);

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "添加子任务",
            Content = layout,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                args.Cancel = true;
                titleBox.Focus(FocusState.Programmatic);
            }
        };

        var result = await dialog.ShowAsync();
        var title = titleBox.Text.Trim();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        AddTask(
            title,
            parent.ListId,
            parent.IsImportant,
            parent.StartDate == default ? DateTime.Today : parent.StartDate.Date,
            parent.DueDate,
            parent.Id,
            notesBox.Text);
    }

    private async Task ShowAddListDialogAsync()
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "清单名称",
            MinWidth = 280
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "新建清单",
            Content = nameBox,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        var name = nameBox.Text.Trim();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var list = new TodoList
        {
            Id = TodoStore.NewId("list"),
            Name = name,
            CreatedAt = DateTimeOffset.Now
        };
        _data.Lists.Add(list);
        _currentViewId = TodoViewIds.List(list.Id);
        _selectedTaskId = null;
        SaveDataAndRefresh();
    }

    private async Task ShowRenameListDialogAsync(TodoList list)
    {
        var nameBox = new TextBox
        {
            Text = list.Name,
            PlaceholderText = "清单名称",
            MinWidth = 280
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "重命名清单",
            Content = nameBox,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                args.Cancel = true;
                nameBox.Focus(FocusState.Programmatic);
            }
        };

        var result = await dialog.ShowAsync();
        var name = nameBox.Text.Trim();
        if (result != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(name) ||
            string.Equals(list.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        list.Name = name;
        _store.SaveData(_data);
        RefreshAll();
    }

    private async Task ShowDeleteListDialogAsync(TodoList list)
    {
        if (IsDefaultList(list.Id) || _data.Lists.Count <= 1)
        {
            var blocked = new ContentDialog
            {
                XamlRoot = _root.XamlRoot,
                RequestedTheme = ResolveElementTheme(),
                Title = "无法删除默认清单",
                Content = "默认清单用于承接未分类任务，不能删除。",
                CloseButtonText = "知道了"
            };
            await blocked.ShowAsync();
            return;
        }

        var taskCount = TasksForList(list.Id).Count();
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "删除清单",
            Content = taskCount > 0
                ? $"删除“{list.Name}”后，其中 {taskCount} 个任务会移动到默认清单。"
                : $"确定删除“{list.Name}”？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var fallbackListId = DefaultListId();
        foreach (var task in TasksForList(list.Id).ToList())
        {
            task.ListId = fallbackListId;
            Touch(task);
        }

        _data.Lists.Remove(list);
        if (string.Equals(_currentViewId, TodoViewIds.List(list.Id), StringComparison.Ordinal))
        {
            _currentViewId = TodoViewIds.Today;
            _selectedTaskId = FirstTaskForSelection(_currentViewId)?.Id;
        }

        SaveDataAndRefresh();
    }

    private async Task ShowFilterDialogAsync()
    {
        var listFilterBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var allLists = new ComboBoxItem { Tag = string.Empty, Content = "全部清单" };
        listFilterBox.Items.Add(allLists);
        listFilterBox.SelectedItem = allLists;
        foreach (var list in OrderedLists())
        {
            var item = new ComboBoxItem { Tag = list.Id, Content = list.Name };
            listFilterBox.Items.Add(item);
            if (string.Equals(_listIdFilter, list.Id, StringComparison.Ordinal))
            {
                listFilterBox.SelectedItem = item;
            }
        }

        var hierarchyBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in new[] { (1, "仅一级"), (2, "一级和二级"), (TodoQuery.MaxTaskTreeDepth, "全部层级") })
        {
            var item = new ComboBoxItem { Tag = choice.Item1, Content = choice.Item2 };
            hierarchyBox.Items.Add(item);
            if (choice.Item1 == _maximumVisibleTaskDepth)
            {
                hierarchyBox.SelectedItem = item;
            }
        }

        var dateModeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in new[]
                 {
                     ("none", "不按日期筛选"),
                     ("start", "开始日期"),
                     ("period", "执行周期")
                 })
        {
            var item = new ComboBoxItem { Tag = choice.Item1, Content = choice.Item2 };
            dateModeBox.Items.Add(item);
            var selected = _dateRangeFilter switch
            {
                null => choice.Item1 == "none",
                { Mode: TodoDateFilterMode.StartDate } => choice.Item1 == "start",
                _ => choice.Item1 == "period"
            };
            if (selected)
            {
                dateModeBox.SelectedItem = item;
            }
        }

        var startPicker = new CalendarDatePicker
        {
            Date = _dateRangeFilter is null ? null : new DateTimeOffset(_dateRangeFilter.StartDate.Date),
            PlaceholderText = "开始日期",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var endPicker = new CalendarDatePicker
        {
            Date = _dateRangeFilter is null ? null : new DateTimeOffset(_dateRangeFilter.EndDate.Date),
            PlaceholderText = "结束日期",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var dateRangeFields = new StackPanel { Spacing = 12 };
        dateRangeFields.Children.Add(startPicker);
        dateRangeFields.Children.Add(endPicker);
        void UpdateDateRangeVisibility()
        {
            var hasDateMode = dateModeBox.SelectedItem is ComboBoxItem { Tag: string mode } && mode != "none";
            dateRangeFields.Visibility = hasDateMode ? Visibility.Visible : Visibility.Collapsed;
        }
        void SetRange(DateTime start, DateTime end)
        {
            startPicker.Date = new DateTimeOffset(start.Date);
            endPicker.Date = new DateTimeOffset(end.Date);
            if (dateModeBox.SelectedItem is ComboBoxItem { Tag: string mode } && mode == "none")
            {
                dateModeBox.SelectedItem = dateModeBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag as string, "period", StringComparison.Ordinal));
            }

            UpdateDateRangeVisibility();
        }
        dateModeBox.SelectionChanged += (_, _) => UpdateDateRangeVisibility();

        var quickActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var week = PillButton("本周", "\uE787");
        week.Click += (_, _) =>
        {
            var today = DateTime.Today;
            var offset = ((int)today.DayOfWeek + 6) % 7;
            SetRange(today.AddDays(-offset), today.AddDays(6 - offset));
        };
        quickActions.Children.Add(week);
        var month = PillButton("本月", "\uE81C");
        month.Click += (_, _) =>
        {
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1);
            SetRange(first, first.AddMonths(1).AddDays(-1));
        };
        quickActions.Children.Add(month);

        var layout = new StackPanel { Spacing = 12, MinWidth = 360 };
        layout.Children.Add(FieldLabel("任务清单"));
        layout.Children.Add(listFilterBox);
        layout.Children.Add(FieldLabel("任务层级"));
        layout.Children.Add(hierarchyBox);
        layout.Children.Add(FieldLabel("日期范围"));
        layout.Children.Add(dateModeBox);
        layout.Children.Add(quickActions);
        layout.Children.Add(dateRangeFields);
        UpdateDateRangeVisibility();

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "筛选",
            Content = layout,
            PrimaryButtonText = "应用",
            SecondaryButtonText = "清除筛选",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (dateModeBox.SelectedItem is ComboBoxItem { Tag: string mode } && mode != "none" &&
                (startPicker.Date is null || endPicker.Date is null || startPicker.Date.Value.Date > endPicker.Date.Value.Date))
            {
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
        {
            return;
        }

        if (result == ContentDialogResult.Secondary)
        {
            _dateRangeFilter = null;
            _listIdFilter = null;
            _maximumVisibleTaskDepth = TodoQuery.MaxTaskTreeDepth;
            RefreshAll();
            return;
        }

        _maximumVisibleTaskDepth = hierarchyBox.SelectedItem is ComboBoxItem { Tag: int depth }
            ? Math.Clamp(depth, 1, TodoQuery.MaxTaskTreeDepth)
            : TodoQuery.MaxTaskTreeDepth;
        _dateRangeFilter = dateModeBox.SelectedItem is ComboBoxItem { Tag: string dateMode } &&
            dateMode != "none" && startPicker.Date is { } start && endPicker.Date is { } end
            ? new TodoDateRangeFilter
            {
                Mode = dateMode == "period" ? TodoDateFilterMode.ExecutionPeriod : TodoDateFilterMode.StartDate,
                StartDate = start.DateTime.Date,
                EndDate = end.DateTime.Date
            }
            : null;
        _listIdFilter = listFilterBox.SelectedItem is ComboBoxItem { Tag: string listId } &&
            !string.IsNullOrWhiteSpace(listId)
            ? listId
            : null;
        if (_dateRangeFilter is not null || _listIdFilter is not null)
        {
            _currentViewId = TodoViewIds.All;
        }

        _selectedTaskId = FirstTaskForSelection(_currentViewId)?.Id;
        SaveSettings();
        RefreshAll();
    }

    private bool IsFilterActive()
    {
        return _dateRangeFilter is { IsValid: true } ||
            !string.IsNullOrWhiteSpace(_listIdFilter) ||
            _maximumVisibleTaskDepth < TodoQuery.MaxTaskTreeDepth;
    }

    private void RefreshFilterButtonState()
    {
        var isActive = IsFilterActive();
        if (_filterButton.Content is StackPanel stack)
        {
            var label = stack.Children.OfType<TextBlock>().FirstOrDefault();
            if (label is not null)
            {
                label.Text = isActive ? "筛选中" : "筛选";
                label.Foreground = isActive ? FilterActiveTextBrush() : TextBrush();
            }
        }

        var border = isActive ? FilterActiveBorderBrush() : Brush(0xDCE7EA);
        var background = isActive ? FilterActiveBackgroundBrush() : Brush(0xFFFFFF);
        var hoverBorder = FilterHoverBorderBrush(isActive);
        var hoverBackground = FilterHoverBackgroundBrush(isActive);
        var pressedBorder = FilterPressedBorderBrush(isActive);
        var pressedBackground = FilterPressedBackgroundBrush(isActive);
        _filterButton.Resources["ButtonBackground"] = background;
        _filterButton.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
        _filterButton.Resources["ButtonBackgroundPressed"] = pressedBackground;
        _filterButton.Resources["ButtonBorderBrush"] = border;
        _filterButton.Resources["ButtonBorderBrushPointerOver"] = hoverBorder;
        _filterButton.Resources["ButtonBorderBrushPressed"] = pressedBorder;
        ToolTipService.SetToolTip(_filterButton, isActive ? "已应用筛选，点击可调整或清空" : "筛选任务");
        UpdateVirtualTitleBarRegions();
    }

    private async Task ShowSettingsDialogAsync()
    {
        var themeBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 300
        };
        AddThemeItem(themeBox, "跟随系统", TodoThemeIds.System);
        AddThemeItem(themeBox, "浅色主题", TodoThemeIds.Light);
        AddThemeItem(themeBox, "深色主题", TodoThemeIds.Dark);

        var recycleBinEnabledBox = new CheckBox
        {
            Content = "启用回收站",
            IsChecked = _settings.IsRecycleBinEnabled
        };
        var retentionBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 300
        };
        foreach (var preset in new[]
                 {
                     (TodoRecycleBinRetentionPresets.SevenDays, "7 天"),
                     (TodoRecycleBinRetentionPresets.ThirtyDays, "30 天"),
                     (TodoRecycleBinRetentionPresets.NinetyDays, "90 天"),
                     (TodoRecycleBinRetentionPresets.Custom, "自定义")
                 })
        {
            var item = new ComboBoxItem { Tag = preset.Item1, Content = preset.Item2 };
            retentionBox.Items.Add(item);
            if (string.Equals(preset.Item1, _settings.RecycleBinRetentionPreset, StringComparison.Ordinal))
            {
                retentionBox.SelectedItem = item;
            }
        }

        var customRetentionDaysBox = new TextBox
        {
            Text = _settings.RecycleBinCustomRetentionDays.ToString(),
            PlaceholderText = "请输入 1–365 天",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }
        };
        var customRetentionHint = new TextBlock
        {
            Text = "仅支持 1–365 的整数天数。",
            FontSize = 12,
            Foreground = SecondaryTextBrush(),
            TextWrapping = TextWrapping.Wrap
        };
        var customRetentionError = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush(0xB42318),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        bool ValidateCustomRetentionDays(bool showError)
        {
            var isCustom = retentionBox.SelectedItem is ComboBoxItem { Tag: string preset } &&
                string.Equals(preset, TodoRecycleBinRetentionPresets.Custom, StringComparison.Ordinal);
            var isValid = !isCustom ||
                (int.TryParse(customRetentionDaysBox.Text, out var days) && days is >= 1 and <= 365);
            customRetentionError.Text = isValid ? string.Empty : "请输入 1–365 之间的整数天数。";
            customRetentionError.Visibility = showError && !isValid && recycleBinEnabledBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            return isValid;
        }
        void UpdateRecycleBinControls()
        {
            var custom = retentionBox.SelectedItem is ComboBoxItem { Tag: string preset } &&
                string.Equals(preset, TodoRecycleBinRetentionPresets.Custom, StringComparison.Ordinal);
            retentionBox.IsEnabled = recycleBinEnabledBox.IsChecked == true;
            customRetentionDaysBox.Visibility = custom && recycleBinEnabledBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            customRetentionHint.Visibility = custom && recycleBinEnabledBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            ValidateCustomRetentionDays(showError: true);
        }
        retentionBox.SelectionChanged += (_, _) => UpdateRecycleBinControls();
        recycleBinEnabledBox.Checked += (_, _) => UpdateRecycleBinControls();
        recycleBinEnabledBox.Unchecked += (_, _) => UpdateRecycleBinControls();
        customRetentionDaysBox.TextChanged += (_, _) => ValidateCustomRetentionDays(showError: true);
        UpdateRecycleBinControls();

        var layout = new StackPanel
        {
            Spacing = 14,
            MinWidth = 320
        };
        layout.Children.Add(new TextBlock
        {
            Text = "主题",
            FontSize = 14,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = TextBrush()
        });
        layout.Children.Add(themeBox);
        layout.Children.Add(new TextBlock
        {
            Text = "回收站",
            FontSize = 14,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = TextBrush(),
            Margin = new Thickness(0, 8, 0, 0)
        });
        layout.Children.Add(recycleBinEnabledBox);
        layout.Children.Add(new TextBlock
        {
            Text = "自动清理周期",
            FontSize = 13,
            Foreground = SecondaryTextBrush()
        });
        layout.Children.Add(retentionBox);
        layout.Children.Add(customRetentionDaysBox);
        layout.Children.Add(customRetentionHint);
        layout.Children.Add(customRetentionError);
        layout.Children.Add(new TextBlock
        {
            Text = "便签模式可在这里打开，打开后主界面会暂时隐藏。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SecondaryTextBrush(),
            FontSize = 13
        });

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "设置",
            Content = layout,
            PrimaryButtonText = "保存",
            SecondaryButtonText = "打开便签模式",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (retentionBox.SelectedItem is not ComboBoxItem { Tag: string preset })
            {
                args.Cancel = true;
                return;
            }

            if (recycleBinEnabledBox.IsChecked == true &&
                string.Equals(preset, TodoRecycleBinRetentionPresets.Custom, StringComparison.Ordinal) &&
                !ValidateCustomRetentionDays(showError: true))
            {
                args.Cancel = true;
                customRetentionDaysBox.Focus(FocusState.Programmatic);
            }
        };

        var result = await dialog.ShowAsync();
        if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary))
        {
            return;
        }

        if (themeBox.SelectedItem is ComboBoxItem { Tag: string theme })
        {
            SetTheme(theme);
        }

        _settings.IsRecycleBinEnabled = recycleBinEnabledBox.IsChecked == true;
        if (retentionBox.SelectedItem is ComboBoxItem { Tag: string retentionPreset })
        {
            _settings.RecycleBinRetentionPreset = retentionPreset;
        }

        if (int.TryParse(customRetentionDaysBox.Text, out var customDays))
        {
            _settings.RecycleBinCustomRetentionDays = customDays;
        }

        PurgeExpiredRecycleBin();
        if (_currentViewId == TodoViewIds.RecycleBin && !_settings.IsRecycleBinEnabled)
        {
            _currentViewId = TodoViewIds.Today;
            _selectedTaskId = FirstTaskForSelection(_currentViewId)?.Id;
        }
        SaveSettings();
        BuildShell();
        RefreshAll();

        if (result == ContentDialogResult.Secondary)
        {
            OpenStickyMode();
        }
    }

    private async Task ShowHelpDialogAsync()
    {
        var contentWidth = HelpDialogContentWidth();
        var useTwoColumns = contentWidth >= 760;
        var layout = new StackPanel
        {
            Spacing = 18,
            Width = contentWidth,
            MaxWidth = contentWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };

        layout.Children.Add(HelpHero());
        layout.Children.Add(HelpModeSection(
            "主界面",
            "整理、编辑和查看完整任务",
            "适合集中管理任务、清单、日期、备注和子任务。",
            [
                new HelpCardInfo("\uE8A5", "切换视图", "在左侧切换今日、计划、重要、全部、已完成、回收站和自定义清单。"),
                new HelpCardInfo("\uE710", "新增任务", "在顶部输入任务后按回车；输入为空时点击添加按钮会打开新建任务弹窗。"),
                new HelpCardInfo("\uE70F", "编辑详情", "点击任务后，在右侧编辑标题、清单、开始时间、截止日期、备注和重要标记；完成时间修改需点击应用，切换任务会自动撤销未应用内容。"),
                new HelpCardInfo("\uE712", "任务菜单", "右键任务可创建带备注的子任务或删除任务；删除含后代任务时会先确认。"),
                new HelpCardInfo("\uE7C2", "移动窗口", "可拖动顶部品牌和空白区域移动主窗口；筛选、侧栏菜单和任务操作仍可正常点击。"),
                new HelpCardInfo("\uE8FD", "管理清单", "左侧清单区可以新增清单，也可以对已有清单改名或删除。"),
                new HelpCardInfo("\uE8C8", "子任务", "选中任务后可添加子任务并填写备注；父任务可展开或收起，最多支持三层。"),
                new HelpCardInfo("\uE73E", "完成与恢复", "点击任务前的圆形按钮完成任务；已完成任务会弱化显示，可恢复或删除。")
            ],
            useTwoColumns));
        layout.Children.Add(HelpModeSection(
            "便签模式",
            "贴在桌面上的轻量任务窗口",
            "适合把当前任务留在屏幕上，随手新增、完成和调整顺序。",
            [
                new HelpCardInfo("\uE8A7", "进入便签", "在设置中打开便签模式；进入后主界面会隐藏，可从便签回到大界面。"),
                new HelpCardInfo("\uE840", "置顶显示", "点击置顶按钮，让便签保持在其他窗口上方。"),
                new HelpCardInfo("\uE890", "透明度与缩放", "在便签设置中调整透明度和 50%-200% 缩放，也可一键恢复 100%。"),
                new HelpCardInfo("\uE8AB", "调整窗口", "拖动边缘改变大小，拖动顶部 Fowan 区域移动窗口位置。"),
                new HelpCardInfo("\uE7C2", "拖动排序", "长按任务后拖动；列表边缘会自动滚动，虚线框会预览松手后的具体位置和父子归属。"),
                new HelpCardInfo("\uE73E", "快速处理", "便签固定显示今日任务；双击任务可编辑标题和备注，也可通过右键菜单删除或创建带备注的子任务。")
            ],
            useTwoColumns));
        layout.Children.Add(HelpModeSection(
            "回收站与筛选",
            "批量整理、找回和缩小任务范围",
            "删除会按任务树处理；筛选只影响主窗口列表，日期筛选会切到全部任务。",
            [
                new HelpCardInfo("\uE74D", "回收站", "启用回收站时，删除任务会先移入回收站；可恢复整棵任务树，也可永久删除。"),
                new HelpCardInfo("\uE74E", "自动清理", "设置中可关闭回收站，或选择 7 天、30 天、90 天和自定义清理周期，默认 30 天。"),
                new HelpCardInfo("\uE71C", "层级筛选", "筛选可只显示一层、一至两层或全部层级；不会补入不符合条件的祖先任务。"),
                new HelpCardInfo("\uE787", "组合筛选", "可按任务清单、开始日期或执行周期组合筛选，并提供本周、本月快捷项；应用后自动切到全部任务并显示筛选中。"),
                new HelpCardInfo("\uE8CB", "今日规则", "今日任务页只显示今日未完成项和当天完成项；切换导航视图会清除日期筛选。"),
                new HelpCardInfo("\uE7C2", "边缘滚动", "拖拽任务靠近列表上下边缘时会自动上下滚动，离边缘越远速度越快并有上限；未完成和已完成任务不会自动跨越分组边界。")
            ],
            useTwoColumns));
        layout.Children.Add(HelpTipBand());

        var scroll = new ScrollViewer
        {
            Content = layout,
            Width = contentWidth,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };

        var overlay = new Grid
        {
            RequestedTheme = IsHelpDarkTheme() ? ElementTheme.Dark : ElementTheme.Light,
            Background = OverlayBrush(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = true
        };
        Grid.SetColumn(overlay, 0);
        Grid.SetColumnSpan(overlay, Math.Max(1, _root.ColumnDefinitions.Count));
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, Math.Max(1, _root.RowDefinitions.Count));
        Canvas.SetZIndex(overlay, 1000);

        var modalHost = new Grid
        {
            RequestedTheme = IsHelpDarkTheme() ? ElementTheme.Dark : ElementTheme.Light,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        overlay.Children.Add(modalHost);

        var closeButton = new Button
        {
            Content = "知道了",
            Width = 92,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = TransparentBrush(),
            Foreground = HelpPrimaryButtonTextBrush(),
            BorderBrush = TransparentBrush(),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ApplyHelpButtonColors(
            closeButton,
            TransparentBrush(),
            HelpPrimaryButtonTextBrush(),
            TransparentBrush(),
            HelpPrimaryButtonHoverBrush(),
            HelpPrimaryButtonPressedBrush());
        var closeButtonFrame = new Border
        {
            Width = 92,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = HelpPrimaryButtonBrush(),
            BorderBrush = HelpPrimaryButtonBrush(),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = closeButton
        };

        var panelWidth = HelpDialogWidth(contentWidth);
        var panelHeight = HelpDialogPanelMaxHeight();
        var panelContent = new Grid
        {
            Width = Math.Max(300, panelWidth - 48),
            Height = Math.Max(0, panelHeight - 48),
            RowSpacing = 12
        };
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panelContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
            MinHeight = 38,
            ColumnSpacing = 16
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "使用说明",
            FontSize = 22,
            FontWeight = MuxFontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = HelpTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });
        var iconCloseFrame = new Border
        {
            Width = 38,
            Height = 38,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Background = HelpCloseButtonBrush(),
            BorderBrush = HelpPanelBorderBrush(),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 12,
                Foreground = HelpTextBrush(),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        AutomationProperties.SetName(iconCloseFrame, "关闭使用说明");
        iconCloseFrame.PointerEntered += (_, _) => iconCloseFrame.Background = HelpCloseButtonHoverBrush();
        iconCloseFrame.PointerExited += (_, _) => iconCloseFrame.Background = HelpCloseButtonBrush();
        iconCloseFrame.PointerPressed += (_, _) => iconCloseFrame.Background = HelpCloseButtonPressedBrush();
        iconCloseFrame.PointerReleased += (_, _) => iconCloseFrame.Background = HelpCloseButtonHoverBrush();
        var iconCloseHost = new Grid
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconCloseHost.Children.Add(iconCloseFrame);
        Grid.SetColumn(iconCloseHost, 1);
        header.Children.Add(iconCloseHost);
        panelContent.Children.Add(header);

        Grid.SetRow(scroll, 1);
        panelContent.Children.Add(scroll);

        var footer = new Grid { MinHeight = 40 };
        closeButtonFrame.Margin = new Thickness(0, 0, 2, 2);
        footer.Children.Add(closeButtonFrame);
        Grid.SetRow(footer, 2);
        panelContent.Children.Add(footer);

        var panel = new Border
        {
            Width = panelWidth,
            Height = panelHeight,
            Padding = new Thickness(24),
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(18),
            Background = HelpPanelBrush(),
            BorderBrush = HelpPanelBorderBrush(),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panelContent
        };
        panel.PointerPressed += (_, args) => args.Handled = true;
        modalHost.Children.Add(panel);

        void ApplyResponsiveHelpLayout()
        {
            var nextContentWidth = HelpDialogContentWidth();
            var nextPanelWidth = HelpDialogWidth(nextContentWidth);
            var nextPanelHeight = HelpDialogPanelMaxHeight();
            var nextInnerWidth = Math.Max(300, nextPanelWidth - 48);
            var nextInnerHeight = Math.Max(0, nextPanelHeight - 48);

            layout.Width = nextContentWidth;
            layout.MaxWidth = nextContentWidth;
            scroll.Width = nextContentWidth;
            panel.Width = nextPanelWidth;
            panel.Height = nextPanelHeight;
            panelContent.Width = nextInnerWidth;
            panelContent.Height = nextInnerHeight;
        }

        var completion = new TaskCompletionSource<object?>();
        var closed = false;
        SizeChangedEventHandler? sizeChangedHandler = (_, _) => ApplyResponsiveHelpLayout();
        void Close()
        {
            if (closed)
            {
                return;
            }

            closed = true;
            if (sizeChangedHandler is not null)
            {
                _root.SizeChanged -= sizeChangedHandler;
                sizeChangedHandler = null;
            }
            _root.Children.Remove(overlay);
            completion.TrySetResult(null);
        }

        overlay.PointerPressed += (_, _) => Close();
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Escape)
            {
                args.Handled = true;
                Close();
            }
        };
        closeButton.Click += (_, _) => Close();
        iconCloseFrame.Tapped += (_, _) => Close();
        overlay.Loaded += (_, _) => overlay.Focus(FocusState.Programmatic);

        _root.SizeChanged += sizeChangedHandler;
        ApplyResponsiveHelpLayout();
        _root.Children.Add(overlay);
        await completion.Task;
    }

    private SolidColorBrush OverlayBrush()
    {
        return new SolidColorBrush(ColorHelper.FromArgb(IsHelpDarkTheme() ? (byte)150 : (byte)92, 0, 14, 24));
    }

    private SolidColorBrush HelpPanelBrush() => HelpBrush(0xFFFFFF, 0x0F151B);
    private SolidColorBrush HelpPanelBorderBrush() => HelpBrush(0xC7D8DE, 0x3A4854);
    private SolidColorBrush HelpSectionBrush() => HelpBrush(0xF7FAFB, 0x151D25);
    private SolidColorBrush HelpCardBrush() => HelpBrush(0xFFFFFF, 0x1B2530);
    private SolidColorBrush HelpHeroBrush() => HelpBrush(0xEAF8FA, 0x132A33);
    private SolidColorBrush HelpHeroBorderBrush() => HelpBrush(0xB9E2EA, 0x2C5964);
    private SolidColorBrush HelpIconBackgroundBrush() => HelpBrush(0xDDF3F7, 0x123A45);
    private SolidColorBrush HelpCloseButtonBrush() => HelpBrush(0xF1F6F8, 0x202A34);
    private SolidColorBrush HelpBorderBrush() => HelpBrush(0xD9E6EA, 0x303D49);
    private SolidColorBrush HelpTipBrush() => HelpBrush(0xFFF6DA, 0x2D2315);
    private SolidColorBrush HelpTipBorderBrush() => HelpBrush(0xE4BC55, 0x9E7C36);
    private SolidColorBrush HelpTextBrush() => HelpBrush(0x142229, 0xEEF5F8);
    private SolidColorBrush HelpSecondaryTextBrush() => HelpBrush(0x536771, 0xAAB8C2);
    private SolidColorBrush HelpTipTextBrush() => HelpBrush(0x4A3510, 0xF2D18F);
    private SolidColorBrush HelpAccentBrush() => HelpBrush(0x0C7588, 0x2BB7C6);
    private SolidColorBrush HelpPrimaryButtonBrush() => HelpBrush(0x0C7588, 0x2BB7C6);
    private SolidColorBrush HelpPrimaryButtonHoverBrush() => HelpBrush(0x09687A, 0x35C8D8);
    private SolidColorBrush HelpPrimaryButtonPressedBrush() => HelpBrush(0x075A69, 0x1D9EAE);
    private SolidColorBrush HelpPrimaryButtonTextBrush() => HelpBrush(0xFFFFFF, 0x061A20);
    private SolidColorBrush HelpCloseButtonHoverBrush() => HelpBrush(0xE7F0F3, 0x2A3642);
    private SolidColorBrush HelpCloseButtonPressedBrush() => HelpBrush(0xD9E6EA, 0x18232C);

    private SolidColorBrush HelpBrush(uint lightRgb, uint darkRgb)
    {
        return SolidBrush(IsHelpDarkTheme() ? darkRgb : lightRgb);
    }

    private bool IsHelpDarkTheme()
    {
        return _settings.Theme switch
        {
            TodoThemeIds.Dark => true,
            TodoThemeIds.Light => false,
            _ => _root.ActualTheme switch
            {
                ElementTheme.Dark => true,
                ElementTheme.Light => false,
                _ => IsSystemThemeDark()
            }
        };
    }

    private void ApplyHelpButtonColors(
        Button button,
        SolidColorBrush background,
        SolidColorBrush foreground,
        SolidColorBrush border,
        SolidColorBrush pointerOverBackground,
        SolidColorBrush pressedBackground)
    {
        button.RequestedTheme = IsHelpDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
        button.Background = background;
        button.Foreground = foreground;
        button.BorderBrush = border;

        button.Resources["ButtonBackground"] = background;
        button.Resources["ButtonBackgroundPointerOver"] = pointerOverBackground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        button.Resources["ButtonBackgroundDisabled"] = background;
        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonForegroundDisabled"] = HelpSecondaryTextBrush();
        button.Resources["ButtonBorderBrush"] = border;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonBorderBrushPressed"] = border;
        button.Resources["ButtonBorderBrushDisabled"] = border;
    }

    private double HelpDialogPanelMaxHeight()
    {
        var rootHeight = _root.XamlRoot?.Size.Height ?? _root.ActualHeight;
        if (rootHeight <= 0)
        {
            rootHeight = 860;
        }

        return Math.Min(840, Math.Max(1, rootHeight - 32));
    }

    private double HelpDialogWidth(double contentWidth)
    {
        var rootWidth = _root.XamlRoot?.Size.Width ?? _root.ActualWidth;
        if (rootWidth <= 0)
        {
            rootWidth = contentWidth + 96;
        }

        return Math.Min(contentWidth + 96, Math.Max(360, rootWidth - 48));
    }

    private double HelpDialogContentWidth()
    {
        var rootWidth = _root.XamlRoot?.Size.Width ?? _root.ActualWidth;
        if (rootWidth <= 0)
        {
            rootWidth = 1280;
        }

        return Math.Min(920, Math.Max(300, rootWidth - 140));
    }

    private double HelpDialogMaxHeight()
    {
        var rootHeight = _root.XamlRoot?.Size.Height ?? _root.ActualHeight;
        if (rootHeight <= 0)
        {
            rootHeight = 860;
        }

        return Math.Min(700, Math.Max(260, rootHeight - 220));
    }

    private UIElement HelpHero()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            Background = HelpHeroBrush(),
            BorderBrush = HelpHeroBorderBrush(),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                ColumnSpacing = 14,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new Border
                    {
                        Width = 44,
                        Height = 44,
                        CornerRadius = new CornerRadius(12),
                        Background = HelpAccentBrush(),
                        Child = new FontIcon
                        {
                            Glyph = "\uE897",
                            FontSize = 22,
                            Foreground = HelpPrimaryButtonTextBrush()
                        }
                    },
                    HelpHeroText()
                }
            }
        };
    }

    private UIElement HelpHeroText()
    {
        var stack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new TextBlock
        {
            Text = "Fowan 待办可以做什么？",
            FontSize = 18,
            FontWeight = MuxFontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = HelpTextBrush()
        });
        stack.Children.Add(new TextBlock
        {
            Text = "这里只介绍当前已经支持的功能和操作方法。需要完整整理任务时用主界面，需要贴在桌面随手处理时用便签模式。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = HelpSecondaryTextBrush()
        });

        Grid.SetColumn(stack, 1);
        return stack;
    }

    private UIElement HelpModeSection(
        string mode,
        string title,
        string description,
        IReadOnlyList<HelpCardInfo> cards,
        bool useTwoColumns)
    {
        var section = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = HelpBorderBrush(),
            Background = HelpSectionBrush(),
            Padding = new Thickness(16)
        };

        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = mode,
                    FontSize = 12,
                    FontWeight = MuxFontWeights.SemiBold,
                    Foreground = HelpAccentBrush()
                },
                new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = MuxFontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = HelpTextBrush()
                },
                new TextBlock
                {
                    Text = description,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = HelpSecondaryTextBrush()
                }
            }
        });
        stack.Children.Add(HelpCardGrid(cards, useTwoColumns));
        section.Child = stack;
        return section;
    }

    private UIElement HelpCardGrid(IReadOnlyList<HelpCardInfo> cards, bool useTwoColumns)
    {
        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (useTwoColumns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var columns = useTwoColumns ? 2 : 1;
        for (var row = 0; row < (cards.Count + columns - 1) / columns; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < cards.Count; index++)
        {
            var card = HelpCard(cards[index]);
            Grid.SetRow(card, index / columns);
            Grid.SetColumn(card, index % columns);
            grid.Children.Add(card);
        }

        return grid;
    }

    private FrameworkElement HelpCard(HelpCardInfo card)
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Background = HelpCardBrush(),
            BorderBrush = HelpBorderBrush(),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid
        {
            ColumnSpacing = 10
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = HelpIconBackgroundBrush(),
            Child = new FontIcon
            {
                Glyph = card.Glyph,
                FontSize = 16,
                Foreground = HelpAccentBrush()
            }
        });

        var text = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = card.Title,
                    FontSize = 14,
                    FontWeight = MuxFontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = HelpTextBrush()
                },
                new TextBlock
                {
                    Text = card.Description,
                    FontSize = 12,
                    LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = HelpSecondaryTextBrush()
                }
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        root.Child = grid;
        return root;
    }

    private UIElement HelpTipBand()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Background = HelpTipBrush(),
            BorderBrush = HelpTipBorderBrush(),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = $"小提示：任务最多支持 {TodoQuery.MaxTaskTreeDepth} 层子任务；每个任务最多 {TodoQuery.MaxChildTasksPerTask} 个直接子任务。完成包含未完成子任务的父任务时，会先询问是否一起完成。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 20,
                Foreground = HelpTipTextBrush()
            }
        };
    }

    private sealed record HelpCardInfo(string Glyph, string Title, string Description);

    private void AddThemeItem(ComboBox themeBox, string label, string theme)
    {
        var item = new ComboBoxItem
        {
            Content = label,
            Tag = theme
        };
        themeBox.Items.Add(item);
        if (string.Equals(_settings.Theme, theme, StringComparison.OrdinalIgnoreCase))
        {
            themeBox.SelectedItem = item;
        }
    }

    private void SetTaskCompleted(TodoTask task, bool completed)
    {
        if (task.IsCompleted == completed)
        {
            return;
        }

        task.IsCompleted = completed;
        task.CompletedAt = completed ? DateTimeOffset.Now : null;
        Touch(task);
        _selectedTaskId = task.Id;
        SaveDataAndRefresh();
    }

    private async Task ToggleTaskCompletedAsync(TodoTask task)
    {
        var completed = !task.IsCompleted;
        if (!completed)
        {
            SetTaskCompleted(task, false);
            return;
        }

        if (!HasIncompleteDescendants(task))
        {
            SetTaskCompleted(task, true);
            return;
        }

        if (!await ConfirmCompleteTaskWithChildrenAsync())
        {
            return;
        }

        CompleteTaskAndDescendants(task);
    }

    private bool HasIncompleteDescendants(TodoTask task)
    {
        var descendantIds = TodoQuery.DescendantIds(_data, task.Id).ToHashSet(StringComparer.Ordinal);
        return _data.Tasks.Any(candidate => descendantIds.Contains(candidate.Id) && !candidate.IsCompleted);
    }

    private async Task<bool> ConfirmCompleteTaskWithChildrenAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "完成当前任务？",
            Content = "是否完成当前任务，仍有未完成的子任务",
            PrimaryButtonText = "确认完成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void CompleteTaskAndDescendants(TodoTask task)
    {
        var completeIds = TodoQuery.DescendantIds(_data, task.Id)
            .Append(task.Id)
            .ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.Now;
        foreach (var candidate in _data.Tasks.Where(candidate => completeIds.Contains(candidate.Id)))
        {
            if (candidate.IsCompleted)
            {
                continue;
            }

            candidate.IsCompleted = true;
            candidate.CompletedAt = now;
            Touch(candidate);
        }

        _selectedTaskId = task.Id;
        SaveDataAndRefresh();
    }

    private void ToggleImportant(TodoTask task)
    {
        task.IsImportant = !task.IsImportant;
        Touch(task);
        SaveDataAndRefresh();
    }

    private void UpdateTaskTitle(TodoTask task, string title)
    {
        var trimmed = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(task.Title, trimmed, StringComparison.Ordinal))
        {
            RefreshDetail();
            return;
        }

        task.Title = trimmed;
        Touch(task);
        SaveDataAndRefresh();
    }

    private async Task DeleteTaskAsync(TodoTask task)
    {
        var descendantCount = TodoQuery.DescendantIds(_data, task.Id).Count();
        if (descendantCount > 0 && !await ConfirmDeleteTaskTreeAsync(descendantCount))
        {
            return;
        }

        DeleteTaskTree(task);
    }

    private async Task<bool> ConfirmDeleteTaskTreeAsync(int descendantCount)
    {
        var target = _settings.IsRecycleBinEnabled ? "移动到回收站" : "永久删除";
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "删除任务树",
            Content = $"当前任务包含 {descendantCount} 个子任务或孙任务。确认后将{target}当前任务及全部后代。",
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void DeleteTaskTree(TodoTask task)
    {
        var treeIds = TodoRecycleBin.TaskTreeIds(_data, task.Id);
        if (!TodoRecycleBin.DeleteTaskTree(_data, _settings, task.Id))
        {
            return;
        }

        if (!_settings.IsRecycleBinEnabled)
        {
            _settings.CollapsedTaskIds.RemoveAll(treeIds.Contains);
        }

        _selectedTaskId = null;
        SaveDataAndRefresh();
    }

    private void RestoreTaskTree(TodoTask task)
    {
        if (TodoRecycleBin.RestoreTaskTree(_data, task.Id))
        {
            _selectedTaskId = null;
            SaveDataAndRefresh();
        }
    }

    private async Task PermanentlyDeleteTaskTreeAsync(TodoTask task)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = "永久删除任务",
            Content = "此操作无法恢复所选任务及其全部后代。",
            PrimaryButtonText = "永久删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (TodoRecycleBin.PermanentlyDeleteTaskTree(_data, task.Id))
        {
            _selectedTaskId = null;
            SaveDataAndRefresh();
        }
    }

    private void SaveDataAndRefresh()
    {
        TodoRecycleBin.PurgeExpired(_data, _settings);
        _store.SaveData(_data);
        SaveSettings();
        RefreshAll();
    }

    private void SaveSettings()
    {
        _settings.CurrentViewId = _currentViewId;
        _settings.SelectedTaskId = _selectedTaskId;
        _store.SaveSettings(_settings);
    }

    private void SetTheme(string theme)
    {
        if (_settings.Theme == theme)
        {
            return;
        }

        _settings.Theme = theme;
        _store.SaveSettings(_settings);
        ApplyCaptionButtonColorsToCurrentWindow();
        BuildShell();
        RefreshAll();
    }

    private void OpenStickyMode(bool closeMainWindow = true)
    {
        _settings.IsStickyModeEnabled = true;
        _store.SaveSettings(_settings);

        if (StickyLauncher.TryShow(out var process))
        {
            _stickyProcess = process;

            if (closeMainWindow)
            {
                HideNativeWindow();
            }
            else
            {
                HideNativeWindow();
            }
            return;
        }

        _settings.IsStickyModeEnabled = false;
        _store.SaveSettings(_settings);
        _stickyProcess = null;
        ShowNativeWindow();
        Activate();
    }

    private void QueueStickyPrewarm()
    {
        DispatcherQueue.TryEnqueue(PrewarmStickyIfNeeded);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            DispatcherQueue.TryEnqueue(PrewarmStickyIfNeeded);
        });
    }

    private void PrewarmStickyIfNeeded()
    {
        if (_settings.IsStickyModeEnabled)
        {
            return;
        }

        if (StickyLauncher.TryPrewarm(out var process))
        {
            _stickyProcess = process;
        }
    }

    private void ReloadDataAndSettings()
    {
        _data = _store.LoadData();
        _settings = _store.LoadSettings();
        PurgeExpiredRecycleBin();
        _currentViewId = IsKnownView(_settings.CurrentViewId) ? _settings.CurrentViewId : TodoViewIds.Today;
        if (_currentViewId == TodoViewIds.RecycleBin && !_settings.IsRecycleBinEnabled)
        {
            _currentViewId = TodoViewIds.Today;
        }
        _selectedTaskId = _settings.SelectedTaskId;
    }

    private void NavigateToView(string viewId)
    {
        _dateRangeFilter = null;
        _listIdFilter = null;
        _currentViewId = viewId;
        _selectedTaskId = FirstTaskForSelection(viewId)?.Id;
        SaveSettings();
        RefreshAll();
    }

    private void PurgeExpiredRecycleBin()
    {
        if (TodoRecycleBin.PurgeExpired(_data, _settings) > 0)
        {
            _store.SaveData(_data);
        }
    }

    private void StartRecycleBinMaintenanceTimer()
    {
        _recycleBinMaintenanceTimer = DispatcherQueue.CreateTimer();
        _recycleBinMaintenanceTimer.Interval = TimeSpan.FromHours(1);
        _recycleBinMaintenanceTimer.Tick += (_, _) =>
        {
            if (!_store.UpdateData((latestData, latestSettings) =>
                    TodoRecycleBin.PurgeExpired(latestData, latestSettings) > 0))
            {
                return;
            }

            ReloadDataAndSettings();
            RefreshAll();
        };
        _recycleBinMaintenanceTimer.Start();
    }

    private IEnumerable<TodoTask> ActiveTasksForView(string viewId)
    {
        return ActiveTaskNodesForView(viewId).Select(node => node.Task);
    }

    private IEnumerable<TodoTask> CompletedTasksForView(string viewId)
    {
        return CompletedTaskNodesForView(viewId).Select(node => node.Task);
    }

    private IEnumerable<TodoTaskNode> ActiveTaskNodesForView(string viewId)
    {
        return TodoQuery.ActiveTaskNodesForView(
            _data,
            viewId,
            CollapsedTaskIds(),
            dateFilter: _dateRangeFilter,
            maximumDepth: _maximumVisibleTaskDepth,
            listIdFilter: _listIdFilter);
    }

    private IEnumerable<TodoTaskNode> CompletedTaskNodesForView(string viewId)
    {
        return TodoQuery.CompletedTaskNodesForView(
            _data,
            viewId,
            CollapsedTaskIds(),
            dateFilter: _dateRangeFilter,
            maximumDepth: _maximumVisibleTaskDepth,
            listIdFilter: _listIdFilter);
    }

    private HashSet<string> CollapsedTaskIds()
    {
        return new HashSet<string>(_settings.CollapsedTaskIds, StringComparer.Ordinal);
    }

    private IEnumerable<TodoTask> FilterTasks(string viewId, bool completed)
    {
        return TodoQuery.FilterTasks(_data, viewId, completed, dateFilter: _dateRangeFilter, listIdFilter: _listIdFilter);
    }

    private IEnumerable<TodoTask> TasksForList(string listId)
    {
        return _data.Tasks.Where(task => task.DeletedAt is null && string.Equals(task.ListId, listId, StringComparison.Ordinal));
    }

    private TodoTask? SelectedTask()
    {
        var task = _data.Tasks.FirstOrDefault(task => string.Equals(task.Id, _selectedTaskId, StringComparison.Ordinal));
        if (task is not null && TaskBelongsToView(task, _currentViewId))
        {
            return task;
        }

        task = FirstTaskForSelection(_currentViewId);
        _selectedTaskId = task?.Id;
        return task;
    }

    private TodoTask? FirstTaskForSelection(string viewId)
    {
        if (viewId == TodoViewIds.RecycleBin)
        {
            return null;
        }

        return viewId == TodoViewIds.Completed
            ? CompletedTasksForView(viewId).FirstOrDefault()
            : ActiveTasksForView(viewId).Concat(CompletedTasksForView(viewId)).FirstOrDefault();
    }

    private bool TaskBelongsToView(TodoTask task, string viewId)
    {
        if (task.DeletedAt is not null || viewId == TodoViewIds.RecycleBin)
        {
            return false;
        }
        if (viewId == TodoViewIds.Completed)
        {
            return task.IsCompleted;
        }

        return task.IsCompleted
            ? CompletedTasksForView(viewId).Any(candidate => candidate.Id == task.Id)
            : ActiveTasksForView(viewId).Any(candidate => candidate.Id == task.Id);
    }

    private string ViewTitle(string viewId)
    {
        return TodoQuery.ViewTitle(_data, viewId);
    }

    private string DefaultListIdForNewTask() => DefaultListId();

    private string DefaultListId()
    {
        return _data.Lists.FirstOrDefault(list => IsDefaultList(list.Id))?.Id
            ?? _data.Lists.First().Id;
    }

    private bool ListExists(string listId)
    {
        return _data.Lists.Any(list => string.Equals(list.Id, listId, StringComparison.Ordinal));
    }

    private static bool IsDefaultList(string listId)
    {
        return string.Equals(listId, TodoStore.DefaultListId, StringComparison.Ordinal);
    }

    private IEnumerable<TodoList> OrderedLists()
    {
        return _data.Lists
            .OrderByDescending(list => IsDefaultList(list.Id))
            .ThenBy(list => list.CreatedAt);
    }

    private ComboBox BuildListComboBox(string selectedListId)
    {
        var listBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var list in OrderedLists())
        {
            var item = new ComboBoxItem
            {
                Content = list.Name,
                Tag = list.Id
            };
            listBox.Items.Add(item);
            if (string.Equals(selectedListId, list.Id, StringComparison.Ordinal))
            {
                listBox.SelectedItem = item;
            }
        }

        if (listBox.SelectedItem is null && listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = 0;
        }

        return listBox;
    }

    private DateTime? DefaultDueDateForCurrentView()
    {
        return _currentViewId switch
        {
            TodoViewIds.Today => DateTime.Today,
            TodoViewIds.Planned => DateTime.Today.AddDays(1),
            _ => null
        };
    }

    private bool IsKnownView(string viewId)
    {
        return TodoQuery.IsKnownView(_data, viewId) &&
            (viewId != TodoViewIds.RecycleBin || _settings.IsRecycleBinEnabled);
    }

    private string TaskMeta(TodoTask task)
    {
        var parts = new List<string>();
        var listName = _data.Lists.FirstOrDefault(list => list.Id == task.ListId)?.Name;
        if (!string.IsNullOrWhiteSpace(listName))
        {
            parts.Add(listName);
        }

        parts.Add(task.StartDate.Date == DateTime.Today
            ? "开始 今天"
            : $"开始 {task.StartDate:yyyy-MM-dd}");

        if (task.DueDate.HasValue)
        {
            parts.Add(task.DueDate.Value.Date == DateTime.Today
                ? "截止 今天"
                : $"截止 {task.DueDate.Value:yyyy-MM-dd}");
        }

        return string.Join(" · ", parts);
    }

    private Button TaskCheckButton(TodoTask task)
    {
        var completed = task.IsCompleted;
        var button = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        var circle = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderThickness = completed ? new Thickness(0) : new Thickness(1.6),
            BorderBrush = Brush(0x8BA0AE),
            Background = completed ? Brush(0x138A43) : TransparentBrush(),
            Child = completed
                ? new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 12,
                    Foreground = PureWhiteBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : null
        };
        button.Content = circle;
        button.Click += async (_, _) => await ToggleTaskCompletedAsync(task);
        return button;
    }

    private Button TreeToggleButton(TodoTask task)
    {
        var collapsed = IsTaskCollapsed(task.Id);
        var button = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = collapsed ? "\uE76C" : "\uE70D",
                FontSize = 12,
                Foreground = SecondaryTextBrush()
            }
        };
        ToolTipService.SetToolTip(button, collapsed ? "展开子任务" : "收起子任务");
        button.Click += (_, _) => ToggleTaskCollapsed(task.Id);
        return button;
    }

    private bool IsTaskCollapsed(string taskId)
    {
        return _settings.CollapsedTaskIds.Contains(taskId, StringComparer.Ordinal);
    }

    private void ToggleTaskCollapsed(string taskId)
    {
        if (IsTaskCollapsed(taskId))
        {
            _settings.CollapsedTaskIds.RemoveAll(id => string.Equals(id, taskId, StringComparison.Ordinal));
        }
        else
        {
            _settings.CollapsedTaskIds.Add(taskId);
        }

        SaveSettings();
        RefreshTaskContent();
    }

    private Border TaskListPill(string listId)
    {
        var listName = _data.Lists.FirstOrDefault(list => list.Id == listId)?.Name ?? "任务清单";
        return new Border
        {
            MinWidth = 72,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            Background = ListSoftColorBrush(listId),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new TextBlock
            {
                Text = listName,
                FontSize = 12,
                Foreground = ListColorBrush(listId),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private string TaskTimeText(TodoTask task)
    {
        if (task.IsCompleted)
        {
            if (!task.CompletedAt.HasValue)
            {
                return "已完成";
            }

            var local = task.CompletedAt.Value.ToLocalTime();
            return local.Date == DateTimeOffset.Now.Date
                ? $"今天 {local:HH:mm} 完成"
                : $"{local:MM-dd HH:mm} 完成";
        }

        if (!task.DueDate.HasValue)
        {
            var startDate = task.StartDate.Date;
            return startDate == DateTime.Today ? "今天开始" : $"{startDate:MM-dd} 开始";
        }

        var date = task.DueDate.Value.Date;
        if (date == DateTime.Today)
        {
            return "今天";
        }

        if (date == DateTime.Today.AddDays(1))
        {
            return "明天";
        }

        if (date == DateTime.Today.AddDays(2))
        {
            return "后天";
        }

        return date.ToString("MM-dd");
    }

    private Button RowIconButton(string glyph, string label, Brush foreground)
    {
        var button = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = foreground
            }
        };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private Button HeaderActionButton(string glyph, string text)
    {
        var button = new Button
        {
            Height = 32,
            Padding = new Thickness(8, 0, 8, 0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = glyph,
                        FontSize = 15,
                        Foreground = SecondaryTextBrush()
                    },
                    new TextBlock
                    {
                        Text = text,
                        FontSize = 13,
                        Foreground = SecondaryTextBrush(),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
        return button;
    }

    private UIElement DetailField(
        string glyph,
        string label,
        FrameworkElement value,
        double minimumValueWidth = 0)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            MinHeight = 44
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = minimumValueWidth
        });
        grid.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            Foreground = SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 14,
            Foreground = TextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 1);
        grid.Children.Add(labelBlock);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private Button SidebarIconButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 18,
                Foreground = Brush(0xAFC1D8)
            }
        };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private void ChangeListColor(TodoList list, string colorId)
    {
        var normalizedColorId = TodoListColorIds.Normalize(colorId, list.Id);
        if (string.Equals(list.ColorId, normalizedColorId, StringComparison.Ordinal))
        {
            return;
        }

        list.ColorId = normalizedColorId;
        _store.SaveData(_data);
        RefreshAll();
    }

    private async Task ShowListColorDialogAsync(TodoList list)
    {
        var palette = new Grid
        {
            MinWidth = 392,
            ColumnSpacing = 12,
            RowSpacing = 12
        };
        palette.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        palette.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < TodoListColorIds.All.Count / 2; row++)
        {
            palette.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ResolveElementTheme(),
            Title = $"{list.Name} 的配色",
            Content = palette,
            CloseButtonText = "完成"
        };

        for (var index = 0; index < TodoListColorIds.All.Count; index++)
        {
            var colorId = TodoListColorIds.Normalize(TodoListColorIds.All[index], list.Id);
            var isSelected = string.Equals(list.ColorId, colorId, StringComparison.Ordinal);
            var primary = ListColorBrushForColorId(colorId);
            var soft = ListSoftColorBrushForColorId(colorId);
            var card = new Button
            {
                Height = 56,
                Padding = new Thickness(14, 0, 12, 0),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                BorderBrush = isSelected ? primary : PaletteCardBorderBrush(),
                Background = soft,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            card.Resources["ButtonBackground"] = soft;
            card.Resources["ButtonBackgroundPointerOver"] = soft;
            card.Resources["ButtonBackgroundPressed"] = soft;
            card.Resources["ButtonBorderBrush"] = card.BorderBrush;
            card.Resources["ButtonBorderBrushPointerOver"] = primary;
            card.Resources["ButtonBorderBrushPressed"] = primary;
            card.Resources["ButtonBorderThickness"] = card.BorderThickness;
            card.Resources["ButtonBorderThicknessPointerOver"] = card.BorderThickness;
            card.Resources["ButtonBorderThicknessPressed"] = card.BorderThickness;

            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = primary,
                VerticalAlignment = VerticalAlignment.Center
            });
            var label = new TextBlock
            {
                Text = ListColorLabel(colorId),
                FontSize = 14,
                FontWeight = MuxFontWeights.SemiBold,
                Foreground = primary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            if (isSelected)
            {
                var selected = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 16,
                    Foreground = primary,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(selected, 2);
                content.Children.Add(selected);
            }

            card.Content = content;
            card.Click += (_, _) =>
            {
                ChangeListColor(list, colorId);
                dialog.Hide();
            };
            Grid.SetRow(card, index / 2);
            Grid.SetColumn(card, index % 2);
            palette.Children.Add(card);
        }

        await dialog.ShowAsync();
    }

    private string ListColorId(string listId)
    {
        var colorId = _data.Lists.FirstOrDefault(list => string.Equals(list.Id, listId, StringComparison.Ordinal))?.ColorId;
        return TodoListColorIds.Normalize(colorId, listId);
    }

    private Brush ListColorBrush(string listId) => ListColorBrushForColorId(ListColorId(listId));

    private Brush ListColorBrushForColorId(string colorId)
    {
        return TodoListColorIds.Normalize(colorId) switch
        {
            TodoListColorIds.Cyan => Brush(0x2B7F82),
            TodoListColorIds.Indigo => Brush(0x5D6F9D),
            TodoListColorIds.Purple => Brush(0x8064A7),
            TodoListColorIds.Pink => Brush(0xB86B87),
            TodoListColorIds.Red => Brush(0xB86A62),
            TodoListColorIds.Orange => Brush(0xB98B45),
            TodoListColorIds.Green => Brush(0x4A8B6B),
            TodoListColorIds.Cobalt => Brush(0x526C91),
            TodoListColorIds.Mauve => Brush(0x967B9B),
            TodoListColorIds.Olive => Brush(0x7F8A59),
            TodoListColorIds.Copper => Brush(0xA17461),
            _ => Brush(0x426DAD)
        };
    }

    private Brush ListSoftColorBrush(string listId) => ListSoftColorBrushForColorId(ListColorId(listId));

    private Brush ListSoftColorBrushForColorId(string colorId)
    {
        return TodoListColorIds.Normalize(colorId) switch
        {
            TodoListColorIds.Cyan => Brush(0xE5F1F1),
            TodoListColorIds.Indigo => Brush(0xEBEEF4),
            TodoListColorIds.Purple => Brush(0xF0ECF7),
            TodoListColorIds.Pink => Brush(0xF8ECEF),
            TodoListColorIds.Red => Brush(0xF8ECEA),
            TodoListColorIds.Orange => Brush(0xF8F1E4),
            TodoListColorIds.Green => Brush(0xEAF3EE),
            TodoListColorIds.Cobalt => Brush(0xECF0F5),
            TodoListColorIds.Mauve => Brush(0xF3EDF4),
            TodoListColorIds.Olive => Brush(0xF1F2E8),
            TodoListColorIds.Copper => Brush(0xF5EDE8),
            _ => Brush(0xE8EEF8)
        };
    }

    private static string ListColorLabel(string colorId)
    {
        return TodoListColorIds.Normalize(colorId) switch
        {
            TodoListColorIds.Cyan => "孔雀青",
            TodoListColorIds.Indigo => "石板蓝",
            TodoListColorIds.Purple => "紫晶",
            TodoListColorIds.Pink => "玫瑰",
            TodoListColorIds.Red => "珊瑚",
            TodoListColorIds.Orange => "琥珀",
            TodoListColorIds.Green => "祖母绿",
            TodoListColorIds.Cobalt => "深海蓝",
            TodoListColorIds.Mauve => "雾紫",
            TodoListColorIds.Olive => "橄榄",
            TodoListColorIds.Copper => "铜棕",
            _ => "蓝宝石"
        };
    }

    private void RestoreCompletedForCurrentView()
    {
        foreach (var task in CompletedTasksForView(_currentViewId).ToList())
        {
            task.IsCompleted = false;
            task.CompletedAt = null;
            Touch(task);
        }

        SaveDataAndRefresh();
    }

    private void ClearCompletedForCurrentView()
    {
        foreach (var task in CompletedTasksForView(_currentViewId)
                     .Where(task => task.DeletedAt is null)
                     .ToList())
        {
            TodoRecycleBin.DeleteTaskTree(_data, _settings, task.Id);
        }

        _selectedTaskId = null;
        SaveDataAndRefresh();
    }

    private static void Touch(TodoTask task)
    {
        task.UpdatedAt = DateTimeOffset.Now;
    }

    private UIElement EmptyState(string text)
    {
        return new Border
        {
            MinHeight = 132,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(0xE1EAED),
            Background = Brush(0xFFFFFF),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 15,
                Foreground = SecondaryTextBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private TextBlock SectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Margin = new Thickness(10, 0, 0, 4),
            FontSize = 12,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = SecondaryTextBrush()
        };
    }

    private TextBlock FieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = MuxFontWeights.SemiBold,
            Foreground = SecondaryTextBrush(),
            Margin = new Thickness(0, 2, 0, -8)
        };
    }

    private Button PillButton(string text, string glyph)
    {
        var button = new Button
        {
            Height = 38,
            MinWidth = 112,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(0xDCE7EA),
            Background = Brush(0xFFFFFF),
            Padding = new Thickness(12, 0, 12, 0)
        };
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 15,
                    Foreground = AccentBrush()
                },
                new TextBlock
                {
                    Text = text,
                    FontSize = 14,
                    FontWeight = MuxFontWeights.SemiBold,
                    Foreground = TextBrush(),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        return button;
    }

    private Button PrimaryButton(string text, string glyph)
    {
        var button = PillButton(text, glyph);
        button.Background = AccentBrush();
        button.BorderBrush = AccentBrush();
        if (button.Content is StackPanel stack)
        {
            foreach (var child in stack.Children)
            {
                if (child is FontIcon icon)
                {
                    icon.Foreground = PureWhiteBrush();
                }
                else if (child is TextBlock block)
                {
                    block.Foreground = PureWhiteBrush();
                }
            }
        }

        return button;
    }

    private Button DangerButton(string text, string glyph)
    {
        var button = PillButton(text, glyph);
        button.BorderBrush = Brush(0xF2C8C8);
        button.Background = Brush(0xFFF7F7);
        if (button.Content is StackPanel stack)
        {
            foreach (var child in stack.Children)
            {
                if (child is FontIcon icon)
                {
                    icon.Foreground = Brush(0xB42318);
                }
                else if (child is TextBlock block)
                {
                    block.Foreground = Brush(0xB42318);
                }
            }
        }

        return button;
    }

    private Button IconOnlyButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = SecondaryTextBrush()
            }
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void ApplyFlatTextBoxStyle(TextBox textBox)
    {
        var foreground = TextBrush();
        textBox.Resources["TextControlBackground"] = TransparentBrush();
        textBox.Resources["TextControlBackgroundPointerOver"] = TransparentBrush();
        textBox.Resources["TextControlBackgroundFocused"] = TransparentBrush();
        textBox.Resources["TextControlBorderBrush"] = TransparentBrush();
        textBox.Resources["TextControlBorderBrushPointerOver"] = TransparentBrush();
        textBox.Resources["TextControlBorderBrushFocused"] = TransparentBrush();
        textBox.Resources["TextControlForeground"] = foreground;
        textBox.Resources["TextControlForegroundPointerOver"] = foreground;
        textBox.Resources["TextControlForegroundFocused"] = foreground;
        textBox.Resources["TextControlPlaceholderForeground"] = foreground;
        textBox.Resources["TextControlPlaceholderForegroundPointerOver"] = foreground;
        textBox.Resources["TextControlPlaceholderForegroundFocused"] = foreground;
        textBox.Foreground = foreground;
    }

    private ElementTheme ResolveElementTheme()
    {
        return _settings.Theme switch
        {
            TodoThemeIds.Light => ElementTheme.Light,
            TodoThemeIds.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private bool IsDarkTheme()
    {
        return _settings.Theme switch
        {
            TodoThemeIds.Dark => true,
            TodoThemeIds.Light => false,
            _ => IsSystemThemeDark()
        };
    }

    private bool IsSystemThemeDark()
    {
        try
        {
            var color = _uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background);
            return color.R + color.G + color.B < 384;
        }
        catch
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }

    private SolidColorBrush TextBrush() => Brush(0x17242A);
    private SolidColorBrush SecondaryTextBrush() => Brush(0x6F7F86);
    private SolidColorBrush MutedBrush() => Brush(0x96A4AA);
    private SolidColorBrush AccentBrush() => Brush(0x128CA2);
    private SolidColorBrush AccentDarkBrush() => Brush(0x0C6F82);
    private SolidColorBrush TaskHoverBorderBrush(bool completed) => IsDarkTheme()
        ? Brush(completed ? 0x4E7C63u : 0x3F7480u)
        : Brush(completed ? 0xA9D7BDu : 0x9BCED7u);
    private SolidColorBrush TaskHoverBackgroundBrush(bool completed) => IsDarkTheme()
        ? Brush(completed ? 0x1B2A23u : 0x19282Fu)
        : Brush(completed ? 0xF5FBF6u : 0xF4FAFBu);
    private SolidColorBrush FilterActiveBorderBrush() => IsDarkTheme() ? Brush(0x3F8694) : Brush(0x8CC9D3);
    private SolidColorBrush FilterActiveBackgroundBrush() => IsDarkTheme() ? Brush(0x19313A) : Brush(0xEEF9FA);
    private SolidColorBrush FilterActiveTextBrush() => IsDarkTheme() ? Brush(0x9DDFE8) : Brush(0x0C6F82);
    private SolidColorBrush FilterHoverBorderBrush(bool active) => IsDarkTheme()
        ? Brush(active ? 0x63BBC9u : 0x4F94A1u)
        : Brush(active ? 0x5EB3C1u : 0x7ABCC7u);
    private SolidColorBrush FilterHoverBackgroundBrush(bool active) => IsDarkTheme()
        ? Brush(active ? 0x20444Fu : 0x1C2B33u)
        : Brush(active ? 0xDDF4F7u : 0xF1F8FAu);
    private SolidColorBrush FilterPressedBorderBrush(bool active) => IsDarkTheme()
        ? Brush(active ? 0x4FAABAu : 0x407D89u)
        : Brush(active ? 0x3599A9u : 0x4B9FAAu);
    private SolidColorBrush FilterPressedBackgroundBrush(bool active) => IsDarkTheme()
        ? Brush(active ? 0x173942u : 0x16252Cu)
        : Brush(active ? 0xCFEFF3u : 0xE6F3F6u);
    private SolidColorBrush PaletteCardBorderBrush() => IsDarkTheme() ? Brush(0x40505E) : Brush(0xD8E2E6);
    private static SolidColorBrush TransparentBrush() => new(Colors.Transparent);
    private static SolidColorBrush PureWhiteBrush() => new(Colors.White);
    private static SolidColorBrush SolidBrush(uint rgb)
    {
        return new SolidColorBrush(ColorHelper.FromArgb(
            255,
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF)));
    }

    private SolidColorBrush Brush(uint rgb)
    {
        if (IsDarkTheme())
        {
            rgb = DarkThemeColor(rgb);
        }

        return SolidBrush(rgb);
    }

    private uint DarkThemeColor(uint rgb)
    {
        return rgb switch
        {
            0xFFFFFF => 0x151B22,
            0xF7FAFB => 0x11161C,
            0xFBFCFC => 0x151F26,
            0xF8FAFB => 0x151F26,
            0xEEF9FA => 0x17323A,
            0xEEF8F1 => 0x143E34,
            0xDFF4F7 => 0x0B3A7A,
            0xDCE7EA => 0x28333E,
            0xE7EEF0 => 0x212B35,
            0xE1EAED => 0x28333E,
            0xBFDCCB => 0x2A5A45,
            0xF2C8C8 => 0x7A3534,
            0xFFF7F7 => 0x27181A,
            0xB42318 => 0xFF615C,
            0x17242A => 0xEEF3F8,
            0x6F7F86 => 0x98A7B6,
            0x96A4AA => 0x9EACBA,
            0x128CA2 => 0x34B7C8,
            0x0C6F82 => 0x58CDF0,
            0x8BA0AE => 0x7F90A4,
            0x138A43 => 0x25B765,
            0xF06423 => 0xFF8A3D,
            0xF2B01E => 0xF2B01E,
            0xE8F1FF => 0x19304B,
            0xE5F7EA => 0x173524,
            0xF0E8FF => 0x2A2541,
            0xE6F7F9 => 0x123740,
            0xEDEBFF => 0x292542,
            0xFCE7F3 => 0x421F35,
            0xFEE2E2 => 0x421E22,
            0xFFF0E5 => 0x43281B,
            0x1D6DFF => 0x7EB0FF,
            0x4F46E5 => 0xA5B4FC,
            0x18A957 => 0x8CE9AD,
            0x8B5CF6 => 0xC8B5FF,
            0xDB2777 => 0xF472B6,
            0xDC2626 => 0xF87171,
            0xEA580C => 0xFB923C,
            0x2B7F82 => 0x75C1C2,
            0x426DAD => 0x94B7EC,
            0x5D6F9D => 0xA7B4C9,
            0x8064A7 => 0xC1AFE0,
            0xB86B87 => 0xE5A8B9,
            0xB86A62 => 0xE2A8A0,
            0xB98B45 => 0xE4C182,
            0x4A8B6B => 0x9DCEB3,
            0xE5F1F1 => 0x244344,
            0xE8EEF8 => 0x26364F,
            0xEBEEF4 => 0x303948,
            0xF0ECF7 => 0x383044,
            0xF8ECEF => 0x472E38,
            0xF8ECEA => 0x482D2A,
            0xF8F1E4 => 0x493B25,
            0xEAF3EE => 0x294235,
            0x526C91 => 0xAAB9CD,
            0x967B9B => 0xD3BAD5,
            0x7F8A59 => 0xC7D0A0,
            0xA17461 => 0xD8B2A1,
            0xECF0F5 => 0x303943,
            0xF3EDF4 => 0x3D3340,
            0xF1F2E8 => 0x3B4130,
            0xF5EDE8 => 0x43342F,
            _ => rgb
        };
    }

    private void ApplyCaptionButtonColors(AppWindow appWindow)
    {
        appWindow.TitleBar.ButtonForegroundColor = CaptionButtonColor();
        appWindow.TitleBar.ButtonInactiveForegroundColor = CaptionButtonInactiveColor();
        appWindow.TitleBar.ButtonHoverForegroundColor = CaptionButtonColor();
        appWindow.TitleBar.ButtonPressedForegroundColor = CaptionButtonColor();
        appWindow.TitleBar.ButtonHoverBackgroundColor = CaptionButtonHoverColor();
        appWindow.TitleBar.ButtonPressedBackgroundColor = CaptionButtonPressedColor();
    }

    private void ApplyCaptionButtonColorsToCurrentWindow()
    {
        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(WindowHandle());
            ApplyCaptionButtonColors(AppWindow.GetFromWindowId(windowId));
        }
        catch
        {
            // Theme switching should still rebuild the app surface if caption APIs are unavailable.
        }
    }

    private Color CaptionButtonColor()
    {
        return IsDarkTheme()
            ? ColorHelper.FromArgb(255, 215, 224, 234)
            : ColorHelper.FromArgb(255, 23, 36, 42);
    }

    private Color CaptionButtonInactiveColor()
    {
        return IsDarkTheme()
            ? ColorHelper.FromArgb(180, 215, 224, 234)
            : ColorHelper.FromArgb(180, 23, 36, 42);
    }

    private Color CaptionButtonHoverColor()
    {
        return IsDarkTheme()
            ? ColorHelper.FromArgb(28, 215, 224, 234)
            : ColorHelper.FromArgb(18, 23, 36, 42);
    }

    private Color CaptionButtonPressedColor()
    {
        return IsDarkTheme()
            ? ColorHelper.FromArgb(42, 215, 224, 234)
            : ColorHelper.FromArgb(32, 23, 36, 42);
    }

    private static Uri FileUri(string path)
    {
        return new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Path = Path.GetFullPath(path)
        }.Uri;
    }

    internal void RestoreFromExternalActivation()
    {
        ReloadDataAndSettings();
        var wasStickyModeEnabled = _settings.IsStickyModeEnabled;
        _settings.IsStickyModeEnabled = false;
        _store.SaveSettings(_settings);
        ApplyCaptionButtonColorsToCurrentWindow();
        BuildShell();
        RefreshAll();

        var hwnd = WindowHandle();
        ShowWindow(hwnd, ShowWindowRestore);
        Activate();
        SetForegroundWindow(hwnd);
        if (wasStickyModeEnabled)
        {
            StickyLauncher.TryShutdown();
        }

        QueueStickyPrewarm();
    }

    private IntPtr WindowHandle() => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void HideNativeWindow()
    {
        ShowWindow(WindowHandle(), ShowWindowHide);
    }

    private void ShowNativeWindow()
    {
        ShowWindow(WindowHandle(), ShowWindowShow);
    }

    private sealed record TodoDropTarget(string TaskId, TodoDropPlacement Placement);

    private enum TodoDropPlacement
    {
        Before,
        After,
        Child,
        TopLevelEnd
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
