using Fowan.Report.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class ReportTableClipboardImporterTests
{
    [Fact]
    public void ParsesTsvIntoRectangularTable()
    {
        Assert.True(ReportTableClipboardImporter.TryParseTabSeparated("事项\t状态\r\n汇报\t完成", out var table));
        Assert.Equal("完成", table[1][1]);
    }

    [Fact]
    public void ParsesHtmlTableWithoutAcceptingOrdinaryHtml()
    {
        Assert.True(ReportTableClipboardImporter.TryParseHtml("<table><tr><th>事项</th><th>状态</th></tr><tr><td>汇报</td><td>完成</td></tr></table>", out var table));
        Assert.Equal(2, table.Count);
        Assert.False(ReportTableClipboardImporter.TryParseHtml("<p>普通文本</p>", out _));
    }

    [Fact]
    public void ParsesSimpleRectangularRtfTable()
    {
        const string rtf = @"{\rtf1\ansi\trowd\cellx1200\cellx2400\intbl A\cell\intbl B\cell\row}";
        Assert.True(ReportTableClipboardImporter.TryParseRtf(rtf, out var table));
        Assert.Equal("B", table[0][1]);
    }
}
