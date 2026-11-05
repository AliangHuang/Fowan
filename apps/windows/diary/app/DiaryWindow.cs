using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Services;
using Fowan.Diary.Windows.Platform.Windows;
using Fowan.Diary.Windows.Presentation;
using Fowan.Diary.Windows.Coordination;
using Fowan.Diary.Windows.Application;
using Fowan.Windows.Platform.Contracts;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Fowan.Diary.Windows;

public sealed class DiaryWindow : Window
{
    private const int DesignWindowWidth = 1680;
    private const int DesignWindowHeight = 940;
    private const double SidebarWidth = 274;
    private const double DetailWidth = 526;
    private const double CompactSidebarWidth = 128;
    private const double CompactDetailWidth = 190;
    private const double TimelineNavigatorWidth = 360;
    private const string TimelineRangeAll = DiaryTimelineStateController.RangeAll;
    private const string TimelineRangeToday = DiaryTimelineStateController.RangeToday;
    private const string TimelineRangeWeek = DiaryTimelineStateController.RangeWeek;
    private const string TimelineRangeMonth = DiaryTimelineStateController.RangeMonth;
    private const string TimelineRangeYear = DiaryTimelineStateController.RangeYear;

    private readonly DiaryWorkspace _workspace;
    private readonly DiaryWindowCommands _commands;
    private readonly DiaryPresentationState _presentation = new();
    private readonly IFileDialogService _filePicker;
    private readonly DiaryTimelineStateController _timeline = new(DiaryRuntime.Today);
    private readonly IDiaryReverseGeocoder _reverseGeocoder = new NominatimReverseGeocoder();
    private readonly IDiaryWeatherProvider _weatherProvider = new OpenMeteoWeatherProvider();
    private readonly DiaryThemePalette _theme;
    private readonly DiaryUiFactory _ui;
    private readonly DiaryTimelinePresenter _timelinePresenter;
    private readonly DiaryComposerPresenter _composerPresenter;
    private readonly DiaryEntryListPresenter _entryListPresenter;
    private readonly DiaryDetailPresenter _detailPresenter;
    private readonly DiarySidebarPresenter _sidebarPresenter;
    private readonly DiaryMainPresenter _mainPresenter;
    private readonly DiaryEnvironmentAcquisitionCoordinator _environmentAcquisition;
    private readonly DiarySettingsDialogCoordinator _settingsDialogs;
    private readonly DiaryTagDialogCoordinator _tagDialogs;
    private readonly DiaryEntryInteractionCoordinator _entryInteractions;

    private string _timelineRangeId => _timeline.RangeId;
    private DateTime _timelineAnchorDate => _timeline.AnchorDate;
    private DateTime _timelineNavigatorMonth => _timeline.NavigatorMonth;
    private DateTime? _timelineDateFilter => _timeline.DateFilter;
    private DateTime? _pendingTimelineScrollDate => _timeline.PendingScrollDate;
    private Grid _root = new();
    private readonly Dictionary<DateTime, FrameworkElement> _timelineDateAnchors = [];
    private bool _buildingShell;

