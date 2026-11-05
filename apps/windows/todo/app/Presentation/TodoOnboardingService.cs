using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoOnboardingPalette(
    Brush Transparent,
    Brush Accent,
    Brush Text,
    Brush SecondaryText,
    Brush Panel,
    Brush PanelBorder,
    Brush Dim);

internal sealed record TodoOnboardingControls(
    Func<string, string, Button> PillButton,
    Func<string, string, Button> PrimaryButton,
    Action<Button> ConfigureSecondary);

internal sealed class TodoOnboardingService(Action enterModal, Action exitModal)
{
    private Grid? _root;
    private Grid? _overlay;
    private SizeChangedEventHandler? _sizeChanged;
    private Action? _complete;
    private bool _modalActive;

    public bool IsShowing => _overlay is not null;

    public void Show(
        Grid root,
        Button stickyModeButton,
        Button helpButton,
        TodoOnboardingPalette palette,
        TodoOnboardingControls controls,
        Action complete)
    {
        if (IsShowing || !root.IsLoaded) return;
        _root = root;
        _complete = complete;
        var overlay = new Grid
        {
            Background = palette.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = true
        };
        _overlay = overlay;
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));
        Canvas.SetZIndex(overlay, 2000);
        var canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        overlay.Children.Add(canvas);
        var regions = Enumerable.Range(0, 4)
            .Select(_ => new Border { Background = palette.Dim, IsHitTestVisible = false })
            .ToArray();
        foreach (var region in regions) canvas.Children.Add(region);
        var highlight = new Border
        {
            CornerRadius = new CornerRadius(10), BorderBrush = palette.Accent,
            BorderThickness = new Thickness(3), Background = palette.Transparent,
            IsHitTestVisible = false
        };
        canvas.Children.Add(highlight);
        var title = new TextBlock
        {
            FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = palette.Text,
            TextWrapping = TextWrapping.Wrap
        };
        var description = new TextBlock
        {
            FontSize = 14, Foreground = palette.SecondaryText,
            TextWrapping = TextWrapping.Wrap, LineHeight = 22
        };
        var indicator = new TextBlock
        {
            FontSize = 12, Foreground = palette.SecondaryText,
            VerticalAlignment = VerticalAlignment.Center
        };
        var skip = controls.PillButton("跳过", "\uE711");
        controls.ConfigureSecondary(skip);
        var next = controls.PrimaryButton("下一步", "\uE72A");
        var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.Children.Add(skip);
        Grid.SetColumn(indicator, 1);
        indicator.HorizontalAlignment = HorizontalAlignment.Center;
        actions.Children.Add(indicator);
        Grid.SetColumn(next, 2);
        actions.Children.Add(next);
        var coach = new Border
        {
            Width = 340, Padding = new Thickness(22), CornerRadius = new CornerRadius(14),
            Background = palette.Panel, BorderBrush = palette.PanelBorder,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 10, Children = { title, description, actions } }
        };
        canvas.Children.Add(coach);
        var step = 0;

        void SetRect(FrameworkElement element, double left, double top, double width, double height)
        {
            Canvas.SetLeft(element, Math.Max(0, left));
            Canvas.SetTop(element, Math.Max(0, top));
            element.Width = Math.Max(0, width);
            element.Height = Math.Max(0, height);
        }
        void Layout()
        {
            if (!ReferenceEquals(_overlay, overlay) || overlay.ActualWidth <= 0 || overlay.ActualHeight <= 0) return;
            var target = step == 0 ? stickyModeButton : helpButton;
            if (!target.IsLoaded || target.ActualWidth <= 0 || target.ActualHeight <= 0) return;
            var bounds = target.TransformToVisual(root).TransformBounds(
                new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            const double padding = 7;
            var left = Math.Max(0, bounds.X - padding);
            var top = Math.Max(0, bounds.Y - padding);
            var right = Math.Min(overlay.ActualWidth, bounds.Right + padding);
            var bottom = Math.Min(overlay.ActualHeight, bounds.Bottom + padding);
            var holeHeight = Math.Max(0, bottom - top);
            SetRect(regions[0], 0, 0, overlay.ActualWidth, top);
            SetRect(regions[1], 0, top, left, holeHeight);
            SetRect(regions[2], right, top, overlay.ActualWidth - right, holeHeight);
            SetRect(regions[3], 0, bottom, overlay.ActualWidth, overlay.ActualHeight - bottom);
            SetRect(highlight, left, top, Math.Max(0, right - left), holeHeight);
            var coachHeight = coach.ActualHeight > 0 ? coach.ActualHeight : 180;
            var coachLeft = Math.Clamp(
                bounds.X + bounds.Width / 2 - coach.Width / 2,
                18,
                Math.Max(18, overlay.ActualWidth - coach.Width - 18));
            var coachTop = bottom + 18;
            if (coachTop + coachHeight > overlay.ActualHeight - 18)
            {
                coachTop = Math.Max(18, top - coachHeight - 18);
            }
            Canvas.SetLeft(coach, coachLeft);
            Canvas.SetTop(coach, coachTop);
        }
        void Update()
        {
            indicator.Text = $"{step + 1} / 2";
            if (step == 0)
            {
                title.Text = "切换到便签模式";
                description.Text = "使用顶部的便签按钮，把今日任务切换到轻量便签窗口。便签可以置顶、调整透明度，也可以吸附到屏幕左右边缘。";
                next.Content = "下一步";
            }
            else
            {
                title.Text = "随时查看使用说明";
                description.Text = "需要回顾操作时，点击侧栏底部的帮助按钮，可以查看主界面、便签、筛选和回收站的完整说明。";
                next.Content = "完成";
            }
            Layout();
        }
        skip.Click += (_, _) => Close(completed: true);
        next.Click += (_, _) =>
        {
            if (step == 0) { step = 1; Update(); } else Close(completed: true);
        };
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            Close(completed: true);
        };
        overlay.Loaded += (_, _) => { overlay.Focus(FocusState.Programmatic); Update(); };
        coach.Loaded += (_, _) => Layout();
        _sizeChanged = (_, _) => Layout();
        root.SizeChanged += _sizeChanged;
        try
        {
            root.Children.Add(overlay);
            _modalActive = true;
            enterModal();
            Update();
        }
        catch
        {
            Close(completed: false);
            throw;
        }
    }

    public void Dismiss() => Close(completed: false);

    private void Close(bool completed)
    {
        if (_overlay is null) return;
        if (_root is not null && _sizeChanged is not null) _root.SizeChanged -= _sizeChanged;
        _sizeChanged = null;
        _root?.Children.Remove(_overlay);
        _overlay = null;
        _root = null;
        if (_modalActive)
        {
            _modalActive = false;
            exitModal();
        }
        var callback = _complete;
        _complete = null;
        if (completed) callback?.Invoke();
    }
}
