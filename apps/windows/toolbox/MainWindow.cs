using Fowan.Windows.Models;
using Fowan.Windows.Services;
using Fowan.Windows.Platform.Contracts;
using Fowan.Windows.Platform.Windows;
using Fowan.Windows.AppPorts;
using Fowan.Windows.Presentation;
using Fowan.Windows.Application;
using Fowan.Windows.Coordination;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace Fowan.Windows;

public sealed class MainWindow : Window
{
    private const string AiChatToolId = "ai-chat";

    private readonly ToolboxSession _session = ToolboxCompositionRoot.CreateSession();
    private readonly ToolboxPresentationState _presentation = new();
    private readonly UpdateService _updateService = new();
    private readonly IProcessLauncher _processLauncher = new WindowsProcessLauncher();
    private readonly IFileDialogService _filePicker;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly AutoStartService _autoStartService = new();
    private readonly LocalizationService _loc = new();
    private readonly IWindowHost _windowHost;
    private readonly ITrayService _trayService;
    private readonly UpdateCheckCoordinator _updateCheckCoordinator;
    private readonly ToolboxThemePalette _theme;
    private readonly ToolLaunchCoordinator _toolLauncher;
    private readonly UpdateInteractionCoordinator _updateInteraction;
    private readonly ToolboxSettingsDialog _settingsDialog;
    private readonly ToolboxProfileDialog _profileDialog;
    private readonly ToolboxDetailPresenter _detailPresenter;
    private readonly ToolboxToolCardFactory _toolCards;
    private readonly ToolboxControlFactory _controls;
    private readonly ToolboxShellBuilder _shellBuilder;
    private readonly ToolboxQuickCaptureDialog _quickCaptureDialog;
    private readonly ToolboxToolGridPresenter _toolGridPresenter;
    private readonly ToolboxHeaderActionsBuilder _headerActions;
    private readonly ToolboxUiStateController _uiState;

    private Grid _root = new();
    private StackPanel _categoryPanel = new();
    private Grid _toolGrid = new();
    private Border _detailPanel = new();
    private Border _toastHost = new();
    private TextBlock _toastText = new();
    private TextBox _searchBox = new();
    private TextBlock _pageTitle = new();
    private TextBlock _resultSummary = new();
    private int _toastVersion;

    private bool _isExitRequested;
    private bool _trayCloseHandlingEnabled;

