using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Sticky.Windows.Platform;
using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fowan.Todo.Sticky.Windows;

public sealed class StickyWindow : Window
{
    private const double BaseWidth = 408;
    private const double BaseHeight = 568;
    private const double FloatingWindowSize = 52;
    private const double FloatingEdgeThresholdDip = 16;
    private const double WindowDragActivationDistanceDip = 6;
    private const double ResizeBorderDip = 10;
    private const int MinResizeBorderPixels = 8;
    private const int MaxResizeBorderPixels = 12;
    private const int TaskDragLongPressMilliseconds = 350;
    private const double TaskDragMoveCancelDistance = 6;
    private const double TaskDropEdgeRatio = 0.28;
    private const double TaskAutoScrollActivationDistance = 96;
    private const double TaskAutoScrollMinimumSpeed = 120;
    private const double TaskAutoScrollMaximumSpeed = 720;
    private const double TaskAutoScrollSectionOverlap = 50;
    private const string StickyViewId = TodoViewIds.Today;
    private const string MainActivationEventName = @"Local\Fowan.Todo.Windows.Activate";
    private const string MainShutdownEventName = @"Local\Fowan.Todo.Windows.Shutdown";

    private readonly TodoPersistenceController _store = TodoPersistenceController.CreateDefault();
    private readonly IProcessLauncher _processLauncher;
    private readonly string? _mainExePath;
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly List<Border> _taskRows = [];

    private TodoData _data;
    private TodoSettings _settings;
    private HwndSource? _source;
    private Grid _root = new();
    private Border _shell = new();
    private Border _popupDismissOverlay = new();
    private Border _addRowBorder = new();
    private Border _taskDivider = new();
    private FrameworkElement _dragHandle = new Grid();
    private FrameworkElement _brandIcon = new Grid();
    private Button _settingsButton = new();
    private Button _addTaskButton = new();
    private TextBlock _titleText = new();
    private TextBlock _countText = new();
    private TextBox _addBox = new();
    private TextBlock _addPlaceholder = new();
    private ScrollViewer _taskScroll = new();
    private StackPanel _activeTasks = new();
    private StackPanel _completedTasks = new();
    private StackPanel _completedTaskSection = new();
    private Button _completedToggle = new();
    private StickyAdjustmentWindow? _adjustmentWindow;
    private StickyAddTaskWindow? _addTaskWindow;
    private StickyTaskDetailWindow? _taskDetailWindow;
    private StickyConfirmWindow? _confirmWindow;
    private bool _isReturningToMain;
    private bool _isClosingFromCoordinatorShutdown;
    private bool _isApplyingScale;
    private bool _isSynchronizingChildWindowPositions;
    private bool _isEnforcingDisplayConstraints;
    private bool _isDraggingWindow;
    private bool _isInNativeMoveSizeLoop;
    private bool _nativeLoopWasMoving;
    private bool _isApplyingWindowMode;
    private bool _hasAppliedInitialWindowMode;
    private bool _isWindowDragCandidate;
    private bool _hasWindowDragMoved;
    private bool _windowDragStartedInFloatingMode;
    private double _windowDragDistanceX;
    private double _windowDragDistanceY;
    private NativePoint _windowDragLastCursor;
    private UIElement? _windowDragCaptureSource;
    private DispatcherTimer? _windowDragReleaseTimer;
    private StickyWindowGeometry? _windowDragStartGeometry;
    private DispatcherTimer? _taskDragTimer;
    private DispatcherTimer? _taskAutoScrollTimer;
    private TodoTask? _dragCandidateTask;
    private Border? _dragCandidateRow;
    private Border? _hoveredTaskRow;
    private Border? _dropHighlightRow;
    private StickyDropTarget? _currentDropTarget;
    private Point _taskDragStartPoint;
    private Point _taskDragCurrentPoint;
    private FrameworkElement? _dropPreview;
    private Panel? _dropPreviewHost;
    private bool _isTaskDragging;

    public StickyWindow(string[] args)
        : this(args, new WindowsProcessLauncher())
    {
    }

    internal StickyWindow(string[] args, IProcessLauncher processLauncher)
    {
        _processLauncher = processLauncher;
        _mainExePath = ParseMainExePath(args);
        _data = _store.LoadData();
        _settings = _store.LoadSettings();
        PurgeExpiredRecycleBin();

        ConfigureWindow();
        BuildUi();
        ApplyStoredSettings();
        RefreshAll();
    }

