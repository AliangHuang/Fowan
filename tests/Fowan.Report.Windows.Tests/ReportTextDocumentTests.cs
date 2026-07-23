using Fowan.Report.Shared;
using Fowan.Report.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class ReportTextDocumentTests
{
    [Fact]
    public void TableCommandsInsertAroundCoordinatesAndPreserveExistingValues()
    {
        var document = WithTable([new[] { "事项", "状态" }, new[] { "编写", "完成" }], out var id);

        Assert.True(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.InsertRowAbove, id, Row: 1), out var withRow));
        Assert.True(ReportTextDocuments.TryApply(withRow, new(ReportTextCommandKind.InsertColumnLeft, id, Column: 1), out var updated));

        var table = Table(updated, id);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("事项", table.Cells[0][0]);
        Assert.Equal("完成", table.Cells[2][2]);
    }

    [Fact]
    public void ClearCommandsRespectCellRowColumnAndTableScopes()
    {
        var document = WithTable([new[] { "A", "B" }, new[] { "C", "D" }], out var id);
        Assert.True(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.ClearCell, id, Row: 0, Column: 0), out var cell));
        Assert.True(ReportTextDocuments.TryApply(cell, new(ReportTextCommandKind.ClearRow, id, Row: 1), out var row));
        Assert.True(ReportTextDocuments.TryApply(row, new(ReportTextCommandKind.ClearColumn, id, Column: 1), out var column));

        var table = Table(column, id);
        Assert.All(table.Cells, values => Assert.All(values, Assert.Empty));
    }

    [Fact]
    public void LastRowAndColumnCannotBeDeletedButTableCanBeRemoved()
    {
        var document = WithTable([new[] { "唯一" }], out var id);
        Assert.False(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.DeleteRow, id, Row: 0), out var noRow));
        Assert.Same(document, noRow);
        Assert.False(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.DeleteColumn, id, Column: 0), out var noColumn));
        Assert.Same(document, noColumn);
        Assert.True(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.DeleteTable, id), out var removed));
        Assert.Single(removed.Blocks);
        Assert.Equal(ReportTextBlockKind.Paragraph, removed.Blocks[0].Kind);
    }

    [Fact]
    public void MarkdownProjectionAndParserKeepStructuredTableAndBlocks()
    {
        var source = ReportTextDocuments.FromMarkdown("# 标题\n\n- [x] 完成\n\n| 事项 | 状态 |\n| --- | --- |\n| 汇报 | 完成 |");
        var markdown = ReportTextDocuments.ToMarkdown(source);
        var parsed = ReportTextDocuments.FromMarkdown(markdown);

        Assert.Contains("# 标题", markdown, StringComparison.Ordinal);
        Assert.Contains("汇报\t完成", ReportTextDocuments.ToPlainText(parsed), StringComparison.Ordinal);
        Assert.Contains(parsed.Blocks, block => block.Kind == ReportTextBlockKind.Table);
    }

    [Fact]
    public void InvalidCommandLeavesDocumentUntouched()
    {
        var document = ReportTextDocument.Empty;

        Assert.False(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.UpdateCell, "missing", Row: 0, Column: 0), out var updated));

        Assert.Same(document, updated);
    }

    [Fact]
    public void SafeDiagnosticsDoNotIncludeExceptionMessage()
    {
        var safe = ReportSafeDiagnostics.Format(new InvalidOperationException("待办备注和自定义要求不得写入日志"));

        Assert.DoesNotContain("待办备注", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("自定义要求", safe, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", safe, StringComparison.Ordinal);
    }

    private static ReportTextDocument WithTable(IReadOnlyList<IReadOnlyList<string>> cells, out string id)
    {
        var document = ReportTextDocument.Empty;
        Assert.True(ReportTextDocuments.TryApply(document, new(ReportTextCommandKind.InsertTable, document.Blocks[0].Id, Cells: cells), out var updated));
        id = updated.Blocks.Single(block => block.Kind == ReportTextBlockKind.Table).Id;
        return updated;
    }

    private static ReportTextTable Table(ReportTextDocument document, string id) =>
        Assert.IsType<ReportTextTable>(document.Blocks.Single(block => block.Id == id).Table);
}
