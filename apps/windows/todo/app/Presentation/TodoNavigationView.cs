using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoNavigationPalette(
    Brush Transparent,
    Brush SelectedBackground,
    Brush SelectedText,
    Brush Text,
    Brush SelectedBadge,
    Brush Badge,
    Func<string, Brush> ListColor);

internal sealed record TodoNavigationActions(
    Action<string> Navigate,
    Func<string, string, Button> SidebarIconButton,
    Func<TodoList, Task> ChangeColor,
    Func<TodoList, Task> Rename,
    Func<TodoList, Task> Delete);

internal sealed class TodoNavigationView(
    string currentViewId,
    TodoNavigationPalette palette,
    TodoNavigationActions actions)
{
    public Button NavigationButton(string viewId, string label, string glyph, int count)
    {
        var selected = string.Equals(currentViewId, viewId, StringComparison.Ordinal);
        var isList = TodoViewIds.IsList(viewId);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0), MinHeight = 44, BorderThickness = new Thickness(0),
            Background = palette.Transparent
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        var shell = new Border
        {
            Height = 46, CornerRadius = new CornerRadius(7),
            Background = selected ? palette.SelectedBackground : palette.Transparent
        };
        var grid = new Grid { ColumnSpacing = 11, Padding = new Thickness(isList ? 14 : 12, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(isList
            ? new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 10, Height = 10, Fill = palette.ListColor(TodoViewIds.ListId(viewId)),
                VerticalAlignment = VerticalAlignment.Center
            }
            : new FontIcon
            {
                Glyph = glyph, FontSize = 20,
                Foreground = selected ? palette.SelectedText : palette.Text,
                VerticalAlignment = VerticalAlignment.Center
            });
        var text = new TextBlock
        {
            Text = label, FontSize = 15,
            Foreground = selected ? palette.SelectedText : palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (count > 0)
        {
            var badge = new TextBlock
            {
                Text = count.ToString(), FontSize = 12,
                Foreground = selected ? palette.SelectedBadge : palette.Badge,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        shell.Child = grid;
        button.Content = shell;
        button.Click += (_, _) => actions.Navigate(viewId);
        return button;
    }

    public UIElement ListItem(TodoList list, int count, bool canDelete)
    {
        var viewId = TodoViewIds.List(list.Id);
        var selected = string.Equals(currentViewId, viewId, StringComparison.Ordinal);
        var shell = new Border
        {
            Height = 46, CornerRadius = new CornerRadius(7),
            Background = selected ? palette.SelectedBackground : palette.Transparent
        };
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(14, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 10, Height = 10, Fill = palette.ListColor(list.Id),
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = list.Name, FontSize = 15,
            Foreground = selected ? palette.SelectedText : palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (count > 0)
        {
            var badge = new TextBlock
            {
                Text = count.ToString(), FontSize = 12,
                Foreground = selected ? palette.SelectedBadge : palette.Badge,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        var manage = actions.SidebarIconButton("\uE712", "管理清单");
        manage.Width = 30;
        manage.Height = 30;
        manage.Tapped += (_, args) => args.Handled = true;
        manage.Flyout = Menu(list, canDelete);
        Grid.SetColumn(manage, 3);
        grid.Children.Add(manage);
        grid.Tapped += (_, _) => actions.Navigate(viewId);
        shell.Child = grid;
        AutomationProperties.SetName(shell, list.Name);
        ToolTipService.SetToolTip(shell, list.Name);
        return shell;
    }

    private MenuFlyout Menu(TodoList list, bool canDelete)
    {
        var menu = new MenuFlyout();
        var color = new MenuFlyoutItem { Text = "更改配色" };
        color.Click += async (_, _) => await actions.ChangeColor(list);
        menu.Items.Add(color);
        var rename = new MenuFlyoutItem { Text = "重命名" };
        rename.Click += async (_, _) => await actions.Rename(list);
        menu.Items.Add(rename);
        var delete = new MenuFlyoutItem { Text = "删除", IsEnabled = canDelete };
        delete.Click += async (_, _) => await actions.Delete(list);
        menu.Items.Add(delete);
        return menu;
    }
}
