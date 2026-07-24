using Fowan.Report.Shared;
using Fowan.Report.Shared.Application.Ports;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;

namespace Fowan.Report.Windows.Presentation;

/// <summary>
/// A small Notion-style document surface. Native RichEditBox is intentionally not
/// used here: structural table updates are pure document commands followed by a
/// controlled rerender, never an RTF mutation from a text-change callback.
/// </summary>
internal sealed class ReportBlockEditor : UserControl
{
    private readonly StackPanel _blocks = new() { Spacing = 6 };
    private readonly Border _surface;
    private readonly Stack<ReportTextDocument> _undo = new();
    private readonly Stack<ReportTextDocument> _redo = new();
    private readonly Dictionary<string, ReportTextDocument> _focusStarts = new(StringComparer.Ordinal);
    private readonly IReportClipboardService _clipboard;
    private ReportTextDocument _document = ReportTextDocument.Empty;
    private bool _isRendering;
    private bool _isInitialized;

    public ReportBlockEditor(string placeholder, double minHeight, IReportClipboardService clipboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        Placeholder = placeholder;
        MinHeight = minHeight;
        _surface = new Border
        {
            Background = ReportDesignSystem.InputSurface,
            BorderBrush = ReportDesignSystem.StrongStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = _blocks
        };
        Content = _surface;
        AutomationProperties.SetName(this, placeholder);
    }

    public event EventHandler<ReportTextDocument>? DocumentChanged;
    public string Placeholder { get; }
    public ReportTextDocument Document => _document;

