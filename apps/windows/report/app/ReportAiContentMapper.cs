using Fowan.Ai.Shared.Models;
using Fowan.Report.Shared;

namespace Fowan.Report.Windows;

internal static class ReportAiContentMapper
{
    public static AiReportContentDocument ToWire(ReportTextDocument source)
    {
        var document = ReportTextDocuments.Normalize(source);
        return new(
            "text",
            document.Blocks.Select(block => new AiReportContentBlock(
                ToWireKind(block.Kind),
                block.Text,
                block.Bold,
                block.Italic,
                block.Link,
                block.IsChecked,
                block.Table is null ? null : new AiReportContentTable(
                    block.Table.Cells.Select(row => (IReadOnlyList<AiReportContentCell>)row
                        .Select(value => new AiReportContentCell(value, "text", true)).ToArray()).ToArray(),
                    false))).ToArray(),
            []);
    }

    public static AiReportContentDocument ToWire(ReportFileContentDocument source) => new(
        source.Format,
        source.Blocks.Select(block => new AiReportContentBlock(
            block.Kind,
            block.Text,
            false,
            false,
            null,
            false,
            block.Table is null ? null : new AiReportContentTable(
                block.Table.Rows.Select(row => (IReadOnlyList<AiReportContentCell>)row
                    .Select(ToWire).ToArray()).ToArray(),
                block.Table.CanAppendRows))).ToArray(),
        source.Sheets.Select(sheet => new AiReportContentSheet(
            sheet.Name,
            sheet.Rows.Select(row => (IReadOnlyList<AiReportContentCell>)row.Select(ToWire).ToArray()).ToArray(),
            sheet.CanAppendRows)).ToArray());

    public static ReportTextDocument FromWireText(AiReportContentDocument source)
    {
        if (!string.Equals(source.Format, "text", StringComparison.Ordinal) || source.Sheets.Count != 0)
            throw new InvalidDataException("AI 返回的文本汇报结构无效。");
        var blocks = source.Blocks.Select(block => new ReportTextBlock(
            Guid.NewGuid().ToString("N"),
            FromWireKind(block.Kind),
            block.Text ?? string.Empty,
            block.Bold,
            block.Italic,
            block.Link,
            block.IsChecked,
            block.Table is null ? null : new ReportTextTable(block.Table.Rows.Select(row =>
                (IReadOnlyList<string>)row.Select(cell => RequireEditableText(cell)).ToArray()).ToArray()))).ToArray();
        return ReportTextDocuments.Normalize(new(ReportTextDocument.CurrentVersion, blocks));
    }

    public static ReportFileContentDocument FromWireFile(AiReportContentDocument source)
    {
        if (source.Format is not "docx" and not "xlsx") throw new InvalidDataException("AI 返回的文件类型无效。");
        return new(
            source.Format,
            source.Blocks.Select(block => new ReportFileBlock(
                block.Kind,
                block.Text,
                block.Table is null ? null : new ReportFileTable(
                    block.Table.Rows.Select(row => (IReadOnlyList<ReportFileCell>)row.Select(FromWire).ToArray()).ToArray(),
                    block.Table.CanAppendRows))).ToArray(),
            source.Sheets.Select(sheet => new ReportFileSheet(
                sheet.Name,
                sheet.Rows.Select(row => (IReadOnlyList<ReportFileCell>)row.Select(FromWire).ToArray()).ToArray(),
                sheet.CanAppendRows)).ToArray());
    }

    private static AiReportContentCell ToWire(ReportFileCell cell) => new(cell.Value, cell.ValueKind, cell.Editable);
    private static ReportFileCell FromWire(AiReportContentCell cell) => new(cell.Value, cell.ValueKind, cell.Editable);

    private static string RequireEditableText(AiReportContentCell cell)
    {
        if (!cell.Editable || !string.Equals(cell.ValueKind, "text", StringComparison.Ordinal))
            throw new InvalidDataException("AI 返回的文本表格单元格无效。");
        return cell.Value ?? string.Empty;
    }

    private static string ToWireKind(ReportTextBlockKind kind) => kind switch
    {
        ReportTextBlockKind.Heading1 => "heading1",
        ReportTextBlockKind.Heading2 => "heading2",
        ReportTextBlockKind.Heading3 => "heading3",
        ReportTextBlockKind.BulletedList => "bulletedList",
        ReportTextBlockKind.NumberedList => "numberedList",
        ReportTextBlockKind.TodoList => "todoList",
        ReportTextBlockKind.Quote => "quote",
        ReportTextBlockKind.Code => "code",
        ReportTextBlockKind.Divider => "divider",
        ReportTextBlockKind.Table => "table",
        _ => "paragraph"
    };

    private static ReportTextBlockKind FromWireKind(string kind) => kind switch
    {
        "paragraph" => ReportTextBlockKind.Paragraph,
        "heading1" => ReportTextBlockKind.Heading1,
        "heading2" => ReportTextBlockKind.Heading2,
        "heading3" => ReportTextBlockKind.Heading3,
        "bulletedList" => ReportTextBlockKind.BulletedList,
        "numberedList" => ReportTextBlockKind.NumberedList,
        "todoList" => ReportTextBlockKind.TodoList,
        "quote" => ReportTextBlockKind.Quote,
        "code" => ReportTextBlockKind.Code,
        "divider" => ReportTextBlockKind.Divider,
        "table" => ReportTextBlockKind.Table,
        _ => throw new InvalidDataException("AI 返回的文本块类型无效。")
    };
}
