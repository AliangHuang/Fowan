using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoHelpPresenter(Action enterModal, Action exitModal)
{
    public async Task ShowAsync(Grid root, bool isDark)
    {
        var service = new TodoHelpDialogService(
            enterModal,
            exitModal,
            () => ContentWidth(root),
            contentWidth => DialogWidth(root, contentWidth),
            () => PanelHeight(root));
        var contentWidth = ContentWidth(root);
        var help = new TodoHelpContentFactory(ContentPalette(isDark));
        var layout = new StackPanel
        {
            Spacing = 18, Width = contentWidth, MaxWidth = contentWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        layout.Children.Add(help.Hero());
        layout.Children.Add(help.Section(
            "主界面",
            "整理、编辑和查看完整任务",
            "适合集中管理任务、清单、日期、备注和子任务。",
            MainCards(),
            contentWidth >= 760));
        layout.Children.Add(help.Section(
            "便签模式",
            "贴在桌面上的轻量任务窗口",
            "适合把当前任务留在屏幕上，随手新增、完成和调整顺序。",
            StickyCards(),
            contentWidth >= 760));
        layout.Children.Add(help.Section(
            "回收站与筛选",
            "批量整理、找回和缩小任务范围",
            "删除会按任务树处理；筛选只影响主窗口列表，日期筛选会切到全部任务。",
            FilterCards(),
            contentWidth >= 760));
        layout.Children.Add(help.TipBand());
        await service.ShowAsync(root, layout, DialogPalette(isDark));
    }

    private static IReadOnlyList<TodoHelpCard> MainCards() =>
    [
        new("\uE8A5", "切换视图", "在左侧切换今日、计划、重要、全部、未完成、已完成、回收站和自定义清单。"),
        new("\uE710", "新增任务", "在顶部输入任务后按回车；输入为空时点击添加按钮会打开新建任务弹窗。"),
        new("\uE70F", "编辑详情", "点击任务后，在右侧编辑标题、清单、开始时间、截止日期、备注和重要标记；完成时间修改需点击应用，切换任务会自动撤销未应用内容。"),
        new("\uE712", "任务菜单", "右键任务可创建带备注的子任务或删除任务；删除含后代任务时会先确认。"),
        new("\uE7C2", "移动窗口", "可拖动顶部品牌和空白区域移动主窗口；筛选、侧栏菜单和任务操作仍可正常点击。"),
        new("\uE8FD", "管理清单", "左侧清单区可以新增清单，也可以对已有清单改名或删除。"),
        new("\uE8C8", "子任务", "选中任务后可添加子任务并填写备注；父任务可展开或收起，最多支持三层。"),
        new("\uE73E", "完成与恢复", "点击任务前的圆形按钮完成任务；已完成任务会弱化显示，可恢复或删除。")
    ];

    private static IReadOnlyList<TodoHelpCard> StickyCards() =>
    [
        new("\uE8A7", "进入便签", "在设置中打开便签模式；进入后主界面会隐藏，可从便签回到大界面。"),
        new("\uE840", "置顶显示", "点击置顶按钮，让便签保持在其他窗口上方。"),
        new("\uE890", "透明度与缩放", "在便签设置中调整透明度和 50%-200% 缩放，也可一键恢复 100%。"),
        new("\uE8AB", "调整窗口", "拖动边缘改变大小，拖动顶部 Fowan 区域移动窗口位置。"),
        new("\uE7C2", "拖动排序", "长按任务后拖动；列表边缘会自动滚动，虚线框会预览松手后的具体位置和父子归属。"),
        new("\uE73E", "快速处理", "便签固定显示今日任务；双击任务可编辑标题和备注，也可通过右键菜单删除或创建带备注的子任务。")
    ];

    private static IReadOnlyList<TodoHelpCard> FilterCards() =>
    [
        new("\uE74D", "回收站", "启用回收站时，删除任务会先移入回收站；可恢复整棵任务树，也可永久删除。"),
        new("\uE74E", "自动清理", "设置中可关闭回收站，或选择 7 天、30 天、90 天和自定义清理周期，默认 30 天。"),
        new("\uE71C", "层级筛选", "筛选可只显示一层、一至两层或全部层级；子任务命中时默认会保留它的父任务，可选择严格过滤父任务。"),
        new("\uE787", "组合筛选", "可按完成状态、任务清单、开始日期或执行周期组合筛选，并提供本周、本月快捷项；应用后自动切到全部任务并显示筛选中。"),
        new("\uE8CB", "今日规则", "今日任务页只显示今日未完成项和当天完成项；切换导航视图会清除日期筛选。"),
        new("\uE7C2", "边缘滚动", "拖拽任务靠近列表上下边缘时会自动上下滚动，离边缘越远速度越快并有上限；未完成和已完成任务不会自动跨越分组边界。")
    ];

    private static TodoHelpContentPalette ContentPalette(bool dark) => new(
        Brush(dark, 0xEAF8FA, 0x132A33), Brush(dark, 0xB9E2EA, 0x2C5964),
        Brush(dark, 0x0C7588, 0x2BB7C6), Brush(dark, 0xFFFFFF, 0x061A20),
        Brush(dark, 0x142229, 0xEEF5F8), Brush(dark, 0x536771, 0xAAB8C2),
        Brush(dark, 0xD9E6EA, 0x303D49), Brush(dark, 0xF7FAFB, 0x151D25),
        Brush(dark, 0xFFFFFF, 0x1B2530), Brush(dark, 0xDDF3F7, 0x123A45),
        Brush(dark, 0xFFF6DA, 0x2D2315), Brush(dark, 0xE4BC55, 0x9E7C36),
        Brush(dark, 0x4A3510, 0xF2D18F));

    private static TodoHelpPalette DialogPalette(bool dark) => new(
        dark ? ElementTheme.Dark : ElementTheme.Light,
        new SolidColorBrush(ColorHelper.FromArgb(dark ? (byte)150 : (byte)92, 0, 14, 24)),
        Brush(dark, 0xFFFFFF, 0x0F151B), Brush(dark, 0xC7D8DE, 0x3A4854),
        Brush(dark, 0x142229, 0xEEF5F8), TodoThemePalette.Transparent,
        Brush(dark, 0x0C7588, 0x2BB7C6), Brush(dark, 0xFFFFFF, 0x061A20),
        Brush(dark, 0x09687A, 0x35C8D8), Brush(dark, 0x075A69, 0x1D9EAE),
        Brush(dark, 0xF1F6F8, 0x202A34), Brush(dark, 0xE7F0F3, 0x2A3642),
        Brush(dark, 0xD9E6EA, 0x18232C));

    private static SolidColorBrush Brush(bool dark, uint light, uint darkValue) =>
        TodoThemePalette.Solid(dark ? darkValue : light);

    private static double ContentWidth(Grid root)
    {
        var width = RootWidth(root, 1280);
        return Math.Min(920, Math.Max(300, width - 140));
    }

    private static double DialogWidth(Grid root, double contentWidth) =>
        Math.Min(contentWidth + 96, Math.Max(360, RootWidth(root, contentWidth + 96) - 48));

    private static double PanelHeight(Grid root) =>
        Math.Min(840, Math.Max(1, RootHeight(root, 860) - 32));

    private static double RootWidth(Grid root, double fallback)
    {
        var value = root.XamlRoot?.Size.Width ?? root.ActualWidth;
        return value > 0 ? value : fallback;
    }

    private static double RootHeight(Grid root, double fallback)
    {
        var value = root.XamlRoot?.Size.Height ?? root.ActualHeight;
        return value > 0 ? value : fallback;
    }
}