    /// <summary>Creates native text controls only after the owning Window is activated.</summary>
    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        Render();
    }

    public void SetDocument(ReportTextDocument? value, bool resetHistory = false)
    {
        var normalized = ReportTextDocuments.Normalize(value);
        if (ReferenceEquals(_document, value) || string.Equals(ReportTextDocuments.ToMarkdown(_document), ReportTextDocuments.ToMarkdown(normalized), StringComparison.Ordinal)) return;
        _document = normalized;
        if (resetHistory)
        {
            _undo.Clear();
            _redo.Clear();
            _focusStarts.Clear();
        }
        if (_isInitialized) Render();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(_document);
        _document = _undo.Pop();
        RenderAndPublish();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_document);
        _document = _redo.Pop();
        RenderAndPublish();
    }

    private void Render()
    {
        if (!_isInitialized) return;
        _isRendering = true;
        _blocks.Children.Clear();
        foreach (var block in _document.Blocks) _blocks.Children.Add(BuildBlock(block));
        _isRendering = false;
    }

    private FrameworkElement BuildBlock(ReportTextBlock block)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) };
        var insert = EdgeButton("\uE710", "在下方插入内容块");
        insert.Click += (_, _) => ShowInsertMenu(insert, block.Id);
        var menu = EdgeButton("\uE712", "块操作");
        menu.Click += (_, _) => ShowBlockMenu(menu, block);
        actions.Children.Add(insert);
        actions.Children.Add(menu);
        row.Children.Add(actions);

        var content = block.Kind == ReportTextBlockKind.Table ? BuildTable(block) : BuildTextBlock(block);
        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        return row;
    }

    private FrameworkElement BuildTextBlock(ReportTextBlock block)
    {
        if (block.Kind == ReportTextBlockKind.Divider)
        {
            return new Border { Height = 1, Background = ReportDesignSystem.Stroke, Margin = new Thickness(0, 13, 0, 13) };
        }

        var host = new Grid { ColumnSpacing = 8 };
        if (block.Kind == ReportTextBlockKind.TodoList)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var checkbox = new CheckBox { IsChecked = block.IsChecked, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            AutomationProperties.SetName(checkbox, "完成待办项");
            checkbox.Checked += (_, _) => Apply(new(ReportTextCommandKind.SetChecked, block.Id, Value: true));
            checkbox.Unchecked += (_, _) => Apply(new(ReportTextCommandKind.SetChecked, block.Id, Value: false));
            host.Children.Add(checkbox);
        }

        var input = new TextBox
        {
            Text = block.Text,
            PlaceholderText = block.Kind == ReportTextBlockKind.Paragraph ? Placeholder : BlockPlaceholder(block.Kind),
            AcceptsReturn = block.Kind is ReportTextBlockKind.Paragraph or ReportTextBlockKind.Quote or ReportTextBlockKind.Code,
            TextWrapping = TextWrapping.Wrap,
            Background = ReportDesignSystem.InputSurface,
            BorderThickness = new Thickness(0),
            Foreground = block.Link is null ? ReportDesignSystem.Text : ReportDesignSystem.AccentBorder,
            Padding = new Thickness(8, 7, 8, 7),
            MinHeight = block.Kind == ReportTextBlockKind.Code ? 76 : 40,
            FontSize = FontSizeFor(block.Kind)
        };
        if (block.Bold || block.Kind is ReportTextBlockKind.Heading1 or ReportTextBlockKind.Heading2 or ReportTextBlockKind.Heading3)
            input.FontWeight = FontWeights.SemiBold;
        if (block.Italic || block.Kind == ReportTextBlockKind.Quote)
            input.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
        if (block.Kind == ReportTextBlockKind.Code)
            input.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono");
        AutomationProperties.SetName(input, $"{BlockName(block.Kind)}内容");
        input.TextChanged += (_, _) =>
        {
            UpdateText(block.Id, input.Text);
            HandleSlashShortcut(block.Id, input);
        };
        input.GotFocus += (_, _) => _focusStarts.TryAdd(block.Id, _document);
        input.LostFocus += (_, _) => CommitFocusedText(block.Id);
        input.KeyDown += (_, args) => HandleEditorKeyDown(args);
        input.Paste += async (_, args) =>
        {
            args.Handled = true;
            await PasteIntoBlockAsync(block.Id, input);
        };
        Grid.SetColumn(input, block.Kind == ReportTextBlockKind.TodoList ? 1 : 0);
        host.Children.Add(input);
        return host;
    }

    private FrameworkElement BuildTable(ReportTextBlock block)
    {
        var table = block.Table ?? new ReportTextTable([new[] { string.Empty }]);
        var shell = new Border
        {
            Background = ReportDesignSystem.SurfaceRaised,
            BorderBrush = ReportDesignSystem.StrongStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var column = 0; column < table.ColumnCount; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 104 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var row = 0; row < table.RowCount; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var corner = TableEdgeButton("\uE712", "整表操作");
        corner.Click += (_, _) => ShowTableMenu(corner, block.Id);
        grid.Children.Add(corner);

        for (var column = 0; column < table.ColumnCount; column++)
        {
            var columnIndex = column;
            var edge = TableEdgeButton("\uE712", $"第 {column + 1} 列操作");
            edge.Click += (_, _) => ShowColumnMenu(edge, block.Id, columnIndex);
            Grid.SetColumn(edge, column + 1);
            grid.Children.Add(edge);
        }
        for (var row = 0; row < table.RowCount; row++)
        {
            var rowIndex = row;
            var edge = TableEdgeButton("\uE712", $"第 {row + 1} 行操作");
            edge.Click += (_, _) => ShowRowMenu(edge, block.Id, rowIndex);
            Grid.SetRow(edge, row + 1);
            grid.Children.Add(edge);
            for (var column = 0; column < table.ColumnCount; column++)
            {
                var columnIndex = column;
                var cell = new Grid();
                var input = new TextBox
                {
                    Text = table.Cells[row][column],
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    MinHeight = 42,
                    Background = ReportDesignSystem.InputSurface,
                    BorderBrush = ReportDesignSystem.Stroke,
                    BorderThickness = new Thickness(1),
                    Foreground = ReportDesignSystem.Text,
                    Padding = new Thickness(9, 7, 9, 7)
                };
                AutomationProperties.SetName(input, $"表格第 {row + 1} 行第 {column + 1} 列");
                input.TextChanged += (_, _) => UpdateCell(block.Id, rowIndex, columnIndex, input.Text);
                input.GotFocus += (_, _) => _focusStarts.TryAdd(block.Id, _document);
                input.LostFocus += (_, _) => CommitFocusedText(block.Id);
                input.KeyDown += (_, args) => HandleTableKeyDown(args, block.Id, rowIndex, columnIndex);
                input.Paste += async (_, args) =>
                {
                    args.Handled = true;
                    await PasteIntoCellAsync(block.Id, rowIndex, columnIndex, input);
                };
                cell.Children.Add(input);

                var cellEdge = TableEdgeButton("\uE712", "当前单元格操作");
                cellEdge.Visibility = Visibility.Collapsed;
                cellEdge.HorizontalAlignment = HorizontalAlignment.Right;
                cellEdge.VerticalAlignment = VerticalAlignment.Top;
                cellEdge.Margin = new Thickness(2);
                cellEdge.Click += (_, _) => ShowCellMenu(cellEdge, block.Id, rowIndex, columnIndex);
                cell.PointerEntered += (_, _) => cellEdge.Visibility = Visibility.Visible;
                cell.PointerExited += (_, _) => cellEdge.Visibility = Visibility.Collapsed;
                cell.Children.Add(cellEdge);
                Grid.SetRow(cell, row + 1);
                Grid.SetColumn(cell, column + 1);
                grid.Children.Add(cell);
            }
        }
        shell.Child = grid;
        return shell;
    }

    private void ShowInsertMenu(FrameworkElement anchor, string afterBlockId)
    {
        var flyout = new MenuFlyout();
        Add(flyout, "段落", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Paragraph)));
        Add(flyout, "标题 1", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Heading1)));
        Add(flyout, "标题 2", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Heading2)));
        Add(flyout, "标题 3", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Heading3)));
        Add(flyout, "项目符号列表", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.BulletedList)));
        Add(flyout, "编号列表", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.NumberedList)));
        Add(flyout, "待办列表", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.TodoList)));
        Add(flyout, "引用", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Quote)));
        Add(flyout, "代码块", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Code)));
        Add(flyout, "分隔线", () => Apply(new(ReportTextCommandKind.InsertBlockAfter, afterBlockId, ReportTextBlockKind.Divider)));
        Add(flyout, "表格", () => Apply(new(ReportTextCommandKind.InsertTable, afterBlockId, Cells: [new[] { "字段", "内容" }, new[] { string.Empty, string.Empty }] )));
        flyout.ShowAt(anchor);
    }

    private void ShowBlockMenu(FrameworkElement anchor, ReportTextBlock block)
    {
        var flyout = new MenuFlyout();
        if (block.Kind != ReportTextBlockKind.Table && block.Kind != ReportTextBlockKind.Divider)
        {
            Add(flyout, block.Bold ? "取消加粗" : "加粗", () => Apply(new(ReportTextCommandKind.SetBold, block.Id, Value: !block.Bold)));
            Add(flyout, block.Italic ? "取消斜体" : "斜体", () => Apply(new(ReportTextCommandKind.SetItalic, block.Id, Value: !block.Italic)));
            Add(flyout, string.IsNullOrWhiteSpace(block.Link) ? "添加链接" : "编辑链接", () => ShowLinkEditor(anchor, block));
            if (!string.IsNullOrWhiteSpace(block.Link)) Add(flyout, "移除链接", () => Apply(new(ReportTextCommandKind.SetLink, block.Id, Text: string.Empty)));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }
        Add(flyout, "删除此块", () => Apply(new(ReportTextCommandKind.DeleteBlock, block.Id)));
        flyout.ShowAt(anchor);
    }

    private void ShowLinkEditor(FrameworkElement anchor, ReportTextBlock block)
    {
        var input = new TextBox { Text = block.Link ?? string.Empty, PlaceholderText = "https://example.com", MinWidth = 280 };
        ReportDesignSystem.ConfigureTextBox(input, 40);
        var save = new Button { Content = "保存链接", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        ReportDesignSystem.ConfigureButton(save, ReportButtonKind.Primary);
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(12), Children = { new TextBlock { Text = "链接地址", Foreground = ReportDesignSystem.SecondaryText }, input, save } };
        var flyout = new Flyout { Content = panel };
        save.Click += (_, _) => { Apply(new(ReportTextCommandKind.SetLink, block.Id, Text: input.Text)); flyout.Hide(); };
        flyout.ShowAt(anchor);
    }

    private void ShowCellMenu(FrameworkElement anchor, string blockId, int row, int column)
    {
        var flyout = new MenuFlyout();
        Add(flyout, "清空当前单元格", () => Apply(new(ReportTextCommandKind.ClearCell, blockId, Row: row, Column: column)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(flyout, "在上方插入行", () => Apply(new(ReportTextCommandKind.InsertRowAbove, blockId, Row: row)));
        Add(flyout, "在下方插入行", () => Apply(new(ReportTextCommandKind.InsertRowBelow, blockId, Row: row)));
        Add(flyout, "在左侧插入列", () => Apply(new(ReportTextCommandKind.InsertColumnLeft, blockId, Column: column)));
        Add(flyout, "在右侧插入列", () => Apply(new(ReportTextCommandKind.InsertColumnRight, blockId, Column: column)));
        flyout.ShowAt(anchor);
    }

    private void ShowRowMenu(FrameworkElement anchor, string blockId, int row)
    {
        var flyout = new MenuFlyout();
        Add(flyout, "在上方插入行", () => Apply(new(ReportTextCommandKind.InsertRowAbove, blockId, Row: row)));
        Add(flyout, "在下方插入行", () => Apply(new(ReportTextCommandKind.InsertRowBelow, blockId, Row: row)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(flyout, "清空此行", () => Apply(new(ReportTextCommandKind.ClearRow, blockId, Row: row)));
        Add(flyout, "删除此行", () => Apply(new(ReportTextCommandKind.DeleteRow, blockId, Row: row)));
        flyout.ShowAt(anchor);
    }

    private void ShowColumnMenu(FrameworkElement anchor, string blockId, int column)
    {
        var flyout = new MenuFlyout();
        Add(flyout, "在左侧插入列", () => Apply(new(ReportTextCommandKind.InsertColumnLeft, blockId, Column: column)));
        Add(flyout, "在右侧插入列", () => Apply(new(ReportTextCommandKind.InsertColumnRight, blockId, Column: column)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(flyout, "清空此列", () => Apply(new(ReportTextCommandKind.ClearColumn, blockId, Column: column)));
        Add(flyout, "删除此列", () => Apply(new(ReportTextCommandKind.DeleteColumn, blockId, Column: column)));
        flyout.ShowAt(anchor);
    }

    private void ShowTableMenu(FrameworkElement anchor, string blockId)
    {
        var flyout = new MenuFlyout();
        Add(flyout, "清空整张表", () => Apply(new(ReportTextCommandKind.ClearTable, blockId)));
        Add(flyout, "删除整张表", () => Apply(new(ReportTextCommandKind.DeleteTable, blockId)));
        flyout.ShowAt(anchor);
    }

    private void UpdateText(string blockId, string value)
    {
        if (_isRendering || !ReportTextDocuments.TryApply(_document, new(ReportTextCommandKind.UpdateText, blockId, Text: value), out var updated)) return;
        _document = updated;
        DocumentChanged?.Invoke(this, _document);
    }

    private void HandleSlashShortcut(string blockId, TextBox input)
    {
        if (_isRendering || !string.Equals(input.Text, "/", StringComparison.Ordinal)) return;
        input.Text = string.Empty;
        ShowInsertMenu(input, blockId);
    }

    private void UpdateCell(string blockId, int row, int column, string value)
    {
        if (_isRendering || !ReportTextDocuments.TryApply(_document, new(ReportTextCommandKind.UpdateCell, blockId, Text: value, Row: row, Column: column), out var updated)) return;
        _document = updated;
        DocumentChanged?.Invoke(this, _document);
    }

    private void CommitFocusedText(string blockId)
    {
        if (!_focusStarts.Remove(blockId, out var before) || ReferenceEquals(before, _document)) return;
        _undo.Push(before);
        _redo.Clear();
    }

    private void Apply(ReportTextCommand command)
    {
        if (!ReportTextDocuments.TryApply(_document, command, out var updated)) return;
        _undo.Push(_document);
        _redo.Clear();
        _document = updated;
        RenderAndPublish();
    }

    private void RenderAndPublish()
    {
        Render();
        DocumentChanged?.Invoke(this, _document);
    }

    private void HandleEditorKeyDown(KeyRoutedEventArgs args)
    {
        if (IsControlPressed(args))
        {
            if (args.Key == global::Windows.System.VirtualKey.Z) { args.Handled = true; Undo(); }
            if (args.Key == global::Windows.System.VirtualKey.Y) { args.Handled = true; Redo(); }
        }
    }

    private void HandleTableKeyDown(KeyRoutedEventArgs args, string blockId, int row, int column)
    {
        HandleEditorKeyDown(args);
        if (args.Handled || args.Key != global::Windows.System.VirtualKey.Tab) return;
        // Native tab navigation continues naturally between table cells; no structural mutation happens on Tab.
        _ = blockId;
        _ = row;
        _ = column;
    }

    private static bool IsControlPressed(KeyRoutedEventArgs args) =>
        (args.KeyStatus.ScanCode != 0) && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Control).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    private async Task PasteIntoBlockAsync(string blockId, TextBox input)
    {
        try
        {
            var content = await _clipboard.ReadAsync();
            if (content.Table is not null)
            {
                Apply(new(ReportTextCommandKind.InsertTable, blockId, Cells: content.Table));
                return;
            }
            if (content.Text is not null) input.SelectedText = content.Text;
        }
        catch
        {
            // Clipboard ownership can change between the paste request and read. The current document remains unchanged.
        }
    }

    private async Task PasteIntoCellAsync(string blockId, int row, int column, TextBox input)
    {
        try
        {
            var content = await _clipboard.ReadAsync();
            if (content.Table is not null)
            {
                Apply(new(ReportTextCommandKind.FillTable, blockId, Row: row, Column: column, Cells: content.Table));
                return;
            }
            if (content.Text is not null) input.SelectedText = content.Text;
        }
        catch
        {
            // See PasteIntoBlockAsync: no failed clipboard read may mutate document state.
        }
    }

    private static Button EdgeButton(string glyph, string name)
    {
        var button = new Button { Content = ReportDesignSystem.Icon(glyph, 13, ReportDesignSystem.MutedText), Width = 24, Height = 28, Padding = new Thickness(0), Background = ReportDesignSystem.InputSurface, BorderThickness = new Thickness(0) };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private static Button TableEdgeButton(string glyph, string name)
    {
        var button = EdgeButton(glyph, name);
        button.Width = 28;
        button.Height = 28;
        button.Background = ReportDesignSystem.Surface;
        button.BorderBrush = ReportDesignSystem.Stroke;
        button.BorderThickness = new Thickness(1);
        return button;
    }

    private static void Add(MenuFlyout flyout, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        flyout.Items.Add(item);
    }

    private static double FontSizeFor(ReportTextBlockKind kind) => kind switch
    {
        ReportTextBlockKind.Heading1 => 28,
        ReportTextBlockKind.Heading2 => 23,
        ReportTextBlockKind.Heading3 => 19,
        ReportTextBlockKind.Code => 14,
        _ => 16
    };
    private static string BlockName(ReportTextBlockKind kind) => kind switch
    {
        ReportTextBlockKind.Heading1 => "一级标题",
        ReportTextBlockKind.Heading2 => "二级标题",
        ReportTextBlockKind.Heading3 => "三级标题",
        ReportTextBlockKind.BulletedList => "项目符号",
        ReportTextBlockKind.NumberedList => "编号列表",
        ReportTextBlockKind.TodoList => "待办列表",
        ReportTextBlockKind.Quote => "引用",
        ReportTextBlockKind.Code => "代码块",
        _ => "段落"
    };
    private static string BlockPlaceholder(ReportTextBlockKind kind) => kind switch
    {
        ReportTextBlockKind.Heading1 or ReportTextBlockKind.Heading2 or ReportTextBlockKind.Heading3 => "输入标题",
        ReportTextBlockKind.Code => "输入代码或固定格式文本",
        _ => "输入内容"
    };
}
