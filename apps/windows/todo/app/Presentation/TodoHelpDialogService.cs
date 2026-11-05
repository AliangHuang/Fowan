using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using MuxFontWeights = Microsoft.UI.Text.FontWeights;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoHelpPalette(
    ElementTheme Theme,
    Brush Overlay,
    Brush Panel,
    Brush PanelBorder,
    Brush Text,
    Brush Transparent,
    Brush PrimaryButton,
    Brush PrimaryButtonText,
    Brush PrimaryButtonHover,
    Brush PrimaryButtonPressed,
    Brush CloseButton,
    Brush CloseButtonHover,
    Brush CloseButtonPressed);

internal sealed class TodoHelpDialogService(
    Action enterModal,
    Action exitModal,
    Func<double> contentWidth,
    Func<double, double> dialogWidth,
    Func<double> panelHeight)
{
    public async Task ShowAsync(Grid root, StackPanel content, TodoHelpPalette palette)
    {
        var width = contentWidth();
        content.Width = width;
        content.MaxWidth = width;
        var scroll = new ScrollViewer
        {
            Content = content,
            Width = width,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };
        var overlay = new Grid
        {
            RequestedTheme = palette.Theme,
            Background = palette.Overlay,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = true
        };
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));
        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        Canvas.SetZIndex(overlay, 1000);
        var modalHost = new Grid
        {
            RequestedTheme = palette.Theme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        overlay.Children.Add(modalHost);

        var closeButton = new Button
        {
            Content = "知道了", Width = 92, Height = 36, Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8), Background = palette.Transparent,
            Foreground = palette.PrimaryButtonText, BorderBrush = palette.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        SetButtonColors(closeButton, palette);
        var closeButtonFrame = new Border
        {
            Width = 92, Height = 36, CornerRadius = new CornerRadius(8),
            Background = palette.PrimaryButton, BorderBrush = palette.PrimaryButton,
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Child = closeButton
        };

        var initialPanelWidth = dialogWidth(width);
        var initialPanelHeight = panelHeight();
        var panelContent = new Grid
        {
            Width = Math.Max(300, initialPanelWidth - 48),
            Height = Math.Max(0, initialPanelHeight - 48),
            RowSpacing = 12
        };
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panelContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { MinHeight = 38, ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "使用说明", FontSize = 22, FontWeight = MuxFontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        });
        var iconCloseFrame = CloseIcon(palette);
        AutomationProperties.SetName(iconCloseFrame, "关闭使用说明");
        var iconCloseHost = new Grid
        {
            Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Children = { iconCloseFrame }
        };
        Grid.SetColumn(iconCloseHost, 1);
        header.Children.Add(iconCloseHost);
        panelContent.Children.Add(header);
        Grid.SetRow(scroll, 1);
        panelContent.Children.Add(scroll);
        closeButtonFrame.Margin = new Thickness(0, 0, 2, 2);
        var footer = new Grid { MinHeight = 40, Children = { closeButtonFrame } };
        Grid.SetRow(footer, 2);
        panelContent.Children.Add(footer);
        var panel = new Border
        {
            Width = initialPanelWidth, Height = initialPanelHeight, Padding = new Thickness(24),
            Margin = new Thickness(16), CornerRadius = new CornerRadius(18),
            Background = palette.Panel, BorderBrush = palette.PanelBorder,
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = panelContent
        };
        panel.PointerPressed += (_, args) => args.Handled = true;
        modalHost.Children.Add(panel);

        void Resize()
        {
            var nextContentWidth = contentWidth();
            var nextPanelWidth = dialogWidth(nextContentWidth);
            var nextPanelHeight = panelHeight();
            content.Width = nextContentWidth;
            content.MaxWidth = nextContentWidth;
            scroll.Width = nextContentWidth;
            panel.Width = nextPanelWidth;
            panel.Height = nextPanelHeight;
            panelContent.Width = Math.Max(300, nextPanelWidth - 48);
            panelContent.Height = Math.Max(0, nextPanelHeight - 48);
        }
        var completion = new TaskCompletionSource<object?>();
        var closed = false;
        SizeChangedEventHandler? sizeChanged = (_, _) => Resize();
        void Close()
        {
            if (closed) return;
            closed = true;
            if (sizeChanged is not null) root.SizeChanged -= sizeChanged;
            sizeChanged = null;
            root.Children.Remove(overlay);
            exitModal();
            completion.TrySetResult(null);
        }
        overlay.PointerPressed += (_, _) => Close();
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            Close();
        };
        closeButton.Click += (_, _) => Close();
        iconCloseFrame.Tapped += (_, _) => Close();
        overlay.Loaded += (_, _) => overlay.Focus(FocusState.Programmatic);
        root.SizeChanged += sizeChanged;
        Resize();
        enterModal();
        root.Children.Add(overlay);
        await completion.Task;
    }

    private static Border CloseIcon(TodoHelpPalette palette)
    {
        var frame = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(9),
            Background = palette.CloseButton, BorderBrush = palette.PanelBorder,
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE711", FontSize = 12, Foreground = palette.Text,
                IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        frame.PointerEntered += (_, _) => frame.Background = palette.CloseButtonHover;
        frame.PointerExited += (_, _) => frame.Background = palette.CloseButton;
        frame.PointerPressed += (_, _) => frame.Background = palette.CloseButtonPressed;
        frame.PointerReleased += (_, _) => frame.Background = palette.CloseButtonHover;
        return frame;
    }

    private static void SetButtonColors(Button button, TodoHelpPalette palette)
    {
        button.Resources["ButtonBackground"] = palette.Transparent;
        button.Resources["ButtonBackgroundPointerOver"] = palette.PrimaryButtonHover;
        button.Resources["ButtonBackgroundPressed"] = palette.PrimaryButtonPressed;
        button.Resources["ButtonForeground"] = palette.PrimaryButtonText;
        button.Resources["ButtonForegroundPointerOver"] = palette.PrimaryButtonText;
        button.Resources["ButtonForegroundPressed"] = palette.PrimaryButtonText;
        button.Resources["ButtonBorderBrush"] = palette.Transparent;
        button.Resources["ButtonBorderBrushPointerOver"] = palette.Transparent;
        button.Resources["ButtonBorderBrushPressed"] = palette.Transparent;
    }
}