    public DiaryWindow()
    {
        _filePicker = new WindowsFileDialogService(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _workspace = DiaryWorkspace.CreateDefault();
        _commands = new DiaryWindowCommands(_workspace);
        _commands.Reload();
        _theme = new DiaryThemePalette(_workspace.QuerySettings, _workspace.QueryData);
        _ui = new DiaryUiFactory(_theme);
        _composerPresenter = new DiaryComposerPresenter(
            _workspace.QueryData, _workspace.QuerySettings, _workspace, _theme, _ui, EnsureDraft, () => _presentation.DraftEntry(_workspace.QueryData()), () => _buildingShell,
            BuildShell, EnsureSelectedEntryVisible, AcquireWeatherAsync,
            AcquireLocationAsync, ShowSettingsDialogAsync, AddImageAttachmentAsync, ShowTagPickerAsync,
            ShowTemplateMenu, ShowSearchDialogAsync, SaveDraft);
        _entryListPresenter = new DiaryEntryListPresenter(
            _workspace.QueryData, () => IsTimelineView, () => TimelineNotebookId, () => _presentation.SelectedEntry(_workspace.QueryData()),
            _presentation.Select, _timelineDateAnchors, _theme, _ui, ShowEntryEditorAsync,
            ToggleFavorite, ShowEntryMenu, BuildShell);
        _detailPresenter = new DiaryDetailPresenter(
            _workspace.QueryData, _workspace.QuerySettings, () => _presentation.SelectedEntry(_workspace.QueryData()), _presentation.Select, _theme, _ui,
            ShowTagPickerAsync, ToggleFavorite, ShowEntryMenu, ShowEntryEditorAsync, ShowTodoPickerAsync,
            SelectView, ExportEntryAsync, DeleteEntryAsync, BuildShell);
        _sidebarPresenter = new DiarySidebarPresenter(
            _workspace.QueryData, _workspace.QuerySettings, _theme, _ui, SetTitleBar, SelectView,
            ShowNotebookMenu, ShowSettingsDialogAsync, ShowHelpDialogAsync);
        _mainPresenter = new DiaryMainPresenter(
            _workspace.QueryData, _workspace.QuerySettings, _theme, _ui, _composerPresenter, _entryListPresenter,
            _detailPresenter, FilteredEntries, PageTitle, PageListTitle, BuildTimelineNotebookSelector,
            BeginDraft, ShowHeaderMenu, ShowCreateTagDialogAsync, ShowEditTagDialogAsync);
        _timelinePresenter = new DiaryTimelinePresenter(
            _workspace.QueryData, _workspace.QuerySettings, _timeline, _theme, _ui, _entryListPresenter.Build, PageListTitle,
            ShowSearchDialogAsync, CreateTimelineEntryAsync, EnsureSelectedEntryVisible, PersistPreferences, BuildShell);
        _environmentAcquisition = new DiaryEnvironmentAcquisitionCoordinator(
            _reverseGeocoder, _weatherProvider, _workspace.QuerySettings, () => _root.XamlRoot,
            PersistPreferences, _composerPresenter.SetQuickLocation, _composerPresenter.SetQuickWeather, ShowMessageAsync);
        _settingsDialogs = new DiarySettingsDialogCoordinator(
            () => _root.XamlRoot, _workspace.QueryData, _workspace.QuerySettings, _workspace, _theme,
            _environmentAcquisition, data => PersistDiary(silent: true, data), PersistPreferences,
            ApplyCaptionButtonColorsToCurrentWindow, BuildShell, ShowMessageAsync);
        _tagDialogs = new DiaryTagDialogCoordinator(
            () => _root.XamlRoot, _workspace.QueryData, _theme, data => PersistDiary(silent: true, data), BuildShell,
            () => _composerPresenter.TagFilter, value => _composerPresenter.TagFilter = value, ShowMessageAsync);
        _entryInteractions = new DiaryEntryInteractionCoordinator(
            () => _root.XamlRoot, _workspace.QueryData, _workspace, _filePicker, _theme, EnsureDraft,
            data => PersistDiary(silent: true, data), BuildShell, () => IsTimelineView, NavigateTimelineToEntry,
            () => _presentation.SelectedEntry(_workspace.QueryData()), _presentation.Select,
            () => _presentation.DraftEntry(_workspace.QueryData()), _presentation.SetDraft,
            FilteredEntries, ShowMessageAsync);
        NormalizeTimelineNotebookSelection();
        InitializeTimelineSessionState();
        _presentation.Restore(_workspace.QueryData());
        ConfigureWindow();
        BuildShell();
    }

    internal void ActivateInitialMode() => Activate();

    internal void RestoreFromExternalActivation() => Activate();

    private void ConfigureWindow()
    {
        Title = "Fowan Diary";
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            var workArea = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd), DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min(workArea.Width, DesignWindowWidth);
            var height = Math.Min(workArea.Height, DesignWindowHeight);
            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(workArea.X + Math.Max(0, (workArea.Width - width) / 2), workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
            ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            ApplyCaptionButtonColors(appWindow);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // The diary remains usable if window decoration APIs are unavailable.
        }
    }

