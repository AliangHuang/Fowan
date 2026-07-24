using Fowan.Report.Shared;

namespace Fowan.Report.Windows.Presentation;

/// <summary>Deterministic, non-persistent data for 16:9 visual-regression captures.</summary>
internal static class ReportVisualFixture
{
    public static ReportTaskPreview Tasks { get; } = new(
        [
            new("完成周报筛选语义复用", "确认开始日期和执行周期结果一致。", "产品研发", 1, true,
                new DateTime(2026, 7, 20), new DateTime(2026, 7, 20), new DateTimeOffset(2026, 7, 20, 17, 30, 0, TimeSpan.FromHours(8)), "completed"),
            new("整理客户反馈", "汇总本周问题和改善项。", "产品研发", 2, false,
                new DateTime(2026, 7, 19), null, new DateTimeOffset(2026, 7, 20, 11, 10, 0, TimeSpan.FromHours(8)), "completed")
            ,new("设计并验证核心流程原型", "完成关键页面交互验证。", "用户研究", 2, false,
                new DateTime(2026, 7, 18), new DateTime(2026, 7, 19), new DateTimeOffset(2026, 7, 19, 16, 40, 0, TimeSpan.FromHours(8)), "completed")
            ,new("完成用户访谈需求整理", "沉淀用户痛点与优先级。", "用户研究", 1, false,
                new DateTime(2026, 7, 17), new DateTime(2026, 7, 18), new DateTimeOffset(2026, 7, 18, 18, 10, 0, TimeSpan.FromHours(8)), "completed")
            ,new("开发数据看板关键接口", "提供汇报统计所需数据。", "工程开发", 1, true,
                new DateTime(2026, 7, 16), new DateTime(2026, 7, 18), new DateTimeOffset(2026, 7, 18, 15, 20, 0, TimeSpan.FromHours(8)), "completed")
            ,new("完善自动化测试用例", "覆盖日期筛选和层级组合。", "工程开发", 2, false,
                new DateTime(2026, 7, 16), null, new DateTimeOffset(2026, 7, 18, 14, 0, 0, TimeSpan.FromHours(8)), "completed")
            ,new("编写 API 接口文档", "补充模型输出约束说明。", "工程开发", 2, false,
                new DateTime(2026, 7, 15), new DateTime(2026, 7, 17), new DateTimeOffset(2026, 7, 17, 17, 0, 0, TimeSpan.FromHours(8)), "completed")
            ,new("准备版本发布说明", "整理本周变化和已知风险。", "发布上线", 1, false,
                new DateTime(2026, 7, 15), new DateTime(2026, 7, 17), new DateTimeOffset(2026, 7, 17, 16, 10, 0, TimeSpan.FromHours(8)), "completed")
            ,new("校验模板副本可重新打开", "验证 Word 与 Excel 输出副本。", "产品研发", 1, false,
                new DateTime(2026, 7, 14), new DateTime(2026, 7, 16), new DateTimeOffset(2026, 7, 16, 13, 0, 0, TimeSpan.FromHours(8)), "completed")
            ,new("补充授权提示文案", "明确本次请求的数据边界。", "产品研发", 2, false,
                new DateTime(2026, 7, 14), null, new DateTimeOffset(2026, 7, 16, 10, 20, 0, TimeSpan.FromHours(8)), "completed")
            ,new("复核视觉夹具截图", "确认深色 Fluent 布局。", "工程开发", 1, false,
                new DateTime(2026, 7, 13), new DateTime(2026, 7, 15), new DateTimeOffset(2026, 7, 15, 18, 30, 0, TimeSpan.FromHours(8)), "completed")
            ,new("归档本周决策记录", "记录范围、模板和安全约束。", "产品研发", 2, false,
                new DateTime(2026, 7, 13), new DateTime(2026, 7, 14), new DateTimeOffset(2026, 7, 14, 17, 10, 0, TimeSpan.FromHours(8)), "completed")
        ],
        [
            new("完成汇报模板验收", "验证 Word 和 Excel 副本可重新打开。", "产品研发", 1, true,
                new DateTime(2026, 7, 21), new DateTime(2026, 7, 25), null, "unfinished"),
            new("准备下周计划", "按优先级列出可执行事项。", "产品研发", 1, false,
                new DateTime(2026, 7, 21), null, null, "unfinished")
            ,new("完善自动化测试用例", "补齐文件填充失败场景。", "工程开发", 1, true,
                new DateTime(2026, 7, 21), new DateTime(2026, 7, 26), null, "unfinished")
            ,new("编写 API 接口文档", "补充结构化输出字段说明。", "工程开发", 2, false,
                new DateTime(2026, 7, 22), new DateTime(2026, 7, 27), null, "unfinished")
            ,new("准备版本发布说明", "整理发布验证清单。", "发布上线", 1, false,
                new DateTime(2026, 7, 23), new DateTime(2026, 7, 28), null, "unfinished")
        ]);

    public const string Template = "# 本周工作汇报\n\n## 已完成\n{{completed_rows}}\n\n## 进行中\n{{unfinished_rows}}";
    public const string Example = "- 标题：简短、可验证\n- 状态：已完成 / 进行中";
    public const string Requirements = "突出完成结果与下周风险，避免空泛表述。";
}
