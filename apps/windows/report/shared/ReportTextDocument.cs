using System.Text;

namespace Fowan.Report.Shared;

/// <summary>
/// Portable, user-authored document used by the report text mode. It deliberately
/// contains no WinUI, RTF, clipboard, or provider-specific representation.
/// </summary>
public enum ReportTextBlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    BulletedList,
    NumberedList,
    TodoList,
    Quote,
    Code,
    Divider,
    Table
}

public sealed record ReportTextTable(IReadOnlyList<IReadOnlyList<string>> Cells)
{
    public int RowCount => Cells.Count;
    public int ColumnCount => Cells.Count == 0 ? 0 : Cells[0].Count;
}

public sealed record ReportTextBlock(
    string Id,
    ReportTextBlockKind Kind,
    string Text = "",
    bool Bold = false,
    bool Italic = false,
    string? Link = null,
    bool IsChecked = false,
    ReportTextTable? Table = null);

public sealed record ReportTextDocument(int Version, IReadOnlyList<ReportTextBlock> Blocks)
{
    public const int CurrentVersion = 1;

    public static ReportTextDocument Empty { get; } = new(CurrentVersion,
        [new ReportTextBlock(Guid.NewGuid().ToString("N"), ReportTextBlockKind.Paragraph)]);
}

public enum ReportTextCommandKind
{
    UpdateText,
    InsertBlockAfter,
    DeleteBlock,
    SetBold,
    SetItalic,
    SetLink,
    SetChecked,
    InsertTable,
    UpdateCell,
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    ClearCell,
    ClearRow,
    ClearColumn,
    ClearTable,
    DeleteRow,
    DeleteColumn,
    DeleteTable,
    FillTable
}

/// <summary>Typed, all-or-nothing edit against one block or table coordinate.</summary>
public sealed record ReportTextCommand(
    ReportTextCommandKind Kind,
    string? BlockId = null,
    ReportTextBlockKind? BlockKind = null,
    string? Text = null,
    int Row = -1,
    int Column = -1,
    bool? Value = null,
    IReadOnlyList<IReadOnlyList<string>>? Cells = null);

public static class ReportTextDocuments
{
    public static ReportTextDocument Normalize(ReportTextDocument? source)
    {
        if (source is null || source.Blocks.Count == 0) return ReportTextDocument.Empty;
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var blocks = new List<ReportTextBlock>(source.Blocks.Count);
        foreach (var sourceBlock in source.Blocks)
        {
            var id = string.IsNullOrWhiteSpace(sourceBlock.Id) || !identifiers.Add(sourceBlock.Id)
                ? Guid.NewGuid().ToString("N")
                : sourceBlock.Id;
            var table = sourceBlock.Kind == ReportTextBlockKind.Table
                ? NormalizeTable(sourceBlock.Table?.Cells)
                : null;
            blocks.Add(sourceBlock with
            {
                Id = id,
                Text = sourceBlock.Kind == ReportTextBlockKind.Divider ? string.Empty : sourceBlock.Text ?? string.Empty,
                Link = NormalizeLink(sourceBlock.Link),
                Table = table
            });
        }
        return new(ReportTextDocument.CurrentVersion, blocks);
    }