    public MainWindow(bool startHidden = false)
    {
        StartupTrace.Mark("MainWindow ctor begin");
        var windowHandle = WindowHandle();
        _windowHost = new WindowsWindowHost(windowHandle, Activate);
        _filePicker = new WindowsFileDialogService(windowHandle);
        _uiDispatcher = new DispatcherQueueUiDispatcher(DispatcherQueue);
        _updateCheckCoordinator = new UpdateCheckCoordinator(
            _updateService,
            _uiDispatcher,
            ShowUpdatePromptAsync,
            StartupTrace.Mark);
        _trayService = new WindowsTrayService(
            windowHandle,
            _uiDispatcher,
            () => _trayCloseHandlingEnabled && !_isExitRequested && _session.State.CloseBehavior != CloseBehaviorIds.Exit,
            () => L("Tray_OpenToolbox"),
            () => L("Tray_Exit"));
        _trayService.MinimizeRequested += MinimizeToolboxToTray;
        _trayService.RestoreRequested += RestoreToolboxFromTray;
        _trayService.ExitRequested += ExitToolboxFromTray;
        if (startHidden)
        {
            _windowHost.Hide();
            StartupTrace.Mark("Native window hidden before initialization");
        }

        StartupTrace.Mark("Settings loaded and normalized by ToolboxSession");

        _theme = new ToolboxThemePalette(() => _session.State);
        _toolLauncher = new ToolLaunchCoordinator(
            _processLauncher, L, ShowInfo, SelectTool,
            ShowQuickCaptureDialogAsync, ShowSettingsDialogAsync,
            ShowToolboxHome, MinimizeToolboxToTray);
        _updateInteraction = new UpdateInteractionCoordinator(
            _updateService, _processLauncher, () => _session.State, () => _root.XamlRoot,
            L, ThemeBrush, _session.DisableUpdateChecks, _session.IgnoreUpdate, ShowInfo, ExitForUpdate);
        _settingsDialog = new ToolboxSettingsDialog(
            _autoStartService, () => _session.State, () => _root.XamlRoot,
            L, ThemeBrush, ApplySettingsAndRebuild, ShowInfo);
        _profileDialog = new ToolboxProfileDialog(
            () => _session.State, () => _root.XamlRoot, L, ThemeBrush,
            AvatarView, PickAvatarImagePathAsync, SaveProfileAndRebuild, ShowInfo);
        _controls = new ToolboxControlFactory(ThemeBrush, StatusBrush);
        _headerActions = new ToolboxHeaderActionsBuilder(
            L, ThemeBrush, _controls, SortLabel, () => (int)_presentation.SortMode,
            mode => SetSortMode((ToolSortMode)mode));
        _toolCards = new ToolboxToolCardFactory(
            L, ThemeBrush, () => _presentation.SelectedTool.Id, _controls.IconTile, CanPinTool, IsPinnedTool,
            PinActionLabel, TogglePinnedTool, HandleToolCardClickAsync, SelectTool, ExecutePrimaryActionAsync);
        _detailPresenter = new ToolboxDetailPresenter(
            L, ThemeBrush, () => _presentation.SelectedTool, () => _presentation.HasVisibleTools, () => _presentation.SearchText,
            () => _session.State.Captures.Length, _controls.IconTile, _toolCards.StatusPill, CanPinTool, PinActionLabel,
            TogglePinnedTool, ExecutePrimaryActionAsync, CategoryNameKey, ClearSearch);
        _shellBuilder = new ToolboxShellBuilder(
            this, () => _session.State, ResolveTheme, L, ThemeBrush, _controls,
            () => _presentation.SelectedCategoryId, () => _presentation.SearchText, () => _presentation.ViewMode == ToolViewMode.Grid,
            CategoryNameKey, BuildDetailPanel, BuildEngineStatusButton, BuildSortButton, AvatarView,
            value => { _presentation.SearchText = value; RefreshToolGrid(); }, ClearSearch, HandleCategorySelection,
            ToggleSidebar, () => SetViewMode(ToolViewMode.Grid), () => SetViewMode(ToolViewMode.List),
            ShowSettingsDialogAsync, ShowProfileDialogAsync);
        _quickCaptureDialog = new ToolboxQuickCaptureDialog(() => _root.XamlRoot, L, ThemeBrush);
        _toolGridPresenter = new ToolboxToolGridPresenter(
            () => _toolGrid, () => _resultSummary, () => _detailPanel, CurrentTools,
            () => _presentation.SelectedTool, tool => _presentation.SelectedTool = tool, visible => _presentation.HasVisibleTools = visible,
            () => _presentation.ViewMode == ToolViewMode.Grid, BuildToolCard, BuildToolListItem,
            BuildCurrentDetailContent, L, ThemeBrush);
        _uiState = new ToolboxUiStateController(
            _session, () => _session.State, _loc, _presentation,
            () => _root, () => _searchBox,
            SelectFirstCurrentTool, RefreshToolGrid, BuildShell);
        _trayCloseHandlingEnabled = true;
        _loc.SetLanguage(_session.State.Language);
        StartupTrace.Mark("Localization loaded");
        SelectFirstCurrentTool();
        ConfigureWindow();
        StartupTrace.Mark("Window configured");
        BuildShell();
        StartupTrace.Mark("Shell built");
        Closed += (_, _) =>
        {
            _updateCheckCoordinator.Cancel();
            _trayService.Dispose();
        };
    }

    private string L(string key) => _loc.Get(key);

    public void QueueStartupUpdateCheck()
    {
        if (!_session.State.UpdateCheckEnabled)
        {
            StartupTrace.Mark("Update check disabled");
            return;
        }

        if (!_updateCheckCoordinator.Start())
        {
            StartupTrace.Mark("Update check already queued");
        }
    }

