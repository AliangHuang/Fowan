namespace Fowan.Report.Shared;

/// <summary>
/// Provider-neutral, position-free content projection of a local Office template.
/// The document keeps only editable text/cell values and their tree order; the
/// Open XML package remains the client-owned formatting and layout authority.
/// </summary>
public sealed record ReportFileCell(string? Value, string ValueKind = "text", bool Editable = true);

public sealed record ReportFileTable(
    IReadOnlyList<IReadOnlyList<ReportFileCell>> Rows,
    bool CanAppendRows = false);

public sealed record ReportFileBlock(
    string Kind,
    string? Text = null,
    ReportFileTable? Table = null);

public sealed record ReportFileSheet(
    string Name,
    IReadOnlyList<IReadOnlyList<ReportFileCell>> Rows,
    bool CanAppendRows = false);

public sealed record ReportFileContentDocument(
    string Format,
    IReadOnlyList<ReportFileBlock> Blocks,
    IReadOnlyList<ReportFileSheet> Sheets)
{
    public static ReportFileContentDocument Empty(string format) => new(format, [], []);
}
