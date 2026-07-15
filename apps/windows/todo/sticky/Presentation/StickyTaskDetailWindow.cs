using Fowan.Todo.Shared.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;
internal sealed class StickyTaskDetailWindow : Window, IStickyChildWindow
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
        var scale = _owner.Settings.StickyScale;
        Width = BaseWindowWidth * scale;
        Height = BaseWindowHeight * scale;
        Topmost = _owner.Topmost;
        _windowScale.ScaleX = scale;
        _windowScale.ScaleY = scale;

        _panel.Background = _owner.SurfaceBrush();
        _panel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
        _titleBorder.Background = _owner.PanelBrush(0xF5FAFB);
        _titleBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
        _notesBorder.Background = _owner.PanelBrush(0xF5FAFB);
        _notesBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
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
