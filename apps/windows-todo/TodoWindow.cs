using Fowan.Todo.Core.Models;
using Fowan.Todo.Core.Services;
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
using Windows.System;
using Windows.UI;
using MuxFontWeights = Microsoft.UI.Text.FontWeights;
using WinTextDecorations = Windows.UI.Text.TextDecorations;

namespace Fowan.Todo.Windows;

public sealed class TodoWindow : Window
{
    private const double SidebarWidth = 248;
    private const double DetailWidth = 382;

    private readonly TodoStore _store = new();
    private readonly TodoData _data;
    private readonly TodoSettings _settings;

    private Grid _root = new();
    private StackPanel _navigationPanel = new();
    private StackPanel _listPanel = new();
    private StackPanel _taskContent = new();
    private Border _detailHost = new();
    private TextBox _addTaskBox = new();
    private TextBlock _taskTitle = new();
    private TextBlock _taskSummary = new();

    private string _currentViewId;
    private string? _selectedTaskId;
    private System.Diagnostics.Process? _stickyProcess;
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();

    public TodoWindow()
    {
        _data = _store.LoadData();
        _settings = _store.LoadSettings();
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
    }

    internal void ActivateInitialMode()
    {
        if (_settings.IsStickyModeEnabled)
        {
            OpenStickyMode(closeMainWindow: true);
            return;
        }

        Activate();
    }

