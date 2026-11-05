using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Report.Shared;

namespace Fowan.Report.Windows;

internal sealed class ReportAiGateway : IAsyncDisposable
{
    private readonly AiCoreClient _client;
    private readonly AiCoreApi _api;
    private readonly AiConsentCoordinator _consent;
    private readonly HashSet<string> _reportDisclosedEndpoints = new(StringComparer.OrdinalIgnoreCase);

    public ReportAiGateway() : this(new AiCoreClient(new Platform.Windows.WindowsAiCoreProcessLauncher()))
    {
    }

    internal ReportAiGateway(AiCoreClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _api = new AiCoreApi(_client);
        _consent = new AiConsentCoordinator(_client);
    }

    public event EventHandler<AiCoreNotificationEventArgs>? Notification;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _client.NotificationAsync = (notification, _) =>
        {
            Notification?.Invoke(this, notification);
            return Task.CompletedTask;
        };
        await _client.ConnectAsync(["ai.report.v1"], cancellationToken);
    }

    public async Task<AiConsentExecution<AiReportInvocation>> GenerateAsync(
        ReportGenerationInput input,
        Func<string, Task<bool>> confirmAsync,
        Action? onRequestAccepted = null,
        int attempt = 1,
        AiReportContentDocument? candidate = null,
        string? validationFeedback = null,
        CancellationToken cancellationToken = default)
    {
        // Core exits after an idle period. A report window may remain open longer than that,
        // so every foreground generation re-establishes the verified report capability.
        await ConnectAsync(cancellationToken);
        var bindings = await _api.ListBindingsAsync(cancellationToken);
        var binding = bindings.FirstOrDefault(item => item.FeatureId == "ai.report")
            ?? throw new AiCoreException("not_found", "尚未为“汇报”功能配置默认模型。");
        var credential = (await _api.ListCredentialsAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == binding.CredentialId)
            ?? throw new AiCoreException("not_found", "汇报功能绑定的密钥不存在。");
        if (!_reportDisclosedEndpoints.Contains(credential.BaseUrl) && !await confirmAsync(credential.BaseUrl))
        {
            return new AiConsentExecution<AiReportInvocation>(false, null);
        }
        _reportDisclosedEndpoints.Add(credential.BaseUrl);
        onRequestAccepted?.Invoke();
        var request = ToRequest(input, attempt, candidate, validationFeedback);
        return await _consent.TryExecuteAsync(
            credential.BaseUrl,
            _ => Task.FromResult(true),
            token => _api.GenerateReportAsync(request, token),
            cancellationToken);
    }

    public Task CancelAsync(string invocationId, CancellationToken cancellationToken = default) =>
        _api.CancelReportAsync(invocationId, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    internal static AiReportRequest ToRequest(
        ReportGenerationInput input,
        int attempt = 1,
        AiReportContentDocument? candidate = null,
        string? validationFeedback = null)
    {
        var isTextMode = input.Template.Mode == ReportTemplateMode.Text;
        var template = isTextMode
            ? ReportAiContentMapper.ToWire(input.Template.TextDocument ?? ReportTextDocument.Empty)
            : ReportAiContentMapper.ToWire(input.Template.FileDocument
                ?? throw new InvalidOperationException("文件模板缺少可编辑内容树。"));
        var example = isTextMode
            ? ReportAiContentMapper.ToWire(input.Template.ExampleTextDocument
                ?? ReportTextDocuments.FromMarkdown(input.Template.Example))
            : input.Template.ExampleFileDocument is null ? null : ReportAiContentMapper.ToWire(input.Template.ExampleFileDocument);
        return new(
        input.Range.Kind switch
        {
            ReportRangeKind.ThisMonth => "monthly",
            ReportRangeKind.Custom => "range",
            _ => "weekly"
        },
        input.Range.Start.ToString("yyyy-MM-dd"),
        input.Range.End.ToString("yyyy-MM-dd"),
        input.Style switch
        {
            ReportStyle.Plain => "plain",
            ReportStyle.Concise => "concise",
            _ => "professional"
        },
        input.CustomRequirements,
        isTextMode ? "text" : "file",
        template,
        example,
        attempt,
        candidate,
        validationFeedback,
        input.Tasks.Completed.Select(ToTask).ToArray(),
        input.Tasks.Unfinished.Select(ToTask).ToArray());
    }

    private static AiReportTask ToTask(ReportTaskSnapshot task) => new(
        task.Title,
        task.Notes,
        task.ListName,
        task.Level,
        task.Important,
        task.StartDate.ToString("yyyy-MM-dd"),
        task.DueDate?.ToString("yyyy-MM-dd"),
        task.CompletedAt?.ToString("O"),
        task.Status);
}
