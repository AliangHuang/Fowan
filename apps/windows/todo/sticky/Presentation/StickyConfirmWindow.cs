using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;
internal sealed class StickyConfirmWindow : Window, IStickyChildWindow
{
    private const double BaseWindowWidth = 336;
    private const double BaseWindowHeight = 174;

    private readonly StickyWindow _owner;
    private readonly string _headingText;
    private readonly string _messageText;
    private readonly string _confirmText;
    private readonly Action _confirmAction;
    private readonly ScaleTransform _windowScale = new(1, 1);
    private readonly Border _panel = new();
    private readonly TextBlock _heading = new();
    private readonly TextBlock _message = new();
    private readonly Button _cancelButton = new();
    private readonly Button _confirmButton = new();

    public StickyConfirmWindow(
        StickyWindow owner,
        string headingText,
        string messageText,
        string confirmText,
        Action confirmAction)
    {
        _owner = owner;
        _headingText = headingText;
        _messageText = messageText;
        _confirmText = confirmText;
        _confirmAction = confirmAction;
        Width = BaseWindowWidth;
        Height = BaseWindowHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = BuildContent();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Close();
                args.Handled = true;
            }
        };
    }

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
        _panel.Padding = new Thickness(14);

        var stack = new StackPanel();
        _heading.Text = "完成当前任务？";
        _heading.FontSize = 14;
        _heading.FontWeight = FontWeights.SemiBold;
        _heading.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_heading);

        _message.Text = "是否完成当前任务，仍有未完成的子任务";
        _message.FontSize = 13;
        _message.TextWrapping = TextWrapping.Wrap;
        _message.LineHeight = 19;
        stack.Children.Add(_message);
        _heading.Text = _headingText;
        _message.Text = _messageText;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
        _cancelButton.Click += (_, _) => Close();
        actions.Children.Add(_cancelButton);

        ConfigureActionButton(_confirmButton, "确认完成", isPrimary: true);
        _confirmButton.Content = _confirmText;
        _confirmButton.Margin = new Thickness(8, 0, 0, 0);
        _confirmButton.Click += (_, _) =>
        {
            _confirmAction();
            Close();
        };
        actions.Children.Add(_confirmButton);

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
        _heading.Foreground = _owner.TextBrush();
        _message.Foreground = _owner.SecondaryTextBrush();
        ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
        ConfigureActionButton(_confirmButton, "确认完成", isPrimary: true);
        _confirmButton.Content = _confirmText;

        if (reposition)
        {
            _owner.PositionConfirmWindow(this);
        }
    }

    private void ConfigureActionButton(Button button, string text, bool isPrimary)
    {
        button.MinWidth = isPrimary ? 82 : 64;
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