    private void ConfigureWindow()
    {
        Title = "Fowan Todo";

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
        SetTitleBar(brandIcon);
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

        var filterButton = PillButton("筛选", "\uE71C");
        filterButton.MinWidth = 92;
        Grid.SetColumn(filterButton, 1);
        header.Children.Add(filterButton);

        var stickyMode = IconOnlyButton("\uE8A7", "切换便签模式");
        stickyMode.Click += (_, _) => OpenStickyMode();
        Grid.SetColumn(stickyMode, 2);
        header.Children.Add(stickyMode);
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

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _taskContent = new StackPanel { Spacing = 8 };
        scroll.Content = _taskContent;
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);

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
            _currentViewId = viewId;
            _selectedTaskId = FirstTaskForSelection(viewId)?.Id;
            SaveSettings();
            RefreshAll();
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
            _currentViewId = viewId;
            _selectedTaskId = FirstTaskForSelection(viewId)?.Id;
            SaveSettings();
            RefreshAll();
        };

        shell.Child = grid;
        AutomationProperties.SetName(shell, list.Name);
        ToolTipService.SetToolTip(shell, list.Name);
        return shell;
    }

    private MenuFlyout BuildListMenu(TodoList list)
    {
        var menu = new MenuFlyout();

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
        _taskContent.Children.Clear();
        _taskTitle.Text = ViewTitle(_currentViewId);

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
        if (activeTasks.Count == 0)
        {
            _taskContent.Children.Add(EmptyState("当前没有待办任务"));
        }
        else
        {
            foreach (var node in activeTasks)
            {
                _taskContent.Children.Add(TaskRow(node.Task, completed: false, depth: node.Depth));
            }
        }

        _taskContent.Children.Add(CompletedSection(completedTasks));
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

    private UIElement TaskRow(TodoTask task, bool completed, int depth = 0)
    {
        var selected = string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal);
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = 54,
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Margin = new Thickness(Math.Clamp(depth, 0, TodoQuery.MaxTaskTreeDepth - 1) * 22, 0, 0, 0)
        };
        AutomationProperties.SetName(button, task.Title);

        var shell = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = selected ? (completed ? Brush(0xBFDCCB) : AccentBrush()) : Brush(0xE1EAED),
            Background = selected ? (completed ? Brush(0xEEF8F1) : Brush(0xEEF9FA)) : Brush(completed ? 0xFBFCFCu : 0xFFFFFFu),
            Padding = new Thickness(14, 8, 10, 8)
        };

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
        if (TodoQuery.DirectChildCount(_data, task.Id) > 0)
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

        if (completed)
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
            delete.Click += (_, _) => DeleteTask(task);
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
            _selectedTaskId = task.Id;
            SaveSettings();
            RefreshTaskContent();
            RefreshDetail();
        };

        return button;
    }

    private void RefreshDetail()
    {
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
        delete.Click += (_, _) => DeleteTask(task);
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

    private void AddTask(string title, string listId, bool isImportant, DateTime startDate, DateTime? dueDate, string? parentTaskId = null)
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
            CreatedAt = now,
            UpdatedAt = now
        };
        _data.Tasks.Add(task);
        _selectedTaskId = task.Id;
        if (_currentViewId == TodoViewIds.Completed)
        {
            _currentViewId = TodoViewIds.All;
        }

        SaveDataAndRefresh();
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
            duePicker.Date?.DateTime.Date);
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
            parent.Id);
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

        var result = await dialog.ShowAsync();
        if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary))
        {
            return;
        }

        if (themeBox.SelectedItem is ComboBoxItem { Tag: string theme })
        {
            SetTheme(theme);
        }

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
            VerticalAlignment = VerticalAlignment.Center
        };

        layout.Children.Add(HelpHero());
        layout.Children.Add(HelpModeSection(
            "主界面",
            "整理、编辑和查看完整任务",
            "适合集中管理任务、清单、日期、备注和子任务。",
            [
                new HelpCardInfo("\uE8A5", "切换视图", "在左侧切换今日、计划、重要、全部、已完成和自定义清单。"),
                new HelpCardInfo("\uE710", "新增任务", "在顶部输入任务后按回车；输入为空时点击添加按钮会打开新建任务弹窗。"),
                new HelpCardInfo("\uE70F", "编辑详情", "点击任务后，在右侧编辑标题、清单、开始时间、截止日期、备注和重要标记。"),
                new HelpCardInfo("\uE8FD", "管理清单", "左侧清单区可以新增清单，也可以对已有清单改名或删除。"),
                new HelpCardInfo("\uE8C8", "子任务", "选中任务后可添加子任务；父任务可展开或收起，最多支持三层。"),
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
                new HelpCardInfo("\uE7C2", "拖动排序", "长按任务后拖动；虚线框会预览松手后的具体位置和父子归属。"),
                new HelpCardInfo("\uE73E", "快速处理", "可直接新增、完成、恢复任务，并展开或折叠已完成分组。")
            ],
            useTwoColumns));
        layout.Children.Add(HelpTipBand());

        var scroll = new ScrollViewer
        {
            Content = layout,
            Width = contentWidth,
            MaxHeight = HelpDialogMaxHeight(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
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
            MinWidth = 92,
            Height = 36,
            Padding = new Thickness(18, 0, 18, 0),
            CornerRadius = new CornerRadius(8),
            Background = HelpPrimaryButtonBrush(),
            Foreground = HelpPrimaryButtonTextBrush(),
            BorderBrush = HelpPrimaryButtonBrush()
        };
        ApplyHelpButtonColors(
            closeButton,
            HelpPrimaryButtonBrush(),
            HelpPrimaryButtonTextBrush(),
            HelpPrimaryButtonBrush(),
            HelpPrimaryButtonHoverBrush(),
            HelpPrimaryButtonPressedBrush());

        var panelWidth = HelpDialogWidth(contentWidth);
        var panelContent = new Grid
        {
            Width = Math.Max(300, panelWidth - 48),
            RowSpacing = 18
        };
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
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
        var iconClose = new Button
        {
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            Background = TransparentBrush(),
            Foreground = HelpTextBrush(),
            BorderBrush = TransparentBrush(),
            Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 12,
                Foreground = HelpTextBrush()
            }
        };
        ApplyHelpButtonColors(
            iconClose,
            TransparentBrush(),
            HelpTextBrush(),
            TransparentBrush(),
            HelpCloseButtonHoverBrush(),
            HelpCloseButtonPressedBrush());
        var iconCloseFrame = new Border
        {
            Width = 38,
            Height = 38,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Background = HelpCloseButtonBrush(),
            BorderBrush = HelpPanelBorderBrush(),
            BorderThickness = new Thickness(1),
            Child = iconClose
        };
        Grid.SetColumn(iconCloseFrame, 1);
        header.Children.Add(iconCloseFrame);
        panelContent.Children.Add(header);

        Grid.SetRow(scroll, 1);
        panelContent.Children.Add(scroll);

        var footer = new Grid();
        footer.Children.Add(closeButton);
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(footer, 2);
        panelContent.Children.Add(footer);

        var panel = new Border
        {
            Width = panelWidth,
            MaxHeight = HelpDialogPanelMaxHeight(),
            Padding = new Thickness(24),
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

        var completion = new TaskCompletionSource<object?>();
        var closed = false;
        void Close()
        {
            if (closed)
            {
                return;
            }

            closed = true;
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
        iconClose.Click += (_, _) => Close();
        overlay.Loaded += (_, _) => overlay.Focus(FocusState.Programmatic);

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

        return Math.Min(840, Math.Max(360, rootHeight - 64));
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

    private void DeleteTask(TodoTask task)
    {
        var removeIds = TodoQuery.DescendantIds(_data, task.Id)
            .Append(task.Id)
            .ToHashSet(StringComparer.Ordinal);
        _data.Tasks.RemoveAll(candidate => removeIds.Contains(candidate.Id));
        _settings.CollapsedTaskIds.RemoveAll(removeIds.Contains);
        _selectedTaskId = FirstTaskForSelection(_currentViewId)?.Id;
        SaveDataAndRefresh();
    }

    private void SaveDataAndRefresh()
    {
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
        if (_stickyProcess is not null && !_stickyProcess.HasExited)
        {
            return;
        }

        var stickyViewId = _currentViewId == TodoViewIds.Completed ? TodoViewIds.Today : _currentViewId;
        _settings.CurrentViewId = stickyViewId;
        _settings.IsStickyModeEnabled = true;
        _store.SaveSettings(_settings);

        if (StickyLauncher.TryLaunch(out var process))
        {
            _stickyProcess = process;

            if (closeMainWindow)
            {
                Close();
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
        return TodoQuery.ActiveTaskNodesForView(_data, viewId, CollapsedTaskIds());
    }

    private IEnumerable<TodoTaskNode> CompletedTaskNodesForView(string viewId)
    {
        return TodoQuery.CompletedTaskNodesForView(_data, viewId, CollapsedTaskIds());
    }

    private HashSet<string> CollapsedTaskIds()
    {
        return new HashSet<string>(_settings.CollapsedTaskIds, StringComparer.Ordinal);
    }

    private IEnumerable<TodoTask> FilterTasks(string viewId, bool completed)
    {
        return TodoQuery.FilterTasks(_data, viewId, completed);
    }

    private IEnumerable<TodoTask> TasksForList(string listId)
    {
        return _data.Tasks.Where(task => string.Equals(task.ListId, listId, StringComparison.Ordinal));
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
        return viewId == TodoViewIds.Completed
            ? CompletedTasksForView(viewId).FirstOrDefault()
            : ActiveTasksForView(viewId).Concat(CompletedTasksForView(viewId)).FirstOrDefault();
    }

    private bool TaskBelongsToView(TodoTask task, string viewId)
    {
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
        if (TodoViewIds.IsList(viewId))
        {
            return _data.Lists.FirstOrDefault(list => list.Id == TodoViewIds.ListId(viewId))?.Name ?? "任务清单";
        }

        return viewId switch
        {
            TodoViewIds.Planned => "计划任务",
            TodoViewIds.Important => "重要任务",
            TodoViewIds.All => "全部任务",
            TodoViewIds.Completed => "已完成",
            _ => "今日任务"
        };
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
        if (viewId is TodoViewIds.Today or TodoViewIds.Planned or TodoViewIds.Important or TodoViewIds.All or TodoViewIds.Completed)
        {
            return true;
        }

        return TodoViewIds.IsList(viewId) &&
            _data.Lists.Any(list => string.Equals(list.Id, TodoViewIds.ListId(viewId), StringComparison.Ordinal));
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

    private UIElement DetailField(string glyph, string label, FrameworkElement value)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            MinHeight = 44
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

    private Brush ListColorBrush(string listId)
    {
        return listId switch
        {
            TodoStore.DefaultListId => Brush(0x128CA2),
            "personal" => Brush(0x18A957),
            "study" => Brush(0x8B5CF6),
            _ => Brush(0x1D6DFF)
        };
    }

    private Brush ListSoftColorBrush(string listId)
    {
        return listId switch
        {
            TodoStore.DefaultListId => Brush(0xE6F7F9),
            "personal" => Brush(0xE5F7EA),
            "study" => Brush(0xF0E8FF),
            _ => Brush(0xE8F1FF)
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
        foreach (var task in CompletedTasksForView(_currentViewId).ToList())
        {
            _data.Tasks.Remove(task);
        }

        _selectedTaskId = FirstTaskForSelection(_currentViewId)?.Id;
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
        textBox.Resources["TextControlBackground"] = TransparentBrush();
        textBox.Resources["TextControlBackgroundPointerOver"] = TransparentBrush();
        textBox.Resources["TextControlBackgroundFocused"] = TransparentBrush();
        textBox.Resources["TextControlBorderBrush"] = TransparentBrush();
        textBox.Resources["TextControlBorderBrushPointerOver"] = TransparentBrush();
        textBox.Resources["TextControlBorderBrushFocused"] = TransparentBrush();
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
            0x1D6DFF => 0x7EB0FF,
            0x18A957 => 0x8CE9AD,
            0x8B5CF6 => 0xC8B5FF,
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

    private IntPtr WindowHandle() => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void HideNativeWindow()
    {
        ShowWindow(WindowHandle(), 0);
    }

    private void ShowNativeWindow()
    {
        ShowWindow(WindowHandle(), 5);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
