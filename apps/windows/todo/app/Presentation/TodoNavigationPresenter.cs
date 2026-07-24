using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoNavigationPresenter(
    TodoThemePalette palette,
    TodoListColorPalette listColors)
{
    public void Refresh(
        StackPanel navigationPanel,
        StackPanel listPanel,
        TodoData data,
        TodoSettings settings,
        string currentViewId,
        TodoWindowQuery filterQuery,
        TodoWindowQuery unfilteredQuery,
        TodoNavigationActions actions)
    {
        var navigation = new TodoNavigationView(
            currentViewId,
            new TodoNavigationPalette(
                TodoThemePalette.Transparent,
                palette.Brush(0x0B3A7A),
                TodoThemePalette.PureWhite,
                palette.Brush(0xE5EDF8),
                palette.Brush(0xDDEBFF),
                palette.Brush(0xD6E3F5),
                listId => listColors.Foreground(data, listId)),
            actions);
        navigationPanel.Children.Clear();
        Add(navigationPanel, navigation, TodoViewIds.Today, "今日任务", "\uE787", unfilteredQuery.ActiveTasks(TodoViewIds.Today).Count());
        Add(navigationPanel, navigation, TodoViewIds.Planned, "计划任务", "\uE163", unfilteredQuery.ActiveTasks(TodoViewIds.Planned).Count());
        Add(navigationPanel, navigation, TodoViewIds.Important, "重要任务", "\uE735", unfilteredQuery.ActiveTasks(TodoViewIds.Important).Count());
        Add(
            navigationPanel,
            navigation,
            TodoViewIds.Recurring,
            "循环任务",
            "\uE8EE",
            unfilteredQuery.ActiveTasks(TodoViewIds.Recurring).Count() + unfilteredQuery.CompletedTasks(TodoViewIds.Recurring).Count());
        Add(
            navigationPanel,
            navigation,
            TodoViewIds.All,
            "全部任务",
            "\uE8FD",
            filterQuery.IsFilteringActive
                ? filterQuery.FilteredNodes(TodoViewIds.All).Count()
                : unfilteredQuery.ActiveTasks(TodoViewIds.All).Count() + unfilteredQuery.CompletedTasks(TodoViewIds.All).Count());
        Add(navigationPanel, navigation, TodoViewIds.Completed, "已完成", "\uE73E", unfilteredQuery.CompletedTasks(TodoViewIds.Completed).Count());
        if (settings.IsRecycleBinEnabled)
        {
            Add(navigationPanel, navigation, TodoViewIds.RecycleBin, "回收站", "\uE74D", TodoQuery.RecycleBinTasks(data).Count());
        }
        listPanel.Children.Clear();
        foreach (var list in unfilteredQuery.OrderedLists())
        {
            listPanel.Children.Add(navigation.ListItem(
                list,
                unfilteredQuery.TasksForList(list.Id).Count(task => !task.IsCompleted),
                !unfilteredQuery.IsDefaultList(list.Id) && data.Lists.Count > 1));
        }
    }

    public TodoNavigationActions Actions(
        Action<string> navigate,
        Func<string, string, Button> sidebarButton,
        Func<TodoList, Task> showColor,
        Func<TodoList, Task> rename,
        Func<TodoList, Task> delete) =>
        new(navigate, sidebarButton, showColor, rename, delete);

    private static void Add(
        StackPanel panel,
        TodoNavigationView navigation,
        string id,
        string text,
        string glyph,
        int count) => panel.Children.Add(navigation.NavigationButton(id, text, glyph, count));
}
