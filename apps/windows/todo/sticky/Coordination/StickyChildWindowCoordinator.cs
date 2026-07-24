using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Sticky.Windows.Presentation;
using System.Windows;
using System.Windows.Controls;

namespace Fowan.Todo.Sticky.Windows.Coordination;

internal sealed class StickyChildWindowCoordinator(
    StickyWindow owner,
    Func<TodoSettings> settings,
    Func<TodoData> data,
    Func<Border> dismissOverlay,
    Func<string, TodoTask?> findTask,
    Action<Window, bool> updatePointerState,
    Action<bool> updateMenuDismissOverlay)
{
    private StickyAdjustmentWindow? _adjustment;
    private StickyAddTaskWindow? _addTask;
    private StickyTaskDetailWindow? _taskDetail;
    private StickyConfirmWindow? _confirm;
    private bool _isSynchronizingPositions;
    private readonly HashSet<Window> _childWindows = [];

    public bool HasOpenWindow() =>
        _adjustment is { IsVisible: true } ||
        _addTask is { IsVisible: true } ||
        _taskDetail is { IsVisible: true } ||
        _confirm is { IsVisible: true };

    public void ToggleAdjustment()
    {
        if (_adjustment is { IsVisible: true })
        {
            _adjustment.Close();
            _adjustment = null;
            return;
        }
        CloseAll();
        _adjustment = new StickyAdjustmentWindow(owner);
        Register(_adjustment);
        _adjustment.Closed += (_, _) => { _adjustment = null; UpdateDismissOverlay(); };
        ApplyState(_adjustment, reposition: true);
        _adjustment.Show();
        UpdateDismissOverlay();
    }

    public void ShowAddTask(TodoTask? parent = null)
    {
        if (parent is null && _addTask is { IsVisible: true })
        {
            _addTask.Activate();
            _addTask.FocusTitleBox();
            return;
        }
        CloseAll();
        _addTask = new StickyAddTaskWindow(owner, data(), parent);
        Register(_addTask);
        _addTask.Closed += (_, _) => { _addTask = null; UpdateDismissOverlay(); };
        ApplyState(_addTask, reposition: true);
        _addTask.Show();
        UpdateDismissOverlay();
        _addTask.FocusTitleBox();
    }

    public void ShowAddSubtask(TodoTask parent)
    {
        if (TodoQuery.CanAddChild(data(), parent)) ShowAddTask(parent);
    }

    public void ShowTaskDetail(string taskId)
    {
        var task = findTask(taskId);
        if (task is null || task.DeletedAt is not null) return;
        if (_taskDetail is { IsVisible: true } detail)
        {
            if (string.Equals(detail.TaskId, taskId, StringComparison.Ordinal))
            {
                detail.Activate();
                detail.FocusTitleBox();
                return;
            }
            detail.Close();
        }
        CloseAll();
        _taskDetail = new StickyTaskDetailWindow(owner, task);
        Register(_taskDetail);
        _taskDetail.Closed += (_, _) => { _taskDetail = null; UpdateDismissOverlay(); };
        ApplyState(_taskDetail, reposition: true);
        _taskDetail.Show();
        UpdateDismissOverlay();
        _taskDetail.FocusTitleBox();
    }

    public void ShowConfirmation(string heading, string message, string confirmText, Action confirmAction)
    {
        CloseAll();
        _confirm = new StickyConfirmWindow(owner, heading, message, confirmText, confirmAction);
        Register(_confirm);
        _confirm.Closed += (_, _) => { _confirm = null; UpdateDismissOverlay(); };
        ApplyState(_confirm, reposition: true);
        _confirm.Show();
        UpdateDismissOverlay();
    }

    public void PositionAdjustment(Window window)
    {
        window.Left = owner.Left + Math.Max(0, (owner.Width - window.Width) / 2);
        window.Top = owner.Top + Math.Max(18 * settings().StickyScale, (owner.Height - window.Height) * 0.18);
    }

    public void PositionCentered(Window window)
    {
        window.Left = owner.Left + Math.Max(0, (owner.Width - window.Width) / 2);
        window.Top = owner.Top + Math.Max(0, (owner.Height - window.Height) / 2);
    }

    public void Synchronize(bool reposition)
    {
        foreach (var window in _childWindows.ToList())
        {
            window.Topmost = owner.Topmost;
            if (window is IStickyChildWindow child) ApplyState(child, reposition);
        }
    }

    public void CloseAll()
    {
        foreach (var window in _childWindows.ToList()) window.Close();
        _adjustment = null;
        _addTask = null;
        _taskDetail = null;
        _confirm = null;
        UpdateDismissOverlay();
    }

    public void RefreshDismissOverlay() => UpdateDismissOverlay();

    private void Register(Window window)
    {
        if (window is not IStickyChildWindow child)
            throw new InvalidOperationException("Sticky child windows must synchronize with the owner window.");
        window.Owner = owner;
        _childWindows.Add(window);
        window.ShowInTaskbar = false;
        window.Topmost = owner.Topmost;
        window.MouseEnter += (_, _) => updatePointerState(window, true);
        window.MouseLeave += (_, _) => updatePointerState(window, false);
        window.Closed += (_, _) =>
        {
            _childWindows.Remove(window);
            updatePointerState(window, false);
        };
        window.LocationChanged += (_, _) =>
        {
            if (!_isSynchronizingPositions && window.IsVisible) ApplyState(child, reposition: true);
        };
        ApplyState(child, reposition: false);
    }

    private void ApplyState(IStickyChildWindow child, bool reposition)
    {
        _isSynchronizingPositions = true;
        try { child.ApplyStickyOwnerState(reposition); }
        finally { _isSynchronizingPositions = false; }
    }

    private void UpdateDismissOverlay()
    {
        var overlay = dismissOverlay();
        var visible = HasOpenWindow();
        overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        overlay.IsHitTestVisible = visible;
        updateMenuDismissOverlay(visible);
    }
}