    public static bool TryApply(ReportTextDocument source, ReportTextCommand command, out ReportTextDocument updated)
    {
        ArgumentNullException.ThrowIfNull(source);
        var original = source;
        source = Normalize(source);
        updated = original;
        ArgumentNullException.ThrowIfNull(command);
        var blocks = source.Blocks.ToList();
        var index = string.IsNullOrWhiteSpace(command.BlockId)
            ? -1
            : blocks.FindIndex(block => string.Equals(block.Id, command.BlockId, StringComparison.Ordinal));

        if (command.Kind == ReportTextCommandKind.InsertBlockAfter)
        {
            var block = NewBlock(command.BlockKind ?? ReportTextBlockKind.Paragraph, command.Text);
            blocks.Insert(index < 0 ? blocks.Count : index + 1, block);
            updated = new(ReportTextDocument.CurrentVersion, blocks);
            return true;
        }
        if (command.Kind == ReportTextCommandKind.InsertTable)
        {
            var table = NewBlock(ReportTextBlockKind.Table) with { Table = NormalizeTable(command.Cells) };
            blocks.Insert(index < 0 ? blocks.Count : index + 1, table);
            updated = new(ReportTextDocument.CurrentVersion, blocks);
            return true;
        }
        if (index < 0) return false;

        var target = blocks[index];
        switch (command.Kind)
        {
            case ReportTextCommandKind.UpdateText when target.Kind != ReportTextBlockKind.Table && target.Kind != ReportTextBlockKind.Divider:
                blocks[index] = target with { Text = command.Text ?? string.Empty };
                break;
            case ReportTextCommandKind.DeleteBlock:
                if (blocks.Count == 1)
                    blocks[0] = NewBlock(ReportTextBlockKind.Paragraph);
                else
                    blocks.RemoveAt(index);
                break;
            case ReportTextCommandKind.SetBold when target.Kind != ReportTextBlockKind.Table && target.Kind != ReportTextBlockKind.Divider:
                blocks[index] = target with { Bold = command.Value ?? !target.Bold };
                break;
            case ReportTextCommandKind.SetItalic when target.Kind != ReportTextBlockKind.Table && target.Kind != ReportTextBlockKind.Divider:
                blocks[index] = target with { Italic = command.Value ?? !target.Italic };
                break;
            case ReportTextCommandKind.SetLink when target.Kind != ReportTextBlockKind.Table && target.Kind != ReportTextBlockKind.Divider:
                blocks[index] = target with { Link = NormalizeLink(command.Text) };
                break;
            case ReportTextCommandKind.SetChecked when target.Kind == ReportTextBlockKind.TodoList:
                blocks[index] = target with { IsChecked = command.Value ?? !target.IsChecked };
                break;
            case ReportTextCommandKind.DeleteTable when target.Kind == ReportTextBlockKind.Table:
                if (blocks.Count == 1) blocks[0] = NewBlock(ReportTextBlockKind.Paragraph);
                else blocks.RemoveAt(index);
                break;
            default:
                if (!TryApplyTable(target, command, out var table)) return false;
                blocks[index] = target with { Table = table };
                break;
        }

        updated = Normalize(new(ReportTextDocument.CurrentVersion, blocks));
        return true;
    }

