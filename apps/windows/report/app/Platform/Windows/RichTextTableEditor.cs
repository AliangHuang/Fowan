using System.Text.RegularExpressions;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>
/// Performs bounded structural edits on ordinary RTF tables. Complex or merged-cell tables are
/// deliberately left untouched rather than risking document corruption.
/// </summary>
internal static class RichTextTableEditor
{
    public static IReadOnlyList<RichTextTableInfo> GetTables(string? rtf) =>
        Parse(rtf).Select((table, index) => new RichTextTableInfo(index, table.Rows.Count, table.ColumnCount)).ToArray();

    public static bool TryEdit(string? rtf, int tableIndex, RichTextTableEdit edit, out string updatedRtf)
    {
        updatedRtf = rtf ?? string.Empty;
        var tables = Parse(updatedRtf);
        if (tableIndex < 0 || tableIndex >= tables.Count) return false;

        var table = tables[tableIndex];
        if (!table.IsRectangular) return false;

        return edit switch
        {
            RichTextTableEdit.AppendRow => TryAppendRow(updatedRtf, table, out updatedRtf),
            RichTextTableEdit.RemoveLastRow => TryRemoveLastRow(updatedRtf, table, out updatedRtf),
            RichTextTableEdit.AppendColumn => TryAppendColumn(updatedRtf, table, out updatedRtf),
            RichTextTableEdit.RemoveLastColumn => TryRemoveLastColumn(updatedRtf, table, out updatedRtf),
            _ => false
        };
    }