    private void ApplyCaptionButtonColors(AppWindow appWindow)
    {
        var foreground = IsDarkTheme() ? Colors.White : Colors.Black;
        appWindow.TitleBar.ButtonForegroundColor = foreground;
        appWindow.TitleBar.ButtonInactiveForegroundColor = IsDarkTheme() ? ColorHelper.FromArgb(180, 255, 255, 255) : ColorHelper.FromArgb(180, 0, 0, 0);
        appWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        appWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        appWindow.TitleBar.ButtonHoverBackgroundColor = IsDarkTheme() ? ColorHelper.FromArgb(45, 255, 255, 255) : ColorHelper.FromArgb(28, 0, 0, 0);
        appWindow.TitleBar.ButtonPressedBackgroundColor = IsDarkTheme() ? ColorHelper.FromArgb(70, 255, 255, 255) : ColorHelper.FromArgb(45, 0, 0, 0);
    }

    private void ApplyCaptionButtonColorsToCurrentWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ApplyCaptionButtonColors(AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd)));
        }
        catch
        {
            // Theme changes do not depend on caption button support.
        }
    }

    private void BuildShell()
    {
        _buildingShell = true;
        var isTimeline = IsTimelineView;
        _timelineDateAnchors.Clear();
        _root = new Grid { RequestedTheme = ResolveElementTheme(), Background = Brush("AppBackground") };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!isTimeline)
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DetailWidth) });
        }
        _root.Loaded += (_, _) => ApplyResponsiveColumns(_root.ActualWidth);
        _root.SizeChanged += (_, args) => ApplyResponsiveColumns(args.NewSize.Width);
        _root.Children.Add(BuildSidebar());
        if (isTimeline)
        {
            var timeline = BuildTimelineWorkspace();
            Grid.SetColumn(timeline, 1);
            _root.Children.Add(timeline);
        }
        else
        {
            var main = BuildMainColumn();
            Grid.SetColumn(main, 1);
            _root.Children.Add(main);
            var detail = BuildDetailColumn();
            Grid.SetColumn(detail, 2);
            _root.Children.Add(detail);
        }
        Content = _root;
        _buildingShell = false;
        QueuePendingTimelineScroll();
    }

    private void ApplyResponsiveColumns(double width)
    {
        if (width <= 0 || _root.ColumnDefinitions.Count < 2)
        {
            return;
        }
        var layoutWidth = width / CurrentDpiScale();
        var sidebar = layoutWidth < 760 ? CompactSidebarWidth : layoutWidth < 1200 ? 206 : SidebarWidth;
        _root.ColumnDefinitions[0].Width = new GridLength(sidebar);
        if (IsTimelineView)
        {
            return;
        }
        var detail = layoutWidth < 760 ? CompactDetailWidth : layoutWidth < 1200 ? 332 : DetailWidth;
        var main = Math.Max(220, layoutWidth - sidebar - detail);
        if (sidebar + detail + main > layoutWidth)
        {
            var fixedBudget = Math.Max(0, layoutWidth - main);
            sidebar = Math.Max(120, Math.Min(sidebar, fixedBudget * 0.42));
            detail = Math.Max(180, Math.Min(detail, fixedBudget - sidebar));
        }
        _root.ColumnDefinitions[2].Width = new GridLength(detail);
    }

    private double CurrentDpiScale()
    {
        try
        {
            var dpi = NativeWindowMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            return dpi > 0 ? dpi / 96.0 : 1;
        }
        catch
        {
            return 1;
        }
    }

    private FrameworkElement BuildSidebar() => _sidebarPresenter.Build();

    private FrameworkElement BuildTimelineWorkspace() => _timelinePresenter.BuildWorkspace();

    private void NavigateTimelineToDate(DateTime month, DateTime date) => _timelinePresenter.NavigateToDate(month, date);
    private void NavigateTimelineToEntry(DiaryEntry entry)
    {
        _presentation.Select(entry);
        _workspace.SetTimelineNotebook(DiaryTimeline.AllNotebooksId);
        NavigateTimelineToDate(entry.CreatedAt.LocalDateTime.Date, entry.CreatedAt.LocalDateTime.Date);
    }

    private void InitializeTimelineSessionState()
    {
        _timeline.Initialize(
            Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_RANGE"),
            Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_ANCHOR"),
            Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_DATE"),
            Environment.GetEnvironmentVariable("FOWAN_DIARY_TIMELINE_NAVIGATOR_MONTH"));
    }

    private IReadOnlyList<DiaryEntry> TimelineEntries() => _timelinePresenter.Entries();

    private async Task CreateTimelineEntryAsync()
    {
        var draft = EnsureDraft();
        _presentation.Select(draft);
        await ShowEntryEditorAsync(draft);
    }

    private void QueuePendingTimelineScroll()
    {
        if (!IsTimelineView || _pendingTimelineScrollDate is null)
        {
            return;
        }
        _root.Loaded += (_, _) => DispatcherQueue.TryEnqueue(ScrollPendingTimelineAnchor);
        if (_root.IsLoaded)
        {
            DispatcherQueue.TryEnqueue(ScrollPendingTimelineAnchor);
        }
    }

    private void ScrollPendingTimelineAnchor()
    {
        if (_pendingTimelineScrollDate is not DateTime date || !_timelineDateAnchors.TryGetValue(date, out var anchor))
        {
            return;
        }
        _timeline.ClearPendingScroll();
        anchor.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.12 });
    }

    private bool IsTimelineView => string.Equals(_workspace.State.Settings.CurrentViewId, DiaryViewIds.Timeline, StringComparison.Ordinal);

    private FrameworkElement BuildMainColumn() => _mainPresenter.BuildColumn();

    private FrameworkElement BuildDetailColumn() => _detailPresenter.BuildColumn();

    private void BeginDraft(bool focusEditor)
    {
        EnsureDraft();
        BuildShell();
        if (focusEditor)
        {
            _composerPresenter.FocusEditor();
        }
    }

    private DiaryEntry EnsureDraft()
    {
        var existingDraft = _presentation.DraftEntry(_workspace.QueryData());
        if (existingDraft is not null)
        {
            return existingDraft;
        }
        var currentView = _workspace.State.Settings.CurrentViewId;
        var draftSnapshot = _workspace.EnsureDraft(new DiaryDraftInput(
            DiaryViewIds.IsNotebook(currentView)
                ? DiaryViewIds.NotebookId(currentView)
                : IsTimelineView && !string.Equals(TimelineNotebookId, DiaryTimeline.AllNotebooksId, StringComparison.Ordinal)
                    ? TimelineNotebookId
                    : _workspace.State.Notebooks[0].Id,
            _composerPresenter.ComposeMood, _composerPresenter.ComposeWeather, _composerPresenter.ComposeLocation));
        var draft = _workspace.QueryData().Entries.First(entry => entry.Id == draftSnapshot.Id);
        _presentation.SetDraft(draft);
        return draft;
    }

    private void SaveDraft()
    {
        var draft = _presentation.DraftEntry(_workspace.QueryData());
        if (draft is null || string.IsNullOrWhiteSpace(draft.Body))
        {
            return;
        }
        var result = _workspace.FinalizeDraft(draft.Id);
        if (!result.Succeeded) return;
        _presentation.Select(_workspace.QueryData().Entries.First(entry => entry.Id == draft.Id));
        _presentation.SetDraft(null);
        BuildShell();
    }

    private void SelectView(string viewId)
    {
        _workspace.SetCurrentView(viewId);
        if (!string.Equals(viewId, DiaryViewIds.Tags, StringComparison.Ordinal)) _composerPresenter.TagFilter = null;
        EnsureSelectedEntryVisible();
        BuildShell();
    }

    private ComboBox BuildTimelineNotebookSelector() => _timelinePresenter.BuildNotebookSelector();

    private void SelectEntry(string entryId)
    {
        var selected = _workspace.QueryData().Entries.FirstOrDefault(entry => string.Equals(entry.Id, entryId, StringComparison.Ordinal));
        _presentation.Select(selected);
        if (selected?.IsDraft == true) _presentation.SetDraft(selected);
        BuildShell();
    }

    private void ToggleFavorite(string entryId)
    {
        var result = _workspace.ToggleFavorite(entryId);
        if (!result.Succeeded) return;
        _presentation.Select(_workspace.QueryData().Entries.FirstOrDefault(candidate => candidate.Id == entryId));
        BuildShell();
    }

    private void EnsureSelectedEntryVisible()
    {
        var visible = FilteredEntries().ToList();
        var selected = _presentation.SelectedEntry(_workspace.QueryData());
        if (selected is null || visible.All(entry => entry.Id != selected.Id))
        {
            _presentation.Select(visible.FirstOrDefault() ?? _workspace.QueryData().Entries.Where(entry => !entry.IsDraft).OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault());
        }
    }

    private IEnumerable<DiaryEntry> FilteredEntries()
    {
        IEnumerable<DiaryEntry> entries = _workspace.QueryData().Entries;
        var view = _workspace.State.Settings.CurrentViewId;
        if (string.Equals(view, DiaryViewIds.Timeline, StringComparison.Ordinal))
        {
            return TimelineEntries();
        }
        entries = view switch
        {
            DiaryViewIds.Today => entries.Where(entry => entry.CreatedAt.LocalDateTime.Date == DiaryRuntime.Today),
            DiaryViewIds.Favorites => entries.Where(entry => entry.IsFavorite),
            DiaryViewIds.Drafts => entries.Where(entry => entry.IsDraft),
            DiaryViewIds.Calendar => _detailPresenter.FilterCalendar(entries),
            DiaryViewIds.Tags when !string.IsNullOrWhiteSpace(_composerPresenter.TagFilter) => entries.Where(entry => entry.Tags.Any(tag => string.Equals(tag, _composerPresenter.TagFilter, StringComparison.OrdinalIgnoreCase))),
            DiaryViewIds.Tags => entries.Where(entry => entry.Tags.Count > 0),
            _ when DiaryViewIds.IsNotebook(view) => entries.Where(entry => string.Equals(entry.NotebookId, DiaryViewIds.NotebookId(view), StringComparison.Ordinal)),
            _ => entries
        };
        return entries.OrderBy(entry => entry.CreatedAt);
    }

    private string PageTitle()
    {
        var view = _workspace.State.Settings.CurrentViewId;
        if (DiaryViewIds.IsNotebook(view)) return NotebookName(DiaryViewIds.NotebookId(view));
        return view switch
        {
            DiaryViewIds.Timeline => "时间线",
            DiaryViewIds.Calendar => "日历",
            DiaryViewIds.Tags => "标签",
            DiaryViewIds.Favorites => "收藏",
            DiaryViewIds.Drafts => "草稿",
            _ => "今天的日记"
        };
    }

    private string PageListTitle()
    {
        if (string.Equals(_workspace.State.Settings.CurrentViewId, DiaryViewIds.Today, StringComparison.Ordinal))
        {
            return "今天";
        }
        if (string.Equals(_workspace.State.Settings.CurrentViewId, DiaryViewIds.Timeline, StringComparison.Ordinal))
        {
            return string.Equals(TimelineNotebookId, DiaryTimeline.AllNotebooksId, StringComparison.Ordinal)
                ? "全部日记本"
                : NotebookName(TimelineNotebookId);
        }
        return PageTitle();
    }

    private string TimelineNotebookId => string.IsNullOrWhiteSpace(_workspace.State.Settings.TimelineNotebookId) ? DiaryTimeline.AllNotebooksId : _workspace.State.Settings.TimelineNotebookId;

    private void NormalizeTimelineNotebookSelection()
    {
        var normalizedNotebookId = DiaryTimeline.ResolveNotebookId(_workspace.QueryData(), TimelineNotebookId);
        if (string.Equals(_workspace.State.Settings.TimelineNotebookId, normalizedNotebookId, StringComparison.Ordinal))
        {
            return;
        }
        _workspace.SetTimelineNotebook(normalizedNotebookId);
    }

    private string NotebookName(string id) => _workspace.State.Notebooks.FirstOrDefault(notebook => string.Equals(notebook.Id, id, StringComparison.Ordinal))?.Name ?? _workspace.DefaultNotebookName;

    private Task ShowEntryEditorAsync(DiaryEntry entry) => _entryInteractions.ShowEntryEditorAsync(entry);

    private Task ShowTagPickerAsync(DiaryEntry entry) => _tagDialogs.ShowTagPickerAsync(entry);

    private Task ShowCreateTagDialogAsync() => _tagDialogs.ShowCreateTagDialogAsync();

    private Task ShowEditTagDialogAsync(DiaryTagDefinition tag) => _tagDialogs.ShowEditTagDialogAsync(tag);
    private Task AcquireLocationAsync() => _environmentAcquisition.AcquireLocationAsync();
    private Task AcquireWeatherAsync() => _environmentAcquisition.AcquireWeatherAsync();

    private Task AddImageAttachmentAsync() => _entryInteractions.AddImageAttachmentAsync();

    private void ShowTemplateMenu(Button anchor) => _entryInteractions.ShowTemplateMenu(anchor);

    private void ApplyTemplate(DiaryTemplate template) => _entryInteractions.ApplyTemplate(template);

    private Task ShowSearchDialogAsync(string initialQuery = "") => _entryInteractions.ShowSearchDialogAsync(initialQuery);

    private Task ShowTodoPickerAsync(DiaryEntry entry) => _entryInteractions.ShowTodoPickerAsync(entry);

    private Task DeleteEntryAsync(DiaryEntry entry) => _entryInteractions.DeleteEntryAsync(entry);

    private Task ExportEntryAsync(DiaryEntry entry) => _entryInteractions.ExportEntryAsync(entry);

    private void ShowHeaderMenu(Button anchor) => _entryInteractions.ShowHeaderMenu(anchor);

    private void ShowEntryMenu(Button anchor, DiaryEntry entry) => _entryInteractions.ShowEntryMenu(anchor, entry);

    private Task ShowAttachmentsDialogAsync(DiaryEntry? entry) => _entryInteractions.ShowAttachmentsDialogAsync(entry);
    private void ShowNotebookMenu(Button anchor) => _settingsDialogs.ShowNotebookMenu(anchor);

    private Task ShowCreateNotebookDialogAsync() => _settingsDialogs.ShowCreateNotebookDialogAsync();

    private Task ShowManageNotebooksDialogAsync() => _settingsDialogs.ShowManageNotebooksDialogAsync();

    private Task ShowSettingsDialogAsync() => _settingsDialogs.ShowSettingsDialogAsync();

    private Task ShowHelpDialogAsync() => _settingsDialogs.ShowHelpDialogAsync();
    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = title, Content = content, CloseButtonText = "知道了" };
        await dialog.ShowAsync();
    }

    private bool PersistDiary(bool silent, DiaryData? candidate = null)
    {
        var result = _commands.PersistDiary(candidate ?? _workspace.QueryData());
        if (result.Succeeded)
        {
            return true;
        }
        if (!silent)
        {
            _ = ShowMessageAsync(
                "保存失败",
                $"日记仍保留在当前窗口中，请检查本地数据目录。\n\n错误代码：{result.ErrorCode}");
        }
        return false;
    }

    private void PersistPreferences() => PersistPreferences(_workspace.QuerySettings());

    private void PersistPreferences(DiarySettings candidate)
    {
        var result = _commands.PersistPreferences(candidate);
        if (!result.Succeeded)
        {
            _ = ShowMessageAsync(
                "设置保存失败",
                $"设置仍保留在当前窗口中。\n\n错误代码：{result.ErrorCode}");
        }
    }

    private ElementTheme ResolveElementTheme() => _theme.ResolveElementTheme();
    private bool IsDarkTheme() => _theme.IsDark;
    private SolidColorBrush Brush(string key) => _theme.Brush(key);

}
