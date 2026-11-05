using System.Net;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>Imports only rectangular clipboard tables into the safe report document model.</summary>
internal static partial class ReportTableClipboardImporter
{
    public static async Task<IReadOnlyList<IReadOnlyList<string>>?> ReadTableAsync(DataPackageView content)
    {
        try
        {
            if (content.Contains(StandardDataFormats.Html))
            {
                var html = await content.GetHtmlFormatAsync();
                if (TryParseHtml(html, out var htmlTable)) return htmlTable;
            }
            if (content.Contains(StandardDataFormats.Rtf))
            {
                var rtf = await content.GetRtfAsync();
                if (TryParseRtf(rtf, out var rtfTable)) return rtfTable;
            }
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                if (TryParseTabSeparated(text, out var textTable)) return textTable;
            }
        }
        catch
        {
            // Clipboard content may be released by its owner during an asynchronous read.
        }
        return null;
    }

    internal static bool TryParseTabSeparated(string? value, out IReadOnlyList<IReadOnlyList<string>> table)
    {
        table = [];
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('\t')) return false;
        var rows = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(row => row.Split('\t').Select(cell => cell.TrimEnd()).ToArray())
            .Where(row => row.Length > 0)
            .ToArray();
        if (rows.Length == 0 || rows.All(row => row.Length <= 1)) return false;
        table = Rectangular(rows);
        return true;
    }

    internal static bool TryParseHtml(string? value, out IReadOnlyList<IReadOnlyList<string>> table)
    {
        table = [];
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("<table", StringComparison.OrdinalIgnoreCase)) return false;
        var rows = new List<IReadOnlyList<string>>();
        foreach (Match rowMatch in RowRegex().Matches(value))
        {
            var cells = new List<string>();
            foreach (Match cellMatch in CellRegex().Matches(rowMatch.Groups[1].Value))
            {
                var cell = BreakRegex().Replace(cellMatch.Groups[1].Value, "\n");
                cell = TagRegex().Replace(cell, string.Empty);
                cells.Add(WebUtility.HtmlDecode(cell).Trim());
            }
            if (cells.Count > 0) rows.Add(cells);
        }
        if (rows.Count == 0) return false;
        table = Rectangular(rows);
        return true;
    }

    internal static bool TryParseRtf(string? value, out IReadOnlyList<IReadOnlyList<string>> table)
    {
        table = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        var tables = RichTextTableEditor.GetTables(value);
        if (tables.Count == 0 || !RichTextTableEditor.TryGetTableData(value, 0, out var parsed)) return false;
        table = Rectangular(parsed.Cells);
        return true;
    }

    private static IReadOnlyList<IReadOnlyList<string>> Rectangular(IEnumerable<IReadOnlyList<string>> rows)
    {
        var values = rows.ToArray();
        var columns = Math.Max(1, values.Max(row => row.Count));
        return values.Select(row => (IReadOnlyList<string>)Enumerable.Range(0, columns)
            .Select(column => column < row.Count ? row[column] ?? string.Empty : string.Empty).ToArray()).ToArray();
    }

    [GeneratedRegex("<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRegex();
    [GeneratedRegex("<(?:td|th)[^>]*>(.*?)</(?:td|th)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRegex();
    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();
}
