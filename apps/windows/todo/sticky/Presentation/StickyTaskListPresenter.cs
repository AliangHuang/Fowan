using Fowan.Todo.Shared.Application;
using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Fowan.Todo.Sticky.Windows.Application;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Fowan.Todo.Sticky.Windows.Presentation;

internal sealed class StickyTaskListPresenter(
    StickyWindowCommands commands,
    Func<TodoData> data,
    Func<TodoSettings> settings,
    StickyThemePalette palette,
    StickyControlFactory controls,
    StickyTaskDragController taskDrag,
    StickyDropPreviewPresenter dropPreview,
    Func<List<Border>> taskRows,
    Func<StackPanel> activeTasks,
    Func<StackPanel> completedTasks,
    Func<Button> completedToggle,
    Func<TextBlock> titleText,
    Func<TextBlock> countText,
    Func<bool> hasOpenChildWindow,
    Action<string> showTaskDetail,
    Action<TodoTask> showAddSubtask,
    Action<TodoTask> deleteTask,
    Action<TodoTask> toggleCompleted,
    Action<string> toggleImportant)
{
    private const string ViewId = TodoViewIds.Today;

    public void Refresh()
    {
        var source = data();
        var collapsed = new HashSet<string>(settings().CollapsedTaskIds, StringComparer.Ordinal);
        var active = TodoQuery.ActiveTaskNodesForView(source, ViewId, collapsed).ToList();
        var completed = TodoQuery.CompletedTaskNodesForView(source, ViewId, collapsed).ToList();
        titleText().Text = TodoQuery.ViewTitle(source, ViewId);
        countText().Text = $"{active.Count} 项";
        dropPreview.Remove();
        taskRows().Clear();
        activeTasks().Children.Clear();
        if (active.Count == 0) activeTasks().Children.Add(controls.EmptyText("没有待办任务"));
        else foreach (var node in active) activeTasks().Children.Add(BuildRow(node.Task, node.Depth));
        completedToggle().Content = controls.CompletedToggleContent(completed.Count);
        completedTasks().Children.Clear();
        completedTasks().Visibility = settings().IsStickyCompletedExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (settings().IsStickyCompletedExpanded)
            foreach (var node in completed) completedTasks().Children.Add(BuildRow(node.Task, node.Depth));
    }

    private Border BuildRow(TodoTask task, int depth)
    {
        depth = Math.Clamp(depth, 0, TodoQuery.MaxTaskTreeDepth - 1);
        var row = new Border
        {
            Height = task.IsCompleted ? 42 : 54,
            Margin = new Thickness(depth * 18, 0, 0, task.IsCompleted ? 8 : 10),
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(8),
            Background = task.IsCompleted ? palette.Panel(0xF5F8F9) : palette.Panel(0xFFFFFF),
            BorderBrush = task.IsCompleted ? Brushes.Transparent : palette.Brush(0xDCE7EA),
            BorderThickness = task.IsCompleted ? new Thickness(0) : new Thickness(1),
            Tag = task.Id
        };
        taskDrag.Attach(row, task);
        row.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount != 2 || taskDrag.IsDragging || hasOpenChildWindow() ||
                StickyTaskDragController.IsIgnoredSource(args.OriginalSource as DependencyObject, row)) return;
            taskDrag.Cancel();
            showTaskDetail(task.Id);
            args.Handled = true;
        };
        row.MouseEnter += (_, _) => taskDrag.SetPointerOver(row, task, pointerOver: true);
        row.MouseLeave += (_, _) => taskDrag.SetPointerOver(row, task, pointerOver: false);
        row.ContextMenu = BuildContextMenu(task);
        taskRows().Add(row);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var leading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leading.Children.Add(TodoQuery.DirectChildCount(data(), task.Id) > 0
            ? TreeToggleButton(task)
            : new Border { Width = 20, Height = 20 });
        leading.Children.Add(TaskCheckButton(task));
        Grid.SetColumn(leading, 0);
        grid.Children.Add(leading);
        var title = new TextBlock
        {
            Text = task.Title,
            FontSize = 13,
            FontWeight = task.IsCompleted ? FontWeights.Normal : FontWeights.Medium,
            Foreground = task.IsCompleted ? palette.MutedText : palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
            TextDecorations = task.IsCompleted ? TextDecorations.Strikethrough : null
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (task.IsImportant)
        {
            var important = controls.HeaderIconButton("\uE735", "取消重要");
            important.Click += (_, _) => toggleImportant(task.Id);
            Grid.SetColumn(important, 2);
            grid.Children.Add(important);
        }
        row.Child = grid;
        return row;
    }

    private ContextMenu BuildContextMenu(TodoTask task)
    {
        var menu = new ContextMenu
        {
            Background = palette.ContextMenuSurface,
            BorderBrush = palette.ContextMenuBorder,
            BorderThickness = new Thickness(1),
            Foreground = palette.Text,
            Padding = new Thickness(0),
            HasDropShadow = true,
            Template = CreateContextMenuTemplate()
        };
        var itemStyle = new Style(typeof(MenuItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, palette.Text));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, palette.ContextMenuSurface));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 16, 8)));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, CreateContextMenuItemTemplate()));
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, palette.ContextMenuItemHover));
        itemStyle.Triggers.Add(highlighted);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, palette.MutedText));
        itemStyle.Triggers.Add(disabled);
        menu.ItemContainerStyle = itemStyle;
        var createChild = new MenuItem { Header = "创建子任务", IsEnabled = TodoQuery.CanAddChild(data(), task) };
        if (!createChild.IsEnabled) createChild.ToolTip = TodoQuery.AddChildBlockedReason(data(), task);
        createChild.Click += (_, _) => showAddSubtask(task);
        menu.Items.Add(createChild);
        var delete = new MenuItem { Header = "删除任务" };
        delete.Click += (_, _) => deleteTask(task);
        menu.Items.Add(delete);
        return menu;
    }

    private Button TaskCheckButton(TodoTask task)
    {
        var button = new Button
        {
            Width = 22, Height = 22, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
            Content = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                BorderThickness = task.IsCompleted ? new Thickness(0) : new Thickness(2),
                BorderBrush = palette.TaskCheckBorder,
                Background = task.IsCompleted ? palette.Brush(0x9DBEC7) : Brushes.Transparent,
                Child = task.IsCompleted ? StickyControlFactory.MdIcon("\uE73E", 11, Brushes.White) : null
            },
            ToolTip = task.IsCompleted ? "恢复任务" : "完成任务"
        };
        button.Template = controls.ButtonTemplate(new CornerRadius(11), Brushes.Transparent, palette.Brush(0xEEF9FA));
        button.Click += (_, _) => toggleCompleted(task);
        return button;
    }

    private Button TreeToggleButton(TodoTask task)
    {
        var collapsed = IsCollapsed(task.Id);
        var button = new Button
        {
            Width = 20, Height = 22, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Content = StickyControlFactory.MdIcon(collapsed ? "\uE76C" : "\uE70D", 10, palette.SecondaryText),
            ToolTip = collapsed ? "展开子任务" : "收起子任务",
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Template = controls.ButtonTemplate(new CornerRadius(6), Brushes.Transparent, palette.Brush(0xEEF9FA));
        button.Click += (_, _) => ToggleCollapsed(task.Id);
        return button;
    }

    private bool IsCollapsed(string taskId) => settings().CollapsedTaskIds.Contains(taskId, StringComparer.Ordinal);

    private void ToggleCollapsed(string taskId)
    {
        commands.ToggleCollapsed(taskId);
        Refresh();
    }

    private static ControlTemplate CreateContextMenuTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding(Control.BorderBrushProperty));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.PaddingProperty, new Thickness(4));
        border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        return new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
    }

    private static ControlTemplate CreateContextMenuItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.MarginProperty, new Thickness(2));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(HeaderedItemsControl.HeaderProperty));
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(HeaderedItemsControl.HeaderTemplateProperty));
        presenter.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(HeaderedItemsControl.HeaderTemplateSelectorProperty));
        presenter.SetBinding(FrameworkElement.MarginProperty, TemplateBinding(Control.PaddingProperty));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
    }

    private static Binding TemplateBinding(DependencyProperty property) => new()
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        Path = new PropertyPath(property)
    };
}