    public static bool TryGetTableData(string? rtf, int tableIndex, out RichTextTableData tableData)
    {
        tableData = new RichTextTableData(tableIndex, []);
        if (string.IsNullOrWhiteSpace(rtf)) return false;

        var tables = Parse(rtf);
        if (tableIndex < 0 || tableIndex >= tables.Count || !tables[tableIndex].IsRectangular) return false;

        tableData = new RichTextTableData(
            tableIndex,
            tables[tableIndex].Rows
                .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, row.CellEnds.Count)
                    .Select(cell => RtfPlainText(rtf[CellContentStart(row, cell)..row.CellEnds[cell].Start]))
                    .ToArray())
                .ToArray());
        return true;
    }

    public static bool TrySetTableData(string? rtf, int tableIndex, IReadOnlyList<IReadOnlyList<string>> cells, out string updatedRtf)
    {
        updatedRtf = rtf ?? string.Empty;
        var tables = Parse(updatedRtf);
        if (tableIndex < 0 || tableIndex >= tables.Count) return false;

        var table = tables[tableIndex];
        if (!table.IsRectangular || cells.Count != table.Rows.Count || cells.Any(row => row.Count != table.ColumnCount)) return false;

        var replacements = new List<Replacement>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.CellEnds.Count; columnIndex++)
            {
                var contentStart = CellContentStart(row, columnIndex);
                var contentEnd = row.CellEnds[columnIndex].Start;
                var current = RtfPlainText(updatedRtf[contentStart..contentEnd]);
                var desired = cells[rowIndex][columnIndex] ?? string.Empty;
                if (string.Equals(current, desired, StringComparison.Ordinal)) continue;
                replacements.Add(new Replacement(contentStart, contentEnd - contentStart, @"\intbl " + EscapeRtf(desired)));
            }
        }

        updatedRtf = ApplyReplacements(updatedRtf, replacements);
        return true;
    }

    private static bool TryAppendRow(string rtf, TableSpan table, out string updatedRtf)
    {
        var row = table.Rows[^1];
        var prefix = rtf[row.Start..row.ContentStart];
        var cells = string.Concat(Enumerable.Repeat(@"\intbl \cell", table.ColumnCount));
        updatedRtf = rtf.Insert(row.End, prefix + cells + @"\row");
        return true;
    }

    private static bool TryRemoveLastRow(string rtf, TableSpan table, out string updatedRtf)
    {
        updatedRtf = rtf;
        if (table.Rows.Count <= 1) return false;

        var row = table.Rows[^1];
        updatedRtf = rtf.Remove(row.Start, row.End - row.Start);
        return true;
    }

    private static bool TryAppendColumn(string rtf, TableSpan table, out string updatedRtf)
    {
        var replacements = new List<Replacement>();
        foreach (var row in table.Rows)
        {
            var lastBoundary = row.CellBoundaries[^1];
            var lastPosition = lastBoundary.Parameter!.Value;
            var previousPosition = row.CellBoundaries.Count > 1 ? row.CellBoundaries[^2].Parameter!.Value : 0;
            var width = Math.Max(720, lastPosition - previousPosition);
            if (lastPosition > int.MaxValue - width) { updatedRtf = rtf; return false; }

            replacements.Add(new Replacement(lastBoundary.End, 0, @"\cellx" + (lastPosition + width)));
            replacements.Add(new Replacement(row.End - row.RowToken.Length, 0, @"\intbl \cell"));
        }

        updatedRtf = ApplyReplacements(rtf, replacements);
        return true;
    }

    private static bool TryRemoveLastColumn(string rtf, TableSpan table, out string updatedRtf)
    {
        updatedRtf = rtf;
        if (table.ColumnCount <= 1) return false;

        var replacements = new List<Replacement>();
        foreach (var row in table.Rows)
        {
            var contentStart = row.CellEnds[^2].End;
            replacements.Add(new Replacement(contentStart, row.RowToken.Start - contentStart, string.Empty));
            var boundary = row.CellBoundaries[^1];
            replacements.Add(new Replacement(boundary.Start, boundary.End - boundary.Start, string.Empty));
        }

        updatedRtf = ApplyReplacements(rtf, replacements);
        return true;
    }

    private static string ApplyReplacements(string source, IEnumerable<Replacement> replacements)
    {
        var result = source;
        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
            result = result.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Text);
        return result;
    }

    private static int CellContentStart(RowSpan row, int cellIndex) =>
        cellIndex == 0 ? row.ContentStart : row.CellEnds[cellIndex - 1].End;

    private static string EscapeRtf(string value)
    {
        var result = new System.Text.StringBuilder();
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': result.Append(@"\\"); break;
                case '{': result.Append(@"\{"); break;
                case '}': result.Append(@"\}"); break;
                case '\r': break;
                case '\n': result.Append(@"\line "); break;
                default:
                    if (character <= 0x7f) result.Append(character);
                    else result.Append(@"\u").Append(unchecked((short)character)).Append('?');
                    break;
            }
        }
        return result.ToString();
    }

    private static string RtfPlainText(string rtf)
    {
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < rtf.Length; index++)
        {
            var character = rtf[index];
            if (character is '{' or '}') continue;
            if (character != '\\' || ++index >= rtf.Length)
            {
                result.Append(character);
                continue;
            }

            var escaped = rtf[index];
            if (escaped is '\\' or '{' or '}') { result.Append(escaped); continue; }
            if (escaped == '~') { result.Append(' '); continue; }
            if (escaped == '\'' && index + 2 < rtf.Length && byte.TryParse(rtf.AsSpan(index + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                result.Append(System.Text.Encoding.GetEncoding(1252).GetString([value]));
                index += 2;
                continue;
            }
            if (!char.IsLetter(escaped)) continue;

            var nameStart = index;
            while (index < rtf.Length && char.IsLetter(rtf[index])) index++;
            var name = rtf[nameStart..index];
            var parameterStart = index;
            if (index < rtf.Length && rtf[index] is '-' or '+') index++;
            while (index < rtf.Length && char.IsDigit(rtf[index])) index++;
            var hasParameter = index > parameterStart;
            var parameter = hasParameter && int.TryParse(rtf[parameterStart..index], out var parsed) ? parsed : 0;
            if (index < rtf.Length && rtf[index] == ' ') index++;

            if (name.Equals("u", StringComparison.OrdinalIgnoreCase) && hasParameter)
            {
                result.Append((char)unchecked((short)parameter));
                if (index < rtf.Length && rtf[index] is not '\\' and not '{' and not '}') index++;
            }
            else if (name.Equals("par", StringComparison.OrdinalIgnoreCase) || name.Equals("line", StringComparison.OrdinalIgnoreCase)) result.Append('\n');
            else if (name.Equals("tab", StringComparison.OrdinalIgnoreCase)) result.Append('\t');

            index--;
        }
        return result.ToString().Trim();
    }

    private static List<TableSpan> Parse(string? rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf)) return [];

        var controls = ScanControls(rtf);
        var rows = new List<RowSpan>();
        foreach (var start in controls.Where(token => token.Name.Equals("trowd", StringComparison.OrdinalIgnoreCase)))
        {
            var end = controls.FirstOrDefault(token =>
                token.Start >= start.End &&
                token.Depth == start.Depth &&
                token.Name.Equals("row", StringComparison.OrdinalIgnoreCase));
            if (end is null) continue;

            var rowControls = controls.Where(token => token.Start >= start.Start && token.End <= end.End && token.Depth == start.Depth).ToArray();
            var cellEnds = rowControls.Where(token => token.Name.Equals("cell", StringComparison.OrdinalIgnoreCase)).ToArray();
            var boundaries = rowControls
                .Where(token => token.Start < (cellEnds.FirstOrDefault()?.Start ?? int.MaxValue) && token.Name.Equals("cellx", StringComparison.OrdinalIgnoreCase) && token.Parameter is not null)
                .ToArray();
            if (cellEnds.Length == 0 || boundaries.Length != cellEnds.Length) continue;
            var hasMergedCells = rowControls.Any(token => token.Name.Equals("clmgf", StringComparison.OrdinalIgnoreCase) ||
                token.Name.Equals("clmrg", StringComparison.OrdinalIgnoreCase) ||
                token.Name.Equals("clvmgf", StringComparison.OrdinalIgnoreCase) ||
                token.Name.Equals("clvmrg", StringComparison.OrdinalIgnoreCase));

            rows.Add(new RowSpan(start.Start, end.End, boundaries[^1].End, start, end, cellEnds, boundaries, hasMergedCells));
        }

        rows = rows.OrderBy(row => row.Start).ToList();
        var tables = new List<TableSpan>();
        var current = new List<RowSpan>();
        foreach (var row in rows)
        {
            if (current.Count > 0 && HasVisibleText(rtf![current[^1].End..row.Start]))
            {
                tables.Add(new TableSpan(current));
                current = [];
            }
            current.Add(row);
        }
        if (current.Count > 0) tables.Add(new TableSpan(current));
        return tables;
    }

    private static bool HasVisibleText(string betweenRows)
    {
        for (var index = 0; index < betweenRows.Length; index++)
        {
            var character = betweenRows[index];
            if (character is '{' or '}' || char.IsWhiteSpace(character)) continue;
            if (character != '\\') return true;
            if (++index >= betweenRows.Length) return false;

            if (betweenRows[index] == '\'') return true;
            if (!char.IsLetter(betweenRows[index]))
            {
                if (betweenRows[index] is '{' or '}' or '\\') return true;
                continue;
            }

            while (index + 1 < betweenRows.Length && char.IsLetter(betweenRows[index + 1])) index++;
            if (index + 1 < betweenRows.Length && betweenRows[index + 1] is '-' or '+') index++;
            while (index + 1 < betweenRows.Length && char.IsDigit(betweenRows[index + 1])) index++;
        }
        return false;
    }

    private static List<ControlToken> ScanControls(string rtf)
    {
        var result = new List<ControlToken>();
        var depth = 0;
        for (var index = 0; index < rtf.Length; index++)
        {
            if (rtf[index] == '{') { depth++; continue; }
            if (rtf[index] == '}') { depth--; continue; }
            if (rtf[index] != '\\' || ++index >= rtf.Length) continue;

            var start = index - 1;
            if (!char.IsLetter(rtf[index]))
            {
                if (rtf[index] == '\'' && index + 2 < rtf.Length) index += 2;
                continue;
            }

            var nameStart = index;
            while (index < rtf.Length && char.IsLetter(rtf[index])) index++;
            var name = rtf[nameStart..index];
            var parameterStart = index;
            if (index < rtf.Length && rtf[index] is '-' or '+') index++;
            while (index < rtf.Length && char.IsDigit(rtf[index])) index++;
            int? parameter = null;
            if (index > parameterStart && int.TryParse(rtf[parameterStart..index], out var value)) parameter = value;
            if (index < rtf.Length && rtf[index] == ' ') index++;
            result.Add(new ControlToken(name, start, index, depth, parameter));
            index--;
        }
        return result;
    }

    internal sealed record RichTextTableInfo(int Index, int RowCount, int ColumnCount);

    internal sealed record RichTextTableData(int Index, IReadOnlyList<IReadOnlyList<string>> Cells);

    internal enum RichTextTableEdit
    {
        AppendRow,
        RemoveLastRow,
        AppendColumn,
        RemoveLastColumn
    }

    private sealed record ControlToken(string Name, int Start, int End, int Depth, int? Parameter)
    {
        public int Length => End - Start;
    }

    private sealed record RowSpan(
        int Start,
        int End,
        int ContentStart,
        ControlToken StartToken,
        ControlToken RowToken,
        IReadOnlyList<ControlToken> CellEnds,
        IReadOnlyList<ControlToken> CellBoundaries,
        bool HasMergedCells);

    private sealed class TableSpan(IReadOnlyList<RowSpan> rows)
    {
        public IReadOnlyList<RowSpan> Rows { get; } = rows;
        public int ColumnCount => Rows[0].CellEnds.Count;
        public bool IsRectangular => Rows.All(row => !row.HasMergedCells && row.CellEnds.Count == ColumnCount && row.CellBoundaries.Count == ColumnCount);
    }

    private sealed record Replacement(int Start, int Length, string Text);
}
