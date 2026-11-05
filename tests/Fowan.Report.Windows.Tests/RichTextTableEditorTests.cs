using Fowan.Report.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class RichTextTableEditorTests
{
    [Fact]
    public void EnumeratesAndAppendsRowWithoutChangingExistingCells()
    {
        var changed = RichTextTableEditor.TryEdit(TwoByTwoTable, 0, RichTextTableEditor.RichTextTableEdit.AppendRow, out var updated);

        var table = Assert.Single(RichTextTableEditor.GetTables(updated));
        Assert.True(changed);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(2, table.ColumnCount);
        Assert.Contains("计划", updated, StringComparison.Ordinal);
        Assert.Contains("完成", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void AddsAndRemovesLastColumnForEveryRow()
    {
        var added = RichTextTableEditor.TryEdit(TwoByTwoTable, 0, RichTextTableEditor.RichTextTableEdit.AppendColumn, out var withColumn);
        var afterAdd = Assert.Single(RichTextTableEditor.GetTables(withColumn));
        var removed = RichTextTableEditor.TryEdit(withColumn, 0, RichTextTableEditor.RichTextTableEdit.RemoveLastColumn, out var restored);
        var afterRemove = Assert.Single(RichTextTableEditor.GetTables(restored));

        Assert.True(added);
        Assert.Equal(3, afterAdd.ColumnCount);
        Assert.True(removed);
        Assert.Equal(2, afterRemove.ColumnCount);
        Assert.Contains("\\cellx3600", withColumn, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToRemoveTheLastRowOrColumn()
    {
        const string single = @"{\rtf1\ansi\trowd\cellx1800\intbl 唯一单元格\cell\row}";

        var removeRow = RichTextTableEditor.TryEdit(single, 0, RichTextTableEditor.RichTextTableEdit.RemoveLastRow, out var rowResult);
        var removeColumn = RichTextTableEditor.TryEdit(single, 0, RichTextTableEditor.RichTextTableEdit.RemoveLastColumn, out var columnResult);

        Assert.False(removeRow);
        Assert.Equal(single, rowResult);
        Assert.False(removeColumn);
        Assert.Equal(single, columnResult);
    }

    [Fact]
    public void RefusesStructuralEditsForMergedCells()
    {
        const string merged = @"{\rtf1\ansi\trowd\clmgf\cellx1200\clmrg\cellx2400\intbl 合并标题\cell\intbl \cell\row}";

        var changed = RichTextTableEditor.TryEdit(merged, 0, RichTextTableEditor.RichTextTableEdit.AppendColumn, out var result);

        Assert.False(changed);
        Assert.Equal(merged, result);
    }

    [Fact]
    public void ReadsAndWritesTableCellsWithoutChangingUntouchedCells()
    {
        var source = new[]
        {
            (IReadOnlyList<string>)new[] { "事项", "进度" },
            (IReadOnlyList<string>)new[] { "计划", "完成" }
        };

        var changed = RichTextTableEditor.TrySetTableData(TwoByTwoTable, 0, source, out var updated);
        var read = RichTextTableEditor.TryGetTableData(updated, 0, out var table);

        Assert.True(changed);
        Assert.True(read);
        Assert.Equal("进度", table.Cells[0][1]);
        Assert.Equal("完成", table.Cells[1][1]);
    }

    private const string TwoByTwoTable = @"{\rtf1\ansi\trowd\cellx1200\cellx2400\intbl 事项\cell\intbl 状态\cell\row\trowd\cellx1200\cellx2400\intbl 计划\cell\intbl 完成\cell\row}";
}
