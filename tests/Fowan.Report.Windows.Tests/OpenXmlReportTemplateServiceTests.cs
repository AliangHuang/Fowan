using DocumentFormat.OpenXml.Packaging;
using Fowan.Report.Shared;
using Fowan.Report.Windows.Platform.Windows;
using System.Security.Cryptography;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class OpenXmlReportTemplateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Fowan.Report.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Word_template_without_placeholders_writes_a_validated_copy_and_keeps_its_default_run_style()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "template.docx");
        var destination = Path.Combine(_root, "output.docx");
        CreateWord(source);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source));

        var inspection = OpenXmlReportTemplateService.Inspect(source);
        Assert.Equal("docx", inspection.Document.Format);
        Assert.Equal(2, inspection.Document.Blocks.Count);
        Assert.True(inspection.Document.Blocks[1].Table!.CanAppendRows);

        await OpenXmlReportTemplateService.WriteAsync(source, destination, WordCandidate());

        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        using var output = WordprocessingDocument.Open(destination, false);
        var body = output.MainDocumentPart!.Document!.Body!;
        Assert.Contains("Weekly delivery", body.InnerText);
        Assert.Contains("Done task", body.InnerText);
        Assert.Contains("New task", body.InnerText);
        Assert.NotEmpty(body.Elements<W.Paragraph>().First().Descendants<W.Bold>());
    }

    [Fact]
    public async Task Excel_template_without_placeholders_preserves_types_and_can_safely_append_from_its_blank_tail_row()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "template.xlsx");
        var destination = Path.Combine(_root, "output.xlsx");
        CreateExcel(source);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source));

        var inspection = OpenXmlReportTemplateService.Inspect(source);
        Assert.Equal("xlsx", inspection.Document.Format);
        Assert.True(Assert.Single(inspection.Document.Sheets).CanAppendRows);
        await OpenXmlReportTemplateService.WriteAsync(source, destination, ExcelCandidate());

        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        using var output = SpreadsheetDocument.Open(destination, false);
        var cells = output.WorkbookPart!.WorksheetParts.SelectMany(part => part.Worksheet!.Descendants<S.Cell>())
            .Select(cell => cell.InnerText).ToArray();
        Assert.Contains(cells, value => value.Contains("Done task", StringComparison.Ordinal));
        Assert.Contains(cells, value => value.Contains("New task", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Excel_formula_or_protected_content_cannot_be_changed_by_a_candidate()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "formula.xlsx");
        CreateFormulaExcel(source);
        var candidate = new ReportFileContentDocument("xlsx", [],
            [new("Report", [[new("3", "formula", true)]])]);

        var validation = await OpenXmlReportTemplateService.ValidateCandidateAsync(source, candidate);

        Assert.False(validation.IsValid);
        Assert.Contains("公式", validation.SafeDiagnostic);
    }

    [Fact]
    public void Ordinary_file_templates_are_accepted_without_fill_markers()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "ordinary.docx");
        using (var document = WordprocessingDocument.Create(source, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("普通 Word 模板")))));
            main.Document.Save();
        }

        var inspection = OpenXmlReportTemplateService.Inspect(source);

        Assert.Equal("普通 Word 模板", Assert.Single(inspection.Document.Blocks).Text);
    }

    [Fact]
    public void Native_file_dialog_builds_a_double_null_terminated_office_filter()
    {
        var filter = WindowsReportFileDialogService.BuildFilter([".docx", ".xlsx"]);

        Assert.Equal("所有支持的模板 (*.docx;*.xlsx)\0*.docx;*.xlsx\0Word 文档 (*.docx)\0*.docx\0Excel 工作簿 (*.xlsx)\0*.xlsx\0\0", filter);
    }

    [Fact]
    public void Native_file_dialog_uses_the_pointer_only_abi_size()
    {
        var expected = IntPtr.Size == 8 ? 152 : 88;

        Assert.Equal(expected, WindowsReportFileDialogService.NativeStructureSize);
    }

    [Fact]
    public void Native_file_dialog_signature_reaches_comdlg_without_showing_ui()
    {
        Assert.Equal(0x0001U, WindowsReportFileDialogService.ProbeNativeSignature());
    }

    private static ReportFileContentDocument WordCandidate() => new("docx",
        [new("paragraph", "Weekly delivery"), new("table", Table: new ReportFileTable(
            [Row("Task", "Status"), Row("Done task", "Completed"), Row("New task", "Open")], true))], []);

    private static ReportFileContentDocument ExcelCandidate() => new("xlsx", [],
        [new("Report", [Row("Weekly delivery"), Row("Task", "Status"), Row("Done task", "Completed"), Row("New task", "Open")], true)]);

    private static IReadOnlyList<ReportFileCell> Row(params string[] values) => values.Select(value => new ReportFileCell(value)).ToArray();

    private static void CreateWord(string path)
    {
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var title = new W.Paragraph(new W.Run(new W.RunProperties(new W.Bold()), new W.Text("Template title")));
        var table = new W.Table(
            new W.TableRow(Cell("Task"), Cell("Status")),
            new W.TableRow(Cell(string.Empty), Cell(string.Empty)));
        main.Document = new W.Document(new W.Body(title, table));
        main.Document.Save();
    }

    private static W.TableCell Cell(string value) => new(new W.Paragraph(new W.Run(new W.Text(value))));

    private static void CreateExcel(string path)
    {
        using var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook();
        var sheet = workbook.AddNewPart<WorksheetPart>();
        sheet.Worksheet = new S.Worksheet(new S.SheetData(
            ExcelRow(1, "Weekly template"),
            ExcelRow(2, "Task", "Status"),
            ExcelRow(3, string.Empty, string.Empty)));
        var sheets = workbook.Workbook.AppendChild(new S.Sheets());
        sheets.AppendChild(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Report" });
        workbook.Workbook.Save();
    }

    private static void CreateFormulaExcel(string path)
    {
        using var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook();
        var sheet = workbook.AddNewPart<WorksheetPart>();
        sheet.Worksheet = new S.Worksheet(new S.SheetData(
            new S.Row(new S.Cell { CellReference = "A1", CellFormula = new S.CellFormula("SUM(1,2)"), CellValue = new S.CellValue("3") }) { RowIndex = 1U }));
        var sheets = workbook.Workbook.AppendChild(new S.Sheets());
        sheets.AppendChild(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Report" });
        workbook.Workbook.Save();
    }

    private static S.Row ExcelRow(uint index, params string[] values) => new(values.Select((value, column) =>
        new S.Cell
        {
            CellReference = $"{(char)('A' + column)}{index}",
            DataType = S.CellValues.InlineString,
            InlineString = new S.InlineString(new S.Text(value))
        })) { RowIndex = index };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
