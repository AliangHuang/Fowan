using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Fowan.Report.Shared;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using W = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Fowan.Report.Windows.Platform.Windows;

internal sealed record ReportTemplateInspection(ReportFileContentDocument Document, string Extension);

/// <summary>Controlled local validation failure that is safe to show without disclosing document content or paths.</summary>
internal sealed class ReportTemplateValidationException(string safeMessage) : InvalidOperationException(safeMessage)
{
    public string SafeMessage { get; } = safeMessage;
}

internal sealed record ReportTemplateValidationResult(bool IsValid, string? SafeDiagnostic)
{
    public static ReportTemplateValidationResult Valid { get; } = new(true, null);
}

/// <summary>
/// Client-owned Office adapter. It projects editable content in document order and
/// applies only a validated replacement tree to a copied Open XML package. The
/// provider never receives local paths, packages, formulas, or document operations.
/// </summary>
internal static class OpenXmlReportTemplateService
{
    private const int MaximumContentLength = 64 * 1024;

    public static ReportTemplateInspection Inspect(string path)
    {
        var extension = ValidateExtension(path);
        var document = extension == ".docx" ? ReadWord(path) : ReadExcel(path);
        return new(document, extension);
    }

    public static async Task<ReportTemplateValidationResult> ValidateCandidateAsync(
        string sourcePath,
        ReportFileContentDocument candidate,
        CancellationToken cancellationToken = default)
    {
        var extension = ValidateExtension(sourcePath);
        var temporary = Path.Combine(Path.GetTempPath(), $"fowan-report-{Guid.NewGuid():N}{extension}");
        try
        {
            await WriteCopyAsync(sourcePath, temporary, candidate, cancellationToken);
            return ReportTemplateValidationResult.Valid;
        }
        catch (ReportTemplateValidationException exception)
        {
            return new(false, exception.SafeMessage);
        }
        catch (InvalidDataException)
        {
            return new(false, "AI 返回内容与模板结构不匹配，请在现有布局内调整后重试。");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static Task WriteAsync(
        string sourcePath,
        string destinationPath,
        ReportFileContentDocument candidate,
        CancellationToken cancellationToken = default) =>
        WriteCopyAsync(sourcePath, destinationPath, candidate, cancellationToken);

    private static async Task WriteCopyAsync(
        string sourcePath,
        string destinationPath,
        ReportFileContentDocument candidate,
        CancellationToken cancellationToken)
    {
        var extension = ValidateExtension(sourcePath);
        if (!string.Equals(extension, Path.GetExtension(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new ReportTemplateValidationException("输出文件必须与模板使用同一种文件类型。");
        if (!string.Equals(candidate.Format, extension[1..], StringComparison.OrdinalIgnoreCase))
            throw new ReportTemplateValidationException("AI 返回的文件类型与模板不一致。");

        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath, cancellationToken));
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ReportTemplateValidationException("输出位置无效。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}{extension}.tmp");
        try
        {
            File.Copy(sourcePath, temporary, true);
            if (extension == ".docx") ApplyWord(temporary, candidate);
            else ApplyExcel(temporary, candidate);
            VerifyReadable(temporary, extension);
            var afterHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, afterHash))
                throw new ReportTemplateValidationException("源模板在生成过程中发生变化，已取消输出。");
            CommitAtomically(temporary, destinationPath);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static ReportFileContentDocument ReadWord(string path)
    {
        using var package = WordprocessingDocument.Open(path, false);
        var body = package.MainDocumentPart?.Document?.Body
            ?? throw new ReportTemplateValidationException("Word 模板没有正文。");
        var blocks = new List<ReportFileBlock>();
        foreach (var child in body.ChildElements)
        {
            switch (child)
            {
                case W.Paragraph paragraph:
                    blocks.Add(new("paragraph", paragraph.InnerText));
                    break;
                case W.Table table:
                    blocks.Add(new("table", Table: ReadWordTable(table)));
                    break;
            }
        }
        if (blocks.Count == 0) throw new ReportTemplateValidationException("模板没有可写入的正文或表格内容。");
        return new("docx", blocks, []);
    }

    private static ReportFileTable ReadWordTable(W.Table table)
    {
        var rows = table.Elements<W.TableRow>().Select(row =>
            (IReadOnlyList<ReportFileCell>)row.Elements<W.TableCell>()
                .Select(cell => new ReportFileCell(cell.InnerText, "text", true)).ToArray()).ToArray();
        var canAppend = rows.Length > 0 && rows[^1].All(cell => string.IsNullOrWhiteSpace(cell.Value)) &&
            !table.Descendants<W.VerticalMerge>().Any();
        return new(rows, canAppend);
    }

    private static ReportFileContentDocument ReadExcel(string path)
    {
        using var package = SpreadsheetDocument.Open(path, false);
        var workbook = package.WorkbookPart ?? throw new ReportTemplateValidationException("Excel 模板没有工作簿。");
        var sheets = new List<ReportFileSheet>();
        foreach (var sheet in workbook.Workbook?.Sheets?.Elements<S.Sheet>() ?? [])
        {
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
            var worksheet = part.Worksheet ?? throw new ReportTemplateValidationException("Excel 工作表无效。");
            var rows = (worksheet.GetFirstChild<S.SheetData>()?.Elements<S.Row>() ?? [])
                .Select(row => (IReadOnlyList<ReportFileCell>)row.Elements<S.Cell>().Select(cell => ReadExcelCell(cell, workbook)).ToArray())
                .ToArray();
            var canAppend = rows.Length > 0 && rows[^1].All(cell => cell.Editable && string.IsNullOrWhiteSpace(cell.Value)) &&
                !worksheet.Elements<S.MergeCells>().Any() && !worksheet.Elements<S.ConditionalFormatting>().Any() &&
                !worksheet.Elements<S.DataValidations>().Any() && !worksheet.Elements<S.TableParts>().Any();
            sheets.Add(new(sheet.Name?.Value ?? "Sheet", rows, canAppend));
        }
        if (sheets.Count == 0) throw new ReportTemplateValidationException("Excel 模板没有可写入的工作表。");
        return new("xlsx", [], sheets);
    }

    private static ReportFileCell ReadExcelCell(S.Cell cell, WorkbookPart workbook)
    {
        if (cell.CellFormula is not null) return new(null, "formula", false);
        var kind = cell.DataType?.Value == S.CellValues.Boolean
            ? "boolean"
            : cell.DataType is null && decimal.TryParse(cell.CellValue?.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                ? "number"
                : "text";
        return new(ReadCell(cell, workbook), kind, true);
    }

    private static void ApplyWord(string path, ReportFileContentDocument candidate)
    {
        using var package = WordprocessingDocument.Open(path, true);
        var body = package.MainDocumentPart?.Document?.Body
            ?? throw new ReportTemplateValidationException("Word 模板没有正文。");
        var source = body.ChildElements.Where(IsWordContent).ToArray();
        if (candidate.Blocks.Count != source.Length || candidate.Sheets.Count != 0)
            throw new ReportTemplateValidationException("AI 返回内容与 Word 模板结构不匹配，请重试。");
        for (var index = 0; index < source.Length; index++)
        {
            var block = candidate.Blocks[index];
            if (source[index] is W.Paragraph paragraph)
            {
                if (!string.Equals(block.Kind, "paragraph", StringComparison.Ordinal) || block.Table is not null)
                    throw new ReportTemplateValidationException("AI 返回内容与 Word 模板结构不匹配，请重试。");
                ReplaceParagraph(paragraph, RequireText(block.Text));
                continue;
            }
            if (source[index] is W.Table table)
            {
                if (!string.Equals(block.Kind, "table", StringComparison.Ordinal) || block.Table is null)
                    throw new ReportTemplateValidationException("AI 返回内容与 Word 模板结构不匹配，请重试。");
                ApplyWordTable(table, block.Table);
            }
        }
        package.MainDocumentPart!.Document!.Save();
    }

    private static bool IsWordContent(OpenXmlElement element) => element is W.Paragraph or W.Table;

    private static void ApplyWordTable(W.Table table, ReportFileTable candidate)
    {
        var sourceRows = table.Elements<W.TableRow>().ToList();
        if (sourceRows.Count == 0 || candidate.Rows.Count < sourceRows.Count ||
            (candidate.Rows.Count > sourceRows.Count && (!candidate.CanAppendRows || !CanAppendWordRows(table, sourceRows))))
            throw new ReportTemplateValidationException("AI 返回的表格行数不符合模板的安全写入规则，请在现有表格内精简内容后重试。");
        while (sourceRows.Count < candidate.Rows.Count)
        {
            var clone = (W.TableRow)sourceRows[^1].CloneNode(true);
            table.AppendChild(clone);
            sourceRows.Add(clone);
        }
        for (var row = 0; row < sourceRows.Count; row++)
        {
            var cells = sourceRows[row].Elements<W.TableCell>().ToArray();
            if (candidate.Rows[row].Count != cells.Length)
                throw new ReportTemplateValidationException("AI 返回的表格列数与模板不一致，请重试。");
            for (var column = 0; column < cells.Length; column++)
            {
                var value = candidate.Rows[row][column];
                if (!value.Editable || !string.Equals(value.ValueKind, "text", StringComparison.Ordinal) || value.Value?.Length > MaximumContentLength)
                    throw new ReportTemplateValidationException("AI 返回了不受支持的 Word 单元格内容，请重试。");
                ReplaceCell(cells[column], value.Value ?? string.Empty);
            }
        }
    }

    private static bool CanAppendWordRows(W.Table table, IReadOnlyList<W.TableRow> rows) =>
        rows.Count > 0 && rows[^1].Elements<W.TableCell>().All(cell => string.IsNullOrWhiteSpace(cell.InnerText)) &&
        !table.Descendants<W.VerticalMerge>().Any();

    private static void ReplaceCell(W.TableCell cell, string value)
    {
        var paragraph = cell.Elements<W.Paragraph>().FirstOrDefault();
        if (paragraph is null)
        {
            cell.AppendChild(new W.Paragraph());
            paragraph = cell.Elements<W.Paragraph>().First();
        }
        ReplaceParagraph(paragraph, value);
        foreach (var extra in cell.Elements<W.Paragraph>().Skip(1).ToArray()) extra.Remove();
    }

    private static void ReplaceParagraph(W.Paragraph paragraph, string value)
    {
        if (value.Length > MaximumContentLength) throw new ReportTemplateValidationException("AI 返回的文本过长，请精简后重试。");
        var properties = paragraph.ParagraphProperties?.CloneNode(true);
        var runProperties = paragraph.Descendants<W.Run>().Select(run => run.RunProperties?.CloneNode(true) as W.RunProperties)
            .FirstOrDefault(value => value is not null);
        paragraph.RemoveAllChildren();
        if (properties is not null) paragraph.AppendChild(properties);
        var run = new W.Run();
        if (runProperties is not null) run.AppendChild(runProperties);
        run.AppendChild(new W.Text(value) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
    }

    private static void ApplyExcel(string path, ReportFileContentDocument candidate)
    {
        using var package = SpreadsheetDocument.Open(path, true);
        var workbook = package.WorkbookPart ?? throw new ReportTemplateValidationException("Excel 模板没有工作簿。");
        var sourceSheets = (workbook.Workbook?.Sheets?.Elements<S.Sheet>() ?? []).ToArray();
        if (candidate.Blocks.Count != 0 || candidate.Sheets.Count != sourceSheets.Length)
            throw new ReportTemplateValidationException("AI 返回内容与 Excel 模板结构不匹配，请重试。");
        for (var index = 0; index < sourceSheets.Length; index++)
        {
            var sourceSheet = sourceSheets[index];
            var outputSheet = candidate.Sheets[index];
            if (!string.Equals(sourceSheet.Name?.Value, outputSheet.Name, StringComparison.Ordinal))
                throw new ReportTemplateValidationException("AI 返回的工作表顺序或名称不匹配，请重试。");
            var part = (WorksheetPart)workbook.GetPartById(sourceSheet.Id!);
            ApplyExcelSheet(part, workbook, outputSheet);
            part.Worksheet!.Save();
        }
        workbook.Workbook!.Save();
    }

    private static void ApplyExcelSheet(WorksheetPart part, WorkbookPart workbook, ReportFileSheet candidate)
    {
        var worksheet = part.Worksheet ?? throw new ReportTemplateValidationException("Excel 工作表无效。");
        var data = worksheet.GetFirstChild<S.SheetData>() ?? throw new ReportTemplateValidationException("Excel 工作表没有数据区域。");
        var sourceRows = data.Elements<S.Row>().ToList();
        var canAppend = CanAppendExcelRows(worksheet, sourceRows, workbook);
        if (candidate.Rows.Count < sourceRows.Count || (candidate.Rows.Count > sourceRows.Count && (!candidate.CanAppendRows || !canAppend)))
            throw new ReportTemplateValidationException("AI 返回的 Excel 行数不符合模板的安全写入规则，请在现有表格内精简内容后重试。");
        while (sourceRows.Count < candidate.Rows.Count)
        {
            var clone = (S.Row)sourceRows[^1].CloneNode(true);
            MoveExcelRow(clone, 1U);
            data.AppendChild(clone);
            sourceRows.Add(clone);
        }
        for (var row = 0; row < sourceRows.Count; row++)
        {
            var cells = sourceRows[row].Elements<S.Cell>().ToArray();
            if (candidate.Rows[row].Count != cells.Length)
                throw new ReportTemplateValidationException("AI 返回的 Excel 列数与模板不一致，请重试。");
            for (var column = 0; column < cells.Length; column++) ApplyExcelCell(cells[column], workbook, candidate.Rows[row][column]);
        }
    }

    private static bool CanAppendExcelRows(S.Worksheet worksheet, IReadOnlyList<S.Row> rows, WorkbookPart workbook) =>
        rows.Count > 0 && rows[^1].Elements<S.Cell>().All(cell => cell.CellFormula is null && string.IsNullOrWhiteSpace(ReadCell(cell, workbook))) &&
        !worksheet.Elements<S.MergeCells>().Any() && !worksheet.Elements<S.ConditionalFormatting>().Any() &&
        !worksheet.Elements<S.DataValidations>().Any() && !worksheet.Elements<S.TableParts>().Any();

    private static void ApplyExcelCell(S.Cell cell, WorkbookPart workbook, ReportFileCell candidate)
    {
        var source = ReadExcelCell(cell, workbook);
        if (!source.Editable)
        {
            if (candidate.Editable || !string.Equals(candidate.ValueKind, source.ValueKind, StringComparison.Ordinal) || candidate.Value is not null)
                throw new ReportTemplateValidationException("AI 尝试修改 Excel 公式或受保护内容，已拒绝输出。");
            return;
        }
        if (!candidate.Editable || !string.Equals(candidate.ValueKind, source.ValueKind, StringComparison.Ordinal) || candidate.Value?.Length > MaximumContentLength)
            throw new ReportTemplateValidationException("AI 返回了不受支持的 Excel 单元格内容，请重试。");
        SetCell(cell, candidate.Value ?? string.Empty, candidate.ValueKind);
    }

    private static string RequireText(string? value)
    {
        if (value is null) throw new ReportTemplateValidationException("AI 返回内容缺少文本值，请重试。");
        return value;
    }

    private static string ReadCell(S.Cell cell, WorkbookPart workbook)
    {
        var raw = cell.CellValue?.Text ?? cell.InnerText;
        if (cell.DataType?.Value == S.CellValues.SharedString && int.TryParse(raw, out var index) &&
            workbook.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().ElementAtOrDefault(index) is { } shared)
            return shared.InnerText;
        return raw;
    }

    private static void SetCell(S.Cell cell, string value, string valueKind)
    {
        cell.CellFormula = null;
        cell.InlineString = null;
        if (valueKind == "number")
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                throw new ReportTemplateValidationException("Excel 数字单元格必须返回数字。");
            cell.DataType = null;
            cell.CellValue = new S.CellValue(value);
            return;
        }
        if (valueKind == "boolean")
        {
            if (!bool.TryParse(value, out var boolean)) throw new ReportTemplateValidationException("Excel 布尔单元格必须返回 true 或 false。");
            cell.DataType = S.CellValues.Boolean;
            cell.CellValue = new S.CellValue(boolean ? "1" : "0");
            return;
        }
        cell.CellValue = null;
        cell.DataType = S.CellValues.InlineString;
        cell.InlineString = new S.InlineString(new S.Text(value));
    }

    private static void MoveExcelRow(S.Row row, uint offset)
    {
        var rowIndex = (row.RowIndex?.Value ?? 1U) + offset;
        row.RowIndex = rowIndex;
        foreach (var cell in row.Elements<S.Cell>())
        {
            if (cell.CellReference?.Value is { Length: > 0 } reference)
                cell.CellReference = Regex.Replace(reference, "[0-9]+$", rowIndex.ToString());
        }
    }

    private static void VerifyReadable(string path, string extension)
    {
        if (extension == ".docx")
        {
            using var word = WordprocessingDocument.Open(path, false);
        }
        else
        {
            using var workbook = SpreadsheetDocument.Open(path, false);
        }
    }

    private static void CommitAtomically(string temporary, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(temporary, destination);
            return;
        }
        var backup = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.bak");
        try { File.Replace(temporary, destination, backup, true); }
        finally { if (File.Exists(backup)) File.Delete(backup); }
    }

    private static string ValidateExtension(string path)
    {
        if (!File.Exists(path)) throw new ReportTemplateValidationException("找不到模板文件。");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".docx" or ".xlsx"
            ? extension
            : throw new ReportTemplateValidationException("仅支持 Word .docx 与 Excel .xlsx；不支持 .doc、.xls、宏文件或 PDF。");
    }
}
