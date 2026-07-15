using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Windows.AppPorts;
using Fowan.Todo.Windows.Platform.Windows;
using Fowan.Todo.Windows.Presentation;
using Fowan.Todo.Windows.Application;
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
    private const double CompletedTimestampEditorWidth = 216;
    // Keep 20 DIP free after the timestamp editor so its right border and the
    // TimePicker minute column survive DPI rounding inside the detail panel.
    private const double DetailWidth = 433;

    private readonly TodoWorkspace _workspace = TodoWorkspace.CreateDefault();
    private TodoWorkspace _store => _workspace;
    private readonly TodoWindowCommands _commands;
    private readonly TodoPresentationState _presentation = new();
    private readonly TodoWindowChromeController _chrome;
    private readonly TodoNativeWindowController _nativeWindow;
    private readonly TodoDialogService _dialogs;
    private readonly TodoHelpPresenter _helpPresenter;
    private readonly TodoOnboardingCoordinator _onboarding;
    private readonly TodoListColorDialog _listColorDialog;
    private readonly TodoThemePalette _palette;
    private readonly TodoListColorPalette _listColors;
    private readonly TodoTaskCommandCoordinator _taskCommands;
    private readonly TodoCreationCoordinator _creation;
    private readonly TodoFilterController _filter;
    private readonly TodoNavigationPresenter _navigation;
    private readonly TodoTaskAreaPresenter _taskArea;
    private readonly TodoSettingsCoordinator _settingsCoordinator;
    private readonly TodoControlFactory _controls;
    private readonly IStickyProcessCoordinator _stickyProcesses = new WindowsStickyProcessCoordinator();

    private Grid _root = new();
    private StackPanel _navigationPanel = new();
    private StackPanel _listPanel = new();
    private FrameworkElement _brandArea = new Grid();
    private Button _helpButton = new();
    private Button _stickyModeButton = new();
    private TextBox _addTaskBox = new();
    private Button _filterButton = new();

    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _recycleBinMaintenanceTimer;

    public TodoWindow()
    {
        _chrome = new TodoWindowChromeController(this);
        _nativeWindow = new TodoNativeWindowController(this, IsDarkTheme);
        _commands = new TodoWindowCommands(_workspace);
        _workspace.Reload();
        _palette = new TodoThemePalette(IsDarkTheme);
        _listColors = new TodoListColorPalette(_palette);
        _controls = new TodoControlFactory(_palette);
        _dialogs = new TodoDialogService(
            () => _root.XamlRoot,
            ResolveElementTheme,
            _chrome.ShowDialogAsync,
            PillButton);
        _helpPresenter = new TodoHelpPresenter(_chrome.EnterModalSurface, _chrome.ExitModalSurface);
        _onboarding = new TodoOnboardingCoordinator(_chrome.EnterModalSurface, _chrome.ExitModalSurface);
        _listColorDialog = new TodoListColorDialog(
            () => _root.XamlRoot,
            ResolveElementTheme,
            _chrome.ShowDialogAsync,
            () => _palette.PaletteCardBorder);
        _filter = new TodoFilterController(_dialogs, _palette);
        _navigation = new TodoNavigationPresenter(_palette, _listColors);
        _taskCommands = new TodoTaskCommandCoordinator(
            _workspace,
            () => _workspace.State,
            () => _root.XamlRoot,
            ResolveElementTheme,
            _chrome.ShowDialogAsync,
            taskId => _presentation.SelectedTaskId = taskId,
            RefreshAll,
            RefreshAfterMutation,
            RefreshDetail);
        _creation = new TodoCreationCoordinator(
            _workspace,
            () => _workspace.State,
            _dialogs,
            () => _presentation.CurrentViewId,
            viewId => _presentation.CurrentViewId = viewId,
            taskId => _presentation.SelectedTaskId = taskId,
            OrderedLists,
            DefaultListIdForNewTask,
            IsDefaultList,
            listId => Query().TasksForList(listId),
            FirstTaskForSelection,
            () => _palette.SecondaryText,
            RefreshAfterMutation);
        _taskArea = new TodoTaskAreaPresenter(
            _workspace,
            () => _workspace.State,
            _palette,
            _controls,
            _listColors,
            _filter,
            _taskCommands,
            _creation,
            () => _presentation.CurrentViewId,
            () => _presentation.SelectedTaskId,
            taskId => _presentation.SelectedTaskId = taskId,
            Query,
            PersistNavigation,
            RefreshAll,
            RefreshAfterMutation);
        _settingsCoordinator = new TodoSettingsCoordinator(
            _dialogs,
            _onboarding,
            _palette,
            () => _workspace.State.Settings,
            () => _presentation.CurrentViewId,
            viewId => _presentation.CurrentViewId = viewId,
            taskId => _presentation.SelectedTaskId = taskId,
            FirstTaskForSelection,
            SetTheme,
            _commands.UpdateRecycleBinSettings,
            _commands.SetOnboardingCompleted,
            PurgeExpiredRecycleBin,
            BuildShell,
            RefreshAll,
            () => OpenStickyMode(),
            QueueMainOnboardingIfNeeded);
        PurgeExpiredRecycleBin();
        _presentation.Restore(_workspace.State.Settings, IsKnownView);
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            if (_workspace.State.Settings.Theme != TodoThemeIds.System)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                _nativeWindow.ApplyCaptionColors();
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
        if (_workspace.State.Settings.IsStickyModeEnabled)
        {
            OpenStickyMode(closeMainWindow: true);
            return;
        }

        Activate();
        QueueStickyPrewarm();
        DispatcherQueue.TryEnqueue(QueueMainOnboardingIfNeeded);
    }

    private void ConfigureWindow()
    {
        _nativeWindow.Configure("Fowan Todo", Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo.ico"));
        Closed += (_, _) => _stickyProcesses.TryShutdown();
        Activated += (_, _) =>
        {
            _chrome.QueueRegionUpdate();
            if (!_workspace.State.Settings.HasCompletedMainOnboarding && !_onboarding.IsShowing)
            {
                DispatcherQueue.TryEnqueue(QueueMainOnboardingIfNeeded);
            }
        };

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

        var shell = new TodoShellView(
            new TodoShellPalette(
                Brush(0x001B3D),
                Brush(0x0D2E5F),
                Brush(0x082B62),
                TodoThemePalette.PureWhite,
                Brush(0x173B70),
                Brush(0xD6E3F5),
                Brush(0xFFFFFF),
                Brush(0xDCE7EA),
                _palette.Text,
                _palette.SecondaryText,
                _palette.Accent,
                TodoThemePalette.Transparent),
            new TodoShellControls(_controls.SidebarIconButton, PillButton, IconOnlyButton, ApplyFlatTextBoxStyle),
            new TodoShellActions(
                _creation.ShowAddListAsync,
                ShowSettingsDialogAsync,
                ShowHelpDialogAsync,
                ShowFilterDialogAsync,
                () => OpenStickyMode(),
                () => _creation.AddFromInputAsync(_addTaskBox)),
            new Uri(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets", "fowan-todo-app-icon-256.png"))));
        var sidebar = shell.BuildSidebar();
        _brandArea = sidebar.BrandArea;
        _navigationPanel = sidebar.NavigationPanel;
        _listPanel = sidebar.ListPanel;
        _helpButton = sidebar.HelpButton;
        _root.Children.Add(sidebar.Root);

        var taskArea = shell.BuildTaskArea();
        _filterButton = taskArea.FilterButton;
        _stickyModeButton = taskArea.StickyModeButton;
        _addTaskBox = taskArea.AddTaskBox;
        RefreshFilterButtonState();
        Grid.SetColumn(taskArea.Root, 1);
        _root.Children.Add(taskArea.Root);

        var detailHost = new Border
        {
            Background = Brush(0xFFFFFF),
            BorderBrush = Brush(0xDCE7EA),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };
        Grid.SetColumn(detailHost, 2);
        _root.Children.Add(detailHost);
        _taskArea.Attach(_root, taskArea, detailHost);
        Content = _root;
        _chrome.SetLayout(_root, _brandArea, _filterButton, _stickyModeButton);
    }


    private void RefreshAll()
    {
        if (!IsKnownView(_presentation.CurrentViewId))
        {
            _presentation.CurrentViewId = TodoViewIds.Today;
        }

        RefreshNavigation();
        RefreshFilterButtonState();
        RefreshTaskContent();
        RefreshDetail();
    }

    private void RefreshNavigation()
    {
        _navigation.Refresh(
            _navigationPanel,
            _listPanel,
            _workspace.State.ToQueryData(),
            _workspace.State.ToQuerySettings(),
            _presentation.CurrentViewId,
            Query(),
            _navigation.Actions(
            NavigateToView,
            _controls.SidebarIconButton,
            ShowListColorDialogAsync,
            _creation.ShowRenameListAsync,
            _creation.ShowDeleteListAsync));
    }


    private void RefreshTaskContent()
    {
        _taskArea.Refresh();
    }


    private void RefreshDetail()
    {
        _taskArea.RefreshDetail();
    }
    private async Task ShowFilterDialogAsync()
    {
        await _filter.ShowAsync(
            OrderedLists(),
            () => _presentation.CurrentViewId,
            viewId => _presentation.CurrentViewId = viewId,
            taskId => _presentation.SelectedTaskId = taskId,
            FirstTaskForSelection,
            PersistNavigation,
            RefreshAll);
    }

    private void RefreshFilterButtonState()
    {
        _filter.StyleButton(_filterButton);
        _chrome.QueueRegionUpdate();
    }

    private async Task ShowSettingsDialogAsync()
    {
        await _settingsCoordinator.ShowAsync();
    }

    private async Task ShowHelpDialogAsync()
    {
        await _helpPresenter.ShowAsync(_root, IsDarkTheme());
    }

    private void QueueMainOnboardingIfNeeded()
    {
        _onboarding.Queue(
            () => _workspace.State.Settings,
            () => _root,
            () => _stickyModeButton,
            () => _helpButton,
            () => new TodoOnboardingPalette(
                    TodoThemePalette.Transparent,
                    _palette.Accent,
                    _palette.Text,
                    _palette.SecondaryText,
                    Brush(0xFFFFFF),
                    Brush(0xDCE7EA),
                    new SolidColorBrush(ColorHelper.FromArgb(190, 4, 18, 28))),
            new TodoOnboardingControls(PillButton, PrimaryButton, ConfigureStableSecondaryButtonStates),
            _chrome.WaitForLayoutAsync,
            () => _commands.SetOnboardingCompleted(true));
    }
    private void RefreshAfterMutation()
    {
        PersistNavigation();
        RefreshAll();
    }

    private void PersistNavigation()
    {
        _commands.PersistNavigation(_presentation.CurrentViewId, _presentation.SelectedTaskId);
    }

    private void SetTheme(string theme)
    {
        if (_workspace.State.Settings.Theme == theme)
        {
            return;
        }

        _commands.SetTheme(theme);
        _nativeWindow.ApplyCaptionColors();
        BuildShell();
        RefreshAll();
    }

    private void OpenStickyMode(bool closeMainWindow = true)
    {
        _commands.SetStickyModeEnabled(true);

        if (_stickyProcesses.TryShow())
        {
            if (closeMainWindow)
            {
                _nativeWindow.Hide();
            }
            else
            {
                _nativeWindow.Hide();
            }
            return;
        }

        _commands.SetStickyModeEnabled(false);
        _nativeWindow.Show();
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
        if (_workspace.State.Settings.IsStickyModeEnabled)
        {
            return;
        }

        _stickyProcesses.TryPrewarm();
    }

    private void ReloadDataAndSettings()
    {
        _commands.Reload();
        PurgeExpiredRecycleBin();
        _presentation.Restore(_workspace.State.Settings, IsKnownView);
    }

    private void NavigateToView(string viewId)
    {
        _filter.Clear();
        _presentation.CurrentViewId = viewId;
        _presentation.SelectedTaskId = FirstTaskForSelection(viewId)?.Id;
        PersistNavigation();
        RefreshAll();
    }

    private void PurgeExpiredRecycleBin()
    {
        _commands.PurgeExpiredRecycleBin();
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

    private TodoWindowQuery Query() => new(
        _workspace.State.ToQueryData(),
        _workspace.State.ToQuerySettings(),
        _filter.DateRange,
        _filter.ListId,
        _filter.MaximumDepth,
        _store.DefaultListId);

    private TodoTask? FirstTaskForSelection(string viewId) => Query().FirstForSelection(viewId);

    private string DefaultListIdForNewTask() => Query().DefaultListForNewTask(_presentation.CurrentViewId);

    private bool IsDefaultList(string listId) => Query().IsDefaultList(listId);

    private IEnumerable<TodoList> OrderedLists() => Query().OrderedLists();

    private bool IsKnownView(string viewId) => Query().IsKnownView(viewId);

    private void ChangeListColor(TodoList list, string colorId)
    {
        if (_workspace.SetListColor(list.Id, colorId)) RefreshAll();
    }


    private async Task ShowListColorDialogAsync(TodoList list)
    {
        var selected = await _listColorDialog.ShowAsync(list.Name, _listColors.Choices(list));
        if (selected is not null) ChangeListColor(list, selected);
    }

    private Button PillButton(string text, string glyph) => _controls.PillButton(text, glyph);
    private Button PrimaryButton(string text, string glyph) => _controls.PrimaryButton(text, glyph);
    private void ConfigureStableSecondaryButtonStates(Button button) =>
        _controls.ConfigureStableSecondaryButtonStates(button);
    private Button IconOnlyButton(string glyph, string label) => _controls.IconOnlyButton(glyph, label);
    private void ApplyFlatTextBoxStyle(TextBox textBox) => _controls.ApplyFlatTextBoxStyle(textBox);
    private ElementTheme ResolveElementTheme()
    {
        return _workspace.State.Settings.Theme switch
        {
            TodoThemeIds.Light => ElementTheme.Light,
            TodoThemeIds.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private bool IsDarkTheme()
    {
        return _workspace.State.Settings.Theme switch
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
            return global::Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }


    private SolidColorBrush Brush(uint rgb) => _palette.Brush(rgb);

    internal void RestoreFromExternalActivation()
    {
        ReloadDataAndSettings();
        var wasStickyModeEnabled = _workspace.State.Settings.IsStickyModeEnabled;
        _commands.SetStickyModeEnabled(false);
        _nativeWindow.ApplyCaptionColors();
        BuildShell();
        RefreshAll();

        _nativeWindow.RestoreAndForeground();
        if (wasStickyModeEnabled)
        {
            _stickyProcesses.TryShutdown();
        }

        QueueStickyPrewarm();
    }

}
