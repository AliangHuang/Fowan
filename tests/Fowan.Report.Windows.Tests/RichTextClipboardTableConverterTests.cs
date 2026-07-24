using Fowan.Report.Windows.Platform.Windows;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class RichTextClipboardTableConverterTests
{
    [Fact]
    public void ConvertsExcelTabSeparatedTextToRtfTable()
    {
        var converted = RichTextClipboardTableConverter.TryConvertTabularText("事项\t状态\r\n编写周报\t完成", out var rtf);

        Assert.True(converted);
        Assert.Contains(@"\trowd", rtf, StringComparison.Ordinal);
        Assert.Equal(2, Count(rtf, @"\row"));
        Assert.Contains(@"\u20107?", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\u23436?", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertsHtmlClipboardTableToRtfTable()
    {
        const string html = "<table><tr><th>事项</th><th>说明</th></tr><tr><td>梳理&lt;需求&gt;</td><td>本周完成</td></tr></table>";

        var converted = RichTextClipboardTableConverter.TryConvertHtmlTable(html, out var rtf);

        Assert.True(converted);
        Assert.Equal(2, Count(rtf, @"\row"));
        Assert.Contains(@"\u20107?", rtf, StringComparison.Ordinal);
        Assert.Contains("<\\u-26880?\\u27714?>", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotTurnOrdinaryMultilineTextIntoTable()
    {
        var converted = RichTextClipboardTableConverter.TryConvertTabularText("第一段\n第二段", out var rtf);

        Assert.False(converted);
        Assert.Empty(rtf);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
    }
}
