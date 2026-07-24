using Fowan.Todo.Shared.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;

/// <summary>
/// Owns only the sticky menu chrome. Task content deliberately never shares this visual tree.
/// </summary>
internal sealed class StickyMenuWindow : Window
{
    private readonly StickyWindow _owner;
    private readonly StickyShellBuilder _shellBuilder;
    private readonly Func<TodoSettings> _settings;
    private readonly Action<Window, bool> _updatePointerState;
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private Border _dismissOverlay = new();
    private Button _compactAddTaskButton = new();
    private bool _isDismissOverlayVisible;

    public StickyMenuWindow(
        StickyWindow owner,
        StickyShellBuilder shellBuilder,
        Func<TodoSettings> settings,
        Action<Window, bool> updatePointerState)
    {
        _owner = owner;
        _shellBuilder = shellBuilder;
        _settings = settings;
        _updatePointerState = updatePointerState;

        Owner = owner;
        Title = "Fowan Todo Sticky Menu";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;

        MouseEnter += (_, _) => _updatePointerState(this, true);
        MouseLeave += (_, _) => _updatePointerState(this, false);
        Closed += (_, _) => _updatePointerState(this, false);

        Rebuild();
    }

    public void Rebuild()
    {
        var view = _shellBuilder.BuildMenu();
        view.Root.LayoutTransform = _scaleTransform;
        Content = view.Root;
        _dismissOverlay = view.DismissOverlay;
        _compactAddTaskButton = view.CompactAddTaskButton;
        ApplyScaleAndPreferences();
        ApplyDismissOverlay();
    }

    public void ApplyPresentation(bool show)
    {
        ApplyScaleAndPreferences();
        if (!_owner.IsVisible || !_owner.IsLoaded || _owner.WindowState != WindowState.Normal)
        {
            if (IsVisible) Hide();
            return;
        }

        var menuHeight = StickyShellBuilder.MenuBarHeight * _settings().StickyScale;
        var overlap = StickyShellBuilder.MenuBodyOverlap * _settings().StickyScale;
        Width = _owner.Width;
        Height = menuHeight;
        Left = _owner.Left;
        Top = _owner.Top - menuHeight + overlap;
        Topmost = _owner.Topmost;

        if (show)
        {
            if (!IsVisible) Show();
            return;
        }

        if (IsVisible) Hide();
    }

    public void CloseMenu()
    {
        if (IsVisible) Hide();
        Close();
    }

    public void SetDismissOverlayVisible(bool visible)
    {
        _isDismissOverlayVisible = visible;
        ApplyDismissOverlay();
    }

    public void HideForOwnerTransition()
    {
        if (IsVisible) Hide();
    }

    private void ApplyScaleAndPreferences()
    {
        var settings = _settings();
        _scaleTransform.ScaleX = settings.StickyScale;
        _scaleTransform.ScaleY = settings.StickyScale;
        _compactAddTaskButton.Visibility = settings.IsStickyAddTaskMinimized
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyDismissOverlay()
    {
        _dismissOverlay.Visibility = _isDismissOverlayVisible ? Visibility.Visible : Visibility.Collapsed;
        _dismissOverlay.IsHitTestVisible = _isDismissOverlayVisible;
    }
}