    public static string ToPlainText(ReportTextDocument? source)
    {
        var document = Normalize(source);
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            if (builder.Length > 0) builder.AppendLine();
            switch (block.Kind)
            {
                case ReportTextBlockKind.Heading1:
                case ReportTextBlockKind.Heading2:
                case ReportTextBlockKind.Heading3:
                case ReportTextBlockKind.Paragraph:
                case ReportTextBlockKind.Quote:
                case ReportTextBlockKind.Code:
                    builder.Append(block.Text);
                    break;
                case ReportTextBlockKind.BulletedList:
                    builder.Append("• ").Append(block.Text);
                    break;
                case ReportTextBlockKind.NumberedList:
                    builder.Append("1. ").Append(block.Text);
                    break;
                case ReportTextBlockKind.TodoList:
                    builder.Append(block.IsChecked ? "[x] " : "[ ] ").Append(block.Text);
                    break;
                case ReportTextBlockKind.Divider:
                    builder.Append("────────");
                    break;
                case ReportTextBlockKind.Table:
                    AppendTable(builder, block.Table!, "\t", appendLine: true);
                    break;
            }
        }
        return builder.ToString().Trim();
    }

    public static string ToMarkdown(ReportTextDocument? source)
    {
        var document = Normalize(source);
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            if (builder.Length > 0) builder.AppendLine().AppendLine();
            var text = MarkdownText(block);
            switch (block.Kind)
            {
                case ReportTextBlockKind.Heading1: builder.Append("# ").Append(text); break;
                case ReportTextBlockKind.Heading2: builder.Append("## ").Append(text); break;
                case ReportTextBlockKind.Heading3: builder.Append("### ").Append(text); break;
                case ReportTextBlockKind.BulletedList: builder.Append("- ").Append(text); break;
                case ReportTextBlockKind.NumberedList: builder.Append("1. ").Append(text); break;
                case ReportTextBlockKind.TodoList: builder.Append(block.IsChecked ? "- [x] " : "- [ ] ").Append(text); break;
                case ReportTextBlockKind.Quote: builder.Append("> ").Append(text); break;
                case ReportTextBlockKind.Code: builder.Append("```\n").Append(block.Text).Append("\n```"); break;
                case ReportTextBlockKind.Divider: builder.Append("---"); break;
                case ReportTextBlockKind.Table: AppendMarkdownTable(builder, block.Table!); break;
                default: builder.Append(text); break;
            }
        }
        return builder.ToString().Trim();
    }

    public static ReportTextDocument FromMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return ReportTextDocument.Empty;
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var blocks = new List<ReportTextBlock>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new StringBuilder();
                while (++index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.AppendLine();
                    code.Append(lines[index]);
                }
                blocks.Add(NewBlock(ReportTextBlockKind.Code, code.ToString()));
            }
            else if (IsMarkdownTableLine(line) && index + 1 < lines.Length && IsMarkdownTableLine(lines[index + 1]))
            {
                var rows = new List<IReadOnlyList<string>> { ParseMarkdownTableRow(line) };
                index++;
                if (!IsMarkdownTableSeparator(lines[index])) rows.Add(ParseMarkdownTableRow(lines[index]));
                while (index + 1 < lines.Length && IsMarkdownTableLine(lines[index + 1])) rows.Add(ParseMarkdownTableRow(lines[++index]));
                blocks.Add(NewBlock(ReportTextBlockKind.Table) with { Table = NormalizeTable(rows) });
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.Heading3, line[4..]));
            else if (line.StartsWith("## ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.Heading2, line[3..]));
            else if (line.StartsWith("# ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.Heading1, line[2..]));
            else if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) blocks.Add(NewBlock(ReportTextBlockKind.TodoList, line[6..]) with { IsChecked = true });
            else if (line.StartsWith("- [ ] ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.TodoList, line[6..]));
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.BulletedList, line[2..]));
            else if (line.Length > 3 && char.IsDigit(line[0]) && line.Contains(". ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.NumberedList, line[(line.IndexOf(". ", StringComparison.Ordinal) + 2)..]));
            else if (line.StartsWith("> ", StringComparison.Ordinal)) blocks.Add(NewBlock(ReportTextBlockKind.Quote, line[2..]));
            else if (line is "---" or "***" or "___") blocks.Add(NewBlock(ReportTextBlockKind.Divider));
            else blocks.Add(NewBlock(ReportTextBlockKind.Paragraph, line));
        }
        return Normalize(new(ReportTextDocument.CurrentVersion, blocks));
    }

    private static bool TryApplyTable(ReportTextBlock target, ReportTextCommand command, out ReportTextTable table)
    {
        table = target.Table ?? NormalizeTable(null);
        if (target.Kind != ReportTextBlockKind.Table) return false;
        var rows = table.Cells.Select(row => row.ToList()).ToList();
        if (command.Kind == ReportTextCommandKind.FillTable)
        {
            if (command.Cells is null || command.Cells.Count == 0) return false;
            var input = NormalizeTable(command.Cells);
            var row = InRange(command.Row, rows.Count) ? command.Row : 0;
            var column = InRange(command.Column, rows[0].Count) ? command.Column : 0;
            EnsureSize(rows, row + input.RowCount, column + input.ColumnCount);
            for (var r = 0; r < input.RowCount; r++)
            for (var c = 0; c < input.ColumnCount; c++) rows[row + r][column + c] = input.Cells[r][c];
            table = NewTable(rows);
            return true;
        }
        var rowIndex = InRange(command.Row, rows.Count) ? command.Row : -1;
        var columnIndex = InRange(command.Column, rows[0].Count) ? command.Column : -1;
        switch (command.Kind)
        {
            case ReportTextCommandKind.UpdateCell when rowIndex >= 0 && columnIndex >= 0:
                rows[rowIndex][columnIndex] = command.Text ?? string.Empty;
                break;
            case ReportTextCommandKind.InsertRowAbove when rowIndex >= 0:
                rows.Insert(rowIndex, NewRow(rows[0].Count));
                break;
            case ReportTextCommandKind.InsertRowBelow when rowIndex >= 0:
                rows.Insert(rowIndex + 1, NewRow(rows[0].Count));
                break;
            case ReportTextCommandKind.InsertColumnLeft when columnIndex >= 0:
                foreach (var row in rows) row.Insert(columnIndex, string.Empty);
                break;
            case ReportTextCommandKind.InsertColumnRight when columnIndex >= 0:
                foreach (var row in rows) row.Insert(columnIndex + 1, string.Empty);
                break;
            case ReportTextCommandKind.ClearCell when rowIndex >= 0 && columnIndex >= 0:
                rows[rowIndex][columnIndex] = string.Empty;
                break;
            case ReportTextCommandKind.ClearRow when rowIndex >= 0:
                rows[rowIndex] = NewRow(rows[0].Count);
                break;
            case ReportTextCommandKind.ClearColumn when columnIndex >= 0:
                foreach (var row in rows) row[columnIndex] = string.Empty;
                break;
            case ReportTextCommandKind.ClearTable:
                for (var r = 0; r < rows.Count; r++) rows[r] = NewRow(rows[0].Count);
                break;
            case ReportTextCommandKind.DeleteRow when rowIndex >= 0 && rows.Count > 1:
                rows.RemoveAt(rowIndex);
                break;
            case ReportTextCommandKind.DeleteColumn when columnIndex >= 0 && rows[0].Count > 1:
                foreach (var row in rows) row.RemoveAt(columnIndex);
                break;
            default:
                return false;
        }
        table = NewTable(rows);
        return true;
    }

    private static ReportTextBlock NewBlock(ReportTextBlockKind kind, string? text = null) =>
        new(Guid.NewGuid().ToString("N"), kind, text ?? string.Empty,
            Table: kind == ReportTextBlockKind.Table ? NormalizeTable(null) : null);

    private static ReportTextTable NormalizeTable(IReadOnlyList<IReadOnlyList<string>>? cells)
    {
        if (cells is null || cells.Count == 0) return new([new[] { string.Empty }]);
        var columns = Math.Max(1, cells.Max(row => row?.Count ?? 0));
        var rows = new List<IReadOnlyList<string>>(Math.Max(1, cells.Count));
        foreach (var source in cells)
        {
            var row = new List<string>(columns);
            for (var column = 0; column < columns; column++) row.Add(source is not null && column < source.Count ? source[column] ?? string.Empty : string.Empty);
            rows.Add(row);
        }
        return new(rows);
    }

    private static ReportTextTable NewTable(IReadOnlyList<IReadOnlyList<string>> rows) => NormalizeTable(rows);
    private static List<string> NewRow(int columns) => Enumerable.Repeat(string.Empty, Math.Max(1, columns)).ToList();
    private static bool InRange(int value, int count) => value >= 0 && value < count;
    private static void EnsureSize(List<List<string>> rows, int rowCount, int columnCount)
    {
        var columns = Math.Max(columnCount, rows[0].Count);
        foreach (var row in rows) while (row.Count < columns) row.Add(string.Empty);
        while (rows.Count < rowCount) rows.Add(NewRow(columns));
    }
    private static string? NormalizeLink(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string MarkdownText(ReportTextBlock block)
    {
        var value = block.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(block.Link)) value = $"[{value}]({block.Link})";
        if (block.Bold) value = $"**{value}**";
        if (block.Italic) value = $"*{value}*";
        return value;
    }
    private static void AppendTable(StringBuilder builder, ReportTextTable table, string delimiter, bool appendLine)
    {
        for (var row = 0; row < table.RowCount; row++)
        {
            if (row > 0 && appendLine) builder.AppendLine();
            builder.Append(string.Join(delimiter, table.Cells[row]));
        }
    }
    private static void AppendMarkdownTable(StringBuilder builder, ReportTextTable table)
    {
        for (var row = 0; row < table.RowCount; row++)
        {
            if (row > 0) builder.AppendLine();
            builder.Append("| ").Append(string.Join(" | ", table.Cells[row].Select(value => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)))).Append(" |");
            if (row == 0)
            {
                builder.AppendLine();
                builder.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", table.ColumnCount))).Append(" |");
            }
        }
    }
    private static bool IsMarkdownTableLine(string line) => line.TrimStart().StartsWith("|", StringComparison.Ordinal) && line.Contains('|', StringComparison.Ordinal);
    private static bool IsMarkdownTableSeparator(string line) => line.Replace("|", string.Empty, StringComparison.Ordinal).Trim().All(value => value is '-' or ':' or ' ');
    private static IReadOnlyList<string> ParseMarkdownTableRow(string line) => line.Trim().Trim('|').Split('|').Select(value => value.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
}
