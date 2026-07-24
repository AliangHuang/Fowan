using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Sticky.Windows.AppPorts;
using Fowan.Todo.Sticky.Windows.Coordination;
using Fowan.Todo.Sticky.Windows.Platform.Windows;
using Fowan.Todo.Sticky.Windows.Presentation;
using Fowan.Todo.Sticky.Windows.Application;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fowan.Todo.Sticky.Windows;

public sealed class StickyWindow : Window
{
#if FOWAN_DEVELOPMENT_RUNTIME
    private const string MainExecutableName = "Fowan.Todo.Windows.Dev.exe";
#else
    private const string MainExecutableName = "Fowan.Todo.Windows.exe";
#endif
    internal TodoSettingsSnapshot Settings => _workspace.State.Settings;
    private const double BaseWidth = 408;
    private const double BaseHeight = 568;
    private const double FloatingWindowSize = 52;
    private const double MenuBarHeight = 55;
    private const int MenuHideGracePeriodMilliseconds = 160;
    private const double TaskDropEdgeRatio = 0.28;
    private readonly TodoWorkspace _workspace = TodoWorkspace.CreateDefault();
    private readonly StickyWindowCommands _commands;
    private readonly StickyThemePalette _palette;
    private readonly StickyControlFactory _controls;
    private readonly StickyTaskCommandCoordinator _taskCommands;
    private readonly StickyDropTargetResolver _dropTargets;
    private readonly StickyDropPreviewPresenter _dropPreviewPresenter;
    private readonly StickyDragState _dragState = new();
    private readonly StickyTaskDragController _taskDrag;
    private readonly StickyWindowDragController _windowDrag;
    private readonly StickyFloatingModeController _floatingMode;
    private readonly StickyDisplayGeometryController _displayGeometry;
    private readonly StickyNativeWindowController _nativeWindow;
    private readonly StickyChildWindowCoordinator _childWindows;
    private StickyMenuWindowCoordinator? _menuWindows;
    private readonly StickyTaskListPresenter _taskListPresenter;
    private readonly StickyShellBuilder _shellBuilder;
    private readonly StickyAppearanceController _appearance;
    private readonly StickyTaskInteractionController _taskInteractions;
    private TodoWorkspace _store => _workspace;
    private readonly IStickyMainProcessCoordinator _mainProcesses;
    private readonly string? _mainExePath;
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly List<Border> _taskRows = [];

    private HwndSource? _source;
    private Grid _root = new();
    private Border _shell = new();
    private Border _popupDismissOverlay = new();
    private Border _addRowBorder = new();
    private Border _taskDivider = new();
    private FrameworkElement _titleArea = new Grid();
    private FrameworkElement _brandIcon = new Grid();
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
    private bool _isReturningToMain;
    private bool _isClosingFromCoordinatorShutdown;
    private bool _isApplyingWindowMode;
    private bool _hasAppliedInitialWindowMode;
    private readonly HashSet<Window> _pointerOverStickyAuxiliaryWindows = [];
    private bool _isPointerOverMainWindow;
    private bool _isPointerStateUpdatePending;
    private bool _isMenuHidden;
    private bool _acceptFloatingTaskbarRestore;
    private bool _isFloatingTaskbarRestorePending;
    private bool _isWindowStateTransitionInProgress;
    private readonly DispatcherTimer _menuHideTimer;
    private readonly DispatcherTimer _recurringTaskTimer;

    public StickyWindow(string[] args)
        : this(args, new WindowsStickyMainProcessCoordinator(new WindowsProcessLauncher()))
    {
    }

