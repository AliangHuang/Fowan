using Fowan.Pomodoro.Models;
using Fowan.Pomodoro.Shared.Application;
using Fowan.Pomodoro.Shared.Services;
using Fowan.Pomodoro.Windows.Application.Ports;
using Fowan.Pomodoro.Windows.Platform.Windows;
using Fowan.Reminders.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Fowan.Pomodoro.Windows;

/// <summary>Fluent WinUI shell that renders workspace snapshots and only sends typed commands.</summary>
public sealed class PomodoroWindow : Window
{
    private static readonly Color CanvasColor = Color.FromArgb(255, 247, 249, 253);
    private static readonly Color Navy = Color.FromArgb(255, 3, 36, 73);
    private static readonly Color Ink = Color.FromArgb(255, 8, 38, 80);
    private static readonly Color MutedInk = Color.FromArgb(255, 102, 121, 152);
    private static readonly Color Blue = Color.FromArgb(255, 29, 98, 211);
    private static readonly Color BlueSoft = Color.FromArgb(255, 232, 241, 255);
    private static readonly Color Coral = Color.FromArgb(255, 255, 93, 76);
    private static readonly Color CoralSoft = Color.FromArgb(255, 255, 239, 237);
    private static readonly Color Stroke = Color.FromArgb(255, 226, 233, 243);

    private readonly PomodoroWorkspaceOwner _workspaceOwner = new();
    private PomodoroWorkspace Workspace => _workspaceOwner.Workspace;
    private readonly IPomodoroTodoReader _todoReader = new WindowsPomodoroTodoReader();
    private readonly WindowsPomodoroNotificationService _notifications = new();
    private readonly IToolboxResidentConfigurator _residentConfigurator = new WindowsToolboxResidentConfigurator();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ContentControl _page = new();
    private readonly TextBlock _phaseText = new();
    private readonly TextBlock _clockText = new();
    private readonly TextBlock _roundText = new();
    private readonly TextBlock _taskText = new();
    private readonly TextBlock _nextReminderText = new();
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _tipText = new();
    private readonly ComboBox _focusTask = new();
    private readonly TextBlock _statusText = new();
    private readonly Dictionary<string, Button> _navigation = [];
    private string _currentPage = "focus";

    public PomodoroWindow()
    {
        Title = "Fowan 番茄钟";
        ExtendsContentIntoTitleBar = true;
        Content = BuildShell();
        Workspace.Changed += (_, _) => DispatcherQueue.TryEnqueue(Render);
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
        Workspace.ReconcileWake(DateTimeOffset.Now);
        Render();
    }

