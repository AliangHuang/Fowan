using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoTaskAreaPresenter
{
    private readonly ITodoCommands _applicationCommands;
    private readonly Func<TodoSnapshot> _snapshot;
    private readonly TodoThemePalette _palette;
    private readonly TodoControlFactory _controls;
    private readonly TodoListColorPalette _listColors;
    private readonly TodoFilterController _filter;
    private readonly TodoTaskCommandCoordinator _commands;
    private readonly TodoCreationCoordinator _creation;
    private readonly Func<string> _currentView;
    private readonly Func<string?> _selectedTask;
    private readonly Action<string?> _selectTask;
    private readonly Func<TodoWindowQuery> _query;
    private readonly Action _saveSettings;
    private readonly Action _refreshAll;
    private readonly Action _refreshAfterMutation;
    private readonly TodoTaskDragController _drag;
    private Grid _root = new();
    private ScrollViewer _scroll = new();
    private StackPanel _content = new();
    private TextBlock _title = new();
    private TextBlock _summary = new();
    private Border _detailHost = new();
    private FrameworkElement? _activeSection;
    private FrameworkElement? _completedSection;
    private TodoTaskListView? _listView;

    public TodoTaskAreaPresenter(
        ITodoCommands applicationCommands,
        Func<TodoSnapshot> snapshot,
        TodoThemePalette palette,
        TodoControlFactory controls,
        TodoListColorPalette listColors,
        TodoFilterController filter,
        TodoTaskCommandCoordinator commands,
        TodoCreationCoordinator creation,
        Func<string> currentView,
        Func<string?> selectedTask,
        Action<string?> selectTask,
        Func<TodoWindowQuery> query,
        Action saveSettings,
        Action refreshAll,
        Action refreshAfterMutation)
    {
        _applicationCommands = applicationCommands;
        _snapshot = snapshot;
        _palette = palette;
        _controls = controls;
        _listColors = listColors;
        _filter = filter;
        _commands = commands;
        _creation = creation;
        _currentView = currentView;
        _selectedTask = selectedTask;
        _selectTask = selectTask;
        _query = query;
        _saveSettings = saveSettings;
        _refreshAll = refreshAll;
        _refreshAfterMutation = refreshAfterMutation;
        _drag = new TodoTaskDragController(
            new TodoTaskDropEnvironment(
                () => _root,
                () => _scroll,
                () => _listView?.Rows ?? Array.Empty<Button>(),
                () => _activeSection,
                () => _completedSection,
                () => Data(),
                FindTask,
                IsValidDrop),
            new TodoTaskDropPreviewPalette(palette.Accent, palette.AccentDark, palette.Brush(0xEEF9FA)),
            ApplyDrop);
    }

    public void Attach(Grid root, TodoTaskAreaParts area, Border detailHost)
    {
        _root = root;
        _scroll = area.Scroll;
        _content = area.Content;
        _title = area.Title;
        _summary = area.Summary;
        _detailHost = detailHost;
    }

    public void Refresh()
    {
        _drag.Cancel();
        _listView = CreateListView();
        _content.Children.Clear();
        _activeSection = null;
        _completedSection = null;
        var viewId = _currentView();
        _title.Text = _query().ViewTitle(viewId);
        if (viewId == TodoViewIds.RecycleBin)
        {
            ShowRecycleBin();
            return;
        }

        if (_query().IsFilteringActive)
        {
            var filtered = _query().FilteredNodes(viewId).ToList();
            _summary.Text = $"{filtered.Count} 项";
            AddRowsOrEmpty(
                filtered,
                "没有符合筛选条件的任务",
                completed: false,
                useTaskCompletion: true);
            return;
        }

        var active = _query().ActiveNodes(viewId).ToList();
        var completed = _query().CompletedNodes(viewId).ToList();
        if (viewId == TodoViewIds.Completed)
        {
            _summary.Text = $"{completed.Count} 项";
            AddRowsOrEmpty(completed, "还没有已完成任务", completed: true);
            return;
        }
        if (viewId == TodoViewIds.Uncompleted)
        {
            _summary.Text = $"{active.Count} 项";
            AddRowsOrEmpty(active, "还没有未完成任务", completed: false);
            return;
        }
        _summary.Text = $"{(viewId == TodoViewIds.All ? active.Count + completed.Count : active.Count)} 项";
        var section = new StackPanel { Spacing = 8 };
        _activeSection = section;
        AddRowsOrEmpty(active, "当前没有待办任务", completed: false, section);
        _content.Children.Add(section);
        _completedSection = (FrameworkElement)_listView!.CompletedSection(completed);
        _content.Children.Add(_completedSection);
    }

    public void RefreshDetail()
    {
        var detail = CreateDetailView();
        if (_currentView() == TodoViewIds.RecycleBin)
        {
            _detailHost.Child = detail.Empty();
            return;
        }
        var task = _query().SelectedTask(_selectedTask(), _currentView());
        _selectTask(task?.Id);
        _detailHost.Child = task is null ? detail.Empty() : DetailContent(detail, task);
    }

    private void ShowRecycleBin()
    {
        var deleted = TodoQuery.RecycleBinTaskNodes(
            Data(),
            new HashSet<string>(_snapshot().Settings.CollapsedTaskIds, StringComparer.Ordinal),
            _filter.MaximumDepth).ToList();
        _summary.Text = $"{deleted.Count} 项";
        AddRowsOrEmpty(deleted, "回收站为空", completed: false, recycleBin: true);
    }

    private void AddRowsOrEmpty(
        IReadOnlyList<TodoTaskNode> nodes,
        string emptyText,
        bool completed,
        Panel? target = null,
        bool recycleBin = false,
        bool useTaskCompletion = false)
    {
        target ??= _content;
        if (nodes.Count == 0)
        {
            target.Children.Add(_controls.EmptyState(emptyText));
            return;
        }
        foreach (var node in nodes)
        {
            target.Children.Add(_listView!.Row(
                node.Task,
                recycleBin || useTaskCompletion ? node.Task.IsCompleted : completed,
                node.Depth,
                recycleBin));
        }
    }

    private TodoTaskListView CreateListView() => new(
        Data(),
        _snapshot().Settings.IsCompletedExpanded,
        _selectedTask(),
        new TodoTaskListPalette(
            TodoThemePalette.Transparent,
            _palette.Text,
            _palette.SecondaryText,
            _palette.Muted,
            _palette.Accent,
            _palette.Brush(0xE1EAED),
            _palette.Brush(0xFFFFFF),
            _palette.Brush(0xFBFCFC),
            _palette.Brush(0xEEF9FA),
            _palette.Brush(0xBFDCCB),
            _palette.Brush(0xEEF8F1),
            _palette.Brush(0xF06423),
            _palette.Brush(0xB42318),
            _palette.TaskHoverBorder,
            _palette.TaskHoverBackground),
        new TodoTaskListActions(
            TreeToggleButton,
            task => _controls.TaskCheckButton(task, () => _commands.ToggleCompletedAsync(task)),
            TaskListPill,
            TodoWindowQuery.TaskTimeText,
            _controls.RowIconButton,
            _controls.HeaderActionButton,
            _drag.Attach,
            _drag.TryConsumeSuppressedClick,
            Select,
            _creation.ShowAddSubtaskAsync,
            _commands.DeleteAsync,
            _commands.RestoreTree,
            _commands.PermanentlyDeleteTreeAsync,
            _commands.RestoreCompleted,
            _commands.ToggleImportant,
            ToggleCompletedExpanded,
            RestoreCompleted,
            ClearCompleted));

    private TodoTaskDetailView CreateDetailView() => new(
        new TodoTaskDetailPalette(
            TodoThemePalette.Transparent,
            _palette.Text,
            _palette.SecondaryText,
            _palette.Brush(0xF2B01E),
            _palette.Brush(0x138A43),
            _palette.Brush(0xDCE7EA),
            _palette.Brush(0xE7EEF0),
            _palette.Brush(0xFFFFFF)),
        new TodoTaskDetailControls(
            _controls.ApplyFlatTextBoxStyle,
            _controls.RowIconButton,
            _controls.PillButton,
            _controls.PrimaryButton,
            _controls.IconOnlyButton,
            _controls.DangerButton));

    private UIElement DetailContent(TodoTaskDetailView detail, TodoTask task)
    {
        var actions = new TodoTaskDetailActions(
            title => _commands.UpdateTitle(task, title),
            () => _commands.ToggleImportant(task),
            CloseDetail,
            completedAt => RefreshIf(_applicationCommands.UpdateTaskCompletionTime(task.Id, completedAt)),
            listId => RefreshIf(_applicationCommands.MoveTaskToList(task.Id, listId)),
            startDate => RefreshIf(_applicationCommands.UpdateTaskStartDate(task.Id, startDate)),
            dueDate => RefreshIf(_applicationCommands.UpdateTaskDueDate(task.Id, dueDate)),
            notes => _applicationCommands.UpdateTaskNotes(task.Id, notes),
            () => _creation.ShowAddSubtaskAsync(task),
            () => _commands.ToggleCompletedAsync(task),
            () => _commands.DeleteAsync(task));
        return detail.Build(
            task,
            _query().OrderedLists().ToList(),
            TodoQuery.AddChildBlockedReason(Data(), task),
            actions);
    }

    private void Select(TodoTask task)
    {
        _selectTask(task.Id);
        _saveSettings();
        Refresh();
        RefreshDetail();
    }

    private void CloseDetail()
    {
        _selectTask(null);
        _saveSettings();
        Refresh();
        RefreshDetail();
    }

    private Button TreeToggleButton(TodoTask task) => _controls.TreeToggleButton(
        _snapshot().Settings.CollapsedTaskIds.Contains(task.Id),
        () => ToggleCollapsed(task.Id));

    private void ToggleCollapsed(string taskId)
    {
        _applicationCommands.ToggleTaskCollapsed(taskId);
        Refresh();
    }

    private Border TaskListPill(string listId) => _controls.TaskListPill(
        Data().Lists.FirstOrDefault(list => list.Id == listId)?.Name ?? "任务清单",
        _listColors.Background(Data(), listId),
        _listColors.Foreground(Data(), listId));

    private void ToggleCompletedExpanded()
    {
        _applicationCommands.ToggleCompletedExpanded();
        Refresh();
    }

    private void RestoreCompleted()
    {
        _applicationCommands.RestoreCompleted(_query().CompletedTasks(_currentView()).Select(task => task.Id));
        _refreshAfterMutation();
    }

    private void ClearCompleted()
    {
        _applicationCommands.DeleteTaskTrees(_query().CompletedTasks(_currentView())
            .Where(task => task.DeletedAt is null)
            .Select(task => task.Id));
        _selectTask(null);
        _refreshAfterMutation();
    }

    private void ApplyDrop(TodoTask draggedTask, TodoDropTarget target)
    {
        if (!_applicationCommands.TryApplyTaskDrop(draggedTask.Id, target.TaskId, Placement(target.Placement))) return;
        _selectTask(draggedTask.Id);
        _refreshAll();
    }

    private bool IsValidDrop(TodoTask draggedTask, TodoDropTarget target) =>
        _applicationCommands.CanApplyTaskDrop(draggedTask.Id, target.TaskId, Placement(target.Placement));

    private TodoTask? FindTask(string taskId) => Data().Tasks.FirstOrDefault(task =>
        string.Equals(task.Id, taskId, StringComparison.Ordinal));

    private TodoData Data() => _snapshot().ToQueryData();

    private void RefreshIf(bool changed)
    {
        if (changed) _refreshAfterMutation();
    }

    private static TodoTaskDropPlacement Placement(TodoDropPlacement placement) => placement switch
    {
        TodoDropPlacement.Before => TodoTaskDropPlacement.Before,
        TodoDropPlacement.Child => TodoTaskDropPlacement.Child,
        TodoDropPlacement.After => TodoTaskDropPlacement.After,
        _ => TodoTaskDropPlacement.TopLevelEnd
    };
}