    internal StickyWindow(string[] args, IStickyMainProcessCoordinator mainProcesses)
    {
        _mainProcesses = mainProcesses;
        _mainExePath = ParseMainExePath(args);
        _commands = new StickyWindowCommands(_workspace);
        _workspace.Reload();
        _commands.CreateDueRecurringTasks();
        _palette = new StickyThemePalette(() => _workspace.State.ToQuerySettings());
        _controls = new StickyControlFactory(() => _workspace.State.ToQuerySettings(), _palette);
        _shellBuilder = new StickyShellBuilder(
            this, _commands, () => _workspace.State.ToQuerySettings(), _palette, _controls, _scaleTransform,
            OnHeaderDragMouseLeftButtonDown, OnWindowDragMouseLeftButtonDown,
            ReturnToMain, ToggleAdjustmentWindow, CloseStickyChildWindows,
            () => SynchronizeStickyWindows(reposition: false), AddTaskFromInput,
            () => ShowAddTaskWindow(), FocusInlineAddBox, UpdateAddPlaceholder, RefreshTasks);
        _childWindows = new StickyChildWindowCoordinator(
            this,
            () => _workspace.State.ToQuerySettings(),
            () => _workspace.State.ToQueryData(),
            () => _popupDismissOverlay,
            FindTask,
            SetStickyChildPointerState,
            visible => _menuWindows?.SetDismissOverlayVisible(visible));
        _taskCommands = new StickyTaskCommandCoordinator(_workspace, RefreshAll, ShowConfirmation);
        _taskInteractions = new StickyTaskInteractionController(
            this, _workspace, _taskCommands, () => _workspace.State.ToQueryData(), () => _addBox,
            () => _addPlaceholder, RefreshAll);
        _dropTargets = new StickyDropTargetResolver(
            () => _dragState.CandidateTask,
            () => _dragState.CandidateRow,
            () => _taskRows,
            () => _activeTasks,
            () => _taskDivider,
            () => _workspace.State.ToQueryData(),
            _taskInteractions.FindTask,
            _taskInteractions.IsValidDrop,
            _taskInteractions.TryGetElementBounds,
            TaskDropEdgeRatio);
        _dropPreviewPresenter = new StickyDropPreviewPresenter(
            this,
            () => _workspace.State.ToQueryData(),
            () => _workspace.State.ToQuerySettings(),
            _palette,
            () => _dragState.CandidateTask,
            () => _dragState.HoveredRow,
            () => _taskRows,
            FindTask);
        _taskDrag = new StickyTaskDragController(
            this,
            _dragState,
            HasOpenStickyChildWindow,
            () => _taskRows,
            () => _taskScroll,
            () => _completedTaskSection,
            _dropTargets,
            _dropPreviewPresenter,
            ApplyTaskDrop);
        _taskListPresenter = new StickyTaskListPresenter(
            _commands,
            () => _workspace.State.ToQueryData(),
            () => _workspace.State.ToQuerySettings(),
            _palette,
            _controls,
            _taskDrag,
            _dropPreviewPresenter,
            () => _taskRows,
            () => _activeTasks,
            () => _completedTasks,
            () => _completedToggle,
            () => _titleText,
            () => _countText,
            HasOpenStickyChildWindow,
            ShowTaskDetailWindow,
            ShowAddSubtaskWindow,
            DeleteTaskFromContextMenu,
            ToggleTaskCompleted,
            ToggleImportant);
        _displayGeometry = new StickyDisplayGeometryController(
            this,
            _workspace,
            () => _workspace.State.ToQuerySettings(),
            MenuFootprint,
            () => _root);
        _appearance = new StickyAppearanceController(
            this, _commands, () => _workspace.State.ToQuerySettings(), _palette, _scaleTransform,
            () => _shell, () => _addRowBorder, () => _taskDivider, () => _titleText,
            clamp => _displayGeometry.UpdateMinimumWindowSize(clamp),
            _displayGeometry.CurrentMonitorSizeDip,
            save => _displayGeometry.Enforce(save),
            _displayGeometry.SaveGeometry,
            RefreshTasks, () => SynchronizeStickyWindows(reposition: false),
            BuildUi, RefreshAll, UpdatePopupDismissOverlay);
        _floatingMode = new StickyFloatingModeController(
            this,
            _workspace,
            () => _brandIcon,
            _displayGeometry.DeviceScale,
            _displayGeometry.CurrentMonitorSizeDip,
            MenuFootprint,
            CloseStickyChildWindows,
            () => _menuWindows?.HideForOwnerTransition(),
            BuildUi,
            ApplyStoredSettings,
            RefreshAll,
            _displayGeometry.UpdateMinimumWindowSize,
            StickyNativeWindowController.SetResizeEnabled,
            _displayGeometry.Enforce);
        _windowDrag = new StickyWindowDragController(
            this,
            () => _workspace.State.Settings.IsStickyFloatingModeEnabled,
            _displayGeometry.DeviceScale,
            point => _floatingMode.Snap(point),
            _floatingMode.Exit,
            _displayGeometry.EnforceVerticalConstraints,
            _floatingMode.TryEnter,
            _displayGeometry.SaveGeometry);
        _nativeWindow = new StickyNativeWindowController(
            this,
            () => _workspace.State.Settings.IsStickyFloatingModeEnabled,
            RestoreFromExternalActivation,
            BeginMinimizeTransition,
            () => _windowDrag.IsCandidate,
            () => _windowDrag.CompleteFromExternal(allowFloatingClickRestore: true),
            () => _floatingMode.TryEnter(),
            () => _displayGeometry.Enforce(save: true),
            () => SynchronizeStickyWindows(reposition: true),
            SynchronizeForDpiChange);
        _menuHideTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(MenuHideGracePeriodMilliseconds)
        };
        _menuHideTimer.Tick += (_, _) => ConfirmMenuHideAfterPointerLeaves();
        _recurringTaskTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromHours(1)
        };
        _recurringTaskTimer.Tick += (_, _) =>
        {
            if (_commands.CreateDueRecurringTasks() > 0) RefreshTasks();
        };
        _recurringTaskTimer.Start();
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
        Topmost = _workspace.State.Settings.IsStickyFloatingModeEnabled || _workspace.State.Settings.IsStickyTopmost;
        if (_workspace.State.Settings.IsStickyFloatingModeEnabled)
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
            Width = Math.Clamp(_workspace.State.Settings.StickyWidth ?? BaseWidth * _workspace.State.Settings.StickyScale, MinWidth, initialMaxSize.Width);
            var expandedHeight = _workspace.State.Settings.StickyHeight ?? BaseHeight * _workspace.State.Settings.StickyScale;
            var bodyGeometry = TodoStickyPlacement.BodyGeometryFromExpanded(0, expandedHeight, MenuFootprint());
            Height = Math.Clamp(
                bodyGeometry.Height,
                MinHeight,
                Math.Max(MinHeight, initialMaxSize.Height - MenuFootprint()));
        }

        if (_workspace.State.Settings.StickyLeft.HasValue && _workspace.State.Settings.StickyTop.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _workspace.State.Settings.StickyLeft.Value;
            Top = _workspace.State.Settings.IsStickyFloatingModeEnabled
                ? _workspace.State.Settings.StickyTop.Value
                : TodoStickyPlacement.BodyGeometryFromExpanded(
                    _workspace.State.Settings.StickyTop.Value,
                    0,
                    MenuFootprint()).Top;
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
        Loaded += (_, _) =>
        {
            _isPointerOverMainWindow = IsMouseOver;
            ApplyCurrentWindowMode(save: true);
            ApplyStickyPresentationPreferences();
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => _acceptFloatingTaskbarRestore = true));
        };
        ContentRendered += (_, _) =>
        {
            EnsureMenuWindow(rebuild: false);
            ApplyStickyPresentationPreferences();
        };
        MouseEnter += (_, _) => SetStickyMainPointerState(true);
        MouseLeave += (_, _) => SetStickyMainPointerState(false);
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnWindowDragMouseMove), handledEventsToo: true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnWindowDragMouseLeftButtonUp), handledEventsToo: true);
        AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowDragMouseLeftButtonUp), handledEventsToo: true);
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowDragMouseLeftButtonUp), handledEventsToo: true);
        LostMouseCapture += OnWindowDragLostMouseCapture;
        Activated += (_, _) =>
        {
            if (_acceptFloatingTaskbarRestore && _workspace.State.Settings.IsStickyFloatingModeEnabled)
            {
                RestoreFromExternalActivation();
            }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                BeginMinimizeTransition();

                if (_acceptFloatingTaskbarRestore && !_isFloatingTaskbarRestorePending &&
                    _workspace.State.Settings.IsStickyFloatingModeEnabled)
                {
                    _isFloatingTaskbarRestorePending = true;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        _isFloatingTaskbarRestorePending = false;
                        if (WindowState == WindowState.Minimized && _workspace.State.Settings.IsStickyFloatingModeEnabled)
                        {
                            RestoreFromExternalActivation();
                        }
                    }));
                }

                return;
            }

            if (WindowState != WindowState.Normal || !_isWindowStateTransitionInProgress)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (WindowState == WindowState.Normal)
                {
                    _isWindowStateTransitionInProgress = false;
                    _isPointerOverMainWindow = IsMouseOver;
                    ApplyStickyPresentationPreferences();
                }
            }));
        };
        Deactivated += (_, _) =>
        {
            _windowDrag.CompleteFromExternal(allowFloatingClickRestore: false);
        };
        LocationChanged += (_, _) =>
        {
            if (!_hasAppliedInitialWindowMode || _isApplyingWindowMode || _floatingMode.IsApplying)
            {
                return;
            }

            if (!_workspace.State.Settings.IsStickyFloatingModeEnabled &&
                !_displayGeometry.IsEnforcing &&
                !IsInteractiveMoveResizeInProgress())
            {
                EnforceWindowDisplayConstraints(save: false);
            }

            if (!_windowDrag.IsCandidate || _windowDrag.StartedInFloatingMode)
            {
                SaveGeometry();
            }
            SynchronizeStickyWindows(reposition: true);
        };
        SizeChanged += (_, _) =>
        {
            if (!_hasAppliedInitialWindowMode || _isApplyingWindowMode || _floatingMode.IsApplying)
            {
                return;
            }

            if (!_workspace.State.Settings.IsStickyFloatingModeEnabled &&
                !_displayGeometry.IsEnforcing &&
                !IsInteractiveMoveResizeInProgress())
            {
                EnforceWindowDisplayConstraints(save: false);
            }

            SaveGeometry();
            SynchronizeStickyWindows(reposition: true);
        };
        Closed += (_, _) =>
        {
            _menuHideTimer.Stop();
            _recurringTaskTimer.Stop();
            _source?.RemoveHook(_nativeWindow.WndProc);
            _menuWindows?.Close();
            CloseStickyChildWindows();
        };
        Closing += (_, _) =>
        {
            SaveGeometry();
            var shouldShutdownMain = false;
            if (_isClosingFromCoordinatorShutdown)
            {
                _commands.SetStickyModeEnabled(false);
            }
            else if (!_isReturningToMain)
            {
                _commands.SetStickyModeEnabled(true);
                shouldShutdownMain = true;
            }

            if (shouldShutdownMain)
            {
                _mainProcesses.TryShutdown();
            }
        };
    }

    private void InitializeNativeShell()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        StickyNativeWindowController.SetResizeEnabled(hwnd, !_workspace.State.Settings.IsStickyFloatingModeEnabled);
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(_nativeWindow.WndProc);
    }

    private void BuildUi()
    {
        var view = _shellBuilder.Build();
        _root = view.Root;
        _shell = view.Shell;
        _popupDismissOverlay = view.DismissOverlay;
        _addRowBorder = view.AddRow;
        _taskDivider = view.TaskDivider;
        _titleArea = view.TitleArea;
        _brandIcon = view.BrandIcon;
        _addTaskButton = view.AddTaskButton;
        _titleText = view.TitleText;
        _countText = view.CountText;
        _addBox = view.AddBox;
        _addPlaceholder = view.AddPlaceholder;
        _taskScroll = view.TaskScroll;
        _activeTasks = view.ActiveTasks;
        _completedTasks = view.CompletedTasks;
        _completedTaskSection = view.CompletedTaskSection;
        _completedToggle = view.CompletedToggle;

        if (_workspace.State.Settings.IsStickyFloatingModeEnabled)
        {
            _menuWindows?.ApplyPresentation(showMenu: false);
        }
        else
        {
            EnsureMenuWindow(rebuild: true);
        }

        ApplyStickyPresentationPreferences();
        UpdatePopupDismissOverlay();
    }

    private void EnsureMenuWindow(bool rebuild)
    {
        if (!IsVisible || _workspace.State.Settings.IsStickyFloatingModeEnabled) return;
        if (_menuWindows is null)
        {
            _menuWindows = new StickyMenuWindowCoordinator(
                this,
                _shellBuilder,
                () => _workspace.State.ToQuerySettings(),
                () => _workspace.State.Settings.IsStickyFloatingModeEnabled,
                SetStickyChildPointerState);
            return;
        }

        if (rebuild) _menuWindows.Rebuild();
    }

    private bool HasOpenStickyChildWindow() => _childWindows.HasOpenWindow();

    private void OnHeaderDragMouseLeftButtonDown(object sender, MouseButtonEventArgs args) =>
        _windowDrag.OnHeaderDown(sender, args);
    private void OnWindowDragMouseLeftButtonDown(object sender, MouseButtonEventArgs args) =>
        _windowDrag.OnMouseDown(sender, args);
    private void OnWindowDragMouseMove(object sender, MouseEventArgs args) =>
        _windowDrag.OnMouseMove(sender, args);
    private void OnWindowDragMouseLeftButtonUp(object sender, MouseButtonEventArgs args) =>
        _windowDrag.OnMouseUp(sender, args);
    private void OnWindowDragLostMouseCapture(object sender, MouseEventArgs args) =>
        _windowDrag.OnLostCapture(sender, args);
    private void ApplyStoredSettings() => _appearance.ApplyStored();

    private void RefreshAll()
    {
        _commands.Reload();
        _commands.CreateDueRecurringTasks();
        PurgeExpiredRecycleBin();

        RefreshTasks();
    }

    private void RefreshTasks() => _taskListPresenter.Refresh();
    private void ApplyTaskDrop(TodoTask draggedTask, StickyDropTarget target) => _taskInteractions.ApplyDrop(draggedTask, target);
    private TodoTask? FindTask(string taskId) => _taskInteractions.FindTask(taskId);
    private bool AddTaskFromInput() => _taskInteractions.AddFromInput();
    internal bool AddTask(string title, string? parentTaskId = null, string? notes = null) =>
        _taskInteractions.Add(title, parentTaskId is null ? null : _workspace.State.ToQueryData().Tasks.FirstOrDefault(task => task.Id == parentTaskId), notes);
    internal bool AddTask(
        string title,
        string listId,
        bool isImportant,
        DateTime startDate,
        DateTime? dueDate,
        string? notes = null,
        TodoRecurrenceRule? recurrence = null) =>
        _taskCommands.Add(title, listId, isImportant, startDate, dueDate, notes: notes, recurrence: recurrence);
    private void FocusInlineAddBox() => _taskInteractions.FocusInlineAdd();

    private void ToggleTaskCompleted(TodoTask task)
    {
        _taskCommands.ToggleCompleted(task);
    }

    private void ToggleImportant(string taskId)
    {
        _taskCommands.ToggleImportant(taskId);
    }

    private void UpdateAddPlaceholder() => _taskInteractions.UpdateAddPlaceholder();

    private void ToggleAdjustmentWindow() => _childWindows.ToggleAdjustment();

    internal void PositionAdjustmentWindow(Window adjustmentWindow) => _childWindows.PositionAdjustment(adjustmentWindow);

    private void ShowAddTaskWindow(TodoTask? parent = null) => _childWindows.ShowAddTask(parent);

    private void ShowAddSubtaskWindow(TodoTask parent) => _childWindows.ShowAddSubtask(parent);

    private void ShowTaskDetailWindow(string taskId) => _childWindows.ShowTaskDetail(taskId);

    internal bool SaveTaskDetail(string taskId, string title, string notes)
    {
        return _taskCommands.SaveDetails(taskId, title, notes);
    }

    internal void PositionAddTaskWindow(Window addTaskWindow) => _childWindows.PositionCentered(addTaskWindow);

    private void DeleteTaskFromContextMenu(TodoTask task)
    {
        _taskCommands.Delete(task);
    }

    private void ShowConfirmation(string heading, string message, string confirmText, Action confirmAction) =>
        _childWindows.ShowConfirmation(heading, message, confirmText, confirmAction);

    internal void PositionConfirmWindow(Window confirmWindow) => _childWindows.PositionCentered(confirmWindow);

    private void SynchronizeStickyWindows(bool reposition)
    {
        _childWindows.Synchronize(reposition);
        if (_isWindowStateTransitionInProgress)
        {
            _menuWindows?.HideBeforeOwnerMinimize();
            return;
        }
        _menuWindows?.Synchronize(!_isMenuHidden);
    }

    private void SynchronizeForDpiChange()
    {
        if (!_hasAppliedInitialWindowMode || _isApplyingWindowMode || _floatingMode.IsApplying)
        {
            return;
        }

        if (!_workspace.State.Settings.IsStickyFloatingModeEnabled)
        {
            EnforceWindowDisplayConstraints(save: false);
        }

        SynchronizeStickyWindows(reposition: true);
    }
    private void CloseStickyChildWindows() => _childWindows.CloseAll();
    private void UpdatePopupDismissOverlay() => _childWindows.RefreshDismissOverlay();

    internal void SetStickyOpacity(double opacity)
    {
        _appearance.SetOpacity(opacity);
        _menuWindows?.Rebuild();
        SynchronizeStickyWindows(reposition: false);
    }
    internal void SetStickyTheme(string theme) => _appearance.SetTheme(theme);
    internal void ApplyScaleFromSlider(double sliderValue) => _appearance.ApplyScale(sliderValue);
    internal void SetStickyTitleHidden(bool hidden)
    {
        if (!_commands.SetStickyTitleHidden(hidden)) return;
        ApplyStickyPresentationPreferences();
        SynchronizeStickyWindows(reposition: false);
    }

    internal void SetStickyTitleFontSize(double fontSize)
    {
        if (!_commands.SetStickyTitleFontSize(fontSize)) return;
        _titleText.FontSize = Settings.StickyTitleFontSize;
        SynchronizeStickyWindows(reposition: false);
    }

    internal void SetStickyAddTaskMinimized(bool minimized)
    {
        if (!_commands.SetStickyAddTaskMinimized(minimized)) return;
        ApplyStickyPresentationPreferences();
        SynchronizeStickyWindows(reposition: false);
    }

    internal void SetStickyMenuAutoHideEnabled(bool enabled)
    {
        if (!_commands.SetStickyMenuAutoHideEnabled(enabled)) return;
        _isPointerOverMainWindow = IsMouseOver;
        QueueStickyPointerStateUpdate();
        SynchronizeStickyWindows(reposition: false);
    }

    private void ApplyCurrentWindowMode(bool save) => _floatingMode.ApplyCurrent(save);
    private void UpdateMinimumWindowSize(bool clampCurrentSize) => _displayGeometry.UpdateMinimumWindowSize(clampCurrentSize);
    private void EnforceWindowDisplayConstraints(bool save) => _displayGeometry.Enforce(save);
    private bool IsInteractiveMoveResizeInProgress() => _windowDrag.IsDragging || _nativeWindow.IsInMoveSizeLoop;
    private (double Width, double Height) CurrentMonitorSizeDip() => _displayGeometry.CurrentMonitorSizeDip();
    private void SaveGeometry() => _displayGeometry.SaveGeometry();

    private void SetStickyMainPointerState(bool isOver)
    {
        _isPointerOverMainWindow = isOver;
        QueueStickyPointerStateUpdate();
    }

    internal void SetStickyChildPointerState(Window childWindow, bool isOver)
    {
        if (isOver)
        {
            _pointerOverStickyAuxiliaryWindows.Add(childWindow);
        }
        else
        {
            _pointerOverStickyAuxiliaryWindows.Remove(childWindow);
        }

        QueueStickyPointerStateUpdate();
    }

    private void QueueStickyPointerStateUpdate()
    {
        if (_isReturningToMain || !IsVisible)
        {
            _menuHideTimer.Stop();
            QueueMenuPresentationUpdate();
            return;
        }

        if (ShouldKeepMenuVisible())
        {
            _menuHideTimer.Stop();
            QueueMenuPresentationUpdate();
            return;
        }

        _menuHideTimer.Stop();
        _menuHideTimer.Start();
    }

    private void ConfirmMenuHideAfterPointerLeaves()
    {
        _menuHideTimer.Stop();
        QueueMenuPresentationUpdate();
    }

    private void QueueMenuPresentationUpdate()
    {
        if (_isPointerStateUpdatePending) return;
        _isPointerStateUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _isPointerStateUpdatePending = false;
            ApplyMenuPresentationForCurrentPointerState();
        }));
    }

    private void ApplyStickyPresentationPreferences()
    {
        var settings = _workspace.State.Settings;
        _titleArea.Visibility = settings.IsStickyTitleHidden ? Visibility.Collapsed : Visibility.Visible;
        _addRowBorder.Visibility = settings.IsStickyAddTaskMinimized ? Visibility.Collapsed : Visibility.Visible;

        ApplyMenuPresentationForCurrentPointerState();
    }

    private void ApplyMenuPresentationForCurrentPointerState()
    {
        var settings = _workspace.State.Settings;

        if (_isWindowStateTransitionInProgress)
        {
            _menuWindows?.HideBeforeOwnerMinimize();
            return;
        }

        if (_isReturningToMain || !IsVisible)
        {
            _isMenuHidden = true;
            _menuWindows?.ApplyPresentation(showMenu: false);
            return;
        }

        if (settings.IsStickyFloatingModeEnabled)
        {
            _isMenuHidden = false;
            _menuWindows?.ApplyPresentation(showMenu: false);
            return;
        }

        if (settings.IsStickyMenuAutoHideEnabled && _menuHideTimer.IsEnabled && !ShouldKeepMenuVisible())
        {
            return;
        }

        ApplyMenuVisibility(ShouldKeepMenuVisible());
    }

    private void ApplyMenuVisibility(bool showMenu)
    {
        var shouldHideMenu = !showMenu;
        _isMenuHidden = shouldHideMenu;
        _shell.CornerRadius = showMenu
            ? new CornerRadius(0, 0, 8, 8)
            : new CornerRadius(8);
        _menuWindows?.ApplyPresentation(showMenu);
    }

    private void BeginMinimizeTransition()
    {
        _isWindowStateTransitionInProgress = true;
        _menuHideTimer.Stop();
        _menuWindows?.HideBeforeOwnerMinimize();
    }

    private bool IsPointerOverStickySurface() =>
        _isPointerOverMainWindow ||
        _pointerOverStickyAuxiliaryWindows.Count > 0 ||
        HasOpenStickyChildWindow();

    private bool ShouldKeepMenuVisible() =>
        !_workspace.State.Settings.IsStickyMenuAutoHideEnabled ||
        IsPointerOverStickySurface();

    private double MenuFootprint() =>
        (MenuBarHeight - StickyShellBuilder.MenuBodyOverlap) * _workspace.State.Settings.StickyScale;

    private void ReturnToMain()
    {
        _isReturningToMain = true;
        _menuHideTimer.Stop();
        _commands.SetStickyModeEnabled(false);
        SaveGeometry();

        CloseStickyChildWindows();
        _menuWindows?.HideForOwnerTransition();
        Hide();
        if (!_mainProcesses.TryActivate(ResolveMainExePath()))
        {
            Show();
            _commands.SetStickyModeEnabled(true);
            _isReturningToMain = false;
            ApplyMenuPresentationForCurrentPointerState();
            return;
        }

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
        if (_workspace.State.Settings.IsStickyFloatingModeEnabled)
        {
            _floatingMode.Exit();
        }
        else if (!wasMinimized)
        {
            ReloadSettingsAndRefresh();
        }

        if (!IsVisible)
        {
            Show();
        }

        _isPointerOverMainWindow = IsMouseOver;
        ApplyStickyPresentationPreferences();

        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            StickyNativeMethods.SetForegroundWindow(hwnd);
        }
    }

    internal void CloseFromCoordinatorShutdown()
    {
        _isClosingFromCoordinatorShutdown = true;
        Close();
    }

    private void ReloadSettingsAndRefresh()
    {
        _commands.Reload();
        _commands.CreateDueRecurringTasks();
        PurgeExpiredRecycleBin();

        Topmost = _workspace.State.Settings.IsStickyFloatingModeEnabled || _workspace.State.Settings.IsStickyTopmost;
        CloseStickyChildWindows();
        BuildUi();
        ApplyStoredSettings();
        RefreshAll();
        UpdatePopupDismissOverlay();
        if (WindowState != WindowState.Minimized)
        {
            ApplyCurrentWindowMode(save: false);
        }
        ApplyStickyPresentationPreferences();
    }

    private void PurgeExpiredRecycleBin()
    {
        _commands.PurgeExpiredRecycleBin();
    }

    private string ResolveMainExePath()
    {
        if (!string.IsNullOrWhiteSpace(_mainExePath))
        {
            return _mainExePath;
        }

        return Path.Combine(AppContext.BaseDirectory, MainExecutableName);
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

    internal ControlTemplate ButtonTemplate(CornerRadius radius, Brush normalBackground, Brush hoverBackground) =>
        _controls.ButtonTemplate(radius, normalBackground, hoverBackground);
    internal TextBlock SmallLabel(string text) => _controls.SmallLabel(text);
    internal SolidColorBrush TextBrush() => _palette.Text;
    internal SolidColorBrush SecondaryTextBrush() => _palette.SecondaryText;
    internal SolidColorBrush AccentBrush() => _palette.Accent;
    internal SolidColorBrush AccentDarkBrush() => _palette.AccentDark;
    internal SolidColorBrush SurfaceBrush() => _palette.Surface;
    internal SolidColorBrush PanelBrush(uint rgb) => _palette.Panel(rgb);
    internal SolidColorBrush Brush(uint rgb) => _palette.Brush(rgb);
    internal SolidColorBrush Brush(uint rgb, double opacity) => _palette.Brush(rgb, opacity);


}
