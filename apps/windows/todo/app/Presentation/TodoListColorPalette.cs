using Fowan.Todo.Shared.Models;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoListColorPalette(TodoThemePalette theme)
{
    public IReadOnlyList<TodoListColorChoice> Choices(TodoList list) => TodoListColorIds.All
        .Select(color => TodoListColorIds.Normalize(color, list.Id))
        .Select(color => new TodoListColorChoice(
            color,
            Label(color),
            ForegroundForColor(color),
            BackgroundForColor(color),
            string.Equals(list.ColorId, color, StringComparison.Ordinal)))
        .ToList();

    public Brush Foreground(TodoData data, string listId) => ForegroundForColor(ColorId(data, listId));

    public Brush Background(TodoData data, string listId) => BackgroundForColor(ColorId(data, listId));

    private static string ColorId(TodoData data, string listId)
    {
        var color = data.Lists.FirstOrDefault(list =>
            string.Equals(list.Id, listId, StringComparison.Ordinal))?.ColorId;
        return TodoListColorIds.Normalize(color, listId);
    }

    private Brush ForegroundForColor(string colorId) => TodoListColorIds.Normalize(colorId) switch
    {
        TodoListColorIds.Cyan => theme.Brush(0x2B7F82),
        TodoListColorIds.Indigo => theme.Brush(0x5D6F9D),
        TodoListColorIds.Purple => theme.Brush(0x8064A7),
        TodoListColorIds.Pink => theme.Brush(0xB86B87),
        TodoListColorIds.Red => theme.Brush(0xB86A62),
        TodoListColorIds.Orange => theme.Brush(0xB98B45),
        TodoListColorIds.Green => theme.Brush(0x4A8B6B),
        TodoListColorIds.Cobalt => theme.Brush(0x526C91),
        TodoListColorIds.Mauve => theme.Brush(0x967B9B),
        TodoListColorIds.Olive => theme.Brush(0x7F8A59),
        TodoListColorIds.Copper => theme.Brush(0xA17461),
        _ => theme.Brush(0x426DAD)
    };

    private Brush BackgroundForColor(string colorId) => TodoListColorIds.Normalize(colorId) switch
    {
        TodoListColorIds.Cyan => theme.Brush(0xE5F1F1),
        TodoListColorIds.Indigo => theme.Brush(0xEBEEF4),
        TodoListColorIds.Purple => theme.Brush(0xF0ECF7),
        TodoListColorIds.Pink => theme.Brush(0xF8ECEF),
        TodoListColorIds.Red => theme.Brush(0xF8ECEA),
        TodoListColorIds.Orange => theme.Brush(0xF8F1E4),
        TodoListColorIds.Green => theme.Brush(0xEAF3EE),
        TodoListColorIds.Cobalt => theme.Brush(0xECF0F5),
        TodoListColorIds.Mauve => theme.Brush(0xF3EDF4),
        TodoListColorIds.Olive => theme.Brush(0xF1F2E8),
        TodoListColorIds.Copper => theme.Brush(0xF5EDE8),
        _ => theme.Brush(0xE8EEF8)
    };

    private static string Label(string colorId) => TodoListColorIds.Normalize(colorId) switch
    {
        TodoListColorIds.Cyan => "孔雀青",
        TodoListColorIds.Indigo => "石板蓝",
        TodoListColorIds.Purple => "紫晶",
        TodoListColorIds.Pink => "玫瑰",
        TodoListColorIds.Red => "珊瑚",
        TodoListColorIds.Orange => "琥珀",
        TodoListColorIds.Green => "祖母绿",
        TodoListColorIds.Cobalt => "深海蓝",
        TodoListColorIds.Mauve => "雾紫",
        TodoListColorIds.Olive => "橄榄",
        TodoListColorIds.Copper => "铜棕",
        _ => "蓝宝石"
    };
}