    private UIElement BuildShell()
    {
        var root = new Grid { Background = Brush(CanvasColor) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Grid { Background = Brush(Navy), Padding = new Thickness(20, 54, 20, 26) };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(14, 0, 0, 56) };
        brand.Children.Add(TomatoMark(54));
        brand.Children.Add(new TextBlock
        {
            Text = "Fowan 番茄钟",
            Foreground = Brush(Colors.White),
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        sidebar.Children.Add(brand);

        var nav = new StackPanel { Spacing = 10 };
        nav.Children.Add(NavigationButton("focus", "专注", "\uE80F"));
        nav.Children.Add(NavigationButton("reminders", "提醒", "\uE7ED"));
        nav.Children.Add(NavigationButton("records", "记录", "\uE9D9"));
        nav.Children.Add(NavigationButton("settings", "设置", "\uE713"));
        Grid.SetRow(nav, 1);
        sidebar.Children.Add(nav);

        var footer = new Border
        {
            BorderBrush = Brush(Color.FromArgb(76, 204, 224, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 13, 12, 13),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new FontIcon { Glyph = "\uE73E", FontFamily = FluentIcons(), FontSize = 20, Foreground = Brush(Color.FromArgb(255, 202, 220, 246)) },
                    new TextBlock { Text = "专注成就，时间自由", Foreground = Brush(Color.FromArgb(255, 225, 235, 250)), FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        Grid.SetRow(footer, 2);
        sidebar.Children.Add(footer);
        root.Children.Add(sidebar);

        var content = new Grid { Margin = new Thickness(40, 48, 40, 22) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(_page);
        _statusText.Foreground = Brush(MutedInk);
        _statusText.FontSize = 13;
        _statusText.Margin = new Thickness(4, 12, 4, 0);
        _statusText.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(_statusText, 1);
        content.Children.Add(_statusText);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        Navigate("focus");
        return root;
    }

    private Button NavigationButton(string id, string text, string glyph)
    {
        var icon = new FontIcon { Glyph = glyph, FontFamily = FluentIcons(), FontSize = 23, Foreground = Brush(Color.FromArgb(255, 201, 215, 238)) };
        var label = new TextBlock { Text = text, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new Border { Name = "Selection", Width = 3, CornerRadius = new CornerRadius(2), Background = Brush(Color.FromArgb(255, 139, 200, 255)), Visibility = Visibility.Collapsed });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(16, 0, 14, 0), Children = { icon, label } };
        Grid.SetColumn(row, 1);
        content.Children.Add(row);
        var button = new Button
        {
            Content = content,
            Height = 66,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            Foreground = Brush(Color.FromArgb(255, 223, 233, 248)),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        button.Click += (_, _) => Navigate(id);
        AutomationProperties.SetName(button, text);
        _navigation[id] = button;
        return button;
    }

    private void Navigate(string id)
    {
        // The focus page deliberately owns visual elements that are refreshed every second.
        // Rebuilding it while it is already the active ContentControl child would attempt to
        // attach those same elements to a second visual tree and terminates WinUI with an
        // unhandled XAML exception. A same-page navigation is only a refresh.
        if (id == _currentPage && _page.Content is not null)
        {
            Render();
            return;
        }

        _currentPage = id;
        _page.Content = id switch
        {
            "reminders" => BuildRemindersPage(),
            "records" => BuildRecordsPage(),
            "settings" => BuildSettingsPage(),
            _ => BuildFocusPage()
        };
        foreach (var pair in _navigation)
        {
            var selected = pair.Key == id;
            pair.Value.Background = selected ? Brush(Color.FromArgb(255, 28, 75, 133)) : new SolidColorBrush(Colors.Transparent);
            pair.Value.Foreground = selected ? Brush(Colors.White) : Brush(Color.FromArgb(255, 213, 226, 246));
            if (pair.Value.Content is Grid grid && grid.Children[0] is Border indicator)
                indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
        Render();
    }

    private UIElement BuildFocusPage()
    {
        var root = new Grid { MaxWidth = 1320, HorizontalAlignment = HorizontalAlignment.Left };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(4, 0, 0, 30) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(PageTitle("专注"));
        var utilities = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Right };
        utilities.Children.Add(IconButton("统计概览", "\uE9D9"));
        utilities.Children.Add(IconButton("外观", "\uE7C3"));
        utilities.Children.Add(IconButton("主题", "\uE706"));
        utilities.Children.Add(IconButton("更多", "\uE712"));
        Grid.SetColumn(utilities, 1);
        header.Children.Add(utilities);
        root.Children.Add(header);

        var body = new Grid { ColumnSpacing = 38 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(736) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var main = new StackPanel { Spacing = 24 };
        main.Children.Add(BuildTimerCard());
        main.Children.Add(TipCard());
        body.Children.Add(main);

        var side = new StackPanel { Spacing = 28, Margin = new Thickness(0, 8, 0, 0) };
        side.Children.Add(BuildTaskCard());
        side.Children.Add(BuildNextReminderCard());
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private UIElement BuildTimerCard()
    {
        var card = SurfaceCard(new Thickness(34, 28, 34, 28), 20);
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var dial = new Grid { Width = 470, Height = 470, HorizontalAlignment = HorizontalAlignment.Center };
        dial.Children.Add(BuildTickDial());
        var tomato = TomatoMark(56);
        tomato.VerticalAlignment = VerticalAlignment.Top;
        tomato.Margin = new Thickness(0, 79, 0, 0);
        dial.Children.Add(tomato);
        _phaseText.FontSize = 25;
        _phaseText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _phaseText.Foreground = Brush(Coral);
        _phaseText.HorizontalAlignment = HorizontalAlignment.Center;
        _phaseText.VerticalAlignment = VerticalAlignment.Top;
        _phaseText.Margin = new Thickness(0, 148, 0, 0);
        dial.Children.Add(_phaseText);
        _clockText.FontSize = 84;
        _clockText.FontFamily = new FontFamily("Segoe UI Semibold");
        _clockText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _clockText.Foreground = Brush(Ink);
        _clockText.HorizontalAlignment = HorizontalAlignment.Center;
        _clockText.VerticalAlignment = VerticalAlignment.Top;
        _clockText.Margin = new Thickness(0, 194, 0, 0);
        dial.Children.Add(_clockText);
        _roundText.FontSize = 20;
        _roundText.Foreground = Brush(MutedInk);
        _roundText.HorizontalAlignment = HorizontalAlignment.Center;
        _roundText.VerticalAlignment = VerticalAlignment.Top;
        _roundText.Margin = new Thickness(0, 318, 0, 0);
        dial.Children.Add(_roundText);
        content.Children.Add(dial);

        var actions = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0), ColumnSpacing = 26 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        actions.Children.Add(PrimaryActionButton("开始专注", "\uE768", (_, _) => StartFocus(), Blue, "开始专注"));
        var skip = PrimaryActionButton("跳过", "\uE892", (_, _) => Workspace.Skip(), BlueSoft, "跳过", Blue);
        Grid.SetColumn(skip, 1);
        actions.Children.Add(skip);
        var stop = PrimaryActionButton("停止", "\uE71A", (_, _) => Workspace.Stop(), Coral, "停止");
        Grid.SetColumn(stop, 2);
        actions.Children.Add(stop);
        content.Children.Add(actions);
        card.Child = content;
        return card;
    }

    private UIElement BuildTaskCard()
    {
        var task = SurfaceCard(new Thickness(32, 28, 32, 26), 18);
        var panel = new StackPanel { Spacing = 19 };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(SoftIcon("\uE8FD", BlueSoft, Blue));
        var title = new TextBlock { Text = "当前关联待办", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };
        Grid.SetColumn(title, 1);
        top.Children.Add(title);
        var all = TextAction("查看全部", (_, _) => _statusText.Text = "请在 Todo 工具中查看全部待办。");
        Grid.SetColumn(all, 2);
        top.Children.Add(all);
        panel.Children.Add(top);
        _taskText.FontSize = 26;
        _taskText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _taskText.Foreground = Brush(Ink);
        _taskText.TextWrapping = TextWrapping.Wrap;
        _taskText.MaxLines = 2;
        panel.Children.Add(_taskText);

        _focusTask.PlaceholderText = "选择未完成待办（可选）";
        _focusTask.HorizontalAlignment = HorizontalAlignment.Stretch;
        _focusTask.FontSize = 15;
        _focusTask.Margin = new Thickness(0, 2, 0, 0);
        PopulateTaskSelector(_focusTask, includeNone: true);
        panel.Children.Add(_focusTask);
        var separator = new Border { Height = 1, Background = Brush(Stroke), Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(separator);
        var metadata = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        metadata.Children.Add(Tag("工作", BlueSoft, Blue));
        metadata.Children.Add(new TextBlock { Text = "专注结束后由你决定是否完成待办", Foreground = Brush(MutedInk), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(metadata);
        task.Child = panel;
        return task;
    }

    private UIElement BuildNextReminderCard()
    {
        var reminder = SurfaceCard(new Thickness(32, 28, 32, 24), 18);
        reminder.BorderBrush = Brush(Color.FromArgb(255, 255, 206, 200));
        var panel = new StackPanel { Spacing = 18 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };
        top.Children.Add(SoftIcon("\uE7ED", CoralSoft, Coral));
        top.Children.Add(new TextBlock { Text = "下一提醒", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(top);
        _nextReminderText.FontSize = 17;
        _nextReminderText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _nextReminderText.Foreground = Brush(Ink);
        _nextReminderText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_nextReminderText);
        var times = new Grid { Height = 76, Margin = new Thickness(0, 2, 0, 0) };
        times.Children.Add(new Ellipse { Width = 76, Height = 76, Fill = Brush(Color.FromArgb(40, 31, 122, 227)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
        times.Children.Add(new FontIcon { Glyph = "\uE823", FontFamily = FluentIcons(), FontSize = 43, Foreground = Brush(Color.FromArgb(255, 101, 155, 238)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 15, 0) });
        panel.Children.Add(times);
        _summaryText.FontSize = 14;
        _summaryText.Foreground = Brush(MutedInk);
        _summaryText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_summaryText);
        reminder.Child = panel;
        return reminder;
    }

    private UIElement TipCard()
    {
        var card = SurfaceCard(new Thickness(24, 17, 24, 17), 16);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = "\uE90F", FontFamily = FluentIcons(), FontSize = 28, Foreground = Brush(Blue), VerticalAlignment = VerticalAlignment.Center });
        var copy = new StackPanel { Spacing = 3, Margin = new Thickness(16, 0, 0, 0) };
        copy.Children.Add(new TextBlock { Text = "小贴士", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) });
        _tipText.FontSize = 14;
        _tipText.Foreground = Brush(MutedInk);
        _tipText.Text = "保持专注，避免分心，让每个番茄钟都有价值。";
        copy.Children.Add(_tipText);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        var dismiss = new FontIcon { Glyph = "\uE711", FontFamily = FluentIcons(), FontSize = 18, Foreground = Brush(Color.FromArgb(255, 142, 164, 198)), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(dismiss, 2);
        row.Children.Add(dismiss);
        card.Child = row;
        return card;
    }

    private UIElement BuildRemindersPage()
    {
        var root = new Grid { MaxWidth = 1350, HorizontalAlignment = HorizontalAlignment.Left };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(830) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var title = PageTitle("提醒中心");
        title.Margin = new Thickness(2, 0, 0, 30);
        Grid.SetColumnSpan(title, 2);
        root.Children.Add(title);

        var left = new StackPanel { Spacing = 28 };
        left.Children.Add(BuildReminderEditor());
        left.Children.Add(BuildReminderList());
        Grid.SetRow(left, 1);
        root.Children.Add(left);
        var summary = BuildTodayDigestCard();
        summary.Margin = new Thickness(32, 0, 0, 0);
        Grid.SetColumn(summary, 1);
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private UIElement BuildReminderEditor()
    {
        var card = SurfaceCard(new Thickness(28, 25, 28, 22), 18);
        var editor = new StackPanel { Spacing = 16 };
        editor.Children.Add(new TextBlock { Text = "创建提醒", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) });
        var title = new TextBox
        {
            PlaceholderText = "输入提醒标题",
            PlaceholderForeground = Brush(MutedInk),
            Foreground = Brush(Ink),
            Background = Brush(Colors.White),
            BorderBrush = Brush(Stroke),
            BorderThickness = new Thickness(1),
            FontSize = 16,
            MinHeight = 54
        };
        editor.Children.Add(title);
        var date = new DatePicker { Date = DateTimeOffset.Now.AddMinutes(10), HorizontalAlignment = HorizontalAlignment.Stretch };
        var time = new TimePicker { Time = DateTimeOffset.Now.AddMinutes(10).TimeOfDay, HorizontalAlignment = HorizontalAlignment.Stretch };
        var recurrence = new ComboBox { SelectedIndex = 0, ItemsSource = new[] { "不重复", "每周", "每月" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var task = new ComboBox { PlaceholderText = "关联 Todo（可选）", HorizontalAlignment = HorizontalAlignment.Stretch };
        PopulateTaskSelector(task, includeNone: true);
        var fields = new Grid { ColumnSpacing = 14 };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.32, GridUnitType.Star) });
        fields.Children.Add(LabeledControl("日期", date));
        var timeWrap = LabeledControl("时间", time); Grid.SetColumn(timeWrap, 1); fields.Children.Add(timeWrap);
        var recurrenceWrap = LabeledControl("循环", recurrence); Grid.SetColumn(recurrenceWrap, 2); fields.Children.Add(recurrenceWrap);
        var taskWrap = LabeledControl("关联待办", task); Grid.SetColumn(taskWrap, 3); fields.Children.Add(taskWrap);
        editor.Children.Add(fields);
        var recurrenceDays = new TextBox
        {
            PlaceholderText = "循环日期：每周 1,2；每月 1,15（可选）",
            PlaceholderForeground = Brush(MutedInk),
            Foreground = Brush(Ink),
            Background = Brush(Colors.White),
            BorderBrush = Brush(Stroke),
            BorderThickness = new Thickness(1),
            FontSize = 14,
            MinHeight = 42
        };
        editor.Children.Add(recurrenceDays);
        var create = PrimaryActionButton("创建提醒", "\uE710", async (_, _) =>
        {
            try
            {
                var selected = task.SelectedItem as TodoReminderTask;
                var selectedDate = date.Date.Date;
                var occurrence = new DateTimeOffset(selectedDate.Year, selectedDate.Month, selectedDate.Day, time.Time.Hours, time.Time.Minutes, 0, TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
                var rule = ParseRecurrence(recurrence.SelectedIndex, recurrenceDays.Text);
                var snapshot = Workspace.CreateReminder(title.Text, occurrence, rule, selected?.Id);
                if (selected is null && Workspace.ShouldAskToLinkStandaloneReminder(DateOnly.FromDateTime(DateTime.Now)))
                    await OfferTodoLinkAsync(snapshot);
                await OfferResidentConfigurationAsync();
                title.Text = string.Empty;
                _statusText.Text = "提醒已保存。";
                Navigate("reminders");
            }
            catch (Exception error) { _statusText.Text = error.Message; }
        }, Blue, "创建提醒");
        create.HorizontalAlignment = HorizontalAlignment.Right;
        create.Margin = new Thickness(0, 3, 0, 0);
        editor.Children.Add(create);
        card.Child = editor;
        return card;
    }

    private UIElement BuildReminderList()
    {
        var panel = new StackPanel { Spacing = 17 };
        panel.Children.Add(new TextBlock { Text = "全部提醒", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) });
        if (Workspace.Reminders.Count == 0)
        {
            panel.Children.Add(EmptyReminderCard());
            return panel;
        }
        foreach (var reminder in Workspace.Reminders.OrderBy(item => item.SnoozedUntil ?? item.FirstOccursAt))
            panel.Children.Add(ReminderRow(reminder));
        return panel;
    }

    private UIElement ReminderRow(ReminderSnapshot reminder)
    {
        var card = SurfaceCard(new Thickness(25, 22, 25, 20), 16);
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.Children.Add(SoftIcon(reminder.TodoTaskId is null ? "\uE7ED" : "\uE8FD", reminder.TodoTaskId is null ? CoralSoft : BlueSoft, reminder.TodoTaskId is null ? Coral : Blue));
        var copy = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = reminder.Title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) });
        copy.Children.Add(new TextBlock { Text = $"{(reminder.SnoozedUntil ?? reminder.FirstOccursAt).LocalDateTime:g}  ·  {(reminder.TodoTaskId is null ? "独立提醒" : "关联任务")}", Foreground = Brush(MutedInk), FontSize = 14 });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var actions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (reminder.TodoTaskId is not null) actions.Children.Add(CompactAction("开始专注", "\uE768", (_, _) => StartFocus()));
        actions.Children.Add(CompactAction("稍后 10 分钟", "\uE823", (_, _) => Workspace.SnoozeReminder(reminder.Id, DateTimeOffset.Now)));
        actions.Children.Add(CompactAction(reminder.IsEnabled ? "关闭" : "开启", reminder.IsEnabled ? "\uE711" : "\uE7E8", (_, _) => Workspace.SetReminderEnabled(reminder.Id, !reminder.IsEnabled)));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private UIElement EmptyReminderCard()
    {
        var card = SurfaceCard(new Thickness(28), 16);
        card.Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "还没有提醒", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) },
                new TextBlock { Text = "在上方创建一条提醒，或关联一个 Todo 任务。", FontSize = 14, Foreground = Brush(MutedInk) }
            }
        };
        return card;
    }

    private FrameworkElement BuildTodayDigestCard()
    {
        var card = SurfaceCard(new Thickness(28, 25, 28, 24), 18);
        var panel = new StackPanel { Spacing = 21 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new TextBlock { Text = "今日摘要", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) });
        var sun = SoftIcon("\uE706", Color.FromArgb(255, 255, 247, 232), Color.FromArgb(255, 238, 159, 17));
        Grid.SetColumn(sun, 1);
        header.Children.Add(sun);
        panel.Children.Add(header);
        var reminders = Workspace.Reminders.Where(item => item.IsEnabled).OrderBy(item => item.SnoozedUntil ?? item.FirstOccursAt).Take(4).ToArray();
        if (reminders.Length == 0)
        {
            panel.Children.Add(new TextBlock { Text = "今天没有有效提醒。启用每日摘要后，开始任务与截止任务会在设定时间合并展示。", TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Brush(MutedInk), LineHeight = 23 });
        }
        else
        {
            foreach (var reminder in reminders)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = (reminder.SnoozedUntil ?? reminder.FirstOccursAt).ToLocalTime().ToString("HH:mm"), FontSize = 15, Foreground = Brush(reminder.TodoTaskId is null ? Coral : Blue), VerticalAlignment = VerticalAlignment.Top });
                var copy = new StackPanel { Spacing = 5 };
                copy.Children.Add(new TextBlock { Text = reminder.Title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink), TextWrapping = TextWrapping.Wrap });
                copy.Children.Add(new TextBlock { Text = reminder.TodoTaskId is null ? "独立提醒" : "关联 Todo", FontSize = 13, Foreground = Brush(MutedInk) });
                Grid.SetColumn(copy, 1);
                row.Children.Add(copy);
                panel.Children.Add(row);
            }
        }
        var tip = new Border { Background = Brush(BlueSoft), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 16, 0, 0) };
        tip.Child = new TextBlock { Text = "小贴士\n合理安排时间，专注当下，收获更多。", FontSize = 14, Foreground = Brush(Blue), LineHeight = 22 };
        panel.Children.Add(tip);
        card.Child = panel;
        return card;
    }

    private UIElement BuildRecordsPage()
    {
        var state = Workspace.State;
        var root = new StackPanel { Spacing = 24, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(PageTitle("记录"));
        var hero = SurfaceCard(new Thickness(28), 18);
        hero.Child = new TextBlock { Text = $"今日完成 {state.TodayCompletedFocuses} 个专注 / {state.TodayCompletedMinutes} 分钟    ·    本周 {state.ThisWeekCompletedFocuses} 个专注 / {state.ThisWeekCompletedMinutes} 分钟", FontSize = 19, Foreground = Brush(Ink), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(hero);
        foreach (var session in state.Sessions.OrderByDescending(item => item.EndedAt).Take(30))
        {
            var row = SurfaceCard(new Thickness(22, 16, 22, 16), 14);
            row.Child = new TextBlock { Text = $"{session.EndedAt.LocalDateTime:g}    ·    {PhaseLabel(session.Phase)}    ·    {(session.IsCompleted ? "完成" : "中断")}    ·    {session.TodoTaskTitle ?? "未关联待办"}", FontSize = 15, Foreground = Brush(Ink), TextWrapping = TextWrapping.Wrap };
            root.Children.Add(row);
        }
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement BuildSettingsPage()
    {
        var settings = Workspace.State.Settings;
        var focus = new NumberBox { Value = settings.FocusMinutes, Minimum = 1, Maximum = 180 };
        var shortBreak = new NumberBox { Value = settings.ShortBreakMinutes, Minimum = 1, Maximum = 60 };
        var longBreak = new NumberBox { Value = settings.LongBreakMinutes, Minimum = 1, Maximum = 120 };
        var cycle = new NumberBox { Value = settings.FocusesPerCycle, Minimum = 2, Maximum = 12 };
        var auto = new ToggleSwitch { IsOn = settings.AutoStartNextPhase, OnContent = "开", OffContent = "关" };
        var reminder = Workspace.ReminderSettings;
        var due = new ToggleSwitch { IsOn = reminder.IsDueDigestEnabled, OnContent = "开", OffContent = "关" };
        var start = new ToggleSwitch { IsOn = reminder.IsStartDigestEnabled, OnContent = "开", OffContent = "关" };
        var dueTime = new TimePicker { Time = reminder.DueDigestTime };
        var startTime = new TimePicker { Time = reminder.StartDigestTime };
        var root = new StackPanel { Spacing = 24, MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(PageTitle("设置"));
        var card = SurfaceCard(new Thickness(28), 18);
        var panel = new StackPanel { Spacing = 17 };
        panel.Children.Add(SettingRow("专注分钟", focus));
        panel.Children.Add(SettingRow("短休息分钟", shortBreak));
        panel.Children.Add(SettingRow("长休息分钟", longBreak));
        panel.Children.Add(SettingRow("长休息前专注轮数", cycle));
        panel.Children.Add(SettingRow("自动开始下一阶段", auto));
        panel.Children.Add(new Border { Height = 1, Background = Brush(Stroke), Margin = new Thickness(0, 6, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "每日摘要", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(Ink) });
        panel.Children.Add(SettingRow("每日截止任务摘要", due));
        panel.Children.Add(SettingRow("截止摘要时间", dueTime));
        panel.Children.Add(SettingRow("每日开始任务摘要", start));
        panel.Children.Add(SettingRow("开始摘要时间", startTime));
        var save = PrimaryActionButton("保存设置", "\uE74E", async (_, _) =>
        {
            Workspace.UpdateSettings(new PomodoroSettings { FocusMinutes = (int)focus.Value, ShortBreakMinutes = (int)shortBreak.Value, LongBreakMinutes = (int)longBreak.Value, FocusesPerCycle = (int)cycle.Value, AutoStartNextPhase = auto.IsOn });
            Workspace.UpdateReminderSettings(current => { current.IsDueDigestEnabled = due.IsOn; current.DueDigestTime = dueTime.Time; current.IsStartDigestEnabled = start.IsOn; current.StartDigestTime = startTime.Time; });
            if (due.IsOn || start.IsOn) await OfferResidentConfigurationAsync();
            _statusText.Text = "设置已保存。启用提醒后，请让 Toolbox 保持驻留，才能按时唤醒提醒。";
        }, Blue, "保存设置");
        save.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(save);
        if (reminder.IsStandaloneLinkPromptSuppressedForever)
            panel.Children.Add(TextAction("恢复独立提醒关联询问", (_, _) => { Workspace.RestoreStandaloneLinkPrompt(); _statusText.Text = "下次创建独立提醒时会再次询问。"; }));
        card.Child = panel;
        root.Children.Add(card);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void StartFocus()
    {
        try
        {
            var selected = _focusTask.SelectedItem as TodoReminderTask;
            if (Workspace.State.Phase.State == PomodoroRunState.AwaitingNextPhase) Workspace.StartNextPhase();
            else if (Workspace.State.Phase.State == PomodoroRunState.Paused) Workspace.Resume();
            else if (Workspace.State.Phase.State == PomodoroRunState.Running) Workspace.Pause();
            else Workspace.StartFocus(selected?.Id, selected?.Title);
        }
        catch (Exception error) { _statusText.Text = error.Message; }
    }

    private void Tick()
    {
        var prior = Workspace.State.Phase;
        if (Workspace.AdvanceIfDue(DateTimeOffset.Now) && prior.State == PomodoroRunState.Running)
            _notifications.TryShowPhaseCompleted(prior.Phase);
        if (_currentPage == "focus") Render();
    }

    private void Render()
    {
        if (_currentPage != "focus") return;
        var state = Workspace.State;
        _phaseText.Text = state.Phase.State == PomodoroRunState.Idle ? "准备开始" : PhaseLabel(state.Phase.Phase) + StateSuffix(state.Phase.State);
        _clockText.Text = $"{(int)state.Phase.Remaining.TotalMinutes:00}:{state.Phase.Remaining.Seconds:00}";
        var currentRound = Math.Min(state.Settings.FocusesPerCycle, state.Phase.CompletedFocusesInCycle + 1);
        _roundText.Inlines.Clear();
        _roundText.Inlines.Add(new Run { Text = "第 " });
        _roundText.Inlines.Add(new Run { Text = currentRound.ToString(), Foreground = Brush(Coral), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _roundText.Inlines.Add(new Run { Text = $" / {state.Settings.FocusesPerCycle} 个番茄" });
        _taskText.Text = state.Phase.TodoTaskTitle ?? "还未关联待办";
        var next = Workspace.Reminders.Where(item => item.IsEnabled && item.SuspensionReason == ReminderSuspensionReason.None).OrderBy(item => item.SnoozedUntil ?? item.FirstOccursAt).FirstOrDefault();
        _nextReminderText.Text = next is null
            ? "暂无下一提醒\n创建提醒后会在这里显示"
            : $"{(next.SnoozedUntil ?? next.FirstOccursAt).ToLocalTime():HH:mm}\n{next.Title}";
        _summaryText.Text = $"今天 · {state.TodayCompletedFocuses} 个专注 / {state.TodayCompletedMinutes} 分钟\n本周 · {state.ThisWeekCompletedFocuses} 个专注 / {state.ThisWeekCompletedMinutes} 分钟";
    }

    private async Task OfferTodoLinkAsync(ReminderSnapshot reminder)
    {
        var tasks = _todoReader.ActiveTasks();
        if (tasks.Count == 0) return;
        var selection = new ComboBox { ItemsSource = tasks, DisplayMemberPath = nameof(TodoReminderTask.Title), SelectedIndex = 0, Header = "选择要关联的待办" };
        var createTask = new CheckBox { Content = "创建一个新的 Todo 任务" };
        var todayOnly = new CheckBox { Content = "今日不再询问" };
        var forever = new CheckBox { Content = "以后不再询问" };
        var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "是否将这个独立提醒关联到待办？" }, selection, createTask, todayOnly, forever } };
        var dialog = new ContentDialog { Title = "关联待办", Content = body, PrimaryButtonText = "关联", SecondaryButtonText = "暂不关联", CloseButtonText = "取消", XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (todayOnly.IsChecked == true || forever.IsChecked == true) Workspace.SuppressStandaloneLinkPrompt(DateOnly.FromDateTime(DateTime.Now), forever.IsChecked == true);
        if (result != ContentDialogResult.Primary) return;
        var task = createTask.IsChecked == true ? _todoReader.CreateForReminder(reminder.Title) : selection.SelectedItem as TodoReminderTask;
        if (task is null) return;
        Workspace.UpdateReminder(reminder with { TodoTaskId = task.Id });
    }

    private async Task OfferResidentConfigurationAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "保持提醒可用",
            Content = "启用提醒后，Toolbox 需要在登录时启动并在关闭时驻留到托盘；否则提醒无法自动工作。",
            PrimaryButtonText = "自动配置",
            SecondaryButtonText = "手动管理",
            XamlRoot = _page.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _statusText.Text = _residentConfigurator.Configure()
                ? "已请求 Toolbox 自动配置常驻提醒。"
                : "未找到 Toolbox。请从 Toolbox 设置中开启登录启动和关闭到托盘。";
        }
        else _statusText.Text = "不保持 Toolbox 常驻时，提醒无法自动工作；可在设置页重新选择自动配置。";
    }

    private void PopulateTaskSelector(ComboBox comboBox, bool includeNone)
    {
        comboBox.ItemsSource = _todoReader.ActiveTasks();
        comboBox.DisplayMemberPath = nameof(TodoReminderTask.Title);
        if (includeNone) comboBox.SelectedIndex = -1;
    }

    private static ReminderRecurrenceRule? ParseRecurrence(int index, string? days)
    {
        var values = (days ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var number) ? number : 0).Where(value => value > 0).ToArray();
        return index switch
        {
            1 => new ReminderRecurrenceRule { Frequency = ReminderRecurrenceFrequencies.Weekly, Weekdays = values.Where(value => value <= 7).Select(value => (DayOfWeek)(value % 7)).ToList() },
            2 => new ReminderRecurrenceRule { Frequency = ReminderRecurrenceFrequencies.Monthly, MonthDays = values.Where(value => value <= 28).ToList() },
            _ => null
        };
    }

    private UIElement BuildTickDial()
    {
        var canvas = new Canvas { Width = 470, Height = 470 };
        const double center = 235;
        for (var index = 0; index < 60; index++)
        {
            var tick = new Line
            {
                X1 = center,
                Y1 = 24,
                X2 = center,
                Y2 = index % 5 == 0 ? 39 : 35,
                Stroke = Brush(index < 24 ? Coral : Color.FromArgb(255, 194, 208, 229)),
                StrokeThickness = index % 5 == 0 ? 2.7 : 1.8,
                RenderTransform = new RotateTransform { Angle = index * 6, CenterX = center, CenterY = center }
            };
            canvas.Children.Add(tick);
        }
        var marker = new Ellipse { Width = 17, Height = 17, Fill = Brush(Colors.White), Stroke = Brush(Coral), StrokeThickness = 3, RenderTransform = new RotateTransform { Angle = 138, CenterX = center, CenterY = center } };
        Canvas.SetLeft(marker, center - 8.5);
        Canvas.SetTop(marker, 20);
        canvas.Children.Add(marker);
        return canvas;
    }

    private static FrameworkElement TomatoMark(double size)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Width = size * .88, Height = size * .77, Fill = Brush(Coral), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center });
        grid.Children.Add(new Ellipse { Width = size * .50, Height = size * .22, Fill = Brush(Color.FromArgb(255, 10, 99, 104)), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, size * .05, 0, 0) });
        grid.Children.Add(new Rectangle { Width = size * .12, Height = size * .32, Fill = Brush(Color.FromArgb(255, 10, 99, 104)), RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = new RotateTransform { Angle = 38, CenterX = size * .06, CenterY = size * .16 } });
        return grid;
    }

    private static Border SurfaceCard(Thickness padding, double radius) => new()
    {
        Background = Brush(Colors.White),
        BorderBrush = Brush(Stroke),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = padding
    };

    private static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 40,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = Brush(Ink)
    };

    private static Button IconButton(string name, string glyph)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontFamily = FluentIcons(), FontSize = 23 }, Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Foreground = Brush(Color.FromArgb(255, 72, 94, 129)), Background = new SolidColorBrush(Colors.Transparent) };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button PrimaryActionButton(string text, string glyph, RoutedEventHandler handler, Color background, string name, Color? foreground = null)
    {
        var button = new Button
        {
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 12,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontFamily = FluentIcons(), FontSize = 21 },
                    new TextBlock { Text = text, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                }
            },
            Padding = new Thickness(16, 0, 16, 0),
            Background = Brush(background),
            Foreground = Brush(foreground ?? Colors.White),
            CornerRadius = new CornerRadius(30)
        };
        button.Click += handler;
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button CompactAction(string text, string glyph, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Height = 38,
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 7,
                Children = { new FontIcon { Glyph = glyph, FontFamily = FluentIcons(), FontSize = 15 }, new TextBlock { Text = text, FontSize = 14 } }
            },
            Background = Brush(Colors.White),
            Foreground = Brush(Ink),
            BorderBrush = Brush(Stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button TextAction(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 4, 0, 4), Foreground = Brush(Blue), Background = new SolidColorBrush(Colors.Transparent), FontSize = 15 };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Border SoftIcon(string glyph, Color background, Color foreground) => new()
    {
        Width = 56,
        Height = 56,
        CornerRadius = new CornerRadius(16),
        Background = Brush(background),
        Child = new FontIcon { Glyph = glyph, FontFamily = FluentIcons(), FontSize = 26, Foreground = Brush(foreground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };

    private static Border Tag(string text, Color background, Color foreground) => new()
    {
        Padding = new Thickness(12, 6, 12, 6),
        Background = Brush(background),
        CornerRadius = new CornerRadius(8),
        Child = new TextBlock { Text = text, FontSize = 14, Foreground = Brush(foreground) }
    };

    private static FrameworkElement LabeledControl(string label, Control control)
    {
        control.Foreground = Brush(Ink);
        control.Background = Brush(Colors.White);
        control.BorderBrush = Brush(Stroke);
        control.BorderThickness = new Thickness(1);
        control.MinHeight = 46;
        return new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = label, FontSize = 13, Foreground = Brush(MutedInk) }, control }
        };
    }

    private static UIElement SettingRow(string label, Control control)
    {
        control.Foreground = Brush(Ink);
        control.Background = Brush(Colors.White);
        control.BorderBrush = Brush(Stroke);
        control.BorderThickness = new Thickness(1);
        control.MinHeight = 38;
        control.MinWidth = 180;
        var row = new Grid { MinHeight = 42 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 16, Foreground = Brush(Ink), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static SolidColorBrush Brush(Color color) => new(color);
    private static FontFamily FluentIcons() => new("Segoe Fluent Icons");
    private static string PhaseLabel(string phase) => phase switch { PomodoroPhaseIds.Focus => "专注中", PomodoroPhaseIds.LongBreak => "长休息", _ => "短休息" };
    private static string StateSuffix(PomodoroRunState state) => state switch { PomodoroRunState.Paused => "（已暂停）", PomodoroRunState.AwaitingNextPhase => "（等待开始）", _ => string.Empty };
}
