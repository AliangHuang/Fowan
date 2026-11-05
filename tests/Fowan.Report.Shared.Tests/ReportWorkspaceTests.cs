using Fowan.Report.Shared;
using Fowan.Todo.Shared.Models;
using Xunit;

namespace Fowan.Report.Shared.Tests;

public sealed class ReportWorkspaceTests
{
    [Fact]
    public async Task InitialAndClearedStateHaveNoRangeAndCannotGenerate()
    {
        var workspace = new ReportWorkspace(new FixedReader());

        Assert.Null(workspace.State.Range);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.BeginGenerationAsync());

        workspace.SelectRange(ReportRangeKind.ThisWeek, new DateTime(2026, 7, 21));
        workspace.ClearFilter();

        Assert.Null(workspace.State.Range);
        Assert.Equal(TodoFilterCriteria.Default, workspace.State.Filter);
    }

    [Fact]
    public void PreviousWeekIsTheCompleteMondayToSundayBeforeCurrentWeek()
    {
        var workspace = new ReportWorkspace(new FixedReader());

        workspace.SelectRange(ReportRangeKind.PreviousWeek, new DateTime(2026, 7, 21));

        Assert.Equal(new DateTime(2026, 7, 13), workspace.State.Range!.Start);
        Assert.Equal(new DateTime(2026, 7, 19), workspace.State.Range.End);
    }

    [Fact]
    public void QuickRangeDefaultsToExecutionPeriodAndPreservesExplicitStartDate()
    {
        var workspace = new ReportWorkspace(new FixedReader());

        workspace.SelectRange(ReportRangeKind.ThisWeek, new DateTime(2026, 7, 21));
        Assert.Null(workspace.State.Filter.DateRange);

        workspace.SetFilter(new TodoFilterCriteria(null, 2, new TodoDateRangeFilter
        {
            Mode = TodoDateFilterMode.StartDate,
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 2)
        }));
        workspace.SelectRange(ReportRangeKind.ThisMonth, new DateTime(2026, 7, 21));

        Assert.Equal(TodoDateFilterMode.StartDate, workspace.State.Filter.DateRange!.Mode);
        Assert.Equal(new DateTime(2026, 7, 1), workspace.State.Filter.DateRange.StartDate);
        Assert.Equal(new DateTime(2026, 7, 31), workspace.State.Filter.DateRange.EndDate);
    }

    [Fact]
    public void ManualDatesMatchQuickRangesOrBecomeCustom()
    {
        var workspace = new ReportWorkspace(new FixedReader());
        var previous = Fowan.Todo.Shared.Services.TodoDateRangePresets.PreviousWeek();

        workspace.SetCustomRange(previous.Start, previous.End, TodoDateFilterMode.ExecutionPeriod);
        Assert.Equal(ReportRangeKind.PreviousWeek, workspace.State.Range!.Kind);

        workspace.SetCustomRange(new DateTime(2026, 7, 2), new DateTime(2026, 7, 5), TodoDateFilterMode.ExecutionPeriod);
        Assert.Equal(ReportRangeKind.Custom, workspace.State.Range!.Kind);
    }

    [Fact]
    public async Task GenerationReloadsFreezesTodoAndStoresCompletedTextResultWithoutTheTodoSnapshot()
    {
        var reader = new SequencedReader();
        var store = new MemoryRecordStore();
        var workspace = new ReportWorkspace(reader, store);
        workspace.SelectRange(ReportRangeKind.ThisWeek);

        await workspace.RefreshPreviewAsync();
        var input = await workspace.BeginGenerationAsync();
        workspace.AcceptInvocation("invocation-1");
        var textTemplate = new ReportTextDocument(ReportTextDocument.CurrentVersion,
            [new("body", ReportTextBlockKind.Paragraph, "模板正文")]);
        workspace.Complete("invocation-1", new ReportGenerationOutput(
            textTemplate with { Blocks = [new("body", ReportTextBlockKind.Paragraph, "private report body")] }, null));

        Assert.Equal("second", Assert.Single(input.Tasks.Unfinished).Title);
        var record = Assert.Single(workspace.State.Records);
        Assert.Equal(ReportGenerationRecordStatus.Completed, record.Status);
        Assert.Equal(ReportTemplateMode.Text, record.TemplateMode);
        Assert.Null(record.OutputPath);
        Assert.NotNull(record.TextOutput);
        Assert.Equal("private report body", ReportTextDocuments.ToPlainText(record.TextOutput));
        Assert.Equal(2, reader.Calls);
        Assert.Equal("private report body", ReportTextDocuments.ToPlainText(Assert.Single(store.Records).TextOutput));
    }

    [Fact]
    public async Task FileOutputPathIsRecordedOnlyAfterSuccessfulCommitAndRecordsCanBeDeleted()
    {
        var store = new MemoryRecordStore();
        var workspace = new ReportWorkspace(new FixedReader(), store);
        workspace.SelectRange(ReportRangeKind.ThisWeek);
        workspace.SetTemplate(new ReportTemplateContext(ReportTemplateMode.File, "", "", "template.docx"));

        await workspace.BeginGenerationAsync();
        workspace.AcceptInvocation("invocation-2");
        workspace.Complete("invocation-2", new ReportGenerationOutput(null,
            new ReportFileContentDocument("docx", [new("paragraph", "候选内容")], [])));
        var record = Assert.Single(workspace.State.Records);
        Assert.Equal(ReportFileOutputStatus.Pending, record.FileOutputStatus);
        Assert.Null(record.OutputPath);

        workspace.MarkFileOutputSaved("D:\\Reports\\weekly.docx");
        record = Assert.Single(workspace.State.Records);
        Assert.Equal(ReportFileOutputStatus.Saved, record.FileOutputStatus);
        Assert.Equal("D:\\Reports\\weekly.docx", record.OutputPath);

        workspace.DeleteRecords([record.Id]);
        Assert.Empty(workspace.State.Records);
        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task RepairReservationPreventsCancellationOrCompletionOfThePreviousCandidate()
    {
        var workspace = new ReportWorkspace(new FixedReader());
        workspace.SelectRange(ReportRangeKind.ThisWeek);
        workspace.SetTemplate(new ReportTemplateContext(ReportTemplateMode.File, "", "", "template.docx"));

        await workspace.BeginGenerationAsync();
        workspace.AcceptInvocation("candidate-1");

        Assert.True(workspace.BeginRepair("candidate-1"));
        Assert.Equal(ReportGenerationLifecycle.Generating, workspace.State.Lifecycle);
        Assert.Null(workspace.State.InvocationId);
        Assert.False(workspace.BeginCancellation());
        Assert.False(workspace.Complete("candidate-1", new ReportGenerationOutput(null,
            new ReportFileContentDocument("docx", [new("paragraph", "first candidate")], []))));

        workspace.AcceptInvocation("candidate-2");
        Assert.True(workspace.BeginCancellation());
        Assert.False(workspace.Complete("candidate-2", new ReportGenerationOutput(null,
            new ReportFileContentDocument("docx", [new("paragraph", "late candidate")], []))));

        workspace.Cancel("candidate-2");
        Assert.Equal(ReportGenerationLifecycle.Cancelled, workspace.State.Lifecycle);
    }

    [Fact]
    public void WorkspaceOwnsTextDocumentsButProjectsOnlyReadableTextForAi()
    {
        var workspace = new ReportWorkspace(new FixedReader());
        var template = ReportTextDocuments.FromMarkdown("# 周报\n\n| 事项 | 状态 |\n| --- | --- |\n| 汇报 | 完成 |");
        var example = ReportTextDocuments.FromMarkdown("- 示例");

        workspace.SetTextDocuments(template, example);
        workspace.SetOutputDocument(ReportTextDocuments.FromMarkdown("# 结果"));

        Assert.Contains("汇报\t完成", workspace.State.Template.Template, StringComparison.Ordinal);
        Assert.Contains("示例", workspace.State.Template.Example, StringComparison.Ordinal);
        Assert.NotNull(workspace.State.OutputDocument);
        Assert.Empty(workspace.State.Records);
    }

    [Fact]
    public void GeneratedTextKeepsTheReturnedFullRichBlockAndTableStructure()
    {
        var template = new ReportTextDocument(ReportTextDocument.CurrentVersion,
        [
            new("heading", ReportTextBlockKind.Heading1, "周报", Bold: true),
            new("table", ReportTextBlockKind.Table, Table: new ReportTextTable([
                new[] { "事项", "进度" },
                new[] { "", "" }
            ]))
        ]);

        var output = new ReportTextDocument(ReportTextDocument.CurrentVersion,
        [
            new("heading", ReportTextBlockKind.Heading1, "本周工作汇报", Bold: true),
            new("table", ReportTextBlockKind.Table, Table: new ReportTextTable([
                new[] { "事项", "进度" },
                new[] { "方案设计", "已完成" }
            ]))
        ]);

        Assert.Equal(ReportTextBlockKind.Heading1, output.Blocks[0].Kind);
        Assert.True(output.Blocks[0].Bold);
        Assert.Equal("本周工作汇报", output.Blocks[0].Text);
        Assert.Equal(["事项", "进度"], output.Blocks[1].Table!.Cells[0]);
        Assert.Equal(["方案设计", "已完成"], output.Blocks[1].Table!.Cells[1]);
    }

    [Fact]
    public void EmptyTemplateIsAValidEditableBlockDocumentWithoutProviderTargets()
    {
        var document = ReportTextDocuments.Normalize(ReportTextDocument.Empty);

        Assert.Single(document.Blocks);
        Assert.Equal(ReportTextBlockKind.Paragraph, document.Blocks[0].Kind);
        Assert.DoesNotContain("target", document.Blocks[0].Id, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedReader : IReportTodoReader
    {
        public Task<ReportTaskPreview> ReadAsync(TodoFilterCriteria filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportTaskPreview([], [new("task", "", "list", 1, false, DateTime.Today, null, null, "unfinished")]));
    }

    private sealed class SequencedReader : IReportTodoReader
    {
        public int Calls { get; private set; }
        public Task<ReportTaskPreview> ReadAsync(TodoFilterCriteria filter, CancellationToken cancellationToken = default)
        {
            Calls++;
            var title = Calls == 1 ? "first" : "second";
            return Task.FromResult(new ReportTaskPreview([], [new(title, "", "list", 1, false, DateTime.Today, null, null, "unfinished")]));
        }
    }

    private sealed class MemoryRecordStore : IReportGenerationRecordStore
    {
        public IReadOnlyList<ReportGenerationRecord> Records { get; private set; } = [];
        public IReadOnlyList<ReportGenerationRecord> Load() => Records;
        public void Save(IReadOnlyList<ReportGenerationRecord> records) => Records = records.ToArray();
    }
}
