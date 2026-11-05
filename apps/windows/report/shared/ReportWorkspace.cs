using Fowan.Todo.Shared.Models;
using Fowan.Todo.Shared.Services;

namespace Fowan.Report.Shared;

public enum ReportRangeKind { ThisWeek, PreviousWeek, ThisMonth, Custom }
public enum ReportStyle { Professional, Plain, Concise }
public enum ReportTemplateMode { Text, File }
public enum ReportGenerationLifecycle { Idle, Generating, Cancelling, Completed, Cancelled, Failed }
public enum ReportGenerationRecordStatus { Generating, Completed, Cancelled, Failed }
public enum ReportFileOutputStatus { NotApplicable, Pending, Saved, Cancelled, Failed }

public sealed record ReportRange(ReportRangeKind Kind, DateTime Start, DateTime End)
{
    public string Label => $"{Start:yyyy-MM-dd} 至 {End:yyyy-MM-dd}";
}

public sealed record ReportTaskSnapshot(
    string Title,
    string Notes,
    string ListName,
    int Level,
    bool Important,
    DateTime StartDate,
    DateTime? DueDate,
    DateTimeOffset? CompletedAt,
    string Status);

public sealed record ReportTaskPreview(
    IReadOnlyList<ReportTaskSnapshot> Completed,
    IReadOnlyList<ReportTaskSnapshot> Unfinished)
{
    public int Total => Completed.Count + Unfinished.Count;
}

public sealed record ReportTemplateContext(
    ReportTemplateMode Mode,
    string Template,
    string Example,
    string? TemplateFilePath = null,
    string? ExampleFilePath = null,
    ReportFileContentDocument? FileDocument = null,
    ReportFileContentDocument? ExampleFileDocument = null,
    ReportTextDocument? TextDocument = null,
    ReportTextDocument? ExampleTextDocument = null);

public sealed record ReportGenerationInput(
    ReportRange Range,
    TodoFilterCriteria Filter,
    ReportStyle Style,
    string CustomRequirements,
    ReportTemplateContext Template,
    ReportTaskPreview Tasks);

public sealed record ReportGenerationOutput(
    ReportTextDocument? TextDocument,
    ReportFileContentDocument? FileDocument);

/// <summary>
/// Local history contains operational metadata and, for a completed text report only,
/// its rendered rich-text document. It excludes Todo snapshots, templates, examples
/// and custom requirements.
/// </summary>
public sealed record ReportGenerationRecord(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    ReportGenerationRecordStatus Status,
    ReportRange Range,
    ReportStyle Style,
    ReportTemplateMode TemplateMode,
    int CompletedTaskCount,
    int UnfinishedTaskCount,
    ReportFileOutputStatus FileOutputStatus,
    string? OutputPath,
    string? ErrorCode,
    ReportTextDocument? TextOutput = null);

public interface IReportGenerationRecordStore
{
    IReadOnlyList<ReportGenerationRecord> Load();
    void Save(IReadOnlyList<ReportGenerationRecord> records);
}

public sealed record ReportWorkspaceSnapshot(
    ReportRange? Range,
    TodoFilterCriteria Filter,
    ReportStyle Style,
    string CustomRequirements,
    ReportTemplateContext Template,
    ReportTaskPreview Preview,
    ReportGenerationLifecycle Lifecycle,
    string? InvocationId,
    ReportGenerationOutput? Output,
    string? Error,
    IReadOnlyList<ReportGenerationRecord> Records,
    ReportTextDocument TemplateDocument,
    ReportTextDocument ExampleDocument,
    ReportTextDocument? OutputDocument);