    private void ConfigureWindow()
    {
        Title = "Fowan";

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var scale = Math.Clamp(NativeWindowMethods.GetDpiForWindow(hwnd) / 96.0, 1.0, 3.0);
            var width = (int)Math.Round(1440 * scale);
            var height = (int)Math.Round(880 * scale);
            appWindow.Resize(new SizeInt32(width, height));

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

        }
        catch
        {
            // Window decoration APIs can fail in design-time or restricted hosts; the app shell still works.
        }
    }

    private void BuildShell()
    {
        var shell = _shellBuilder.Build();
        _root = shell.Root;
        _categoryPanel = shell.CategoryPanel;
        _toolGrid = shell.ToolGrid;
        _detailPanel = shell.DetailPanel;
        _toastHost = shell.ToastHost;
        _toastText = shell.ToastText;
        _searchBox = shell.SearchBox;
        _pageTitle = shell.PageTitle;
        _resultSummary = shell.ResultSummary;
        RefreshToolGrid();
        RegisterShellKeyboardAccelerators();
    }

    private Ellipse AvatarView(double size, string? avatarPath = null) => new()
    {
        Width = size,
        Height = size,
        Fill = new ImageBrush
        {
            ImageSource = new BitmapImage(FileUri(AvatarStore.Resolve(avatarPath ?? _session.State.AvatarPath))),
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        },
        Stroke = ThemeBrush("CardStrokeColorDefaultBrush"),
        StrokeThickness = 1
    };

    private void HandleCategorySelection(string categoryId)
    {
        if (categoryId is "settings" or "diagnostics")
        {
            SelectTool(ToolCatalog.Tools.First(tool => tool.Id == categoryId));
            return;
        }
        _presentation.SelectedCategoryId = categoryId;
        SelectFirstCurrentTool();
        RefreshCategories();
        RefreshToolGrid();
    }
    private Border BuildDetailPanel() => _detailPresenter.BuildPanel();
    private UIElement BuildCurrentDetailContent() => _detailPresenter.BuildCurrent();

    private void RefreshCategories()
    {
        _categoryPanel.Children.Clear();
        foreach (var category in ToolCatalog.Categories)
        {
            _categoryPanel.Children.Add(_shellBuilder.CategoryButton(category));
        }
        _pageTitle.Text = L(CategoryNameKey(_presentation.SelectedCategoryId));
    }

    private void RefreshToolGrid() => _toolGridPresenter.Refresh();

    private IEnumerable<ToolCard> CurrentTools()
    {
        var query = _presentation.SearchText;
        var tools = ToolCatalog.Tools
            .Where(tool => tool.Id != "toolbox-home")
            .Where(tool => _presentation.SelectedCategoryId == "all" || tool.CategoryId == _presentation.SelectedCategoryId)
            .Where(tool =>
                string.IsNullOrWhiteSpace(query) ||
                L(tool.NameKey).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                L(tool.DescriptionKey).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                L(CategoryNameKey(tool.CategoryId)).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                StatusText(tool.Status).Contains(query, StringComparison.CurrentCultureIgnoreCase));

        return _presentation.SortMode switch
        {
            ToolSortMode.Status => tools
                .OrderBy(AvailabilitySortKey)
                .ThenBy(PinnedSortKey)
                .ThenBy(tool => tool.Status)
                .ThenBy(tool => L(tool.NameKey), StringComparer.CurrentCultureIgnoreCase),
            ToolSortMode.Category => tools
                .OrderBy(AvailabilitySortKey)
                .ThenBy(PinnedSortKey)
                .ThenBy(tool => L(CategoryNameKey(tool.CategoryId)), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(tool => L(tool.NameKey), StringComparer.CurrentCultureIgnoreCase),
            _ => tools
                .OrderBy(AvailabilitySortKey)
                .ThenBy(PinnedSortKey)
                .ThenBy(tool => L(tool.NameKey), StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private void SelectFirstCurrentTool()
    {
        var firstTool = CurrentTools().FirstOrDefault();
        if (firstTool is not null)
        {
            _presentation.SelectedTool = firstTool;
        }
    }

    private FrameworkElement BuildToolCard(ToolCard tool) => _toolCards.BuildCard(tool);
    private FrameworkElement BuildToolListItem(ToolCard tool) => _toolCards.BuildListItem(tool);

    private static int AvailabilitySortKey(ToolCard tool)
    {
        return tool.Status == ToolStatus.Available ? 0 : 1;
    }

    private int PinnedSortKey(ToolCard tool)
    {
        if (!CanPinTool(tool))
        {
            return int.MaxValue;
        }

        var index = _session.State.PinnedToolIds.IndexOf(tool.Id);
        return index >= 0 ? index : int.MaxValue;
    }

    private bool CanPinTool(ToolCard tool)
    {
        return tool.Status == ToolStatus.Available;
    }

    private bool IsPinnedTool(ToolCard tool)
    {
        return CanPinTool(tool) && _session.State.PinnedToolIds.Contains(tool.Id);
    }

    private string PinActionLabel(ToolCard tool)
    {
        if (!CanPinTool(tool))
        {
            return L("Action_PinUnavailable");
        }

        return IsPinnedTool(tool) ? L("Action_UnpinFromTop") : L("Action_PinToTop");
    }

    private void TogglePinnedTool(ToolCard tool)
    {
        if (!CanPinTool(tool))
        {
            return;
        }

        _session.TogglePinned(tool.Id);

        RefreshToolGrid();
    }

    private SolidColorBrush StatusBrush(ToolStatus status) => ToolboxToolCardFactory.StatusBrush(status);
    private string StatusText(ToolStatus status) => _toolCards.StatusText(status);

    private void SelectTool(ToolCard tool)
    {
        if (_presentation.SelectedTool.Id == tool.Id)
        {
            return;
        }

        _presentation.SelectedTool = tool;
        RefreshToolGrid();
        _detailPanel.Child = BuildCurrentDetailContent();
    }

    private Task HandleToolCardClickAsync(ToolCard tool) => _toolLauncher.HandleClickAsync(tool);
    private Task ExecutePrimaryActionAsync(ToolCard tool) => _toolLauncher.ExecuteAsync(tool);

    private void ShowToolboxHome()
    {
        _presentation.SelectedCategoryId = "all";
        SelectFirstCurrentTool();
        RefreshCategories();
        RefreshToolGrid();
    }

    private Task ShowUpdatePromptAsync(UpdateInfo update) => _updateInteraction.ShowPromptAsync(update);

    private void ExitForUpdate()
    {
        _isExitRequested = true;
        _trayService.Dispose();
        Close();
        global::Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private void MinimizeToolboxToTray()
    {
        var result = TrayVisibilityCoordinator.TryHide(_trayService.EnsureVisible, _windowHost.Hide);
        if (!result.Succeeded)
        {
            ShowInfo(result.Error ?? "Tray initialization failed.", InfoBarSeverity.Error);
        }
    }

    private void RestoreToolboxFromTray()
    {
        _windowHost.RestoreAndActivate();
        QueueStartupUpdateCheck();
    }

    internal void InitializeHiddenToTray()
    {
        var result = TrayVisibilityCoordinator.TryHide(_trayService.EnsureVisible, _windowHost.Hide);
        if (!result.Succeeded)
        {
            StartupTrace.Mark($"Tray initialization failed: {result.Error}");
            _windowHost.RestoreAndActivate();
        }
    }

    internal void RestoreFromExternalActivation()
    {
        RestoreToolboxFromTray();
    }

    private void ExitToolboxFromTray()
    {
        _isExitRequested = true;
        _trayService.Dispose();
        Close();
    }

    private void ToggleSidebar() => _uiState.ToggleSidebar();
    private void ClearSearch() => _uiState.ClearSearch();
    private void SetViewMode(ToolViewMode viewMode) => _uiState.ChangeViewMode((int)viewMode);
    private void SetSortMode(ToolSortMode sortMode) => _uiState.ChangeSortMode((int)sortMode);
    private void ApplySettingsAndRebuild(ToolboxSettingsSelection selection) => _uiState.ApplySettings(selection);
    private void SaveProfileAndRebuild(string displayName, string avatarPath)
    {
        _session.UpdateProfile(displayName, AvatarStore.Save(avatarPath));
        BuildShell();
    }
    private void RegisterShellKeyboardAccelerators() => _uiState.RegisterKeyboardAccelerators();

    private async Task<string?> PickAvatarImagePathAsync()
    {
        return await _filePicker.PickOpenFileAsync(new FileOpenRequest(AvatarStore.ImageExtensions));
    }

    private async Task ShowQuickCaptureDialogAsync()
    {
        var capture = await _quickCaptureDialog.ShowAsync();
        if (capture is null) return;
        _session.AddCapture(capture);
        ShowInfo(L("QuickCapture_Saved"), InfoBarSeverity.Success);
        _detailPanel.Child = BuildCurrentDetailContent();
    }

    private Task ShowProfileDialogAsync() => _profileDialog.ShowAsync();

    private Task ShowSettingsDialogAsync() => _settingsDialog.ShowAsync();

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        _toastText.Text = message;
        _toastHost.Visibility = Visibility.Visible;
        _toastHost.Background = severity switch
        {
            InfoBarSeverity.Error => new SolidColorBrush(ThemeColor("ToastErrorBackground")),
            InfoBarSeverity.Warning => new SolidColorBrush(ThemeColor("ToastWarningBackground")),
            _ => new SolidColorBrush(ThemeColor("ToastBackground"))
        };

        var currentToast = ++_toastVersion;
        _ = Task.Run(async () =>
        {
            await Task.Delay(2800);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (currentToast == _toastVersion)
                {
                    _toastHost.Visibility = Visibility.Collapsed;
                }
            });
        });
    }

    private ElementTheme ResolveTheme() => _session.State.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private string CategoryNameKey(string categoryId)
    {
        if (categoryId == "all")
        {
            return "Category_AllTools";
        }

        return ToolCatalog.Categories.FirstOrDefault(category => category.Id == categoryId)?.NameKey ?? "Category_AllTools";
    }


    private Button BuildEngineStatusButton() => _headerActions.BuildEngineStatusButton();
    private Button BuildSortButton() => _headerActions.BuildSortButton();

    private string SortLabel() => _presentation.SortMode switch
    {
        ToolSortMode.Status => L("Sort_Label_Status"),
        ToolSortMode.Category => L("Sort_Label_Category"),
        _ => L("Sort_Label_Name")
    };


    private static Uri FileUri(string path)
    {
        return new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Path = Path.GetFullPath(path)
        }.Uri;
    }

    private Brush ThemeBrush(string resourceKey) => _theme.Brush(resourceKey);
    private Color ThemeColor(string resourceKey) => _theme.Color(resourceKey);

    private IntPtr WindowHandle() => WinRT.Interop.WindowNative.GetWindowHandle(this);

}
