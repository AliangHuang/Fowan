using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyAddTaskWindow : Window, IStickyChildWindow
{
    private const double BaseWindowWidth = 336;
    private const double BaseTaskWindowHeight = 570;
    private const double BaseSubtaskWindowHeight = 330;

    private enum DatePart
    {
        Year,
        Month,
        Day
    }

    private readonly StickyWindow _owner;
    private readonly IReadOnlyList<TodoList> _lists;
    private readonly string _defaultListId;
    private readonly string? _parentTaskId;
    private readonly ScaleTransform _windowScale = new(1, 1);
    private readonly Border _panel = new();
    private readonly Border _formPanel = new();
    private readonly Border _inputBorder = new();
    private readonly Border _notesBorder = new();
    private readonly Border _listBorder = new();
    private readonly Border _startDateBorder = new();
    private readonly Border _dueDateBorder = new();
    private readonly Border _repeatModeBorder = new();
    private readonly Border _repeatValueBorder = new();
    private readonly Border _repeatDueBorder = new();
    private readonly Border _importantBorder = new();
    private readonly Border _importantTrack = new();
    private readonly Border _importantThumb = new();
    private readonly TextBlock _heading = new();
    private readonly TextBlock _titleLabel = new();
    private readonly TextBlock _notesLabel = new();
    private readonly TextBlock _importantLabel = new();
    private readonly TextBlock _repeatValueLabel = new();
    private readonly TextBlock _repeatDueLabel = new();
    private readonly StackPanel _startDateSegments = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _dueDateSegments = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _titleBox = new();
    private readonly TextBox _notesBox = new();
    private readonly ComboBox _listBox = new();
    private readonly ComboBox _repeatModeBox = new();
    private readonly ComboBox _repeatValueBox = new();
    private readonly ComboBox _repeatDueBox = new();
    private readonly Button _startDateIncreaseButton = new();
    private readonly Button _startDateDecreaseButton = new();
    private readonly Button _dueDateIncreaseButton = new();
    private readonly Button _dueDateDecreaseButton = new();
    private readonly ToggleButton _importantToggle = new();
    private readonly Button _cancelButton = new();
    private readonly Button _addButton = new();
    private readonly Button _closeButton = new();
    private readonly HashSet<Border> _focusedFields = [];
    private readonly List<TextBlock> _fieldLabels = [];
    private DateTime _startDate = DateTime.Today;
    private DateTime? _dueDate;
    private DatePart _startDatePart = DatePart.Day;
    private DatePart _dueDatePart = DatePart.Day;
    private StackPanel? _repeatModeField;
    private StackPanel? _repeatValueField;
    private StackPanel? _repeatDueField;
    private Grid? _manualDateFields;

    private double BaseHeight => _parentTaskId is null ? BaseTaskWindowHeight : BaseSubtaskWindowHeight;

    public StickyAddTaskWindow(StickyWindow owner, TodoData data, TodoTask? parent = null)
    {
        _owner = owner;
        _lists = data.Lists.ToArray();
        _defaultListId = _lists.Count > 0 ? TodoQuery.DefaultListId(data) : string.Empty;
        _parentTaskId = parent?.Id;
        Width = BaseWindowWidth;
        Height = BaseHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = BuildContent();
        Loaded += (_, _) => FocusTitleBox();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close();
            args.Handled = true;
        };
    }

    private UIElement BuildContent()
    {
        var scaleHost = new Grid
        {
            Width = BaseWindowWidth,
            Height = BaseHeight,
            LayoutTransform = _windowScale
        };

        _panel.CornerRadius = new CornerRadius(12);
        _panel.BorderThickness = new Thickness(0);
        _panel.Padding = new Thickness(16);
        _panel.Effect = new DropShadowEffect
        {
            BlurRadius = 24,
            ShadowDepth = 8,
            Opacity = 0.2,
            Color = Color.FromRgb(31, 55, 62)
        };

        var content = new StackPanel();
        content.Children.Add(BuildHeading());
        content.Children.Add(BuildForm());
        content.Children.Add(BuildActions());
        _panel.Child = content;
        scaleHost.Children.Add(_panel);
        return scaleHost;
    }

    private UIElement BuildHeading()
    {
        var heading = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 10) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _heading.Text = _parentTaskId is null ? "添加任务" : "创建子任务";
        _heading.FontSize = 18;
        _heading.FontWeight = FontWeights.SemiBold;
        _heading.VerticalAlignment = VerticalAlignment.Center;
        _heading.Margin = new Thickness(0);
        heading.Children.Add(_heading);

        ConfigureCloseButton();
        _closeButton.Click += (_, _) => Close();
        Grid.SetColumn(_closeButton, 1);
        heading.Children.Add(_closeButton);
        return heading;
    }

    private UIElement BuildForm()
    {
        _formPanel.CornerRadius = new CornerRadius(10);
        _formPanel.BorderThickness = new Thickness(1);
        _formPanel.Padding = new Thickness(12);
        var fields = new StackPanel();

        _titleLabel.Text = _parentTaskId is null ? "任务名称" : "子任务名称";
        AddLabel(fields, _titleLabel, topMargin: 0);
        ConfigureTextInput(_inputBorder, _titleBox, 40, FocusTitleBox);
        AutomationProperties.SetName(_titleBox, _titleLabel.Text);
        _titleBox.FontSize = 15;
        _titleBox.VerticalContentAlignment = VerticalAlignment.Center;
        _titleBox.Padding = new Thickness(0);
        _titleBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            FocusNotesBox();
            args.Handled = true;
        };
        fields.Children.Add(_inputBorder);

        _notesLabel.Text = "备注（可选）";
        AddLabel(fields, _notesLabel);
        ConfigureTextInput(_notesBorder, _notesBox, _parentTaskId is null ? 64 : 76, FocusNotesBox);
        AutomationProperties.SetName(_notesBox, _notesLabel.Text);
        _notesBox.AcceptsReturn = true;
        _notesBox.TextWrapping = TextWrapping.Wrap;
        _notesBox.FontSize = 13;
        _notesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _notesBox.VerticalContentAlignment = VerticalAlignment.Top;
        _notesBox.Padding = new Thickness(0, 3, 0, 3);
        _notesBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                TryAddTask();
                args.Handled = true;
            }
        };
        fields.Children.Add(_notesBorder);

        if (_parentTaskId is null) AddTaskFields(fields);

        _formPanel.Child = fields;
        return _formPanel;
    }

    private UIElement BuildActions()
    {
        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
        _cancelButton.Click += (_, _) => Close();
        Grid.SetColumn(_cancelButton, 1);
        actions.Children.Add(_cancelButton);
        ConfigureActionButton(_addButton, _parentTaskId is null ? "添加" : "提交", isPrimary: true);
        _addButton.Click += (_, _) => TryAddTask();
        Grid.SetColumn(_addButton, 3);
        actions.Children.Add(_addButton);
        return actions;
    }

    public void ApplyStickyOwnerState(bool reposition)
    {
        var scale = _owner.Settings.StickyScale;
        Width = BaseWindowWidth * scale;
        Height = BaseHeight * scale;
        Topmost = _owner.Topmost;
        _windowScale.ScaleX = scale;
        _windowScale.ScaleY = scale;

        _panel.Background = ModalSurfaceBrush();
        _panel.BorderBrush = Brushes.Transparent;
        _formPanel.Background = ModalPanelBrush();
        _formPanel.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.10, 0.0, 1.0));
        _heading.Foreground = _owner.TextBrush();
        foreach (var label in _fieldLabels) label.Foreground = _owner.SecondaryTextBrush();
        ConfigureCloseButton();
        ApplyFieldTheme(_inputBorder);
        ApplyFieldTheme(_notesBorder);
        ApplyFieldTheme(_listBorder);
        ApplyFieldTheme(_startDateBorder);
        ApplyFieldTheme(_dueDateBorder);
        ApplyFieldTheme(_repeatModeBorder);
        ApplyFieldTheme(_repeatValueBorder);
        ApplyFieldTheme(_repeatDueBorder);
        _titleBox.Foreground = _owner.TextBrush();
        _titleBox.CaretBrush = _owner.AccentBrush();
        _notesBox.Foreground = _owner.TextBrush();
        _notesBox.CaretBrush = _owner.AccentBrush();
        _listBox.Foreground = _owner.TextBrush();
        _repeatModeBox.Foreground = _owner.TextBrush();
        _repeatValueBox.Foreground = _owner.TextBrush();
        _repeatDueBox.Foreground = _owner.TextBrush();
        ApplyPickerTheme(_listBox);
        ApplyPickerTheme(_repeatModeBox);
        ApplyPickerTheme(_repeatValueBox);
        ApplyPickerTheme(_repeatDueBox);
        ApplyDateSpinnerTheme(_startDateBorder);
        ApplyDateSpinnerTheme(_dueDateBorder);
        RefreshDateSpinnerText();
        ApplyImportantToggleTheme();
        ConfigureActionButton(_cancelButton, "取消", isPrimary: false);
        ConfigureActionButton(_addButton, _parentTaskId is null ? "添加" : "提交", isPrimary: true);

        if (reposition) _owner.PositionAddTaskWindow(this);
    }

    public void FocusTitleBox()
    {
        Activate();
        if (!_titleBox.Focus()) Keyboard.Focus(_titleBox);
        _titleBox.CaretIndex = _titleBox.Text.Length;
    }

    private void FocusNotesBox()
    {
        Activate();
        if (!_notesBox.Focus()) Keyboard.Focus(_notesBox);
        _notesBox.CaretIndex = _notesBox.Text.Length;
    }

    private void TryAddTask()
    {
        var added = _parentTaskId is null
            ? _owner.AddTask(
                _titleBox.Text,
                SelectedListId(),
                _importantToggle.IsChecked == true,
                _startDate,
                _dueDate,
                _notesBox.Text,
                SelectedRecurrence())
            : _owner.AddTask(_titleBox.Text, _parentTaskId, _notesBox.Text);
        if (added)
        {
            Close();
            return;
        }

        FocusTitleBox();
    }

    private void AddTaskFields(StackPanel fields)
    {
        var listLabel = CreateLabel("所属清单");
        AddLabel(fields, listLabel);
        ConfigureListInput();
        AutomationProperties.SetName(_listBox, listLabel.Text);
        foreach (var list in _lists)
        {
            var item = new ComboBoxItem { Content = list.Name, Tag = list.Id };
            _listBox.Items.Add(item);
            if (string.Equals(list.Id, _defaultListId, StringComparison.Ordinal)) _listBox.SelectedItem = item;
        }
        if (_listBox.SelectedItem is null && _listBox.Items.Count > 0) _listBox.SelectedIndex = 0;
        fields.Children.Add(_listBorder);

        var dates = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        _manualDateFields = dates;
        dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var start = CreateDateSpinnerField(
            "开始时间",
            _startDateBorder,
            _startDateSegments,
            _startDateIncreaseButton,
            _startDateDecreaseButton,
            () => AdjustStartDate(1),
            () => AdjustStartDate(-1));
        Grid.SetColumn(start, 0);
        dates.Children.Add(start);
        var due = CreateDateSpinnerField(
            "截止日期",
            _dueDateBorder,
            _dueDateSegments,
            _dueDateIncreaseButton,
            _dueDateDecreaseButton,
            () => AdjustDueDate(1),
            () => AdjustDueDate(-1));
        Grid.SetColumn(due, 2);
        dates.Children.Add(due);
        fields.Children.Add(dates);

        fields.Children.Add(CreateRecurrenceFields());

        ConfigureImportantToggle();
        fields.Children.Add(_importantBorder);
        RefreshDateSpinnerText();
    }

    private void ConfigureTextInput(Border border, TextBox textBox, double height, Action focus)
    {
        border.Height = height;
        border.CornerRadius = new CornerRadius(8);
        border.BorderThickness = new Thickness(1);
        border.Padding = new Thickness(10, 0, 10, 0);
        border.Child = textBox;
        border.PreviewMouseLeftButtonDown += (_, _) => focus();
        textBox.BorderThickness = new Thickness(0);
        textBox.Background = Brushes.Transparent;
        textBox.FocusVisualStyle = null;
        textBox.Cursor = Cursors.IBeam;
        textBox.MinHeight = 30;
        TrackFocus(textBox, border);
    }

    private void ConfigureListInput()
    {
        ConfigureComboInput(_listBorder, _listBox);
    }

    private void ConfigureComboInput(Border border, ComboBox combo)
    {
        border.Height = 38;
        border.BorderThickness = new Thickness(0);
        border.Padding = new Thickness(0);
        border.Background = Brushes.Transparent;
        border.Child = combo;
        combo.BorderThickness = new Thickness(1);
        combo.Padding = new Thickness(8, 0, 8, 0);
        combo.VerticalContentAlignment = VerticalAlignment.Center;
        combo.FontSize = 12;
        TrackFocus(combo, border);
    }

    private UIElement CreateRecurrenceFields()
    {
        var fields = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _repeatModeField = new StackPanel();
        var modeLabel = CreateLabel("重复");
        AddLabel(_repeatModeField, modeLabel, topMargin: 0);
        ConfigureComboInput(_repeatModeBorder, _repeatModeBox);
        _repeatModeBox.Items.Add(new ComboBoxItem { Tag = string.Empty, Content = "不重复" });
        _repeatModeBox.Items.Add(new ComboBoxItem { Tag = TodoRecurrenceFrequencies.Weekly, Content = "每周" });
        _repeatModeBox.Items.Add(new ComboBoxItem { Tag = TodoRecurrenceFrequencies.Monthly, Content = "每月" });
        _repeatModeBox.SelectedIndex = 0;
        _repeatModeBox.SelectionChanged += (_, _) => UpdateRecurrenceValueChoices();
        _repeatModeField.Children.Add(_repeatModeBorder);
        fields.Children.Add(_repeatModeField);

        _repeatValueField = new StackPanel();
        _repeatValueLabel.Text = "循环开始";
        AddLabel(_repeatValueField, _repeatValueLabel, topMargin: 0);
        ConfigureComboInput(_repeatValueBorder, _repeatValueBox);
        _repeatValueField.Children.Add(_repeatValueBorder);
        Grid.SetColumn(_repeatValueField, 2);
        fields.Children.Add(_repeatValueField);

        _repeatDueField = new StackPanel();
        _repeatDueLabel.Text = "循环截止";
        AddLabel(_repeatDueField, _repeatDueLabel, topMargin: 0);
        ConfigureComboInput(_repeatDueBorder, _repeatDueBox);
        _repeatDueField.Children.Add(_repeatDueBorder);
        Grid.SetRow(_repeatDueField, 2);
        Grid.SetColumnSpan(_repeatDueField, 3);
        fields.Children.Add(_repeatDueField);
        UpdateRecurrenceValueChoices();
        return fields;
    }

    private void UpdateRecurrenceValueChoices()
    {
        var mode = _repeatModeBox.SelectedItem is ComboBoxItem { Tag: string selectedMode }
            ? selectedMode
            : string.Empty;
        _repeatValueBox.Items.Clear();
        _repeatDueBox.Items.Clear();
        _repeatDueBox.Items.Add(new ComboBoxItem { Tag = null, Content = "不设置" });
        if (string.Equals(mode, TodoRecurrenceFrequencies.Weekly, StringComparison.Ordinal))
        {
            _repeatValueLabel.Text = "循环开始";
            foreach (var day in new[]
                     {
                         DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                         DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                     })
            {
                _repeatValueBox.Items.Add(new ComboBoxItem { Tag = day, Content = $"周{WeekdayText(day)}" });
                _repeatDueBox.Items.Add(new ComboBoxItem { Tag = day, Content = $"周{WeekdayText(day)}" });
            }
        }
        else if (string.Equals(mode, TodoRecurrenceFrequencies.Monthly, StringComparison.Ordinal))
        {
            _repeatValueLabel.Text = "循环开始";
            for (var day = 1; day <= 28; day++)
            {
                _repeatValueBox.Items.Add(new ComboBoxItem { Tag = day, Content = $"{day} 日" });
                _repeatDueBox.Items.Add(new ComboBoxItem { Tag = day, Content = $"{day} 日" });
            }
        }

        if (_repeatValueBox.Items.Count > 0) _repeatValueBox.SelectedIndex = 0;
        _repeatDueBox.SelectedIndex = 0;
        if (_repeatValueField is not null)
        {
            _repeatValueField.Visibility = string.IsNullOrEmpty(mode) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_repeatDueField is not null)
        {
            _repeatDueField.Visibility = string.IsNullOrEmpty(mode) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_manualDateFields is not null)
        {
            _manualDateFields.Visibility = string.IsNullOrEmpty(mode) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_repeatModeField is not null)
        {
            Grid.SetColumnSpan(_repeatModeField, string.IsNullOrEmpty(mode) ? 3 : 1);
        }
    }

    private TodoRecurrenceRule? SelectedRecurrence()
    {
        var mode = _repeatModeBox.SelectedItem is ComboBoxItem { Tag: string selectedMode }
            ? selectedMode
            : string.Empty;
        return mode switch
        {
            TodoRecurrenceFrequencies.Weekly when _repeatValueBox.SelectedItem is ComboBoxItem { Tag: DayOfWeek day } => new TodoRecurrenceRule
            {
                Frequency = TodoRecurrenceFrequencies.Weekly,
                Weekdays = [day],
                WeeklyDueDay = _repeatDueBox.SelectedItem is ComboBoxItem { Tag: DayOfWeek dueDay }
                    ? dueDay
                    : null
            },
            TodoRecurrenceFrequencies.Monthly when _repeatValueBox.SelectedItem is ComboBoxItem { Tag: int day } => new TodoRecurrenceRule
            {
                Frequency = TodoRecurrenceFrequencies.Monthly,
                MonthDays = [day],
                MonthlyDueDay = _repeatDueBox.SelectedItem is ComboBoxItem { Tag: int dueDay }
                    ? dueDay
                    : null
            },
            _ => null
        };
    }

    private static string WeekdayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日"
    };

    private UIElement CreateDateSpinnerField(
        string label,
        Border border,
        StackPanel valueHost,
        Button increaseButton,
        Button decreaseButton,
        Action increase,
        Action decrease)
    {
        var field = new StackPanel();
        var fieldLabel = CreateLabel(label);
        fieldLabel.Margin = new Thickness(0, 0, 0, 4);
        _fieldLabels.Add(fieldLabel);
        field.Children.Add(fieldLabel);
        border.Height = 38;
        border.CornerRadius = new CornerRadius(8);
        border.BorderThickness = new Thickness(1);
        border.Padding = new Thickness(8, 0, 5, 0);
        border.PreviewMouseWheel += (_, args) =>
        {
            if (args.Delta > 0) increase();
            else if (args.Delta < 0) decrease();
            args.Handled = true;
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        valueHost.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(valueHost);

        var buttons = new Grid { VerticalAlignment = VerticalAlignment.Center };
        buttons.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        buttons.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        ConfigureDateSpinnerButton(increaseButton, "\uE70E", $"{label} 增加一天");
        ConfigureDateSpinnerButton(decreaseButton, "\uE70D", $"{label} 减少一天");
        increaseButton.Click += (_, _) => increase();
        decreaseButton.Click += (_, _) => decrease();
        TrackFocus(increaseButton, border);
        TrackFocus(decreaseButton, border);
        Grid.SetRow(increaseButton, 0);
        Grid.SetRow(decreaseButton, 1);
        buttons.Children.Add(increaseButton);
        buttons.Children.Add(decreaseButton);
        Grid.SetColumn(buttons, 1);
        layout.Children.Add(buttons);
        border.Child = layout;
        field.Children.Add(border);
        return field;
    }

    private void AdjustStartDate(int direction)
    {
        _startDate = AdjustDatePart(_startDate, _startDatePart, direction);
        if (_dueDate is { } dueDate && dueDate < _startDate) _dueDate = _startDate;
        RefreshDateSpinnerText();
    }

    private void AdjustDueDate(int direction)
    {
        if (_dueDate is null)
        {
            if (direction > 0) _dueDate = AdjustDatePart(_startDate, _dueDatePart, direction);
        }
        else
        {
            var nextDate = AdjustDatePart(_dueDate.Value, _dueDatePart, direction);
            _dueDate = nextDate < _startDate ? null : nextDate;
        }

        RefreshDateSpinnerText();
    }

    private void RefreshDateSpinnerText()
    {
        RenderDateSegments(_startDateSegments, _startDate, _startDatePart, isStartDate: true);
        RenderDateSegments(_dueDateSegments, _dueDate, _dueDatePart, isStartDate: false);
        ConfigureDateSpinnerButton(_startDateIncreaseButton, "\uE70E", $"开始时间增加{DatePartName(_startDatePart)}");
        ConfigureDateSpinnerButton(_startDateDecreaseButton, "\uE70D", $"开始时间减少{DatePartName(_startDatePart)}");
        ConfigureDateSpinnerButton(_dueDateIncreaseButton, "\uE70E", $"截止日期增加{DatePartName(_dueDatePart)}");
        ConfigureDateSpinnerButton(_dueDateDecreaseButton, "\uE70D", $"截止日期减少{DatePartName(_dueDatePart)}");
    }

    private void RenderDateSegments(StackPanel host, DateTime? date, DatePart selectedPart, bool isStartDate)
    {
        host.Children.Clear();
        if (date is null)
        {
            host.Children.Add(new TextBlock
            {
                Text = "未设置",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _owner.SecondaryTextBrush()
            });
            return;
        }

        AddDateSegment(host, date.Value.Year.ToString("0000"), DatePart.Year, selectedPart, isStartDate, 34);
        AddDateSeparator(host);
        AddDateSegment(host, date.Value.Month.ToString("00"), DatePart.Month, selectedPart, isStartDate, 18);
        AddDateSeparator(host);
        AddDateSegment(host, date.Value.Day.ToString("00"), DatePart.Day, selectedPart, isStartDate, 18);
    }

    private void AddDateSegment(
        Panel host,
        string text,
        DatePart part,
        DatePart selectedPart,
        bool isStartDate,
        double width)
    {
        var isSelected = part == selectedPart;
        var button = new Button
        {
            Width = width,
            Height = 22,
            Padding = new Thickness(0),
            BorderThickness = isSelected ? new Thickness(1) : new Thickness(0),
            BorderBrush = isSelected ? _owner.AccentBrush() : Brushes.Transparent,
            Background = Brushes.Transparent,
            Foreground = _owner.TextBrush(),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = text,
            Template = ButtonTemplate(
                typeof(Button),
                Brushes.Transparent,
                _owner.PanelBrush(0xEEF9FA),
                new CornerRadius(4))
        };
        AutomationProperties.SetName(button, $"{(isStartDate ? "开始时间" : "截止日期")}{DatePartName(part)} {text}");
        button.Click += (_, _) =>
        {
            if (isStartDate) _startDatePart = part;
            else _dueDatePart = part;
            RefreshDateSpinnerText();
        };
        host.Children.Add(button);
    }

    private void AddDateSeparator(Panel host) => host.Children.Add(new TextBlock
    {
        Text = "/",
        FontSize = 11,
        Margin = new Thickness(1, 0, 1, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = _owner.SecondaryTextBrush()
    });

    private static DateTime AdjustDatePart(DateTime date, DatePart part, int direction) => part switch
    {
        DatePart.Year => date.AddYears(direction),
        DatePart.Month => date.AddMonths(direction),
        _ => date.AddDays(direction)
    };

    private static string DatePartName(DatePart part) => part switch
    {
        DatePart.Year => "年",
        DatePart.Month => "月",
        _ => "日"
    };

    private void ApplyDateSpinnerTheme(Border border) => ApplyFieldTheme(border);

    private void ConfigureDateSpinnerButton(Button button, string glyph, string automationName)
    {
        button.Height = 18;
        button.Padding = new Thickness(0);
        button.BorderThickness = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.Foreground = _owner.SecondaryTextBrush();
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Content = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Template = ButtonTemplate(typeof(Button), Brushes.Transparent, _owner.PanelBrush(0xEEF9FA), new CornerRadius(4));
        AutomationProperties.SetName(button, automationName);
    }

    private void ConfigureImportantToggle()
    {
        _importantBorder.Height = 42;
        _importantBorder.CornerRadius = new CornerRadius(8);
        _importantBorder.BorderThickness = new Thickness(1);
        _importantBorder.Margin = new Thickness(0, 10, 0, 0);
        _importantBorder.Padding = new Thickness(12, 6, 10, 6);
        _importantBorder.Child = _importantToggle;
        _importantToggle.BorderThickness = new Thickness(0);
        _importantToggle.Background = Brushes.Transparent;
        _importantToggle.Padding = new Thickness(0);
        _importantToggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _importantToggle.VerticalContentAlignment = VerticalAlignment.Center;
        _importantToggle.Template = ButtonTemplate(typeof(ToggleButton), Brushes.Transparent, Brushes.Transparent, new CornerRadius(7));
        AutomationProperties.SetName(_importantToggle, "标为重要");
        _importantToggle.Checked += (_, _) => ApplyImportantToggleTheme();
        _importantToggle.Unchecked += (_, _) => ApplyImportantToggleTheme();

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _importantLabel.Text = "标为重要";
        _importantLabel.FontSize = 12;
        _importantLabel.FontWeight = FontWeights.SemiBold;
        _importantLabel.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(_importantLabel);

        _importantTrack.Width = 40;
        _importantTrack.Height = 22;
        _importantTrack.CornerRadius = new CornerRadius(11);
        _importantTrack.Padding = new Thickness(2);
        _importantTrack.VerticalAlignment = VerticalAlignment.Center;
        _importantThumb.Width = 18;
        _importantThumb.Height = 18;
        _importantThumb.CornerRadius = new CornerRadius(9);
        _importantThumb.HorizontalAlignment = HorizontalAlignment.Left;
        _importantTrack.Child = _importantThumb;
        Grid.SetColumn(_importantTrack, 1);
        content.Children.Add(_importantTrack);
        _importantToggle.Content = content;
    }

    private void ApplyImportantToggleTheme()
    {
        _importantBorder.Background = ModalSurfaceBrush();
        _importantBorder.BorderBrush = _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
        _importantLabel.Foreground = _owner.TextBrush();
        var checkedState = _importantToggle.IsChecked == true;
        _importantTrack.Background = checkedState ? _owner.AccentBrush() : _owner.Brush(0x8BAEB8);
        _importantThumb.Background = checkedState ? Brushes.White : _owner.Brush(0xF7FAFB);
        _importantThumb.HorizontalAlignment = checkedState ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private void ApplyFieldTheme(Border border)
    {
        if (ReferenceEquals(border, _listBorder) || ReferenceEquals(border, _repeatModeBorder) ||
            ReferenceEquals(border, _repeatValueBorder) || ReferenceEquals(border, _repeatDueBorder))
        {
            ApplyComboTheme(border, ComboForBorder(border));
            return;
        }

        border.Background = ModalSurfaceBrush();
        border.BorderThickness = _focusedFields.Contains(border) ? new Thickness(1.5) : new Thickness(1);
        border.BorderBrush = _focusedFields.Contains(border)
            ? _owner.AccentBrush()
            : _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
    }

    private ComboBox ComboForBorder(Border border) => ReferenceEquals(border, _listBorder)
        ? _listBox
        : ReferenceEquals(border, _repeatModeBorder)
            ? _repeatModeBox
            : ReferenceEquals(border, _repeatValueBorder)
                ? _repeatValueBox
                : _repeatDueBox;

    private void ApplyComboTheme(Border border, ComboBox combo)
    {
        border.Background = Brushes.Transparent;
        border.BorderBrush = Brushes.Transparent;
        combo.Background = ModalSurfaceBrush();
        combo.BorderThickness = _focusedFields.Contains(border) ? new Thickness(1.5) : new Thickness(1);
        combo.BorderBrush = _focusedFields.Contains(border)
            ? _owner.AccentBrush()
            : _owner.Brush(0xDCE7EA, Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));
    }

    private void ApplyPickerTheme(Control picker)
    {
        picker.Resources[SystemColors.HighlightBrushKey] = _owner.AccentBrush();
        picker.Resources[SystemColors.ControlBrushKey] = _owner.SurfaceBrush();
        picker.Resources[SystemColors.WindowBrushKey] = _owner.SurfaceBrush();
        picker.Resources[SystemColors.ControlTextBrushKey] = _owner.TextBrush();
        switch (picker)
        {
            case ComboBox comboBox:
                comboBox.Template = CreateComboBoxTemplate();
                comboBox.ItemContainerStyle = CreateComboBoxItemStyle();
                break;
        }
    }

    private ControlTemplate CreateComboBoxTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Border));
        root.Name = "Chrome";
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        root.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        root.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        root.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        var layout = new FrameworkElementFactory(typeof(Grid));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.Name = "ToggleButton";
        toggle.SetValue(ToggleButton.FocusableProperty, false);
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(Control.PaddingProperty, new Thickness(0));
        toggle.SetValue(ToggleButton.TemplateProperty, ButtonTemplate(
            typeof(ToggleButton), Brushes.Transparent, Brushes.Transparent, new CornerRadius(7)));
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        var selection = new FrameworkElementFactory(typeof(DockPanel));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "SelectionBoxItem");
        presenter.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 4, 0));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.SetValue(TextBlock.TextProperty, "\uE70D");
        arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        arrow.SetValue(TextBlock.FontSizeProperty, 11.0);
        arrow.SetValue(TextBlock.ForegroundProperty, _owner.SecondaryTextBrush());
        arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 2, 0));
        arrow.SetValue(DockPanel.DockProperty, Dock.Right);
        selection.AppendChild(arrow);
        selection.AppendChild(presenter);
        toggle.AppendChild(selection);
        layout.AppendChild(toggle);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.StaysOpenProperty, false);
        popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsDropDownOpen")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        popup.SetBinding(Popup.PlacementTargetProperty, new System.Windows.Data.Binding
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        popup.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 4, 0, 0));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        popupBorder.SetValue(Border.BackgroundProperty, ModalSurfaceBrush());
        popupBorder.SetValue(Border.BorderBrushProperty, _owner.Brush(0xDCE7EA));
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(FrameworkElement.MaxHeightProperty, 204.0);
        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        items.Name = "ItemsPresenter";
        scroll.AppendChild(items);
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        layout.AppendChild(popup);
        root.AppendChild(layout);
        return new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
    }

    private Style CreateComboBoxItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, _owner.TextBrush()));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7)));
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.Name = "Chrome";
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        chrome.AppendChild(content);
        var template = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, _owner.PanelBrush(0xEEF9FA), "Chrome"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, _owner.PanelBrush(0xDFF4F7), "Chrome"));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private void TrackFocus(Control control, Border border)
    {
        control.GotKeyboardFocus += (_, _) =>
        {
            _focusedFields.Add(border);
            ApplyFieldTheme(border);
        };
        control.LostKeyboardFocus += (_, _) =>
        {
            _focusedFields.Remove(border);
            ApplyFieldTheme(border);
        };
    }

    private TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold
    };

    private void AddLabel(StackPanel parent, TextBlock label, double topMargin = 10)
    {
        label.Margin = new Thickness(0, topMargin, 0, 4);
        _fieldLabels.Add(label);
        parent.Children.Add(label);
    }

    private string SelectedListId() => _listBox.SelectedItem is ComboBoxItem { Tag: string listId }
        ? listId
        : _defaultListId;

    private void ConfigureActionButton(Button button, string text, bool isPrimary)
    {
        button.Height = 38;
        button.BorderThickness = isPrimary ? new Thickness(0) : new Thickness(1);
        button.BorderBrush = isPrimary ? Brushes.Transparent : _owner.Brush(0xDCE7EA);
        button.Content = text;
        button.FontSize = 13;
        button.FontWeight = FontWeights.SemiBold;
        button.Foreground = isPrimary ? Brushes.White : _owner.TextBrush();
        button.Background = isPrimary ? _owner.AccentBrush() : ModalSurfaceBrush();
        button.FocusVisualStyle = null;
        button.Focusable = false;
        button.IsTabStop = false;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Template = ButtonTemplate(
            typeof(Button),
            isPrimary ? _owner.AccentBrush() : ModalSurfaceBrush(),
            isPrimary ? _owner.AccentDarkBrush() : _owner.PanelBrush(0xEEF9FA),
            new CornerRadius(8));
    }

    private void ConfigureCloseButton()
    {
        _closeButton.Width = 30;
        _closeButton.Height = 30;
        _closeButton.BorderThickness = new Thickness(0);
        _closeButton.Background = Brushes.Transparent;
        _closeButton.Foreground = _owner.SecondaryTextBrush();
        _closeButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _closeButton.VerticalContentAlignment = VerticalAlignment.Center;
        _closeButton.Content = new TextBlock
        {
            Text = "\uE711",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _closeButton.Template = ButtonTemplate(
            typeof(Button),
            Brushes.Transparent,
            _owner.PanelBrush(0xEEF9FA),
            new CornerRadius(15));
        AutomationProperties.SetName(_closeButton, "关闭");
    }

    private SolidColorBrush ModalSurfaceBrush() => _owner.Brush(
        0xFFFFFF,
        Math.Clamp(_owner.Settings.StickyOpacity + 0.18, 0.0, 1.0));

    private SolidColorBrush ModalPanelBrush() => _owner.Brush(
        0xF5F8F9,
        Math.Clamp(_owner.Settings.StickyOpacity + 0.14, 0.0, 1.0));

    private static ControlTemplate ButtonTemplate(Type targetType, Brush normal, Brush hover, CornerRadius radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(Border.BackgroundProperty, normal);
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, new System.Windows.Data.Binding("VerticalContentAlignment")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(presenter);
        var template = new ControlTemplate(targetType) { VisualTree = border };
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Chrome"));
        template.Triggers.Add(hoverTrigger);
        var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.84, "Chrome"));
        template.Triggers.Add(pressedTrigger);
        return template;
    }
}
