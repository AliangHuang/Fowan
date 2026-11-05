using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>Converts common spreadsheet and HTML clipboard tables into bounded RichEdit RTF tables.</summary>
internal static partial class RichTextClipboardTableConverter
{
    private const int MaximumRows = 200;
    private const int MaximumColumns = 40;
    private const int CellWidthTwips = 1800;

    public static bool TryConvertTabularText(string? text, out string rtf)
    {
        rtf = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var rows = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row.Split('\t').Select(cell => cell.Trim()).ToArray())
            .Where(row => row.Length > 0)
            .Take(MaximumRows)
            .ToArray();
        if (rows.Length == 0 || rows.All(row => row.Length < 2)) return false;

        var columns = Math.Min(MaximumColumns, rows.Max(row => row.Length));
        rtf = BuildTable(rows, columns);
        return true;
    }

    public static bool TryConvertHtmlTable(string? html, out string rtf)
    {
        rtf = string.Empty;
        if (string.IsNullOrWhiteSpace(html)) return false;

        var rows = TableRowRegex().Matches(html)
            .Select(match => TableCellRegex().Matches(match.Groups[1].Value)
                .Select(cell => HtmlCellText(cell.Groups[1].Value))
                .ToArray())
            .Where(row => row.Length > 0)
            .Take(MaximumRows)
            .ToArray();
        if (rows.Length == 0 || rows.All(row => row.Length < 2)) return false;

        var columns = Math.Min(MaximumColumns, rows.Max(row => row.Length));
        rtf = BuildTable(rows, columns);
        return true;
    }

    private static string BuildTable(IReadOnlyList<string[]> rows, int columns)
    {
        var result = new StringBuilder(@"{\rtf1\ansi\deff0");
        foreach (var row in rows)
        {
            result.Append(@"\trowd\trgaph108");
            for (var column = 1; column <= columns; column++) result.Append(@"\cellx").Append(column * CellWidthTwips);
            for (var column = 0; column < columns; column++)
            {
                result.Append(@"\intbl ");
                if (column < row.Length) AppendEscapedRtf(result, row[column]);
                result.Append(@"\cell");
            }
            result.Append(@"\row");
        }
        return result.Append('}').ToString();
    }

    private static string HtmlCellText(string input)
    {
        var withBreaks = BreakTagRegex().Replace(input, "\n");
        var withoutTags = HtmlTagRegex().Replace(withBreaks, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Replace('\u00A0', ' ').Trim();
    }

    private static void AppendEscapedRtf(StringBuilder target, string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': target.Append(@"\\"); break;
                case '{': target.Append(@"\{"); break;
                case '}': target.Append(@"\}"); break;
                case '\r': break;
                case '\n': target.Append(@"\line "); break;
                default:
                    if (character <= 0x7f) target.Append(character);
                    else target.Append(@"\u").Append(unchecked((short)character)).Append('?');
                    break;
            }
        }
    }

    [GeneratedRegex("<tr\\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex("<t[dh]\\b[^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();
}