    private void ConfigureWindow()
    {
        Title = "Fowan Todo Sticky";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Topmost = _settings.IsStickyFloatingModeEnabled || _settings.IsStickyTopmost;
        if (_settings.IsStickyFloatingModeEnabled)
        {
            MinWidth = FloatingWindowSize;
            MinHeight = FloatingWindowSize;
            Width = FloatingWindowSize;
            Height = FloatingWindowSize;
        }
        else
        {
            UpdateMinimumWindowSize(clampCurrentSize: false);
            var initialMaxSize = CurrentMonitorSizeDip();
            Width = Math.Clamp(_settings.StickyWidth ?? BaseWidth * _settings.StickyScale, MinWidth, initialMaxSize.Width);
            Height = Math.Clamp(_settings.StickyHeight ?? BaseHeight * _settings.StickyScale, MinHeight, initialMaxSize.Height);
        }

        if (_settings.StickyLeft.HasValue && _settings.StickyTop.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.StickyLeft.Value;
            Top = _settings.StickyTop.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo.ico");
        if (File.Exists(iconPath))
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }

        SourceInitialized += (_, _) =>
        {
            InitializeNativeShell();
            ApplyCurrentWindowMode(save: false);
            _hasAppliedInitialWindowMode = true;
        };
        Loaded += (_, _) => ApplyCurrentWindowMode(save: true);
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnWindowDragMouseMove), handledEventsToo: true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnWindowDragMouseLeftButtonUp), handledEventsToo: true);
        AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowDragMouseLeftButtonUp), handledEventsToo: true);
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowDragMouseLeftButtonUp), handledEventsToo: true);
        LostMouseCapture += OnWindowDragLostMouseCapture;
        Deactivated += (_, _) =>
        {
            if (_isWindowDragCandidate)
            {
                CompleteWindowDrag(allowFloatingClickRestore: false);
            }
        };
        LocationChanged += (_, _) =>
        {
            if (!_hasAppliedInitialWindowMode || _isApplyingWindowMode)
            {
                return;
            }

            if (!_settings.IsStickyFloatingModeEnabled &&
                !_isEnforcingDisplayConstraints &&
                !IsInteractiveMoveResizeInProgress())
            {
                EnforceWindowDisplayConstraints(save: false);
            }

            if (!_isWindowDragCandidate || _windowDragStartedInFloatingMode)
            {
                SaveGeometry();
            }
            SynchronizeStickyChildWindows(reposition: true);
        };
        SizeChanged += (_, _) =>
        {
            if (!_hasAppliedInitialWindowMode || _isApplyingWindowMode)
            {
                return;
            }

            if (!_settings.IsStickyFloatingModeEnabled &&
                !_isEnforcingDisplayConstraints &&
                !IsInteractiveMoveResizeInProgress())
            {
                EnforceWindowDisplayConstraints(save: false);
            }

            SaveGeometry();
            SynchronizeStickyChildWindows(reposition: true);
        };
        Closed += (_, _) =>
        {
            _source?.RemoveHook(WndProc);
            CloseStickyChildWindows();
        };
        Closing += (_, _) =>
        {
            SaveGeometry();
            var shouldShutdownMain = false;
            if (_isClosingFromCoordinatorShutdown)
            {
                _settings.IsStickyModeEnabled = false;
            }
            else if (!_isReturningToMain)
            {
                _settings.IsStickyModeEnabled = true;
                shouldShutdownMain = true;
            }

            _store.SaveSettings(_settings);
            if (shouldShutdownMain)
            {
                SignalMainShutdown();
            }
        };
    }

    private void InitializeNativeShell()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetNativeResizeEnabled(hwnd, !_settings.IsStickyFloatingModeEnabled);
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    private void BuildUi()
    {
        if (_settings.IsStickyFloatingModeEnabled)
        {
            BuildFloatingUi();
            return;
        }

        _root = new Grid
        {
            LayoutTransform = _scaleTransform,
            Background = Brushes.Transparent
        };

        _shell = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = SurfaceBrush(),
            BorderBrush = Brush(0xDCE7EA),
            BorderThickness = new Thickness(1)
        };
        _root.Children.Add(_shell);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _shell.Child = layout;

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var divider = new Border { Background = Brush(0xDCE7EA) };
        Grid.SetRow(divider, 1);
        layout.Children.Add(divider);

        var titleArea = BuildTitleArea();
        Grid.SetRow(titleArea, 2);
        layout.Children.Add(titleArea);

        var addRow = BuildAddRow();
        Grid.SetRow(addRow, 3);
        layout.Children.Add(addRow);

        var scroll = BuildTaskScroll();
        Grid.SetRow(scroll, 4);
        layout.Children.Add(scroll);

        _popupDismissOverlay = new Border
        {
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _popupDismissOverlay.MouseLeftButtonDown += (_, args) =>
        {
            CloseStickyChildWindows();
            args.Handled = true;
        };
        Panel.SetZIndex(_popupDismissOverlay, 20);
        _root.Children.Add(_popupDismissOverlay);

        Content = _root;
    }

    private void BuildFloatingUi()
    {
        _root = new Grid { Background = Brushes.Transparent };
        var floatingIcon = new Image
        {
            Width = 30,
            Height = 30,
            Source = LoadIconImage(),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        System.Windows.Automation.AutomationProperties.SetName(floatingIcon, "悬浮待办图标");
        _shell = new Border
        {
            Width = FloatingWindowSize,
            Height = FloatingWindowSize,
            CornerRadius = new CornerRadius(13),
            Background = SurfaceBrush(),
            BorderBrush = Brush(0xDCE7EA),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = floatingIcon
        };
        ToolTipService.SetToolTip(_shell, "单击恢复便签模式");
        _shell.MouseLeftButtonDown += OnWindowDragMouseLeftButtonDown;
        _root.Children.Add(_shell);
        Content = _root;
    }

    private bool HasOpenStickyChildWindow()
    {
        return _adjustmentWindow is { IsVisible: true } ||
            _addTaskWindow is { IsVisible: true } ||
            _taskDetailWindow is { IsVisible: true } ||
            OwnedWindows.Cast<Window>().Any(window => window.IsVisible);
    }

    private UIElement BuildHeader()
    {
        var header = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };
        _dragHandle = header;
        header.MouseLeftButtonDown += OnHeaderDragMouseLeftButtonDown;

        var grid = new Grid
        {
            Margin = new Thickness(18, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _brandIcon = new Image
        {
            Width = 16,
            Height = 16,
            Source = LoadIconImage(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(_brandIcon, "便签品牌图标");
        var brandIconShell = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = Brush(0x001B3D),
            Child = _brandIcon
        };
        brand.Children.Add(brandIconShell);
        brand.Children.Add(new TextBlock
        {
            Text = "Fowan",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush(),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        grid.Children.Add(brand);

        var topmost = HeaderPillButton(_settings.IsStickyTopmost ? "\uE840" : "\uE718", "置顶");
        topmost.Click += (_, _) =>
        {
            _settings.IsStickyTopmost = !_settings.IsStickyTopmost;
            Topmost = _settings.IsStickyTopmost;
            topmost.Content = HeaderButtonContent(_settings.IsStickyTopmost ? "\uE840" : "\uE718", "置顶", AccentBrush());
            SynchronizeStickyChildWindows(reposition: false);
            _store.SaveSettings(_settings);
        };
        Grid.SetColumn(topmost, 1);
        grid.Children.Add(topmost);

        var restore = HeaderIconButton("\uE72B", "回到大界面");
        restore.Click += (_, _) => ReturnToMain();
        Grid.SetColumn(restore, 2);
        grid.Children.Add(restore);

        _settingsButton = HeaderIconButton("\uE713", "透明度和缩放");
        _settingsButton.Click += (_, _) => ToggleAdjustmentWindow();
        Grid.SetColumn(_settingsButton, 3);
        grid.Children.Add(_settingsButton);

        var minimize = HeaderIconButton("\uE921", "最小化便签");
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        Grid.SetColumn(minimize, 4);
        grid.Children.Add(minimize);

        var close = HeaderIconButton("\uE711", "关闭便签");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 5);
        grid.Children.Add(close);

        header.Child = grid;
        return header;
    }

    private void OnHeaderDragMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ButtonState != MouseButtonState.Pressed ||
            IsHeaderButtonHit(args.OriginalSource as DependencyObject))
        {
            return;
        }

        OnWindowDragMouseLeftButtonDown(sender, args);
    }

    private void OnWindowDragMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left ||
            args.ButtonState != MouseButtonState.Pressed ||
            sender is not UIElement ||
            !GetCursorPos(out _windowDragLastCursor))
        {
            return;
        }

        _isWindowDragCandidate = true;
        _hasWindowDragMoved = false;
        _windowDragStartedInFloatingMode = _settings.IsStickyFloatingModeEnabled;
        _windowDragStartGeometry = !_windowDragStartedInFloatingMode && WindowState == WindowState.Normal
            ? new StickyWindowGeometry(Left, Top, Width, Height)
            : null;
        _windowDragDistanceX = 0;
        _windowDragDistanceY = 0;
        _windowDragCaptureSource = this;
        if (!CaptureMouse())
        {
            _isWindowDragCandidate = false;
            _windowDragCaptureSource = null;
            _windowDragStartGeometry = null;
            return;
        }

        _windowDragReleaseTimer ??= CreateWindowDragReleaseTimer();
        _windowDragReleaseTimer.Start();
        args.Handled = true;
    }

    private DispatcherTimer CreateWindowDragReleaseTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (_isWindowDragCandidate && !IsLeftMouseButtonPressed())
            {
                CompleteWindowDrag(allowFloatingClickRestore: true);
            }
        };
        return timer;
    }

    private void OnWindowDragMouseMove(object sender, MouseEventArgs args)
    {
        if (!_isWindowDragCandidate || args.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var scale = DeviceScale();
        var dx = (cursor.X - _windowDragLastCursor.X) / scale.X;
        var dy = (cursor.Y - _windowDragLastCursor.Y) / scale.Y;
        _windowDragLastCursor = cursor;
        _windowDragDistanceX += dx;
        _windowDragDistanceY += dy;

        if (!_hasWindowDragMoved)
        {
            if (Math.Sqrt(
                    _windowDragDistanceX * _windowDragDistanceX +
                    _windowDragDistanceY * _windowDragDistanceY) < WindowDragActivationDistanceDip)
            {
                return;
            }

            dx = _windowDragDistanceX;
            dy = _windowDragDistanceY;
        }

        _hasWindowDragMoved = true;
        _isDraggingWindow = true;
        Left += dx;
        Top += dy;
        args.Handled = true;
    }

    private void OnWindowDragMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (!_isWindowDragCandidate || args.ChangedButton != MouseButton.Left)
        {
            return;
        }

        CompleteWindowDrag(allowFloatingClickRestore: true);
        args.Handled = true;
    }

    private void OnWindowDragLostMouseCapture(object sender, MouseEventArgs args)
    {
        if (_isWindowDragCandidate)
        {
            if (IsLeftMouseButtonPressed())
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_isWindowDragCandidate && !IsMouseCaptured)
                    {
                        CaptureMouse();
                    }
                });
                return;
            }

            CompleteWindowDrag(allowFloatingClickRestore: false);
        }
    }

    private void CompleteWindowDrag(bool allowFloatingClickRestore)
    {
        if (GetCursorPos(out var releaseCursor))
        {
            _windowDragLastCursor = releaseCursor;
        }

        var moved = _hasWindowDragMoved;
        var startedInFloatingMode = _windowDragStartedInFloatingMode;
        var dragStartGeometry = _windowDragStartGeometry;
        var captureSource = _windowDragCaptureSource;
        _isWindowDragCandidate = false;
        _hasWindowDragMoved = false;
        _isDraggingWindow = false;
        _windowDragReleaseTimer?.Stop();
        _windowDragCaptureSource = null;
        _windowDragStartGeometry = null;
        if (captureSource?.IsMouseCaptured == true)
        {
            captureSource.ReleaseMouseCapture();
        }

        if (startedInFloatingMode)
        {
            if (moved || !allowFloatingClickRestore)
            {
                SnapFloatingWindowToNearestEdge(_windowDragLastCursor);
            }
            else
            {
                ExitFloatingMode();
            }

            return;
        }

        if (moved)
        {
            EnforceVerticalDisplayConstraints();
            if (!TryEnterFloatingModeFromCurrentPosition(_windowDragLastCursor, dragStartGeometry))
            {
                SaveGeometry();
            }
        }
    }

    private static bool IsLeftMouseButtonPressed()
    {
        return (GetAsyncKeyState(0x01) & 0x8000) != 0;
    }

    private static bool IsHeaderButtonHit(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private UIElement BuildTitleArea()
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(20, 20, 20, 0)
        };

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleText = new TextBlock
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = TextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(_titleText);

        _countText = new TextBlock
        {
            FontSize = 12,
            Foreground = SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 4, 0, 0)
        };
        Grid.SetColumn(_countText, 1);
        titleRow.Children.Add(_countText);
        stack.Children.Add(titleRow);
        return stack;
    }

    private UIElement BuildAddRow()
    {
        var border = new Border
        {
            Height = 42,
            Margin = new Thickness(20, 14, 20, 0),
            CornerRadius = new CornerRadius(8),
            Background = PanelBrush(0xF5FAFB),
            BorderBrush = Brush(0xDCE7EA),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, 10, 0)
        };
        _addRowBorder = border;
        border.MouseLeftButtonDown += (_, _) => FocusInlineAddBox();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _addTaskButton = CircleIconButton("\uE710", "添加任务", AccentBrush(), Brushes.White);
        _addTaskButton.Click += (_, _) =>
        {
            if (!AddTaskFromInput())
            {
                ShowAddTaskWindow();
            }
        };
        grid.Children.Add(_addTaskButton);

        var inputHost = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        inputHost.PreviewMouseLeftButtonDown += (_, _) => FocusInlineAddBox();
        _addBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextBrush(),
            CaretBrush = AccentBrush(),
            Cursor = Cursors.IBeam,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 30,
            FocusVisualStyle = null
        };
        _addBox.TextChanged += (_, _) => UpdateAddPlaceholder();
        _addBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                AddTaskFromInput();
                args.Handled = true;
            }
        };
        inputHost.Children.Add(_addBox);
        _addPlaceholder = new TextBlock
        {
            Text = "添加任务",
            IsHitTestVisible = false,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        inputHost.Children.Add(_addPlaceholder);
        Grid.SetColumn(inputHost, 1);
        grid.Children.Add(inputHost);

        border.Child = grid;
        return border;
    }

    private ScrollViewer BuildTaskScroll()
    {
        _taskScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(20, 16, 20, 18)
        };
        ApplyScrollViewerTheme(_taskScroll);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        _activeTasks = new StackPanel { Orientation = Orientation.Vertical };
        _completedToggle = CompletedToggleButton();
        _completedToggle.Click += (_, _) =>
        {
            _settings.IsStickyCompletedExpanded = !_settings.IsStickyCompletedExpanded;
            _store.SaveSettings(_settings);
            RefreshTasks();
        };
        _completedTasks = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _completedTaskSection = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(_activeTasks);
        _taskDivider = new Border
        {
            Height = 1,
            Background = Brush(0xE7EEF0),
            Margin = new Thickness(0, 10, 0, 14)
        };
        _completedTaskSection.Children.Add(_taskDivider);
        _completedTaskSection.Children.Add(_completedToggle);
        _completedTaskSection.Children.Add(_completedTasks);
        stack.Children.Add(_completedTaskSection);
        _taskScroll.Content = stack;
        return _taskScroll;
    }

    private void ApplyScrollViewerTheme(ScrollViewer scroll)
    {
        scroll.Resources[SystemColors.ControlBrushKey] = Brush(0xF5FAFB);
        scroll.Resources[SystemColors.ControlLightBrushKey] = Brush(0xF5FAFB);
        scroll.Resources[SystemColors.ControlDarkBrushKey] = Brush(0x9BB2BC);
        scroll.Resources[SystemColors.WindowBrushKey] = Brush(0xFFFFFF);
        scroll.Resources[typeof(ScrollBar)] = CreateScrollBarStyle();
    }

    private Style CreateScrollBarStyle()
    {
        var track = HexColor(0xF5FAFB, Math.Clamp(_settings.StickyOpacity, 0.0, 1.0));
        var thumb = HexColor(IsDarkTheme() ? 0x536677u : 0x9DBEC7u);
        var hover = HexColor(IsDarkTheme() ? 0x667B8Eu : 0x87AFBAu);
        var xaml = $$"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type ScrollBar}">
                <Setter Property="Width" Value="8" />
                <Setter Property="MinWidth" Value="8" />
                <Setter Property="Background" Value="{{track}}" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type ScrollBar}">
                            <Grid Background="{TemplateBinding Background}" Width="8">
                                <Track x:Name="PART_Track" IsDirectionReversed="True">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0" Focusable="False" />
                                    </Track.DecreaseRepeatButton>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0" Focusable="False" />
                                    </Track.IncreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Background="{{thumb}}">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="{x:Type Thumb}">
                                                    <Border x:Name="ThumbChrome"
                                                            Margin="2"
                                                            CornerRadius="3"
                                                            Background="{TemplateBinding Background}" />
                                                    <ControlTemplate.Triggers>
                                                        <Trigger Property="IsMouseOver" Value="True">
                                                            <Setter TargetName="ThumbChrome" Property="Background" Value="{{hover}}" />
                                                        </Trigger>
                                                    </ControlTemplate.Triggers>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """;

        return (Style)XamlReader.Parse(xaml);
    }

    private void ApplyStoredSettings()
    {
        ApplyStickyOpacity(refreshTasks: false);
        var scale = _settings.IsStickyFloatingModeEnabled ? 1.0 : _settings.StickyScale;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }

    private void RefreshAll()
    {
        _data = _store.LoadData();
        PurgeExpiredRecycleBin();

        RefreshTasks();
    }

    private void RefreshTasks()
    {
        var active = TodoQuery.ActiveTaskNodesForView(_data, StickyViewId, CollapsedTaskIds()).ToList();
        var completed = TodoQuery.CompletedTaskNodesForView(_data, StickyViewId, CollapsedTaskIds()).ToList();
        _titleText.Text = TodoQuery.ViewTitle(_data, StickyViewId);
        _countText.Text = $"{active.Count} 项";

        RemoveDropPreview();
        _taskRows.Clear();
        _activeTasks.Children.Clear();
        if (active.Count == 0)
        {
            _activeTasks.Children.Add(EmptyText("没有待办任务"));
        }
        else
        {
            foreach (var node in active)
            {
                _activeTasks.Children.Add(TaskRow(node.Task, node.Depth));
            }
        }

        _completedToggle.Content = CompletedToggleContent(completed.Count);
        _completedTasks.Children.Clear();
        _completedTasks.Visibility = _settings.IsStickyCompletedExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.IsStickyCompletedExpanded)
        {
            foreach (var node in completed)
            {
                _completedTasks.Children.Add(TaskRow(node.Task, node.Depth));
            }
        }
    }

    private Border TaskRow(TodoTask task, int depth)
    {
        depth = Math.Clamp(depth, 0, TodoQuery.MaxTaskTreeDepth - 1);
        var row = new Border
        {
            Height = task.IsCompleted ? 42 : 54,
            Margin = new Thickness(depth * 18, 0, 0, task.IsCompleted ? 8 : 10),
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(8),
            Background = task.IsCompleted ? PanelBrush(0xF5F8F9) : PanelBrush(0xFFFFFF),
            BorderBrush = task.IsCompleted ? Brushes.Transparent : Brush(0xDCE7EA),
            BorderThickness = task.IsCompleted ? new Thickness(0) : new Thickness(1)
        };
        row.Tag = task.Id;
        AttachTaskDragHandlers(row, task);
        row.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount != 2 ||
                _isTaskDragging ||
                HasOpenStickyChildWindow() ||
                IsTaskDragIgnoredSource(args.OriginalSource as DependencyObject, row))
            {
                return;
            }

            CancelTaskDrag();
            ShowTaskDetailWindow(task.Id);
            args.Handled = true;
        };
        row.MouseEnter += (_, _) =>
        {
            _hoveredTaskRow = row;
            RefreshTaskRowChrome(row, task, pointerOver: true);
        };
        row.MouseLeave += (_, _) =>
        {
            if (ReferenceEquals(_hoveredTaskRow, row))
            {
                _hoveredTaskRow = null;
            }

            RefreshTaskRowChrome(row, task, pointerOver: false);
        };
        row.ContextMenu = BuildTaskContextMenu(task);
        _taskRows.Add(row);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leadingActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (TodoQuery.DirectChildCount(_data, task.Id) > 0)
        {
            leadingActions.Children.Add(TreeToggleButton(task));
        }
        else
        {
            leadingActions.Children.Add(new Border { Width = 20, Height = 20 });
        }

        leadingActions.Children.Add(TaskCheckButton(task));
        Grid.SetColumn(leadingActions, 0);
        grid.Children.Add(leadingActions);

        var title = new TextBlock
        {
            Text = task.Title,
            FontSize = 13,
            FontWeight = task.IsCompleted ? FontWeights.Normal : FontWeights.Medium,
            Foreground = task.IsCompleted ? MutedTextBrush() : TextBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0)
        };
        if (task.IsCompleted)
        {
            title.TextDecorations = TextDecorations.Strikethrough;
        }

        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        if (task.IsImportant)
        {
            var important = HeaderIconButton("\uE735", "取消重要");
            important.Click += (_, _) => ToggleImportant(task.Id);
            Grid.SetColumn(important, 2);
            grid.Children.Add(important);
        }

        row.Child = grid;
        return row;
    }

    private ContextMenu BuildTaskContextMenu(TodoTask task)
    {
        var menu = new ContextMenu
        {
            Background = ContextMenuSurfaceBrush(),
            BorderBrush = ContextMenuBorderBrush(),
            BorderThickness = new Thickness(1),
            Foreground = TextBrush(),
            Padding = new Thickness(0),
            HasDropShadow = true,
            Template = CreateTaskContextMenuTemplate()
        };
        var itemStyle = new Style(typeof(MenuItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush()));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, ContextMenuSurfaceBrush()));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 16, 8)));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, CreateTaskContextMenuItemTemplate()));
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, ContextMenuItemHoverBrush()));
        itemStyle.Triggers.Add(highlighted);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, MutedTextBrush()));
        itemStyle.Triggers.Add(disabled);
        menu.ItemContainerStyle = itemStyle;

        var createChild = new MenuItem
        {
            Header = "创建子任务",
            IsEnabled = TodoQuery.CanAddChild(_data, task)
        };
        if (!createChild.IsEnabled)
        {
            createChild.ToolTip = TodoQuery.AddChildBlockedReason(_data, task);
        }
        createChild.Click += (_, _) => ShowAddSubtaskWindow(task);
        menu.Items.Add(createChild);

        var delete = new MenuItem { Header = "删除任务" };
        delete.Click += (_, _) => DeleteTaskFromContextMenu(task);
        menu.Items.Add(delete);
        return menu;
    }

    private ControlTemplate CreateTaskContextMenuTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding(Control.BorderBrushProperty));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.PaddingProperty, new Thickness(4));

        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
    }

    private ControlTemplate CreateTaskContextMenuItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.MarginProperty, new Thickness(2));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(HeaderedItemsControl.HeaderProperty));
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(HeaderedItemsControl.HeaderTemplateProperty));
        presenter.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(HeaderedItemsControl.HeaderTemplateSelectorProperty));
        presenter.SetBinding(FrameworkElement.MarginProperty, TemplateBinding(Control.PaddingProperty));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
    }

    private static Binding TemplateBinding(DependencyProperty property)
    {
        return new Binding
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            Path = new PropertyPath(property)
        };
    }

    private void AttachTaskDragHandlers(Border row, TodoTask task)
    {
        row.PreviewMouseLeftButtonDown += (_, args) => StartTaskDragCandidate(row, task, args);
        row.PreviewMouseMove += (_, args) => UpdateTaskDrag(args);
        row.PreviewMouseLeftButtonUp += (_, args) => FinishTaskDrag(args);
        row.LostMouseCapture += (_, _) =>
        {
            if (ReferenceEquals(_dragCandidateRow, row))
            {
                CancelTaskDrag();
            }
        };
    }

    private void StartTaskDragCandidate(Border row, TodoTask task, MouseButtonEventArgs args)
    {
        if (args.ButtonState != MouseButtonState.Pressed ||
            HasOpenStickyChildWindow() ||
            IsTaskDragIgnoredSource(args.OriginalSource as DependencyObject, row))
        {
            return;
        }

        CancelTaskDrag();
        _dragCandidateTask = task;
        _dragCandidateRow = row;
        _taskDragStartPoint = args.GetPosition(this);
        _taskDragCurrentPoint = _taskDragStartPoint;
        _taskDragTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TaskDragLongPressMilliseconds)
        };
        _taskDragTimer.Tick += (_, _) => BeginTaskDrag();
        _taskDragTimer.Start();
        row.CaptureMouse();
    }

    private void UpdateTaskDrag(MouseEventArgs args)
    {
        if (_dragCandidateTask is null)
        {
            return;
        }

        var point = args.GetPosition(this);
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

    private void FinishTaskDrag(MouseButtonEventArgs args)
    {
        if (_dragCandidateTask is null)
        {
            return;
        }

        var handled = _isTaskDragging;
        if (_isTaskDragging && _currentDropTarget is not null)
        {
            ApplyTaskDrop(_dragCandidateTask, _currentDropTarget);
        }

        CancelTaskDrag();
        args.Handled = handled;
    }

    private void BeginTaskDrag()
    {
        _taskDragTimer?.Stop();
        if (_dragCandidateTask is null || _dragCandidateRow is null || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CancelTaskDrag();
            return;
        }

        _isTaskDragging = true;
        _dragCandidateRow.Opacity = 0.58;
        Mouse.OverrideCursor = Cursors.SizeAll;
        _currentDropTarget = DropTargetFromPoint(_taskDragCurrentPoint);
        ApplyDropTargetVisual(_currentDropTarget);
        StartTaskAutoScroll();
    }

    private void CancelTaskDrag()
    {
        _taskDragTimer?.Stop();
        _taskDragTimer = null;
        StopTaskAutoScroll();
        Mouse.OverrideCursor = null;
        RemoveDropPreview();

        foreach (var row in _taskRows)
        {
            ResetTaskRowChrome(row);
        }

        if (_dragCandidateRow?.IsMouseCaptured == true)
        {
            _dragCandidateRow.ReleaseMouseCapture();
        }

        _dragCandidateTask = null;
        _dragCandidateRow = null;
        _dropHighlightRow = null;
        _currentDropTarget = null;
        _isTaskDragging = false;
    }

    private void StartTaskAutoScroll()
    {
        _taskAutoScrollTimer ??= new DispatcherTimer();
        _taskAutoScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _taskAutoScrollTimer.Tick -= OnTaskAutoScrollTick;
        _taskAutoScrollTimer.Tick += OnTaskAutoScrollTick;
        _taskAutoScrollTimer.Start();
    }

    private void StopTaskAutoScroll()
    {
        _taskAutoScrollTimer?.Stop();
    }

    private void OnTaskAutoScrollTick(object? sender, EventArgs args)
    {
        if (!_isTaskDragging || _taskScroll.ActualHeight <= 0)
        {
            StopTaskAutoScroll();
            return;
        }

        var origin = _taskScroll.TranslatePoint(new Point(0, 0), this);
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

        _taskScroll.ScrollToVerticalOffset(nextOffset);
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
        if (_dragCandidateTask is null || _completedTaskSection.ActualHeight <= 0)
        {
            return (0, maximum);
        }

        var scrollOrigin = _taskScroll.TranslatePoint(new Point(0, 0), this);
        var completedOrigin = _completedTaskSection.TranslatePoint(new Point(0, 0), this);
        var completedTop = Math.Clamp(
            completedOrigin.Y - scrollOrigin.Y + _taskScroll.VerticalOffset,
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

    private StickyDropTarget? DropTargetFromPoint(Point point)
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
                ? StickyDropPlacement.Before
                : ratio > 1 - TaskDropEdgeRatio
                    ? StickyDropPlacement.After
                    : StickyDropPlacement.Child;
            var target = placement == StickyDropPlacement.After
                ? DropTargetAfterVisibleRow(row, targetTask) ?? new StickyDropTarget(taskId, placement)
                : new StickyDropTarget(taskId, placement);
            return IsValidTaskDrop(_dragCandidateTask, target) ? target : null;
        }

        return DropTargetBetweenRows(point);
    }

    private StickyDropTarget? DropTargetBetweenRows(Point point)
    {
        if (_dragCandidateTask is null)
        {
            return null;
        }

        var rowHits = new List<(Border Row, Panel Host, TodoTask Task, System.Windows.Rect Bounds)>();
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

                    var beforeDragged = new StickyDropTarget(lower.Task.Id, StickyDropPlacement.Before);
                    return IsValidTaskDrop(_dragCandidateTask, beforeDragged) ? beforeDragged : null;
                }

                if (lower.Task.IsCompleted != _dragCandidateTask.IsCompleted)
                {
                    continue;
                }

                var target = new StickyDropTarget(lower.Task.Id, StickyDropPlacement.Before);
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

    private StickyDropTarget? DropTargetAtHostBottom(
        Point point,
        IReadOnlyList<(Border Row, Panel Host, TodoTask Task, System.Windows.Rect Bounds)> orderedRows)
    {
        if (_dragCandidateTask is null || orderedRows.Count == 0)
        {
            return null;
        }

        var last = orderedRows[^1];
        if (last.Task.IsCompleted != _dragCandidateTask.IsCompleted ||
            !TryGetBottomDropBoundary(last.Host, last.Bounds, out var bottomBoundary) ||
            bottomBoundary - last.Bounds.Bottom < 2 ||
            point.Y < last.Bounds.Bottom ||
            point.Y > bottomBoundary)
        {
            return null;
        }

        var target = new StickyDropTarget(last.Task.Id, StickyDropPlacement.TopLevelEnd);
        return IsValidTaskDrop(_dragCandidateTask, target) ? target : null;
    }

    private bool TryGetBottomDropBoundary(Panel host, System.Windows.Rect lastBounds, out double bottomBoundary)
    {
        bottomBoundary = lastBounds.Bottom;

        if (ReferenceEquals(host, _activeTasks) &&
            TryGetElementBounds(_taskDivider, out var dividerBounds) &&
            dividerBounds.Top > lastBounds.Bottom)
        {
            bottomBoundary = dividerBounds.Top;
            return true;
        }

        if (host.Parent is Panel parent)
        {
            var hostIndex = parent.Children.IndexOf(host);
            for (var index = hostIndex + 1; index < parent.Children.Count; index++)
            {
                if (parent.Children[index] is not FrameworkElement next || !next.IsVisible)
                {
                    continue;
                }

                if (TryGetElementBounds(next, out var nextBounds) && nextBounds.Top > lastBounds.Bottom)
                {
                    bottomBoundary = nextBounds.Top;
                    return true;
                }
            }
        }

        bottomBoundary = lastBounds.Bottom + Math.Max(16, lastBounds.Height * TaskDropEdgeRatio);
        return true;
    }

    private StickyDropTarget? DropTargetAfterVisibleRow(Border upperRow, TodoTask upperTask)
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
            if (host.Children[index] is not Border lowerRow ||
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
                ? new StickyDropTarget(lowerTask.Id, StickyDropPlacement.Before)
                : null;
        }

        return null;
    }

    private void ApplyDropTargetVisual(StickyDropTarget? target)
    {
        RemoveDropPreview();

        if (_dropHighlightRow is not null)
        {
            ResetTaskRowChrome(_dropHighlightRow);
            _dropHighlightRow = null;
        }

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

    private void InsertDropPreview(StickyDropTarget target, Border targetRow)
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
        var previewDepth = target.Placement == StickyDropPlacement.TopLevelEnd
            ? 0
            : target.Placement == StickyDropPlacement.Child
                ? Math.Clamp(targetDepth + 1, 0, TodoQuery.MaxTaskTreeDepth - 1)
                : targetDepth;
        var insertIndex = InsertIndexForDrop(host, targetRow, target, targetDepth);
        var preview = CreateDropPreview(target, targetTask, previewDepth);

        _dropPreview = preview;
        _dropPreviewHost = host;
        host.Children.Insert(insertIndex, preview);
    }

    private int InsertIndexForDrop(Panel host, Border targetRow, StickyDropTarget target, int targetDepth)
    {
        var index = host.Children.IndexOf(targetRow);
        if (index < 0)
        {
            return host.Children.Count;
        }

        if (target.Placement == StickyDropPlacement.TopLevelEnd)
        {
            return Math.Min(index + 1, host.Children.Count);
        }

        if (target.Placement == StickyDropPlacement.Child)
        {
            return Math.Min(index + 1, host.Children.Count);
        }

        if (target.Placement == StickyDropPlacement.Before)
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
            if (host.Children[index] is not Border row ||
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

    private FrameworkElement CreateDropPreview(StickyDropTarget target, TodoTask targetTask, int previewDepth)
    {
        var completed = _dragCandidateTask?.IsCompleted == true;
        var preview = new Grid
        {
            Height = completed ? 34 : 44,
            Margin = new Thickness(previewDepth * 18, 0, 0, completed ? 8 : 10),
            IsHitTestVisible = false
        };

        preview.Children.Add(new System.Windows.Shapes.Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            Stroke = AccentBrush(),
            StrokeThickness = 1.6,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = Brush(0xEEF9FA, Math.Clamp(_settings.StickyOpacity + 0.02, TodoSettings.MinStickyOpacity, 0.86))
        });
        preview.Children.Add(new TextBlock
        {
            Text = DropPreviewText(target, targetTask),
            Margin = new Thickness(12, 0, 12, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentDarkBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        return preview;
    }

    private string DropPreviewText(StickyDropTarget target, TodoTask targetTask)
    {
        if (target.Placement == StickyDropPlacement.Child)
        {
            return $"松手后作为“{targetTask.Title}”的子任务";
        }

        if (target.Placement == StickyDropPlacement.TopLevelEnd)
        {
            return "松手后放到顶层任务末尾";
        }

        var parent = string.IsNullOrWhiteSpace(targetTask.ParentTaskId)
            ? null
            : FindTask(targetTask.ParentTaskId);
        var parentName = parent?.Title ?? "顶层任务";
        return target.Placement == StickyDropPlacement.Before
            ? $"松手后放在“{parentName}”下，位于目标之前"
            : $"松手后放在“{parentName}”下，位于目标之后";
    }

    private bool IsPointInsideDropPreview(Point point)
    {
        if (_dropPreview is null || !_dropPreview.IsVisible)
        {
            return false;
        }

        try
        {
            var topLeft = _dropPreview.TransformToAncestor(this).Transform(new Point(0, 0));
            var bounds = new System.Windows.Rect(topLeft, new Size(_dropPreview.ActualWidth, _dropPreview.ActualHeight));
            return bounds.Contains(point);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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

    private static bool DropTargetsEqual(StickyDropTarget? first, StickyDropTarget? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return first.Placement == second.Placement &&
            string.Equals(first.TaskId, second.TaskId, StringComparison.Ordinal);
    }

    private void ResetTaskRowChrome(Border row)
    {
        row.Opacity = 1;
        if (row.Tag is not string taskId)
        {
            return;
        }

        var task = FindTask(taskId);
        if (task is null)
        {
            return;
        }

        RefreshTaskRowChrome(row, task, ReferenceEquals(row, _hoveredTaskRow));
    }

    private void RefreshTaskRowChrome(Border row, TodoTask task, bool pointerOver)
    {
        row.Opacity = 1;
        row.Background = pointerOver
            ? StickyTaskHoverBackgroundBrush(task.IsCompleted)
            : task.IsCompleted
                ? PanelBrush(0xF5F8F9)
                : PanelBrush(0xFFFFFF);
        row.BorderBrush = pointerOver
            ? StickyTaskHoverBorderBrush(task.IsCompleted)
            : task.IsCompleted
                ? Brushes.Transparent
                : Brush(0xDCE7EA);
        row.BorderThickness = pointerOver || !task.IsCompleted ? new Thickness(1) : new Thickness(0);
    }

    private void ApplyTaskDrop(TodoTask draggedTask, StickyDropTarget target)
    {
        if (!TodoTaskDropController.TryApply(
                _data,
                _settings,
                draggedTask.Id,
                target.TaskId,
                SharedDropPlacement(target.Placement),
                DateTimeOffset.Now))
        {
            return;
        }
        _store.SaveSettings(_settings);
        SaveDataAndRefresh();
    }

    private bool IsValidTaskDrop(TodoTask draggedTask, StickyDropTarget target)
    {
        return TodoTaskDropController.CanApply(
            _data,
            draggedTask.Id,
            target.TaskId,
            SharedDropPlacement(target.Placement));
    }

    private static TodoTaskDropPlacement SharedDropPlacement(StickyDropPlacement placement) => placement switch
    {
        StickyDropPlacement.Before => TodoTaskDropPlacement.Before,
        StickyDropPlacement.Child => TodoTaskDropPlacement.Child,
        StickyDropPlacement.After => TodoTaskDropPlacement.After,
        _ => TodoTaskDropPlacement.TopLevelEnd
    };

    private TodoTask? FindTask(string taskId)
    {
        return _data.Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
    }

    private Border? RowForTask(string taskId)
    {
        return _taskRows.FirstOrDefault(row => row.Tag is string rowTaskId &&
            string.Equals(rowTaskId, taskId, StringComparison.Ordinal));
    }

    private bool TryGetTaskRowBounds(Border row, out System.Windows.Rect bounds)
    {
        return TryGetElementBounds(row, out bounds);
    }

    private bool TryGetElementBounds(FrameworkElement element, out System.Windows.Rect bounds)
    {
        try
        {
            var topLeft = element.TransformToAncestor(this).Transform(new Point(0, 0));
            bounds = new System.Windows.Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            bounds = System.Windows.Rect.Empty;
            return false;
        }
    }

    private static bool IsTaskDragIgnoredSource(DependencyObject? source, DependencyObject stopAt)
    {
        while (source is not null && !ReferenceEquals(source, stopAt))
        {
            if (source is ButtonBase or TextBoxBase or Slider or Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static double Distance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private Button TaskCheckButton(TodoTask task)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                BorderThickness = task.IsCompleted ? new Thickness(0) : new Thickness(2),
                BorderBrush = TaskCheckBorderBrush(),
                Background = task.IsCompleted ? Brush(0x9DBEC7) : Brushes.Transparent,
                Child = task.IsCompleted
                    ? MdIcon("\uE73E", 11, Brushes.White)
                    : null
            },
            ToolTip = task.IsCompleted ? "恢复任务" : "完成任务"
        };
        button.Template = ButtonTemplate(new CornerRadius(11), Brushes.Transparent, Brush(0xEEF9FA));
        button.Click += (_, _) => ToggleTaskCompleted(task);
        return button;
    }

    private Button TreeToggleButton(TodoTask task)
    {
        var collapsed = IsTaskCollapsed(task.Id);
        var button = new Button
        {
            Width = 20,
            Height = 22,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Content = MdIcon(collapsed ? "\uE76C" : "\uE70D", 10, SecondaryTextBrush()),
            ToolTip = collapsed ? "展开子任务" : "收起子任务",
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Template = ButtonTemplate(new CornerRadius(6), Brushes.Transparent, Brush(0xEEF9FA));
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

        _store.SaveSettings(_settings);
        RefreshTasks();
    }

    private HashSet<string> CollapsedTaskIds()
    {
        return new HashSet<string>(_settings.CollapsedTaskIds, StringComparer.Ordinal);
    }

    private bool AddTaskFromInput()
    {
        var title = _addBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        AddTask(title);
        _addBox.Text = string.Empty;
        return true;
    }

    private bool AddTask(string title, TodoTask? parent = null, string? notes = null)
    {
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(title) || (parent is not null && !TodoQuery.CanAddChild(_data, parent)))
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        var task = new TodoTask
        {
            Id = _store.CreateTaskId(),
            Title = title,
            Notes = notes?.Trim() ?? string.Empty,
            ListId = parent?.ListId ?? TodoQuery.DefaultListId(_data),
            ParentTaskId = parent?.Id,
            IsImportant = parent?.IsImportant ?? false,
            StartDate = parent?.StartDate.Date ?? DateTime.Today,
            DueDate = parent?.DueDate,
            CreatedAt = now,
            UpdatedAt = now
        };
        _data.Tasks.Add(task);
        InsertNewTaskAsFirstChild(task);
        SaveDataAndRefresh();
        return true;
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
        _store.SaveSettings(_settings);
    }

    private void FocusInlineAddBox()
    {
        if (!_addBox.Focus())
        {
            Keyboard.Focus(_addBox);
        }

        _addBox.CaretIndex = _addBox.Text.Length;
    }

    private void SetTaskCompleted(string taskId, bool completed)
    {
        var task = _data.Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
        if (task is null)
        {
            return;
        }

        if (task.IsCompleted == completed)
        {
            return;
        }

        TodoTaskCommands.SetCompleted(_data, task.Id, completed, includeDescendants: false, now: DateTimeOffset.Now);
        SaveDataAndRefresh();
    }

    private void ToggleTaskCompleted(TodoTask task)
    {
        var completed = !task.IsCompleted;
        if (!completed)
        {
            SetTaskCompleted(task.Id, false);
            return;
        }

        if (HasIncompleteDescendants(task))
        {
            ShowCompleteChildrenConfirmation(task.Id);
            return;
        }

        SetTaskCompleted(task.Id, true);
    }

    private bool HasIncompleteDescendants(TodoTask task)
    {
        return TodoTaskCommands.HasIncompleteDescendants(_data, task.Id);
    }

    private void CompleteTaskAndDescendants(string taskId)
    {
        TodoTaskCommands.SetCompleted(_data, taskId, completed: true, includeDescendants: true, now: DateTimeOffset.Now);
        SaveDataAndRefresh();
    }

    private void ToggleImportant(string taskId)
    {
        var task = _data.Tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
        if (task is null)
        {
            return;
        }

        TodoTaskCommands.ToggleImportant(_data, task.Id, DateTimeOffset.Now);
        SaveDataAndRefresh();
    }

    private void SaveDataAndRefresh()
    {
        TodoRecycleBin.PurgeExpired(_data, _settings);
        _store.SaveData(_data);
        RefreshAll();
    }

    private void UpdateAddPlaceholder()
    {
        _addPlaceholder.Visibility = string.IsNullOrWhiteSpace(_addBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ToggleAdjustmentWindow()
    {
        if (_adjustmentWindow is { IsVisible: true })
        {
            _adjustmentWindow.Close();
            _adjustmentWindow = null;
            return;
        }

        CloseStickyChildWindows();
        _adjustmentWindow = new StickyAdjustmentWindow(this);
        RegisterStickyChildWindow(_adjustmentWindow);
        _adjustmentWindow.Closed += (_, _) =>
        {
            _adjustmentWindow = null;
            UpdatePopupDismissOverlay();
        };
        ApplyStickyChildWindowState(_adjustmentWindow, reposition: true);
        _adjustmentWindow.Show();
        UpdatePopupDismissOverlay();
    }

    private void PositionAdjustmentWindow(Window adjustmentWindow)
    {
        adjustmentWindow.Left = Left + Math.Max(0, (Width - adjustmentWindow.Width) / 2);
        adjustmentWindow.Top = Top + Math.Max(18 * _settings.StickyScale, (Height - adjustmentWindow.Height) * 0.18);
    }

    private void ShowAddTaskWindow(TodoTask? parent = null)
    {
        if (parent is null && _addTaskWindow is { IsVisible: true })
        {
            _addTaskWindow.Activate();
            _addTaskWindow.FocusTitleBox();
            return;
        }

        CloseStickyChildWindows();
        _addTaskWindow = new StickyAddTaskWindow(this, parent);
        RegisterStickyChildWindow(_addTaskWindow);
        _addTaskWindow.Closed += (_, _) =>
        {
            _addTaskWindow = null;
            UpdatePopupDismissOverlay();
        };
        ApplyStickyChildWindowState(_addTaskWindow, reposition: true);
        _addTaskWindow.Show();
        UpdatePopupDismissOverlay();
        _addTaskWindow.FocusTitleBox();
    }

    private void ShowAddSubtaskWindow(TodoTask parent)
    {
        if (TodoQuery.CanAddChild(_data, parent))
        {
            ShowAddTaskWindow(parent);
        }
    }

    private void ShowTaskDetailWindow(string taskId)
    {
        var task = FindTask(taskId);
        if (task is null || task.DeletedAt is not null)
        {
            return;
        }

        if (_taskDetailWindow is { IsVisible: true } detailWindow)
        {
            if (string.Equals(detailWindow.TaskId, taskId, StringComparison.Ordinal))
            {
                detailWindow.Activate();
                detailWindow.FocusTitleBox();
                return;
            }

            detailWindow.Close();
        }

        CloseStickyChildWindows();
        _taskDetailWindow = new StickyTaskDetailWindow(this, task);
        RegisterStickyChildWindow(_taskDetailWindow);
        _taskDetailWindow.Closed += (_, _) =>
        {
            _taskDetailWindow = null;
            UpdatePopupDismissOverlay();
        };
        ApplyStickyChildWindowState(_taskDetailWindow, reposition: true);
        _taskDetailWindow.Show();
        UpdatePopupDismissOverlay();
        _taskDetailWindow.FocusTitleBox();
    }

    private bool SaveTaskDetail(string taskId, string title, string notes)
    {
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var task = FindTask(taskId);
        if (task is null || task.DeletedAt is not null)
        {
            return false;
        }

        var nextNotes = notes.Trim();
        if (!string.Equals(task.Title, title, StringComparison.Ordinal) ||
            !string.Equals(task.Notes, nextNotes, StringComparison.Ordinal))
        {
            task.Title = title;
            task.Notes = nextNotes;
            task.UpdatedAt = DateTimeOffset.Now;
            SaveDataAndRefresh();
        }

        return true;
    }

    private void PositionAddTaskWindow(Window addTaskWindow)
    {
        addTaskWindow.Left = Left + Math.Max(0, (Width - addTaskWindow.Width) / 2);
        addTaskWindow.Top = Top + Math.Max(0, (Height - addTaskWindow.Height) / 2);
    }

    private void ShowCompleteChildrenConfirmation(string taskId)
    {
        ShowConfirmation(
            "完成当前任务",
            "是否完成当前任务？仍有未完成的子任务。",
            "确认完成",
            () => CompleteTaskAndDescendants(taskId));
    }

    private void DeleteTaskFromContextMenu(TodoTask task)
    {
        var descendantCount = TodoQuery.DescendantIds(_data, task.Id).Count();
        if (descendantCount == 0)
        {
            DeleteTaskTree(task);
            return;
        }

        var target = _settings.IsRecycleBinEnabled ? "移动到回收站" : "永久删除";
        ShowConfirmation(
            "删除任务树",
            $"当前任务包含 {descendantCount} 个子任务或孙任务。确认后将{target}当前任务及全部后代。",
            "确认删除",
            () => DeleteTaskTree(task));
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
            _store.SaveSettings(_settings);
        }

        SaveDataAndRefresh();
    }

    private void ShowConfirmation(string heading, string message, string confirmText, Action confirmAction)
    {
        CloseStickyChildWindows();
        _confirmWindow = new StickyConfirmWindow(this, heading, message, confirmText, confirmAction);
        RegisterStickyChildWindow(_confirmWindow);
        _confirmWindow.Closed += (_, _) =>
        {
            _confirmWindow = null;
            UpdatePopupDismissOverlay();
        };
        ApplyStickyChildWindowState(_confirmWindow, reposition: true);
        _confirmWindow.Show();
        UpdatePopupDismissOverlay();
    }

    private void PositionConfirmWindow(Window confirmWindow)
    {
        confirmWindow.Left = Left + Math.Max(0, (Width - confirmWindow.Width) / 2);
        confirmWindow.Top = Top + Math.Max(0, (Height - confirmWindow.Height) / 2);
    }

    private void RegisterStickyChildWindow(Window window)
    {
        if (window is not IStickyChildWindow childWindow)
        {
            throw new InvalidOperationException("Sticky child windows must synchronize with the owner window.");
        }

        window.Owner = this;
        window.ShowInTaskbar = false;
        window.Topmost = Topmost;
        window.LocationChanged += (_, _) =>
        {
            if (!_isSynchronizingChildWindowPositions && window.IsVisible)
            {
                ApplyStickyChildWindowState(childWindow, reposition: true);
            }
        };
        ApplyStickyChildWindowState(childWindow, reposition: false);
    }

    private void SynchronizeStickyChildWindows(bool reposition)
    {
        foreach (Window window in OwnedWindows)
        {
            window.Topmost = Topmost;
            if (window is IStickyChildWindow childWindow)
            {
                ApplyStickyChildWindowState(childWindow, reposition);
            }
        }
    }

    private void ApplyStickyChildWindowState(IStickyChildWindow childWindow, bool reposition)
    {
        _isSynchronizingChildWindowPositions = true;
        try
        {
            childWindow.ApplyStickyOwnerState(reposition);
        }
        finally
        {
            _isSynchronizingChildWindowPositions = false;
        }
    }

    private void CloseStickyChildWindows()
    {
        foreach (Window window in OwnedWindows.Cast<Window>().ToList())
        {
            window.Close();
        }

        _adjustmentWindow = null;
        _addTaskWindow = null;
        _taskDetailWindow = null;
        _confirmWindow = null;
        UpdatePopupDismissOverlay();
    }

    private void UpdatePopupDismissOverlay()
    {
        if (_popupDismissOverlay is null)
        {
            return;
        }

        var visible = HasOpenStickyChildWindow();
        _popupDismissOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _popupDismissOverlay.IsHitTestVisible = visible;
    }

    private void ApplyStickyOpacity(bool refreshTasks)
    {
        _shell.Background = SurfaceBrush();
        _addRowBorder.Background = PanelBrush(0xF5FAFB);
        _taskDivider.Background = Brush(0xE7EEF0, Math.Clamp(_settings.StickyOpacity + 0.12, 0.0, 1.0));

        if (refreshTasks && _titleText is not null)
        {
            RefreshTasks();
        }
    }

    private void SetStickyOpacity(double opacity)
    {
        _settings.StickyOpacity = Math.Clamp(opacity, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity);
        ApplyStickyOpacity(refreshTasks: true);
        SynchronizeStickyChildWindows(reposition: false);
        _store.SaveSettings(_settings);
    }

    private void SetStickyTheme(string theme)
    {
        if (theme is not (TodoThemeIds.System or TodoThemeIds.Light or TodoThemeIds.Dark))
        {
            return;
        }

        if (string.Equals(_settings.Theme, theme, StringComparison.Ordinal))
        {
            SynchronizeStickyChildWindows(reposition: false);
            return;
        }

        _settings.Theme = theme;
        _store.SaveSettings(_settings);
        BuildUi();
        ApplyStoredSettings();
        RefreshAll();
        SynchronizeStickyChildWindows(reposition: true);
        UpdatePopupDismissOverlay();
    }

    private void ApplyScaleFromSlider(double sliderValue)
    {
        if (_isApplyingScale)
        {
            return;
        }

        var nextScale = Math.Clamp(Math.Round(sliderValue / 100, 2), TodoSettings.MinStickyScale, TodoSettings.MaxStickyScale);
        if (Math.Abs(nextScale - _settings.StickyScale) < 0.001)
        {
            return;
        }

        _isApplyingScale = true;
        try
        {
            var oldScale = _settings.StickyScale;
            _settings.StickyScale = nextScale;
            _scaleTransform.ScaleX = nextScale;
            _scaleTransform.ScaleY = nextScale;
            UpdateMinimumWindowSize(clampCurrentSize: false);
            var maxSize = CurrentMonitorSizeDip();
            ResizeAroundCenter(
                Math.Clamp(Width * nextScale / oldScale, MinWidth, maxSize.Width),
                Math.Clamp(Height * nextScale / oldScale, MinHeight, maxSize.Height));
            EnforceWindowDisplayConstraints(save: false);
            SynchronizeStickyChildWindows(reposition: true);
            SaveGeometry();
            _store.SaveSettings(_settings);
        }
        finally
        {
            _isApplyingScale = false;
        }
    }

    private void ResizeAroundCenter(double width, double height)
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        Width = width;
        Height = height;
        Left = centerX - width / 2;
        Top = centerY - height / 2;
    }

    private void ApplyCurrentWindowMode(bool save)
    {
        if (_settings.IsStickyFloatingModeEnabled)
        {
            ApplyFloatingModeGeometry(save);
            return;
        }

        ResizeMode = ResizeMode.CanResize;
        Topmost = _settings.IsStickyTopmost;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetNativeResizeEnabled(hwnd, enabled: true);
        }

        EnforceWindowDisplayConstraints(save);
    }

    private void ApplyFloatingModeGeometry(bool save)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            return;
        }

        var scale = DeviceScale();
        var workLeft = monitorInfo.WorkArea.Left / scale.X;
        var workRight = monitorInfo.WorkArea.Right / scale.X;
        var workTop = monitorInfo.WorkArea.Top / scale.Y;
        var workBottom = monitorInfo.WorkArea.Bottom / scale.Y;
        var desiredTop = _settings.StickyFloatingTop ?? (Top + Math.Max(0, Height - FloatingWindowSize) / 2);

        _isApplyingWindowMode = true;
        try
        {
            CloseStickyChildWindows();
            ResizeMode = ResizeMode.NoResize;
            MinWidth = FloatingWindowSize;
            MinHeight = FloatingWindowSize;
            Width = FloatingWindowSize;
            Height = FloatingWindowSize;
            Left = string.Equals(_settings.StickyFloatingEdge, TodoStickyFloatingEdges.Right, StringComparison.Ordinal)
                ? workRight - FloatingWindowSize
                : workLeft;
            Top = Math.Clamp(desiredTop, workTop, Math.Max(workTop, workBottom - FloatingWindowSize));
            Topmost = true;
            _settings.StickyFloatingTop = Top;
            SetNativeResizeEnabled(hwnd, enabled: false);
        }
        finally
        {
            _isApplyingWindowMode = false;
        }

        if (save)
        {
            _store.SaveSettings(_settings);
        }
    }

    private bool TryEnterFloatingModeFromCurrentPosition(
        NativePoint? releasePoint = null,
        StickyWindowGeometry? dragStartGeometry = null)
    {
        if (_settings.IsStickyFloatingModeEnabled || WindowState != WindowState.Normal)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var monitor = releasePoint.HasValue
            ? MonitorFromPoint(releasePoint.Value, MonitorDefaultToNearest)
            : MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            return false;
        }

        var scale = DeviceScale();
        var workLeft = monitorInfo.WorkArea.Left / scale.X;
        var workRight = monitorInfo.WorkArea.Right / scale.X;
        var windowCenter = Left + (ActualWidth > 0 ? ActualWidth : Width) / 2;
        var dockEdge = TodoStickyPlacement.FindDockEdgeByCenter(
            workLeft,
            workRight,
            windowCenter,
            FloatingEdgeThresholdDip);
        if (dockEdge is null)
        {
            return false;
        }

        var restoreGeometry = dragStartGeometry ?? new StickyWindowGeometry(Left, Top, Width, Height);
        _settings.StickyLeft = restoreGeometry.Left;
        _settings.StickyTop = restoreGeometry.Top;
        _settings.StickyWidth = restoreGeometry.Width;
        _settings.StickyHeight = restoreGeometry.Height;
        _settings.StickyFloatingEdge = dockEdge;
        _settings.StickyFloatingTop = FloatingTopAlignedToBrandIcon();
        _settings.IsStickyFloatingModeEnabled = true;
        _store.SaveSettings(_settings);

        CloseStickyChildWindows();
        BuildUi();
        ApplyStoredSettings();
        ApplyFloatingModeGeometry(save: true);
        return true;
    }

    private double FloatingTopAlignedToBrandIcon()
    {
        if (_brandIcon.IsLoaded && _brandIcon.ActualHeight > 0)
        {
            var iconTopInWindow = _brandIcon.TranslatePoint(new Point(0, 0), this).Y;
            return TodoStickyPlacement.AlignCenters(
                Top + iconTopInWindow,
                _brandIcon.ActualHeight,
                FloatingWindowSize);
        }

        return Top;
    }

    private void SnapFloatingWindowToNearestEdge(NativePoint? releasePoint = null)
    {
        if (!_settings.IsStickyFloatingModeEnabled)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var windowRect))
        {
            return;
        }

        var monitor = releasePoint.HasValue
            ? MonitorFromPoint(releasePoint.Value, MonitorDefaultToNearest)
            : MonitorFromRect(ref windowRect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            return;
        }

        var centerX = releasePoint?.X ?? windowRect.Left + (windowRect.Right - windowRect.Left) / 2;
        _settings.StickyFloatingEdge = TodoStickyPlacement.NearestEdge(
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Right,
            centerX);
        _settings.StickyFloatingTop = Top;
        ApplyFloatingModeGeometry(save: true);
    }

    private void ExitFloatingMode()
    {
        if (!_settings.IsStickyFloatingModeEnabled)
        {
            return;
        }

        _settings.IsStickyFloatingModeEnabled = false;
        _settings.StickyFloatingEdge = null;
        _settings.StickyFloatingTop = null;
        _store.SaveSettings(_settings);

        _isApplyingWindowMode = true;
        try
        {
            ResizeMode = ResizeMode.CanResize;
            UpdateMinimumWindowSize(clampCurrentSize: false);
            var maxSize = CurrentMonitorSizeDip();
            Width = Math.Clamp(_settings.StickyWidth ?? BaseWidth * _settings.StickyScale, MinWidth, maxSize.Width);
            Height = Math.Clamp(_settings.StickyHeight ?? BaseHeight * _settings.StickyScale, MinHeight, maxSize.Height);
            if (_settings.StickyLeft.HasValue && _settings.StickyTop.HasValue)
            {
                Left = _settings.StickyLeft.Value;
                Top = _settings.StickyTop.Value;
            }

            Topmost = _settings.IsStickyTopmost;
            BuildUi();
            ApplyStoredSettings();
            RefreshAll();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetNativeResizeEnabled(hwnd, enabled: true);
            }
        }
        finally
        {
            _isApplyingWindowMode = false;
        }

        EnforceWindowDisplayConstraints(save: true);
        Activate();
    }

    private void UpdateMinimumWindowSize(bool clampCurrentSize)
    {
        var maxSize = CurrentMonitorSizeDip();
        MinWidth = Math.Min(BaseWidth * TodoSettings.MinStickyScale * _settings.StickyScale, maxSize.Width);
        MinHeight = Math.Min(BaseHeight * TodoSettings.MinStickyScale * _settings.StickyScale, maxSize.Height);

        if (!clampCurrentSize)
        {
            return;
        }

        Width = Math.Max(Width, MinWidth);
        Height = Math.Max(Height, MinHeight);
    }

    private void EnforceWindowDisplayConstraints(bool save)
    {
        if (_isEnforcingDisplayConstraints)
        {
            return;
        }

        _isEnforcingDisplayConstraints = true;
        try
        {
            UpdateMinimumWindowSize(clampCurrentSize: false);
            ClampWindowSizeToCurrentMonitor();
            ClampDragHandleToVisibleDisplay();
        }
        finally
        {
            _isEnforcingDisplayConstraints = false;
        }

        if (save)
        {
            SaveGeometry();
        }
    }

    private bool IsInteractiveMoveResizeInProgress()
    {
        return _isDraggingWindow || _isInNativeMoveSizeLoop;
    }

    private void ClampWindowSizeToCurrentMonitor()
    {
        var maxSize = CurrentMonitorSizeDip();
        var maxWidth = Math.Max(MinWidth, maxSize.Width);
        var maxHeight = Math.Max(MinHeight, maxSize.Height);
        var nextWidth = Math.Clamp(Width, MinWidth, maxWidth);
        var nextHeight = Math.Clamp(Height, MinHeight, maxHeight);

        if (Math.Abs(nextWidth - Width) > 0.5)
        {
            Width = nextWidth;
        }

        if (Math.Abs(nextHeight - Height) > 0.5)
        {
            Height = nextHeight;
        }
    }

    private void ClampDragHandleToVisibleDisplay()
    {
        if (!TryGetDragHandleScreenRect(out var dragRect))
        {
            return;
        }

        var monitor = MonitorFromRect(ref dragRect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            return;
        }

        var monitorRect = monitorInfo.Monitor;
        var dx = 0;
        var dy = 0;

        if (dragRect.Left < monitorRect.Left)
        {
            dx = monitorRect.Left - dragRect.Left;
        }
        else if (dragRect.Right > monitorRect.Right)
        {
            dx = monitorRect.Right - dragRect.Right;
        }

        if (dragRect.Top < monitorRect.Top)
        {
            dy = monitorRect.Top - dragRect.Top;
        }
        else if (dragRect.Bottom > monitorRect.Bottom)
        {
            dy = monitorRect.Bottom - dragRect.Bottom;
        }

        if (dx == 0 && dy == 0)
        {
            return;
        }

        var scale = DeviceScale();
        Left += dx / scale.X;
        Top += dy / scale.Y;
    }

    private bool TryGetDragHandleScreenRect(out Rect rect)
    {
        if (_dragHandle.IsLoaded && _dragHandle.ActualWidth > 0 && _dragHandle.ActualHeight > 0)
        {
            rect = ScreenRectForElement(_dragHandle, _dragHandle.ActualWidth, _dragHandle.ActualHeight);
            return true;
        }

        if (_root.IsLoaded)
        {
            var scale = DeviceScale();
            var topLeft = _root.PointToScreen(new Point(18, 0));
            rect = new Rect
            {
                Left = (int)Math.Floor(topLeft.X),
                Top = (int)Math.Floor(topLeft.Y),
                Right = (int)Math.Ceiling(topLeft.X + 150 * scale.X),
                Bottom = (int)Math.Ceiling(topLeft.Y + 54 * scale.Y)
            };
            return true;
        }

        rect = default;
        return false;
    }

    private Rect ScreenRectForElement(FrameworkElement element, double width, double height)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(width, height));
        return new Rect
        {
            Left = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X)),
            Top = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y)),
            Right = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X)),
            Bottom = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y))
        };
    }

    private (double Width, double Height) CurrentMonitorSizeDip()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (TryGetMonitorInfo(monitor, out var monitorInfo))
            {
                var scale = DeviceScale();
                return (
                    Math.Max(1, (monitorInfo.Monitor.Right - monitorInfo.Monitor.Left) / scale.X),
                    Math.Max(1, (monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top) / scale.Y));
            }
        }

        return (SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    private (double X, double Y) DeviceScale()
    {
        var target = PresentationSource.FromVisual(this)?.CompositionTarget;
        return target is null
            ? (1.0, 1.0)
            : (target.TransformToDevice.M11, target.TransformToDevice.M22);
    }

    private static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo monitorInfo)
    {
        monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo);
    }

    private void SaveGeometry()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        if (!IsLoaded && PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        if (_settings.IsStickyFloatingModeEnabled)
        {
            _settings.StickyFloatingTop = Top;
        }
        else
        {
            _settings.StickyLeft = Left;
            _settings.StickyTop = Top;
            _settings.StickyWidth = Width;
            _settings.StickyHeight = Height;
        }
        _store.SaveSettings(_settings);
    }

    private void ReturnToMain()
    {
        _isReturningToMain = true;
        _settings.IsStickyModeEnabled = false;
        SaveGeometry();
        _store.SaveSettings(_settings);

        if (!SignalMainActivation())
        {
            var mainExe = ResolveMainExePath();
            if (File.Exists(mainExe))
            {
                _processLauncher.TryLaunch(mainExe);
            }
        }

        CloseStickyChildWindows();
        Hide();
        _isReturningToMain = false;
    }

    internal void RestoreFromExternalActivation()
    {
        var wasMinimized = WindowState == WindowState.Minimized;
        if (wasMinimized)
        {
            _isApplyingWindowMode = true;
            try
            {
                WindowState = WindowState.Normal;
            }
            finally
            {
                _isApplyingWindowMode = false;
            }

            RefreshAll();
        }
        else
        {
            ReloadSettingsAndRefresh();
        }

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    internal void CloseFromCoordinatorShutdown()
    {
        _isClosingFromCoordinatorShutdown = true;
        Close();
    }

    private void ReloadSettingsAndRefresh()
    {
        _data = _store.LoadData();
        _settings = _store.LoadSettings();
        PurgeExpiredRecycleBin();

        Topmost = _settings.IsStickyFloatingModeEnabled || _settings.IsStickyTopmost;
        CloseStickyChildWindows();
        BuildUi();
        ApplyStoredSettings();
        RefreshAll();
        UpdatePopupDismissOverlay();
        if (WindowState != WindowState.Minimized)
        {
            ApplyCurrentWindowMode(save: false);
        }
    }

    private void PurgeExpiredRecycleBin()
    {
        if (TodoRecycleBin.PurgeExpired(_data, _settings) > 0)
        {
            _store.SaveData(_data);
        }
    }

    private static bool SignalMainActivation()
    {
        return SignalEvent(MainActivationEventName);
    }

    private static bool SignalMainShutdown()
    {
        return SignalEvent(MainShutdownEventName, attempts: 4);
    }

    private static bool SignalEvent(string eventName, int attempts = 20)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(eventName);
                activationEvent.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    private string ResolveMainExePath()
    {
        if (!string.IsNullOrWhiteSpace(_mainExePath))
        {
            return _mainExePath;
        }

        return Path.Combine(AppContext.BaseDirectory, "Fowan.Todo.Windows.exe");
    }

    private static string? ParseMainExePath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--main-exe", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private Button HeaderPillButton(string glyph, string label)
    {
        var button = new Button
        {
            Height = 32,
            MinWidth = 60,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = Brush(0xDFF4F7),
            Content = HeaderButtonContent(glyph, label, AccentBrush()),
            Cursor = Cursors.Arrow,
            ToolTip = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Template = ButtonTemplate(new CornerRadius(7), Brush(0xDFF4F7), Brush(0xCBEFF4));
        return button;
    }

    private Button HeaderTextButton(string glyph, string label)
    {
        var button = new Button
        {
            Height = 32,
            MinWidth = 92,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Content = HeaderButtonContent(glyph, label, TextBrush()),
            Cursor = Cursors.Arrow,
            ToolTip = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Template = ButtonTemplate(new CornerRadius(7), Brushes.Transparent, Brush(0xEEF9FA));
        return button;
    }

    private Button HeaderIconButton(string glyph, string label)
    {
        var button = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Content = MdIcon(glyph, 13, SecondaryTextBrush()),
            Cursor = Cursors.Arrow,
            ToolTip = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Template = ButtonTemplate(new CornerRadius(7), Brushes.Transparent, Brush(0xEEF9FA));
        return button;
    }

    private Button CircleIconButton(string glyph, string label, Brush background, Brush foreground)
    {
        var button = new Button
        {
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = background,
            Content = MdIcon(glyph, 11, foreground),
            ToolTip = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Template = ButtonTemplate(new CornerRadius(10), background, AccentDarkBrush());
        return button;
    }

    private Button CompletedToggleButton()
    {
        var button = new Button
        {
            Height = 28,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Template = ButtonTemplate(new CornerRadius(6), Brushes.Transparent, Brush(0xEEF9FA));
        return button;
    }

    private Grid CompletedToggleContent(int count)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = $"已完成 {count}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });

        var chevron = MdIcon(_settings.IsStickyCompletedExpanded ? "\uE70D" : "\uE76C", 12, SecondaryTextBrush());
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);
        return grid;
    }

    private StackPanel HeaderButtonContent(string glyph, string label, Brush foreground)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                MdIcon(glyph, 12, foreground),
                new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foreground,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private ControlTemplate ButtonTemplate(CornerRadius radius, Brush normalBackground, Brush hoverBackground)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(Border.BackgroundProperty, normalBackground);
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, hoverBackground, "Chrome"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.OpacityProperty, 0.82, "Chrome"));
        template.Triggers.Add(pressed);

        return template;
    }

    private TextBlock SmallLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = SecondaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private TextBlock SmallValue(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = SecondaryTextBrush(),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private TextBlock EmptyText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = SecondaryTextBrush(),
            FontSize = 13,
            Margin = new Thickness(2, 8, 0, 8)
        };
    }

    private static TextBlock MdIcon(string glyph, double fontSize, Brush foreground)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = fontSize,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }

    private ImageSource? LoadIconImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo-app-icon-256.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path, UriKind.Absolute)) : null;
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

    private static bool IsSystemThemeDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value &&
                value == 0;
        }
        catch
        {
            return false;
        }
    }

    private SolidColorBrush TextBrush() => Brush(0x17242A);
    private SolidColorBrush SecondaryTextBrush() => Brush(0x6F7F86);
    private SolidColorBrush MutedTextBrush() => Brush(0x8FA2AA);
    private SolidColorBrush AccentBrush() => Brush(0x128CA2);
    private SolidColorBrush AccentDarkBrush() => Brush(0x0C6F82);
    private SolidColorBrush TaskCheckBorderBrush() => IsDarkTheme() ? Brush(0x9BB2BC) : Brush(0x667B84);
    private SolidColorBrush SurfaceBrush() => Brush(0xFFFFFF, Math.Clamp(_settings.StickyOpacity, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity));
    private SolidColorBrush PanelBrush(uint rgb) => Brush(rgb, Math.Clamp(_settings.StickyOpacity + 0.08, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity));
    private SolidColorBrush StickyTaskHoverBackgroundBrush(bool completed) => PanelBrush(IsDarkTheme()
        ? (completed ? 0x1B2A23u : 0x19282Fu)
        : (completed ? 0xF5FBF6u : 0xF4FAFBu));
    private SolidColorBrush StickyTaskHoverBorderBrush(bool completed) => Brush(IsDarkTheme()
        ? (completed ? 0x4E7C63u : 0x3F7480u)
        : (completed ? 0xA9D7BDu : 0x9BCED7u));
    private SolidColorBrush ContextMenuSurfaceBrush() => PanelBrush(0xEAF4F7);
    private SolidColorBrush ContextMenuBorderBrush() => Brush(0x8BAEB8, Math.Clamp(_settings.StickyOpacity + 0.18, TodoSettings.MinStickyOpacity, TodoSettings.MaxStickyOpacity));
    private SolidColorBrush ContextMenuItemHoverBrush() => PanelBrush(0xD7EDF2);

    private SolidColorBrush Brush(uint rgb)
    {
        return Brush(rgb, 1.0);
    }

    private SolidColorBrush Brush(uint rgb, double opacity)
    {
        rgb = ThemeRgb(rgb);
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255),
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF)));
    }

    private string HexColor(uint rgb, double opacity = 1.0)
    {
        rgb = ThemeRgb(rgb);
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255);
        return $"#{alpha:X2}{(byte)((rgb >> 16) & 0xFF):X2}{(byte)((rgb >> 8) & 0xFF):X2}{(byte)(rgb & 0xFF):X2}";
    }

    private uint ThemeRgb(uint rgb)
    {
        if (IsDarkTheme())
        {
            return rgb switch
            {
                0xFFFFFF => 0x151B22,
                0xF7FAFB => 0x11161C,
                0xF5FAFB => 0x11161C,
                0xF5F8F9 => 0x1A242B,
                0xEEF9FA => 0x17323A,
                0xDFF4F7 => 0x17323A,
                0xCBEFF4 => 0x1E4650,
                0xDCE7EA => 0x28333E,
                0xE7EEF0 => 0x212B35,
                0xEAF4F7 => 0x1B2A33,
                0xD7EDF2 => 0x234652,
                0x8BAEB8 => 0x536B76,
                0x17242A => 0xEEF3F8,
                0x6F7F86 => 0x98A7B6,
                0x8FA2AA => 0x91A4B1,
                0x9BB2BC => 0x7F90A4,
                0x128CA2 => 0x34B7C8,
                0x0C6F82 => 0x58CDF0,
                0x001B3D => 0x001B3D,
                _ => rgb
            };
        }

        return rgb;
    }

    private void SetNativeResizeEnabled(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        if (enabled)
        {
            style |= WsThickFrame | WsMinimizeBox;
            style &= ~WsMaximizeBox;
        }
        else
        {
            style &= ~(WsThickFrame | WsMaximizeBox);
        }

        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((message == WmLeftButtonUp || message == WmNonClientLeftButtonUp) && _isWindowDragCandidate)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isWindowDragCandidate)
                {
                    CompleteWindowDrag(allowFloatingClickRestore: true);
                }
            });
        }

        if (message == WmEnterSizeMove)
        {
            _isInNativeMoveSizeLoop = true;
            _nativeLoopWasMoving = false;
            return IntPtr.Zero;
        }

        if (message == WmMoving)
        {
            _nativeLoopWasMoving = true;
            return IntPtr.Zero;
        }

        if (message == WmExitSizeMove)
        {
            _isInNativeMoveSizeLoop = false;
            var shouldTryFloatingMode = _nativeLoopWasMoving;
            _nativeLoopWasMoving = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (_settings.IsStickyFloatingModeEnabled)
                {
                    return;
                }

                if (!shouldTryFloatingMode || !TryEnterFloatingModeFromCurrentPosition())
                {
                    EnforceWindowDisplayConstraints(save: true);
                    SynchronizeStickyChildWindows(reposition: true);
                }
            });
            return IntPtr.Zero;
        }

        if (message != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        handled = true;
        if (_settings.IsStickyFloatingModeEnabled)
        {
            return new IntPtr(HtClient);
        }

        return new IntPtr(HitTestResizeBorder(hwnd, lParam));
    }

    private int HitTestResizeBorder(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            return HtClient;
        }

        var x = GetX(lParam);
        var y = GetY(lParam);
        var border = ResizeBorderPixels();
        var left = x >= rect.Left && x < rect.Left + border;
        var right = x <= rect.Right && x > rect.Right - border;
        var top = y >= rect.Top && y < rect.Top + border;
        var bottom = y <= rect.Bottom && y > rect.Bottom - border;

        if (left && top)
        {
            return HtTopLeft;
        }

        if (right && top)
        {
            return HtTopRight;
        }

        if (left && bottom)
        {
            return HtBottomLeft;
        }

        if (right && bottom)
        {
            return HtBottomRight;
        }

        if (left)
        {
            return HtLeft;
        }

        if (right)
        {
            return HtRight;
        }

        if (top)
        {
            return HtTop;
        }

        return bottom ? HtBottom : HtClient;
    }

    private int ResizeBorderPixels()
    {
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return Math.Clamp((int)Math.Round(ResizeBorderDip * scale), MinResizeBorderPixels, MaxResizeBorderPixels);
    }

    private static int GetX(IntPtr lParam)
    {
        return unchecked((short)((long)lParam & 0xFFFF));
    }

    private static int GetY(IntPtr lParam)
    {
        return unchecked((short)(((long)lParam >> 16) & 0xFFFF));
    }

    private interface IStickyChildWindow
    {
        void ApplyStickyOwnerState(bool reposition);
    }

    private enum StickyDropPlacement
    {
        Before,
        After,
        Child,
        TopLevelEnd
    }

    private sealed class StickyDropTarget(string taskId, StickyDropPlacement placement)
    {
        public string TaskId { get; } = taskId;
        public StickyDropPlacement Placement { get; } = placement;
    }

    private sealed class StickyAdjustmentWindow : Window, IStickyChildWindow
    {
        private const double BaseWindowWidth = 358;
        private const double BaseWindowHeight = 168;

        private readonly StickyWindow _owner;
        private readonly ScaleTransform _windowScale = new(1, 1);
        private readonly Border _panel = new();
        private readonly TextBlock _opacityValue = new();
        private readonly TextBlock _scaleValue = new();
        private readonly Slider _opacitySlider = new();
        private readonly Slider _scaleSlider = new();
        private readonly Button _resetOpacityButton = new();
        private readonly Button _resetScaleButton = new();
        private readonly Button _systemThemeButton = new();
        private readonly Button _lightThemeButton = new();
        private readonly Button _darkThemeButton = new();
        private bool _isSynchronizing;

        public StickyAdjustmentWindow(StickyWindow owner)
        {
            _owner = owner;
            Width = BaseWindowWidth;
            Height = BaseWindowHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = BuildContent();
            PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    Close();
                    args.Handled = true;
                }
            };
        }

        private UIElement BuildContent()
        {
            var scaleHost = new Grid
            {
                Width = BaseWindowWidth,
                Height = BaseWindowHeight,
                LayoutTransform = _windowScale
            };

            _panel.CornerRadius = new CornerRadius(8);
            _panel.BorderThickness = new Thickness(1);
            _panel.Padding = new Thickness(12);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            grid.Children.Add(_owner.SmallLabel("透明度"));
            _opacitySlider.Minimum = TodoSettings.MinStickyOpacity * 100;
            _opacitySlider.Maximum = TodoSettings.MaxStickyOpacity * 100;
            _opacitySlider.Value = _owner._settings.StickyOpacity * 100;
            _opacitySlider.VerticalAlignment = VerticalAlignment.Center;
            ApplySliderTheme(_opacitySlider);
            _opacitySlider.ValueChanged += (_, _) =>
            {
                if (!_isSynchronizing)
                {
                    _owner.SetStickyOpacity(_opacitySlider.Value / 100);
                }

                _opacityValue.Text = $"{Math.Round(_owner._settings.StickyOpacity * 100):0}%";
            };
            Grid.SetColumn(_opacitySlider, 1);
            grid.Children.Add(_opacitySlider);
            _opacityValue.Text = $"{Math.Round(_owner._settings.StickyOpacity * 100):0}%";
            _opacityValue.Foreground = _owner.SecondaryTextBrush();
            _opacityValue.FontSize = 12;
            _opacityValue.VerticalAlignment = VerticalAlignment.Center;
            _opacityValue.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(_opacityValue, 2);
            grid.Children.Add(_opacityValue);

            ConfigureResetButton(_resetOpacityButton, topMargin: 0);
            _resetOpacityButton.Click += (_, _) =>
            {
                _opacitySlider.Value = 100;
                _owner.SetStickyOpacity(1.0);
            };
            Grid.SetColumn(_resetOpacityButton, 3);
            grid.Children.Add(_resetOpacityButton);

            var scaleLabel = _owner.SmallLabel("缩放");
            scaleLabel.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(scaleLabel, 1);
            grid.Children.Add(scaleLabel);
            _scaleSlider.Minimum = TodoSettings.MinStickyScale * 100;
            _scaleSlider.Maximum = TodoSettings.MaxStickyScale * 100;
            _scaleSlider.Value = _owner._settings.StickyScale * 100;
            _scaleSlider.Margin = new Thickness(0, 10, 0, 0);
            ApplySliderTheme(_scaleSlider);
            _scaleSlider.ValueChanged += (_, _) => _scaleValue.Text = $"{Math.Round(_scaleSlider.Value):0}%";
            _scaleSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _owner.ApplyScaleFromSlider(_scaleSlider.Value)));
            _scaleSlider.PreviewMouseLeftButtonUp += (_, _) => _owner.ApplyScaleFromSlider(_scaleSlider.Value);
            _scaleSlider.LostKeyboardFocus += (_, _) => _owner.ApplyScaleFromSlider(_scaleSlider.Value);
            Grid.SetRow(_scaleSlider, 1);
            Grid.SetColumn(_scaleSlider, 1);
            grid.Children.Add(_scaleSlider);
            _scaleValue.Text = $"{Math.Round(_owner._settings.StickyScale * 100):0}%";
            _scaleValue.Foreground = _owner.SecondaryTextBrush();
            _scaleValue.FontSize = 12;
            _scaleValue.Margin = new Thickness(0, 10, 0, 0);
            _scaleValue.VerticalAlignment = VerticalAlignment.Center;
            _scaleValue.TextAlignment = TextAlignment.Right;
            Grid.SetRow(_scaleValue, 1);
            Grid.SetColumn(_scaleValue, 2);
            grid.Children.Add(_scaleValue);

            ConfigureResetButton(_resetScaleButton, topMargin: 10);
            _resetScaleButton.Click += (_, _) =>
            {
                _scaleSlider.Value = 100;
                _owner.ApplyScaleFromSlider(100);
            };
            Grid.SetRow(_resetScaleButton, 1);
            Grid.SetColumn(_resetScaleButton, 3);
            grid.Children.Add(_resetScaleButton);

            var themeLabel = _owner.SmallLabel("主题");
            themeLabel.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(themeLabel, 2);
            grid.Children.Add(themeLabel);

            var themeButtons = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 12, 0, 0)
            };
            ConfigureThemeButton(_systemThemeButton, "系统", TodoThemeIds.System);
            ConfigureThemeButton(_lightThemeButton, "浅色", TodoThemeIds.Light);
            ConfigureThemeButton(_darkThemeButton, "深色", TodoThemeIds.Dark);
            _systemThemeButton.Click += (_, _) => _owner.SetStickyTheme(TodoThemeIds.System);
            _lightThemeButton.Click += (_, _) => _owner.SetStickyTheme(TodoThemeIds.Light);
            _darkThemeButton.Click += (_, _) => _owner.SetStickyTheme(TodoThemeIds.Dark);
            themeButtons.Children.Add(_systemThemeButton);
            themeButtons.Children.Add(_lightThemeButton);
            themeButtons.Children.Add(_darkThemeButton);
            Grid.SetRow(themeButtons, 2);
            Grid.SetColumn(themeButtons, 1);
            Grid.SetColumnSpan(themeButtons, 3);
            grid.Children.Add(themeButtons);

            _panel.Child = grid;
            scaleHost.Children.Add(_panel);
            return scaleHost;
        }

        public void ApplyStickyOwnerState(bool reposition)
        {
            _isSynchronizing = true;
            try
            {
                var scale = _owner._settings.StickyScale;
                Width = BaseWindowWidth * scale;
                Height = BaseWindowHeight * scale;
                Topmost = _owner.Topmost;
                _windowScale.ScaleX = scale;
                _windowScale.ScaleY = scale;
                _panel.Background = _owner.SurfaceBrush();
                _panel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
                _opacitySlider.Value = _owner._settings.StickyOpacity * 100;
                _scaleSlider.Value = _owner._settings.StickyScale * 100;
                _opacityValue.Text = $"{Math.Round(_owner._settings.StickyOpacity * 100):0}%";
                _scaleValue.Text = $"{Math.Round(_owner._settings.StickyScale * 100):0}%";
                _opacityValue.Foreground = _owner.SecondaryTextBrush();
                _scaleValue.Foreground = _owner.SecondaryTextBrush();
                ApplySliderTheme(_opacitySlider);
                ApplySliderTheme(_scaleSlider);
                ConfigureResetButton(_resetOpacityButton, topMargin: 0);
                ConfigureResetButton(_resetScaleButton, topMargin: 10);
                ConfigureThemeButton(_systemThemeButton, "系统", TodoThemeIds.System);
                ConfigureThemeButton(_lightThemeButton, "浅色", TodoThemeIds.Light);
                ConfigureThemeButton(_darkThemeButton, "深色", TodoThemeIds.Dark);
            }
            finally
            {
                _isSynchronizing = false;
            }

            if (reposition)
            {
                _owner.PositionAdjustmentWindow(this);
            }
        }

        private void ApplySliderTheme(Slider slider)
        {
            slider.Resources[SystemColors.HighlightBrushKey] = _owner.AccentBrush();
            slider.Resources[SystemColors.ControlBrushKey] = _owner.Brush(0xDCE7EA);
            slider.Resources[SystemColors.ControlLightBrushKey] = _owner.Brush(0xE7EEF0);
            slider.Resources[SystemColors.WindowBrushKey] = _owner.Brush(0xFFFFFF, 0.0);
            slider.Foreground = _owner.AccentBrush();
            slider.Background = _owner.Brush(0xDCE7EA);
        }

        private void ConfigureThemeButton(Button button, string text, string theme)
        {
            var selected = string.Equals(_owner._settings.Theme, theme, StringComparison.Ordinal);
            button.Height = 28;
            button.Margin = new Thickness(3, 0, 3, 0);
            button.Padding = new Thickness(8, 0, 8, 0);
            button.BorderThickness = new Thickness(0);
            button.Content = text;
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = selected ? Brushes.White : _owner.TextBrush();
            button.Background = selected ? _owner.AccentBrush() : Brushes.Transparent;
            button.Template = _owner.ButtonTemplate(
                new CornerRadius(7),
                selected ? _owner.AccentBrush() : Brushes.Transparent,
                selected ? _owner.AccentDarkBrush() : _owner.Brush(0xEEF9FA));
        }

        private void ConfigureResetButton(Button button, double topMargin)
        {
            button.Width = 34;
            button.Height = 24;
            button.Margin = new Thickness(4, topMargin, 0, 0);
            button.Padding = new Thickness(0);
            button.BorderThickness = new Thickness(0);
            button.Content = "重置";
            button.FontSize = 11;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = _owner.SecondaryTextBrush();
            button.Background = Brushes.Transparent;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Template = _owner.ButtonTemplate(new CornerRadius(7), Brushes.Transparent, _owner.Brush(0xEEF9FA));
        }
    }

    private sealed class StickyConfirmWindow : Window, IStickyChildWindow
    {
        private const double BaseWindowWidth = 336;
        private const double BaseWindowHeight = 174;

        private readonly StickyWindow _owner;
        private readonly string _headingText;
        private readonly string _messageText;
        private readonly string _confirmText;
        private readonly Action _confirmAction;
        private readonly ScaleTransform _windowScale = new(1, 1);
        private readonly Border _panel = new();
        private readonly TextBlock _heading = new();
        private readonly TextBlock _message = new();
        private readonly Button _cancelButton = new();
        private readonly Button _confirmButton = new();

        public StickyConfirmWindow(
            StickyWindow owner,
            string headingText,
            string messageText,
            string confirmText,
            Action confirmAction)
        {
            _owner = owner;
            _headingText = headingText;
            _messageText = messageText;
            _confirmText = confirmText;
            _confirmAction = confirmAction;
            Width = BaseWindowWidth;
            Height = BaseWindowHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = BuildContent();
            PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    Close();
                    args.Handled = true;
                }
            };
        }

        private UIElement BuildContent()
        {
            var scaleHost = new Grid
            {
                Width = BaseWindowWidth,
                Height = BaseWindowHeight,
                LayoutTransform = _windowScale
            };

            _panel.CornerRadius = new CornerRadius(8);
            _panel.BorderThickness = new Thickness(1);
            _panel.Padding = new Thickness(14);

            var stack = new StackPanel();
            _heading.Text = "完成当前任务？";
            _heading.FontSize = 14;
            _heading.FontWeight = FontWeights.SemiBold;
            _heading.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(_heading);

            _message.Text = "是否完成当前任务，仍有未完成的子任务";
            _message.FontSize = 13;
            _message.TextWrapping = TextWrapping.Wrap;
            _message.LineHeight = 19;
            stack.Children.Add(_message);
            _heading.Text = _headingText;
            _message.Text = _messageText;

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
            _cancelButton.Click += (_, _) => Close();
            actions.Children.Add(_cancelButton);

            ConfigureActionButton(_confirmButton, "确认完成", isPrimary: true);
            _confirmButton.Content = _confirmText;
            _confirmButton.Margin = new Thickness(8, 0, 0, 0);
            _confirmButton.Click += (_, _) =>
            {
                _confirmAction();
                Close();
            };
            actions.Children.Add(_confirmButton);

            stack.Children.Add(actions);
            _panel.Child = stack;
            scaleHost.Children.Add(_panel);
            return scaleHost;
        }

        public void ApplyStickyOwnerState(bool reposition)
        {
            var scale = _owner._settings.StickyScale;
            Width = BaseWindowWidth * scale;
            Height = BaseWindowHeight * scale;
            Topmost = _owner.Topmost;
            _windowScale.ScaleX = scale;
            _windowScale.ScaleY = scale;

            _panel.Background = _owner.SurfaceBrush();
            _panel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _heading.Foreground = _owner.TextBrush();
            _message.Foreground = _owner.SecondaryTextBrush();
            ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
            ConfigureActionButton(_confirmButton, "确认完成", isPrimary: true);
            _confirmButton.Content = _confirmText;

            if (reposition)
            {
                _owner.PositionConfirmWindow(this);
            }
        }

        private void ConfigureActionButton(Button button, string text, bool isPrimary)
        {
            button.MinWidth = isPrimary ? 82 : 64;
            button.Height = 30;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.BorderThickness = new Thickness(0);
            button.Content = text;
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = isPrimary ? Brushes.White : _owner.TextBrush();
            button.Background = isPrimary ? _owner.AccentBrush() : Brushes.Transparent;
            button.Template = _owner.ButtonTemplate(
                new CornerRadius(7),
                isPrimary ? _owner.AccentBrush() : Brushes.Transparent,
                isPrimary ? _owner.AccentDarkBrush() : _owner.Brush(0xEEF9FA));
        }
    }

    private sealed class StickyTaskDetailWindow : Window, IStickyChildWindow
    {
        private const double BaseWindowWidth = 320;
        private const double BaseWindowHeight = 360;

        private readonly StickyWindow _owner;
        private readonly ScaleTransform _windowScale = new(1, 1);
        private readonly Border _panel = new();
        private readonly Border _titleBorder = new();
        private readonly Border _notesBorder = new();
        private readonly TextBlock _heading = new();
        private readonly TextBlock _titleLabel = new();
        private readonly TextBlock _notesLabel = new();
        private readonly TextBox _titleBox = new();
        private readonly TextBox _notesBox = new();
        private readonly Button _cancelButton = new();
        private readonly Button _saveButton = new();

        public StickyTaskDetailWindow(StickyWindow owner, TodoTask task)
        {
            _owner = owner;
            TaskId = task.Id;
            Width = BaseWindowWidth;
            Height = BaseWindowHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = BuildContent();
            _titleBox.Text = task.Title;
            _notesBox.Text = task.Notes;
            Loaded += (_, _) => FocusTitleBox();
            PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    Close();
                    args.Handled = true;
                }
            };
        }

        public string TaskId { get; }

        private UIElement BuildContent()
        {
            var scaleHost = new Grid
            {
                Width = BaseWindowWidth,
                Height = BaseWindowHeight,
                LayoutTransform = _windowScale
            };

            _panel.CornerRadius = new CornerRadius(8);
            _panel.BorderThickness = new Thickness(1);
            _panel.Padding = new Thickness(12);

            var stack = new StackPanel();
            _heading.Text = "任务详情";
            _heading.FontSize = 14;
            _heading.FontWeight = FontWeights.SemiBold;
            _heading.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(_heading);

            _titleLabel.Text = "标题";
            _titleLabel.FontSize = 12;
            _titleLabel.FontWeight = FontWeights.SemiBold;
            _titleLabel.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(_titleLabel);

            _titleBorder.Height = 38;
            _titleBorder.CornerRadius = new CornerRadius(8);
            _titleBorder.BorderThickness = new Thickness(1);
            _titleBorder.Padding = new Thickness(10, 0, 10, 0);
            _titleBorder.Child = _titleBox;
            _titleBorder.PreviewMouseLeftButtonDown += (_, _) => FocusTitleBox();
            _titleBox.BorderThickness = new Thickness(0);
            _titleBox.Background = Brushes.Transparent;
            _titleBox.FontSize = 14;
            _titleBox.Padding = new Thickness(0);
            _titleBox.VerticalContentAlignment = VerticalAlignment.Center;
            _titleBox.FocusVisualStyle = null;
            _titleBox.Cursor = Cursors.IBeam;
            _titleBox.MinHeight = 30;
            _titleBox.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    FocusNotesBox();
                    args.Handled = true;
                }
            };
            stack.Children.Add(_titleBorder);

            _notesLabel.Text = "备注";
            _notesLabel.FontSize = 12;
            _notesLabel.FontWeight = FontWeights.SemiBold;
            _notesLabel.Margin = new Thickness(0, 10, 0, 4);
            stack.Children.Add(_notesLabel);

            _notesBorder.Height = 120;
            _notesBorder.CornerRadius = new CornerRadius(8);
            _notesBorder.BorderThickness = new Thickness(1);
            _notesBorder.Padding = new Thickness(10, 6, 10, 6);
            _notesBorder.Child = _notesBox;
            _notesBorder.PreviewMouseLeftButtonDown += (_, _) => FocusNotesBox();
            _notesBox.AcceptsReturn = true;
            _notesBox.TextWrapping = TextWrapping.Wrap;
            _notesBox.BorderThickness = new Thickness(0);
            _notesBox.Background = Brushes.Transparent;
            _notesBox.FontSize = 13;
            _notesBox.Padding = new Thickness(0);
            _notesBox.FocusVisualStyle = null;
            _notesBox.Cursor = Cursors.IBeam;
            _notesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _notesBox.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    TrySave();
                    args.Handled = true;
                }
            };
            stack.Children.Add(_notesBorder);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
            _cancelButton.Click += (_, _) => Close();
            actions.Children.Add(_cancelButton);
            ConfigureActionButton(_saveButton, "保存", isPrimary: true);
            _saveButton.Margin = new Thickness(8, 0, 0, 0);
            _saveButton.Click += (_, _) => TrySave();
            actions.Children.Add(_saveButton);
            stack.Children.Add(actions);

            _panel.Child = stack;
            scaleHost.Children.Add(_panel);
            return scaleHost;
        }

        public void ApplyStickyOwnerState(bool reposition)
        {
            var scale = _owner._settings.StickyScale;
            Width = BaseWindowWidth * scale;
            Height = BaseWindowHeight * scale;
            Topmost = _owner.Topmost;
            _windowScale.ScaleX = scale;
            _windowScale.ScaleY = scale;

            _panel.Background = _owner.SurfaceBrush();
            _panel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _titleBorder.Background = _owner.PanelBrush(0xF5FAFB);
            _titleBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _notesBorder.Background = _owner.PanelBrush(0xF5FAFB);
            _notesBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _heading.Foreground = _owner.TextBrush();
            _titleLabel.Foreground = _owner.SecondaryTextBrush();
            _notesLabel.Foreground = _owner.SecondaryTextBrush();
            _titleBox.Foreground = _owner.TextBrush();
            _titleBox.CaretBrush = _owner.AccentBrush();
            _notesBox.Foreground = _owner.TextBrush();
            _notesBox.CaretBrush = _owner.AccentBrush();
            ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
            ConfigureActionButton(_saveButton, "保存", isPrimary: true);

            if (reposition)
            {
                _owner.PositionAddTaskWindow(this);
            }
        }

        public void FocusTitleBox()
        {
            Activate();
            if (!_titleBox.Focus())
            {
                Keyboard.Focus(_titleBox);
            }

            _titleBox.CaretIndex = _titleBox.Text.Length;
        }

        private void FocusNotesBox()
        {
            Activate();
            if (!_notesBox.Focus())
            {
                Keyboard.Focus(_notesBox);
            }

            _notesBox.CaretIndex = _notesBox.Text.Length;
        }

        private void TrySave()
        {
            if (_owner.SaveTaskDetail(TaskId, _titleBox.Text, _notesBox.Text))
            {
                Close();
                return;
            }

            FocusTitleBox();
        }

        private void ConfigureActionButton(Button button, string text, bool isPrimary)
        {
            button.MinWidth = 64;
            button.Height = 30;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.BorderThickness = new Thickness(0);
            button.Content = text;
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = isPrimary ? Brushes.White : _owner.TextBrush();
            button.Background = isPrimary ? _owner.AccentBrush() : Brushes.Transparent;
            button.Template = _owner.ButtonTemplate(
                new CornerRadius(7),
                isPrimary ? _owner.AccentBrush() : Brushes.Transparent,
                isPrimary ? _owner.AccentDarkBrush() : _owner.Brush(0xEEF9FA));
        }
    }

    private void EnforceVerticalDisplayConstraints()
    {
        if (!TryGetDragHandleScreenRect(out var dragRect))
        {
            return;
        }

        var monitor = MonitorFromRect(ref dragRect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            return;
        }

        var dy = 0;
        if (dragRect.Top < monitorInfo.WorkArea.Top)
        {
            dy = monitorInfo.WorkArea.Top - dragRect.Top;
        }
        else if (dragRect.Bottom > monitorInfo.WorkArea.Bottom)
        {
            dy = monitorInfo.WorkArea.Bottom - dragRect.Bottom;
        }

        if (dy != 0)
        {
            Top += dy / DeviceScale().Y;
        }
    }

    private sealed class StickyAddTaskWindow : Window, IStickyChildWindow
    {
        private const double BaseWindowWidth = 320;
        private const double BaseWindowHeight = 154;
        private const double BaseSubtaskWindowHeight = 360;

        private readonly StickyWindow _owner;
        private readonly TodoTask? _parent;
        private readonly ScaleTransform _windowScale = new(1, 1);
        private readonly Border _panel = new();
        private readonly Border _inputBorder = new();
        private readonly Border _notesBorder = new();
        private readonly TextBlock _heading = new();
        private readonly TextBlock _titleLabel = new();
        private readonly TextBlock _notesLabel = new();
        private readonly TextBox _titleBox = new();
        private readonly TextBox _notesBox = new();
        private readonly Button _cancelButton = new();
        private readonly Button _addButton = new();

        private double BaseHeight => _parent is null ? BaseWindowHeight : BaseSubtaskWindowHeight;

        public StickyAddTaskWindow(StickyWindow owner, TodoTask? parent = null)
        {
            _owner = owner;
            _parent = parent;
            Width = BaseWindowWidth;
            Height = BaseHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = BuildContent();
            Loaded += (_, _) => FocusTitleBox();
        }

        private UIElement BuildContent()
        {
            var scaleHost = new Grid
            {
                Width = BaseWindowWidth,
                Height = BaseHeight,
                LayoutTransform = _windowScale
            };

            _panel.CornerRadius = new CornerRadius(8);
            _panel.BorderThickness = new Thickness(1);
            _panel.Padding = new Thickness(12);

            var stack = new StackPanel();
            _heading.Text = "新增任务";
            _heading.FontSize = 14;
            _heading.FontWeight = FontWeights.SemiBold;
            _heading.Margin = new Thickness(0, 0, 0, 10);
            if (_parent is not null)
            {
                _heading.Text = "创建子任务";
            }
            stack.Children.Add(_heading);

            _inputBorder.Height = 38;
            _inputBorder.CornerRadius = new CornerRadius(8);
            _inputBorder.BorderThickness = new Thickness(1);
            _inputBorder.Padding = new Thickness(10, 0, 10, 0);
            _inputBorder.Child = _titleBox;
            _inputBorder.PreviewMouseLeftButtonDown += (_, _) => FocusTitleBox();

            _titleBox.BorderThickness = new Thickness(0);
            _titleBox.Background = Brushes.Transparent;
            _titleBox.FontSize = 14;
            _titleBox.Padding = new Thickness(0);
            _titleBox.VerticalContentAlignment = VerticalAlignment.Center;
            _titleBox.FocusVisualStyle = null;
            _titleBox.Cursor = Cursors.IBeam;
            _titleBox.MinHeight = 30;
            _titleBox.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter && _parent is not null && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    FocusNotesBox();
                    args.Handled = true;
                }
                else if (args.Key == Key.Enter)
                {
                    TryAddTask();
                    args.Handled = true;
                }
                else if (args.Key == Key.Escape)
                {
                    Close();
                    args.Handled = true;
                }
            };
            if (_parent is not null)
            {
                _titleLabel.Text = "标题";
                _titleLabel.FontSize = 12;
                _titleLabel.FontWeight = FontWeights.SemiBold;
                _titleLabel.Margin = new Thickness(0, 0, 0, 4);
                stack.Children.Add(_titleLabel);
            }
            stack.Children.Add(_inputBorder);

            if (_parent is not null)
            {
                _notesLabel.Text = "备注";
                _notesLabel.FontSize = 12;
                _notesLabel.FontWeight = FontWeights.SemiBold;
                _notesLabel.Margin = new Thickness(0, 10, 0, 4);
                stack.Children.Add(_notesLabel);
                _notesBorder.Height = 120;
                _notesBorder.Margin = new Thickness(0);
                _notesBorder.CornerRadius = new CornerRadius(8);
                _notesBorder.BorderThickness = new Thickness(1);
                _notesBorder.Padding = new Thickness(10, 6, 10, 6);
                _notesBorder.Child = _notesBox;
                _notesBorder.PreviewMouseLeftButtonDown += (_, _) => FocusNotesBox();

                _notesBox.AcceptsReturn = true;
                _notesBox.TextWrapping = TextWrapping.Wrap;
                _notesBox.BorderThickness = new Thickness(0);
                _notesBox.Background = Brushes.Transparent;
                _notesBox.FontSize = 13;
                _notesBox.Padding = new Thickness(0);
                _notesBox.FocusVisualStyle = null;
                _notesBox.Cursor = Cursors.IBeam;
                _notesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                _notesBox.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        TryAddTask();
                        args.Handled = true;
                    }
                    else if (args.Key == Key.Escape)
                    {
                        Close();
                        args.Handled = true;
                    }
                };
                stack.Children.Add(_notesBorder);
            }

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
            _cancelButton.Click += (_, _) => Close();
            actions.Children.Add(_cancelButton);

            ConfigureActionButton(_addButton, _parent is null ? "添加" : "提交", isPrimary: true);
            _addButton.Margin = new Thickness(8, 0, 0, 0);
            _addButton.Click += (_, _) => TryAddTask();
            actions.Children.Add(_addButton);

            var layout = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(actions, Dock.Bottom);
            layout.Children.Add(actions);
            layout.Children.Add(stack);
            _panel.Child = layout;
            scaleHost.Children.Add(_panel);
            return scaleHost;
        }

        public void ApplyStickyOwnerState(bool reposition)
        {
            var scale = _owner._settings.StickyScale;
            Width = BaseWindowWidth * scale;
            Height = BaseHeight * scale;
            Topmost = _owner.Topmost;
            _windowScale.ScaleX = scale;
            _windowScale.ScaleY = scale;

            _panel.Background = _owner.SurfaceBrush();
            _panel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _inputBorder.Background = _owner.PanelBrush(0xF5FAFB);
            _inputBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _notesBorder.Background = _owner.PanelBrush(0xF5FAFB);
            _notesBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner._settings.StickyOpacity + 0.14, 0.0, 1.0));
            _heading.Foreground = _owner.TextBrush();
            _titleLabel.Foreground = _owner.SecondaryTextBrush();
            _notesLabel.Foreground = _owner.SecondaryTextBrush();
            _titleBox.Foreground = _owner.TextBrush();
            _titleBox.CaretBrush = _owner.AccentBrush();
            _notesBox.Foreground = _owner.TextBrush();
            _notesBox.CaretBrush = _owner.AccentBrush();
            ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
            ConfigureActionButton(_addButton, _parent is null ? "添加" : "提交", isPrimary: true);

            if (reposition)
            {
                _owner.PositionAddTaskWindow(this);
            }
        }

        public void FocusTitleBox()
        {
            Activate();
            if (!_titleBox.Focus())
            {
                Keyboard.Focus(_titleBox);
            }

            _titleBox.CaretIndex = _titleBox.Text.Length;
        }

        private void FocusNotesBox()
        {
            Activate();
            if (!_notesBox.Focus())
            {
                Keyboard.Focus(_notesBox);
            }

            _notesBox.CaretIndex = _notesBox.Text.Length;
        }

        private void TryAddTask()
        {
            if (_owner.AddTask(_titleBox.Text, _parent, _parent is null ? null : _notesBox.Text))
            {
                Close();
                return;
            }

            FocusTitleBox();
        }

        private void ConfigureActionButton(Button button, string text, bool isPrimary)
        {
            button.MinWidth = 64;
            button.Height = 30;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.BorderThickness = new Thickness(0);
            button.Content = text;
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = isPrimary ? Brushes.White : _owner.TextBrush();
            button.Background = isPrimary ? _owner.AccentBrush() : Brushes.Transparent;
            button.Template = _owner.ButtonTemplate(
                new CornerRadius(7),
                isPrimary ? _owner.AccentBrush() : Brushes.Transparent,
                isPrimary ? _owner.AccentDarkBrush() : _owner.Brush(0xEEF9FA));
        }
    }

    private const int GwlStyle = -16;
    private const int WmNcHitTest = 0x0084;
    private const int WmNonClientLeftButtonUp = 0x00A2;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly record struct StickyWindowGeometry(double Left, double Top, double Width, double Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
