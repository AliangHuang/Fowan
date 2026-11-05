namespace Fowan.Report.Shared.Application.Ports;

/// <summary>Platform-neutral clipboard payload consumed by the block editor.</summary>
public sealed record ReportClipboardContent(
    string? Text,
    IReadOnlyList<IReadOnlyList<string>>? Table);

public interface IReportClipboardService
{
    Task<ReportClipboardContent> ReadAsync(CancellationToken cancellationToken = default);

    void SetText(string text);
}

public sealed record ReportFileOpenRequest(IReadOnlyList<string> Extensions);

public sealed record ReportFileSaveRequest(string SuggestedFileName, string DisplayName, string Extension);

public interface IReportFileDialogService
{
    Task<string?> PickOpenAsync(ReportFileOpenRequest request, CancellationToken cancellationToken = default);

    Task<string?> PickSaveAsync(ReportFileSaveRequest request, CancellationToken cancellationToken = default);
}

public sealed record ReportFilePreferences(string? TemplateFileName, string? ExampleFileName);

public sealed record ReportTextPreferences(ReportTextDocument? Template, ReportTextDocument? Example);

public interface IReportPreferences
{
    ReportFilePreferences Load();

    void Save(string templatePath, string? examplePath);

    ReportTextPreferences LoadText();

    void SaveText(ReportTextDocument template, ReportTextDocument example);
}