public interface IReportTodoReader
{
    Task<ReportTaskPreview> ReadAsync(TodoFilterCriteria filter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sole mutable state owner for the report workflow. UI windows render only its
/// immutable snapshots and delegate mutations through this API.
/// </summary>
public sealed class ReportWorkspace
{
    private readonly IReportTodoReader _todoReader;
    private readonly IReportGenerationRecordStore? _recordStore;
    private readonly List<ReportGenerationRecord> _records;
    private ReportRange? _range;
    private TodoFilterCriteria _filter = TodoFilterCriteria.Default;
    private ReportStyle _style = ReportStyle.Professional;
    private string _customRequirements = string.Empty;
    private ReportTemplateContext _template = new(ReportTemplateMode.Text, string.Empty, string.Empty);
    private ReportTaskPreview _preview = new([], []);
    private ReportGenerationLifecycle _lifecycle = ReportGenerationLifecycle.Idle;
    private string? _invocationId;
    private string? _recordId;
    private ReportGenerationOutput? _output;
    private string? _error;
    private ReportTextDocument _templateDocument = ReportTextDocument.Empty;
    private ReportTextDocument _exampleDocument = ReportTextDocument.Empty;
    private ReportTextDocument? _outputDocument;

    public ReportWorkspace(IReportTodoReader todoReader, IReportGenerationRecordStore? recordStore = null)
    {
        _todoReader = todoReader;
        _recordStore = recordStore;
        _records = recordStore?.Load().OrderByDescending(record => record.CreatedAt).ToList() ?? [];
        RecoverInterruptedRecords();
    }

    public event EventHandler<ReportWorkspaceSnapshot>? StateChanged;

    public ReportWorkspaceSnapshot State => new(
        _range, _filter, _style, _customRequirements, _template, _preview,
        _lifecycle, _invocationId, _output, _error, _records.ToArray(),
        _templateDocument, _exampleDocument, _outputDocument);

    public void SelectRange(ReportRangeKind kind, DateTime? today = null)
    {
        EnsureEditable();
        if (kind == ReportRangeKind.Custom) throw new ArgumentException("自定义区间需要提供开始和结束日期。", nameof(kind));
        _range = ToRange(kind, today ?? DateTime.Today);
        if (_filter.DateRange is not null)
        {
            _filter = _filter with { DateRange = TodoDateRangePresets.Apply(_filter.DateRange, _range.Start, _range.End) };
        }
        ClearPreview();
        Publish();
    }

    public void SetCustomRange(DateTime start, DateTime end, TodoDateFilterMode mode)
    {
        EnsureEditable();
        if (start.Date > end.Date) throw new ArgumentException("开始日期不得晚于结束日期。", nameof(start));
        _range = MatchRange(start, end);
        _filter = _filter with { DateRange = TodoDateRangePresets.Apply(new TodoDateRangeFilter { Mode = mode, StartDate = start, EndDate = end }, start, end) };
        ClearPreview();
        Publish();
    }

    /// <summary>Applies Todo criteria without changing a quick report range.</summary>
    public void SetFilter(TodoFilterCriteria criteria)
    {
        EnsureEditable();
        _filter = criteria.Normalize();
        ClearPreview();
        Publish();
    }

    public void ClearFilter()
    {
        EnsureEditable();
        _range = null;
        _filter = TodoFilterCriteria.Default;
        ClearPreview();
        Publish();
    }

    public void SetStyle(ReportStyle style)
    {
        EnsureEditable();
        _style = style;
        Publish();
    }

    public void SetCustomRequirements(string? value)
    {
        EnsureEditable();
        value ??= string.Empty;
        if (value.EnumerateRunes().Count() > 500)
        {
            throw new ArgumentException("自定义要求最多 500 字。", nameof(value));
        }
        _customRequirements = value;
        Publish();
    }

    public void SetTemplate(ReportTemplateContext context)
    {
        EnsureEditable();
        if (context.Mode == ReportTemplateMode.File && string.IsNullOrWhiteSpace(context.TemplateFilePath))
        {
            throw new ArgumentException("文件模式必须提供模板文件。", nameof(context));
        }
        _template = context;
        Publish();
    }

    /// <summary>Updates text-mode documents and their local plain-text preview projection together.</summary>
    public void SetTextDocuments(ReportTextDocument template, ReportTextDocument example)
    {
        EnsureEditable();
        _templateDocument = ReportTextDocuments.Normalize(template);
        _exampleDocument = ReportTextDocuments.Normalize(example);
        _template = new(
            ReportTemplateMode.Text,
            ReportTextDocuments.ToPlainText(_templateDocument),
            ReportTextDocuments.ToPlainText(_exampleDocument),
            TextDocument: _templateDocument,
            ExampleTextDocument: _exampleDocument);
        Publish();
    }

    /// <summary>Sets the editable document shown for the current generation.</summary>
    public void SetOutputDocument(ReportTextDocument? output)
    {
        _outputDocument = output is null ? null : ReportTextDocuments.Normalize(output);
        Publish();
    }

    public async Task RefreshPreviewAsync(CancellationToken cancellationToken = default)
    {
        EnsureEditable();
        if (_range is null)
        {
            ClearPreview();
            _error = null;
            Publish();
            return;
        }
        _preview = await _todoReader.ReadAsync(EffectiveFilter(), cancellationToken);
        _error = null;
        Publish();
    }

    /// <summary>Reloads Todo and freezes that exact snapshot for one request.</summary>
    public async Task<ReportGenerationInput> BeginGenerationAsync(CancellationToken cancellationToken = default)
    {
        EnsureEditable();
        if (_range is null) throw new InvalidOperationException("请先选择汇报时间范围。");
        var effectiveFilter = EffectiveFilter();
        var frozen = await _todoReader.ReadAsync(effectiveFilter, cancellationToken);
        if (frozen.Total == 0)
        {
            _preview = frozen;
            _recordId = CreateRecord(frozen);
            _lifecycle = ReportGenerationLifecycle.Failed;
            _error = "当前筛选范围没有待办，未发送 AI 请求。";
            UpdateRecord(record => record with
            {
                Status = ReportGenerationRecordStatus.Failed,
                FinishedAt = DateTimeOffset.Now,
                FileOutputStatus = ReportFileOutputStatus.NotApplicable,
                ErrorCode = "empty_result"
            });
            Publish();
            throw new InvalidOperationException("当前筛选范围没有待办，未发送 AI 请求。");
        }
        _preview = frozen;
        _lifecycle = ReportGenerationLifecycle.Generating;
        _invocationId = null;
        _output = null;
        _outputDocument = null;
        _error = null;
        _recordId = CreateRecord(frozen);
        Publish();
        return new ReportGenerationInput(_range, effectiveFilter, _style, _customRequirements, _template, frozen);
    }

    public void AcceptInvocation(string invocationId)
    {
        if (_lifecycle != ReportGenerationLifecycle.Generating || string.IsNullOrWhiteSpace(invocationId)) return;
        _invocationId = invocationId;
        Publish();
    }

    public bool BeginCancellation()
    {
        if (_lifecycle != ReportGenerationLifecycle.Generating || string.IsNullOrWhiteSpace(_invocationId)) return false;
        _lifecycle = ReportGenerationLifecycle.Cancelling;
        Publish();
        return true;
    }

    public bool Complete(string invocationId, ReportGenerationOutput output)
    {
        if (_lifecycle != ReportGenerationLifecycle.Generating || !IsCurrent(invocationId)) return false;
        _lifecycle = ReportGenerationLifecycle.Completed;
        _output = output;
        _error = null;
        UpdateRecord(record => record with
        {
            Status = ReportGenerationRecordStatus.Completed,
            FinishedAt = DateTimeOffset.Now,
            FileOutputStatus = record.TemplateMode == ReportTemplateMode.File ? ReportFileOutputStatus.Pending : ReportFileOutputStatus.NotApplicable,
            ErrorCode = null,
            TextOutput = record.TemplateMode == ReportTemplateMode.Text
                ? output.TextDocument is null ? null : ReportTextDocuments.Normalize(output.TextDocument)
                : null
        });
        Publish();
        return true;
    }

    /// <summary>
    /// Releases a completed candidate invocation before its replacement request starts.
    /// This prevents a late cancel action from being sent to the completed candidate.
    /// </summary>
    public bool BeginRepair(string completedInvocationId)
    {
        if (_lifecycle != ReportGenerationLifecycle.Generating || !IsCurrent(completedInvocationId)) return false;
        _invocationId = null;
        Publish();
        return true;
    }

    public void Cancel(string invocationId)
    {
        if (!IsCurrent(invocationId)) return;
        _lifecycle = ReportGenerationLifecycle.Cancelled;
        _output = null;
        UpdateRecord(record => record with
        {
            Status = ReportGenerationRecordStatus.Cancelled,
            FinishedAt = DateTimeOffset.Now,
            FileOutputStatus = ReportFileOutputStatus.NotApplicable,
            ErrorCode = "cancelled"
        });
        Publish();
    }

    public void Fail(string? invocationId, string message, string? errorCode = null)
    {
        if (invocationId is not null && !IsCurrent(invocationId)) return;
        _lifecycle = ReportGenerationLifecycle.Failed;
        _output = null;
        _error = message;
        UpdateRecord(record => record with
        {
            Status = ReportGenerationRecordStatus.Failed,
            FinishedAt = DateTimeOffset.Now,
            FileOutputStatus = ReportFileOutputStatus.NotApplicable,
            ErrorCode = errorCode ?? "generation_failed"
        });
        Publish();
    }

    public void MarkFileOutputSaved(string path)
    {
        if (_lifecycle != ReportGenerationLifecycle.Completed || string.IsNullOrWhiteSpace(path)) return;
        UpdateRecord(record => record with { FileOutputStatus = ReportFileOutputStatus.Saved, OutputPath = path, ErrorCode = null });
        Publish();
    }

    public void MarkFileOutputCancelled()
    {
        if (_lifecycle != ReportGenerationLifecycle.Completed) return;
        UpdateRecord(record => record with { FileOutputStatus = ReportFileOutputStatus.Cancelled });
        Publish();
    }

    public void MarkFileOutputFailed(string errorCode = "file_output_failed")
    {
        if (_lifecycle != ReportGenerationLifecycle.Completed) return;
        UpdateRecord(record => record with { FileOutputStatus = ReportFileOutputStatus.Failed, ErrorCode = errorCode });
        Publish();
    }

    public void DeleteRecords(IEnumerable<string> ids)
    {
        var values = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (values.Count == 0) return;
        _records.RemoveAll(record => values.Contains(record.Id));
        PersistRecords();
        Publish();
    }

    private TodoFilterCriteria EffectiveFilter()
    {
        if (_range is null) throw new InvalidOperationException("请先选择汇报时间范围。");
        var normalized = _filter.Normalize();
        return normalized with
        {
            DateRange = normalized.DateRange ?? TodoDateRangePresets.Apply(null, _range.Start, _range.End)
        };
    }

    private string CreateRecord(ReportTaskPreview frozen)
    {
        var record = new ReportGenerationRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.Now,
            null,
            ReportGenerationRecordStatus.Generating,
            _range!,
            _style,
            _template.Mode,
            frozen.Completed.Count,
            frozen.Unfinished.Count,
            _template.Mode == ReportTemplateMode.File ? ReportFileOutputStatus.Pending : ReportFileOutputStatus.NotApplicable,
            null,
            null);
        _records.Insert(0, record);
        PersistRecords();
        return record.Id;
    }

    private void UpdateRecord(Func<ReportGenerationRecord, ReportGenerationRecord> update)
    {
        if (string.IsNullOrWhiteSpace(_recordId)) return;
        var index = _records.FindIndex(record => string.Equals(record.Id, _recordId, StringComparison.Ordinal));
        if (index < 0) return;
        _records[index] = update(_records[index]);
        PersistRecords();
    }

    private void RecoverInterruptedRecords()
    {
        var changed = false;
        for (var index = 0; index < _records.Count; index++)
        {
            if (_records[index].Status != ReportGenerationRecordStatus.Generating) continue;
            _records[index] = _records[index] with
            {
                Status = ReportGenerationRecordStatus.Failed,
                FinishedAt = DateTimeOffset.Now,
                FileOutputStatus = ReportFileOutputStatus.NotApplicable,
                ErrorCode = "interrupted"
            };
            changed = true;
        }
        if (changed) PersistRecords();
    }

    private void PersistRecords() => _recordStore?.Save(_records);

    private void ClearPreview() => _preview = new([], []);

    private bool IsCurrent(string invocationId) =>
        !string.IsNullOrWhiteSpace(_invocationId) && string.Equals(_invocationId, invocationId, StringComparison.Ordinal);

    private void EnsureEditable()
    {
        if (_lifecycle is ReportGenerationLifecycle.Generating or ReportGenerationLifecycle.Cancelling)
        {
            throw new InvalidOperationException("生成进行中，暂不能调整汇报内容。");
        }
    }

    private void Publish() => StateChanged?.Invoke(this, State);

    private static ReportRange MatchRange(DateTime start, DateTime end, DateTime? today = null)
    {
        var candidates = new[] { ReportRangeKind.ThisWeek, ReportRangeKind.PreviousWeek, ReportRangeKind.ThisMonth };
        foreach (var kind in candidates)
        {
            var candidate = ToRange(kind, today ?? DateTime.Today);
            if (candidate.Start == start.Date && candidate.End == end.Date) return candidate;
        }
        return new(ReportRangeKind.Custom, start.Date, end.Date);
    }

    private static ReportRange ToRange(ReportRangeKind kind, DateTime today) => kind switch
    {
        ReportRangeKind.ThisWeek => From(kind, TodoDateRangePresets.ThisWeek(today)),
        ReportRangeKind.PreviousWeek => From(kind, TodoDateRangePresets.PreviousWeek(today)),
        ReportRangeKind.ThisMonth => From(kind, TodoDateRangePresets.ThisMonth(today)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ReportRange From(ReportRangeKind kind, (DateTime Start, DateTime End) range) => new(kind, range.Start, range.End);
}
