using Fowan.Todo.Shared.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;
internal sealed class StickyAdjustmentWindow : Window, IStickyChildWindow
{
    private const double BaseWindowWidth = 358;
    private const double BaseWindowHeight = 168;

    private readonly StickyWindow _owner;
    private readonly ScaleTransform _windowScale = new(1, 1);
    private readonly Border _panel = new();
    private readonly TextBlock _opacityValue = new();
    private readonly TextBlock _scaleValue = new();
    private readonly Slider _opacitySlider = new();
    private readonly Slider _scaleSlider = new();
    private readonly Button _resetOpacityButton = new();
    private readonly Button _resetScaleButton = new();
    private readonly Button _systemThemeButton = new();
    private readonly Button _lightThemeButton = new();
    private readonly Button _darkThemeButton = new();
    private bool _isSynchronizing;

    public StickyAdjustmentWindow(StickyWindow owner)
    {
        _owner = owner;
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
        _panel.Padding = new Thickness(12);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        grid.Children.Add(_owner.SmallLabel("透明度"));
        _opacitySlider.Minimum = TodoSettings.MinStickyOpacity * 100;
        _opacitySlider.Maximum = TodoSettings.MaxStickyOpacity * 100;
        _opacitySlider.Value = _owner.Settings.StickyOpacity * 100;
        _opacitySlider.VerticalAlignment = VerticalAlignment.Center;
        ApplySliderTheme(_opacitySlider);
        _opacitySlider.ValueChanged += (_, _) =>
        {
            if (!_isSynchronizing)
            {
                _owner.SetStickyOpacity(_opacitySlider.Value / 100);
            }

            _opacityValue.Text = $"{Math.Round(_owner.Settings.StickyOpacity * 100):0}%";
        };
        Grid.SetColumn(_opacitySlider, 1);
        grid.Children.Add(_opacitySlider);
        _opacityValue.Text = $"{Math.Round(_owner.Settings.StickyOpacity * 100):0}%";
        _opacityValue.Foreground = _owner.SecondaryTextBrush();
        _opacityValue.FontSize = 12;
        _opacityValue.VerticalAlignment = VerticalAlignment.Center;
        _opacityValue.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(_opacityValue, 2);
        grid.Children.Add(_opacityValue);

        ConfigureResetButton(_resetOpacityButton, topMargin: 0);
        _resetOpacityButton.Click += (_, _) =>
        {
            _opacitySlider.Value = 100;
            _owner.SetStickyOpacity(1.0);
        };
        Grid.SetColumn(_resetOpacityButton, 3);
        grid.Children.Add(_resetOpacityButton);

        var scaleLabel = _owner.SmallLabel("缩放");
        scaleLabel.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(scaleLabel, 1);
        grid.Children.Add(scaleLabel);
        _scaleSlider.Minimum = TodoSettings.MinStickyScale * 100;
        _scaleSlider.Maximum = TodoSettings.MaxStickyScale * 100;
        _scaleSlider.Value = _owner.Settings.StickyScale * 100;
        _scaleSlider.Margin = new Thickness(0, 10, 0, 0);
        ApplySliderTheme(_scaleSlider);
        _scaleSlider.ValueChanged += (_, _) => _scaleValue.Text = $"{Math.Round(_scaleSlider.Value):0}%";
        _scaleSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _owner.ApplyScaleFromSlider(_scaleSlider.Value)));
        _scaleSlider.PreviewMouseLeftButtonUp += (_, _) => _owner.ApplyScaleFromSlider(_scaleSlider.Value);
        _scaleSlider.LostKeyboardFocus += (_, _) => _owner.ApplyScaleFromSlider(_scaleSlider.Value);
        Grid.SetRow(_scaleSlider, 1);
        Grid.SetColumn(_scaleSlider, 1);
        grid.Children.Add(_scaleSlider);
        _scaleValue.Text = $"{Math.Round(_owner.Settings.StickyScale * 100):0}%";
        _scaleValue.Foreground = _owner.SecondaryTextBrush();
        _scaleValue.FontSize = 12;
        _scaleValue.Margin = new Thickness(0, 10, 0, 0);
        _scaleValue.VerticalAlignment = VerticalAlignment.Center;
        _scaleValue.TextAlignment = TextAlignment.Right;
        Grid.SetRow(_scaleValue, 1);
        Grid.SetColumn(_scaleValue, 2);
        grid.Children.Add(_scaleValue);

        ConfigureResetButton(_resetScaleButton, topMargin: 10);
        _resetScaleButton.Click += (_, _) =>
        {
            _scaleSlider.Value = 100;
            _owner.ApplyScaleFromSlider(100);
        };
        Grid.SetRow(_resetScaleButton, 1);
        Grid.SetColumn(_resetScaleButton, 3);
        grid.Children.Add(_resetScaleButton);

        var themeLabel = _owner.SmallLabel("主题");
        themeLabel.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(themeLabel, 2);
        grid.Children.Add(themeLabel);

        var themeButtons = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 12, 0, 0)
        };
        ConfigureThemeButton(_systemThemeButton, "系统", TodoThemeIds.System);
        ConfigureThemeButton(_lightThemeButton, "浅色", TodoThemeIds.Light);
        ConfigureThemeButton(_darkThemeButton, "深色", TodoThemeIds.Dark);
        _systemThemeButton.Click += (_, _) => _owner.SetStickyTheme(TodoThemeIds.System);
        _lightThemeButton.Click += (_, _) => _owner.SetStickyTheme(TodoThemeIds.Light);
        _darkThemeButton.Click += (_, _) => _owner.SetStickyTheme(TodoThemeIds.Dark);
        themeButtons.Children.Add(_systemThemeButton);
        themeButtons.Children.Add(_lightThemeButton);
        themeButtons.Children.Add(_darkThemeButton);
        Grid.SetRow(themeButtons, 2);
        Grid.SetColumn(themeButtons, 1);
        Grid.SetColumnSpan(themeButtons, 3);
        grid.Children.Add(themeButtons);

        _panel.Child = grid;
        scaleHost.Children.Add(_panel);
        return scaleHost;
    }

    public void ApplyStickyOwnerState(bool reposition)
    {
        _isSynchronizing = true;
        try
        {
            var scale = _owner.Settings.StickyScale;
            Width = BaseWindowWidth * scale;
            Height = BaseWindowHeight * scale;
            Topmost = _owner.Topmost;
            _windowScale.ScaleX = scale;
            _windowScale.ScaleY = scale;
            _panel.Background = _owner.SurfaceBrush();
            _panel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
            _opacitySlider.Value = _owner.Settings.StickyOpacity * 100;
            _scaleSlider.Value = _owner.Settings.StickyScale * 100;
            _opacityValue.Text = $"{Math.Round(_owner.Settings.StickyOpacity * 100):0}%";
            _scaleValue.Text = $"{Math.Round(_owner.Settings.StickyScale * 100):0}%";
            _opacityValue.Foreground = _owner.SecondaryTextBrush();
            _scaleValue.Foreground = _owner.SecondaryTextBrush();
            ApplySliderTheme(_opacitySlider);
            ApplySliderTheme(_scaleSlider);
            ConfigureResetButton(_resetOpacityButton, topMargin: 0);
            ConfigureResetButton(_resetScaleButton, topMargin: 10);
            ConfigureThemeButton(_systemThemeButton, "系统", TodoThemeIds.System);
            ConfigureThemeButton(_lightThemeButton, "浅色", TodoThemeIds.Light);
            ConfigureThemeButton(_darkThemeButton, "深色", TodoThemeIds.Dark);
        }
        finally
        {
            _isSynchronizing = false;
        }

        if (reposition)
        {
            _owner.PositionAdjustmentWindow(this);
        }
    }

    private void ApplySliderTheme(Slider slider)
    {
        slider.Resources[SystemColors.HighlightBrushKey] = _owner.AccentBrush();
        slider.Resources[SystemColors.ControlBrushKey] = _owner.Brush(0xDCE7EA);
        slider.Resources[SystemColors.ControlLightBrushKey] = _owner.Brush(0xE7EEF0);
        slider.Resources[SystemColors.WindowBrushKey] = _owner.Brush(0xFFFFFF, 0.0);
        slider.Foreground = _owner.AccentBrush();
        slider.Background = _owner.Brush(0xDCE7EA);
    }

    private void ConfigureThemeButton(Button button, string text, string theme)
    {
        var selected = string.Equals(_owner.Settings.Theme, theme, StringComparison.Ordinal);
        button.Height = 28;
        button.Margin = new Thickness(3, 0, 3, 0);
        button.Padding = new Thickness(8, 0, 8, 0);
        button.BorderThickness = new Thickness(0);
        button.Content = text;
        button.FontSize = 12;
        button.FontWeight = FontWeights.SemiBold;
        button.Foreground = selected ? Brushes.White : _owner.TextBrush();
        button.Background = selected ? _owner.AccentBrush() : Brushes.Transparent;
        button.Template = _owner.ButtonTemplate(
            new CornerRadius(7),
            selected ? _owner.AccentBrush() : Brushes.Transparent,
            selected ? _owner.AccentDarkBrush() : _owner.Brush(0xEEF9FA));
    }

    private void ConfigureResetButton(Button button, double topMargin)
    {
        button.Width = 34;
        button.Height = 24;
        button.Margin = new Thickness(4, topMargin, 0, 0);
        button.Padding = new Thickness(0);
        button.BorderThickness = new Thickness(0);
        button.Content = "重置";
        button.FontSize = 11;
        button.FontWeight = FontWeights.SemiBold;
        button.Foreground = _owner.SecondaryTextBrush();
        button.Background = Brushes.Transparent;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Template = _owner.ButtonTemplate(new CornerRadius(7), Brushes.Transparent, _owner.Brush(0xEEF9FA));
    }
}
