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

namespace Fowan.Todo.Sticky.Windows;

public sealed class StickyWindow : Window
{
    internal TodoSettingsSnapshot Settings => _workspace.State.Settings;
    private const double BaseWidth = 408;
    private const double BaseHeight = 568;
    private const double FloatingWindowSize = 52;
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
    private bool _isReturningToMain;
    private bool _isClosingFromCoordinatorShutdown;
    private bool _isApplyingWindowMode;
    private bool _hasAppliedInitialWindowMode;

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
        _palette = new StickyThemePalette(() => _workspace.State.ToQuerySettings());
        _controls = new StickyControlFactory(() => _workspace.State.ToQuerySettings(), _palette);
        _shellBuilder = new StickyShellBuilder(
            this, _commands, () => _workspace.State.ToQuerySettings(), _palette, _controls, _scaleTransform,
            OnHeaderDragMouseLeftButtonDown, OnWindowDragMouseLeftButtonDown,
            ReturnToMain, ToggleAdjustmentWindow, CloseStickyChildWindows,
            () => SynchronizeStickyChildWindows(reposition: false), AddTaskFromInput,
            () => ShowAddTaskWindow(), FocusInlineAddBox, UpdateAddPlaceholder, RefreshTasks);
        _childWindows = new StickyChildWindowCoordinator(
            this,
            () => _workspace.State.ToQuerySettings(),
            () => _workspace.State.ToQueryData(),
            () => _popupDismissOverlay,
            FindTask);
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
            () => _root,
            () => _dragHandle);
        _appearance = new StickyAppearanceController(
            this, _commands, () => _workspace.State.ToQuerySettings(), _palette, _scaleTransform,
            () => _shell, () => _addRowBorder, () => _taskDivider, () => _titleText,
            clamp => _displayGeometry.UpdateMinimumWindowSize(clamp),
            _displayGeometry.CurrentMonitorSizeDip,
            save => _displayGeometry.Enforce(save),
            _displayGeometry.SaveGeometry,
            RefreshTasks, () => SynchronizeStickyChildWindows(reposition: false),
            BuildUi, RefreshAll, UpdatePopupDismissOverlay);
        _floatingMode = new StickyFloatingModeController(
            this,
            _workspace,
            () => _brandIcon,
            _displayGeometry.DeviceScale,
            _displayGeometry.CurrentMonitorSizeDip,
            CloseStickyChildWindows,
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
            () => _windowDrag.IsCandidate,
            () => _windowDrag.CompleteFromExternal(allowFloatingClickRestore: true),
            () => _floatingMode.TryEnter(),
            () => _displayGeometry.Enforce(save: true),
            () => SynchronizeStickyChildWindows(reposition: true));
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
            Height = Math.Clamp(_workspace.State.Settings.StickyHeight ?? BaseHeight * _workspace.State.Settings.StickyScale, MinHeight, initialMaxSize.Height);
        }

        if (_workspace.State.Settings.StickyLeft.HasValue && _workspace.State.Settings.StickyTop.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _workspace.State.Settings.StickyLeft.Value;
            Top = _workspace.State.Settings.StickyTop.Value;
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
            SynchronizeStickyChildWindows(reposition: true);
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
            SynchronizeStickyChildWindows(reposition: true);
        };
        Closed += (_, _) =>
        {
            _source?.RemoveHook(_nativeWindow.WndProc);
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
        _dragHandle = view.DragHandle;
        _brandIcon = view.BrandIcon;
        _settingsButton = view.SettingsButton;
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
        PurgeExpiredRecycleBin();

        RefreshTasks();
    }

    private void RefreshTasks() => _taskListPresenter.Refresh();
    private void ApplyTaskDrop(TodoTask draggedTask, StickyDropTarget target) => _taskInteractions.ApplyDrop(draggedTask, target);
    private TodoTask? FindTask(string taskId) => _taskInteractions.FindTask(taskId);
    private bool AddTaskFromInput() => _taskInteractions.AddFromInput();
    internal bool AddTask(string title, string? parentTaskId = null, string? notes = null) =>
        _taskInteractions.Add(title, parentTaskId is null ? null : _workspace.State.ToQueryData().Tasks.FirstOrDefault(task => task.Id == parentTaskId), notes);
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

    private void SynchronizeStickyChildWindows(bool reposition) => _childWindows.Synchronize(reposition);
    private void CloseStickyChildWindows() => _childWindows.CloseAll();
    private void UpdatePopupDismissOverlay() => _childWindows.RefreshDismissOverlay();

    internal void SetStickyOpacity(double opacity) => _appearance.SetOpacity(opacity);
    internal void SetStickyTheme(string theme) => _appearance.SetTheme(theme);
    internal void ApplyScaleFromSlider(double sliderValue) => _appearance.ApplyScale(sliderValue);

    private void ApplyCurrentWindowMode(bool save) => _floatingMode.ApplyCurrent(save);
    private void UpdateMinimumWindowSize(bool clampCurrentSize) => _displayGeometry.UpdateMinimumWindowSize(clampCurrentSize);
    private void EnforceWindowDisplayConstraints(bool save) => _displayGeometry.Enforce(save);
    private bool IsInteractiveMoveResizeInProgress() => _windowDrag.IsDragging || _nativeWindow.IsInMoveSizeLoop;
    private (double Width, double Height) CurrentMonitorSizeDip() => _displayGeometry.CurrentMonitorSizeDip();
    private void SaveGeometry() => _displayGeometry.SaveGeometry();

    private void ReturnToMain()
    {
        _isReturningToMain = true;
        _commands.SetStickyModeEnabled(false);
        SaveGeometry();

        _mainProcesses.TryActivate(ResolveMainExePath());

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
