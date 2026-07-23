using Fowan.Report.Shared;
using Fowan.Report.Windows.Platform.Windows;
using System.Text.Json;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class ReportGenerationRecordStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Fowan.Report.Records.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void StoreWritesAtomicallyAndPersistsFileRecordWithoutInputContent()
    {
        var store = new ReportGenerationRecordStore(_root);
        var record = Record("one", ReportGenerationRecordStatus.Completed, "D:\\Reports\\weekly.docx");

        store.Save([record]);
        store.Save([record with { Status = ReportGenerationRecordStatus.Failed, ErrorCode = "provider_unavailable", OutputPath = null }]);

        var path = Path.Combine(_root, "records.json");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        var text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        var entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.True(entry.TryGetProperty("id", out _));
        Assert.True(entry.TryGetProperty("range", out _));
        Assert.False(text.Contains("\"notes\"", StringComparison.OrdinalIgnoreCase));
        Assert.False(text.Contains("\"template\"", StringComparison.OrdinalIgnoreCase));
        Assert.False(text.Contains("\"requirements\"", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ReportGenerationRecordStatus.Failed, Assert.Single(store.Load()).Status);
    }

    [Fact]
    public void StoreRoundTripsCompletedTextResultForRecordViewing()
    {
        var store = new ReportGenerationRecordStore(_root);
        var textResult = new ReportTextDocument(ReportTextDocument.CurrentVersion,
        [
            new ReportTextBlock("heading", ReportTextBlockKind.Heading1, "本周汇报", Bold: true),
            new ReportTextBlock("table", ReportTextBlockKind.Table, Table: new ReportTextTable(
            [
                new[] { "事项", "状态" },
                new[] { "汇报工具", "已完成" }
            ]))
        ]);
        var record = Record("text", ReportGenerationRecordStatus.Completed, null) with
        {
            TemplateMode = ReportTemplateMode.Text,
            FileOutputStatus = ReportFileOutputStatus.NotApplicable,
            TextOutput = textResult
        };

        store.Save([record]);

        var loaded = Assert.Single(store.Load());
        Assert.NotNull(loaded.TextOutput);
        Assert.Equal("本周汇报", loaded.TextOutput.Blocks[0].Text);
        Assert.Equal("已完成", loaded.TextOutput.Blocks[1].Table!.Cells[1][1]);
        Assert.Contains("\"textOutput\"", File.ReadAllText(Path.Combine(_root, "records.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceRecoversPersistedInFlightRecordAsInterrupted()
    {
        var store = new ReportGenerationRecordStore(_root);
        store.Save([Record("in-flight", ReportGenerationRecordStatus.Generating, null)]);

        var workspace = new ReportWorkspace(new FixedReader(), store);

        var record = Assert.Single(workspace.State.Records);
        Assert.Equal(ReportGenerationRecordStatus.Failed, record.Status);
        Assert.Equal("interrupted", record.ErrorCode);
        Assert.NotNull(record.FinishedAt);
    }

    private static ReportGenerationRecord Record(string id, ReportGenerationRecordStatus status, string? path) => new(
        id,
        new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.FromHours(8)),
        status == ReportGenerationRecordStatus.Generating ? null : new DateTimeOffset(2026, 7, 21, 9, 1, 0, TimeSpan.FromHours(8)),
        status,
        new ReportRange(ReportRangeKind.ThisWeek, new DateTime(2026, 7, 20), new DateTime(2026, 7, 26)),
        ReportStyle.Professional,
        ReportTemplateMode.File,
        2,
        1,
        path is null ? ReportFileOutputStatus.Pending : ReportFileOutputStatus.Saved,
        path,
        null);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedReader : IReportTodoReader
    {
        public Task<ReportTaskPreview> ReadAsync(Fowan.Todo.Shared.Models.TodoFilterCriteria filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportTaskPreview([], []));
    }
}
