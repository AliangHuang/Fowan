using Fowan.Todo.Shared.Models;
using Fowan.Todo.Sticky.Windows.Platform.Windows;
using Fowan.Todo.Sticky.Windows.Presentation;

namespace Fowan.Todo.Sticky.Windows.Coordination;

/// <summary>
/// Keeps the menu window docked above the task body without ever changing task-body geometry.
/// </summary>
internal sealed class StickyMenuWindowCoordinator
{
    private readonly Func<bool> _isFloating;
    private readonly StickyMenuWindow _menuWindow;
    private readonly StickyMenuNativeWindowController _nativeWindow;

    public StickyMenuWindowCoordinator(
        StickyWindow owner,
        StickyShellBuilder shellBuilder,
        Func<TodoSettings> settings,
        Func<bool> isFloating,
        Action<System.Windows.Window, bool> updatePointerState)
    {
        _isFloating = isFloating;
        _menuWindow = new StickyMenuWindow(owner, shellBuilder, settings, updatePointerState);
        _nativeWindow = new StickyMenuNativeWindowController(_menuWindow);
    }

    public void ApplyPresentation(bool showMenu) =>
        _menuWindow.ApplyPresentation(showMenu && !_isFloating());

    public void Synchronize(bool showMenu) => ApplyPresentation(showMenu);

    public void Rebuild() => _menuWindow.Rebuild();

    public void SetDismissOverlayVisible(bool visible) => _menuWindow.SetDismissOverlayVisible(visible);

    public void HideForOwnerTransition() => _menuWindow.HideForOwnerTransition();

    public void HideBeforeOwnerMinimize() => _nativeWindow.HideImmediately();

    public void Close()
    {
        _nativeWindow.Dispose();
        _menuWindow.CloseMenu();
    }
}
