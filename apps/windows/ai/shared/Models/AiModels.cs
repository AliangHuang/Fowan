
using System.Globalization;
using System.Text.Json.Serialization;

namespace Fowan.Ai.Shared.Models;

public sealed record AiChannel(
    string Id,
    string Kind,
    string DisplayName,
    string DefaultBaseUrl,
    bool BuiltIn,
    bool Enabled)
{
    [JsonIgnore]
    public string DisplayLabel => Enabled ? DisplayName : $"{DisplayName}（暂不支持）";
}

public sealed record AiCredential(
    string Id,
    string ChannelId,
    string Label,
    string BaseUrl,
    string SecretHint,
    bool Enabled,
    string? LastTestStatus,
    string? LastTestAt,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore]
    public string DisplayLabel => $"{Label}  {SecretHint}";

    [JsonIgnore]
    public string StatusLabel => Enabled ? "已启用" : "不可用";
}
public sealed record AiModelProfile(
    string Id,
    string CredentialId,
    string ModelId,
    string DisplayName,
    string Source,
    bool Enabled,
    bool ThinkingEnabled,
    int? ContextWindowTokens,
    int? MaxOutputTokens,
    bool LimitsConfigured,
    string? LastTestStatus,
    string? LastTestAt,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<string>? ThinkingEffortOptions = null)
{
    [JsonIgnore]
    public string DisplayLabel => (DisplayName == ModelId ? ModelId : $"{DisplayName}  ({ModelId})") +
        (LimitsConfigured ? string.Empty : "  · 限制待配置");
}

public sealed record AiBinding(
    string FeatureId,
    string CredentialId,
    string ModelProfileId,
    string UpdatedAt,
    string? ThinkingEffort = null);

public sealed record AiToolFeature(string FeatureId, string ToolId, string DisplayName, IReadOnlyList<string> RequiredCapabilities);

public sealed record AiReportTask(
    string Title,
    string? Notes,
    string ListName,
    int Level,
    bool Important,
    string StartDate,
    string? DueDate,
    string? CompletedAt,
    string Status);

/// <summary>
/// A provider-facing projection of report content. It deliberately has no local
/// path, Open XML payload, editing command, or client position identifier.
/// </summary>
public sealed record AiReportContentCell(string? Value, string ValueKind, bool Editable);

public sealed record AiReportContentTable(
    IReadOnlyList<IReadOnlyList<AiReportContentCell>> Rows,
    bool CanAppendRows);

public sealed record AiReportContentBlock(
    string Kind,
    string? Text,
    bool Bold,
    bool Italic,
    string? Link,
    bool IsChecked,
    AiReportContentTable? Table);

public sealed record AiReportContentSheet(
    string Name,
    IReadOnlyList<IReadOnlyList<AiReportContentCell>> Rows,
    bool CanAppendRows);

public sealed record AiReportContentDocument(
    string Format,
    IReadOnlyList<AiReportContentBlock> Blocks,
    IReadOnlyList<AiReportContentSheet> Sheets);

public sealed record AiReportRequest(
    string ReportType,
    string RangeStart,
    string RangeEnd,
    string Style,
    string CustomRequirements,
    string TemplateMode,
    AiReportContentDocument Template,
    AiReportContentDocument? Example,
    int Attempt,
    AiReportContentDocument? Candidate,
    string? ValidationFeedback,
    IReadOnlyList<AiReportTask> CompletedTasks,
    IReadOnlyList<AiReportTask> UnfinishedTasks);

public sealed record AiReportOutput(
    AiReportContentDocument Document);

public sealed record AiReportInvocation(string InvocationId);

public sealed record AiReportCompleted(string InvocationId, AiReportOutput Output);

public sealed record AiReportFinished(string InvocationId, string? ErrorCode, string? RequestId = null);

public sealed record AiConversationSummary(string Id, string Title, string CreatedAt, string UpdatedAt)
{
    [JsonIgnore]
    public string DisplayUpdatedAt =>
        DateTimeOffset.TryParse(UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture)
            : UpdatedAt;
}

public sealed record AiChatMessage(
    string Id,
    string Role,
    string Content,
    string Status,
    string? ParentMessageId,
    int VariantIndex,
    string? ChannelName,
    string? CredentialName,
    string? ModelId,
    string CreatedAt);

public sealed record AiConversation(
    string Id,
    string Title,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<AiChatMessage> Messages);

public sealed record AiPresetModel(string ChannelId, string ModelId, string DisplayName, int ContextWindowTokens, int MaxOutputTokens);

public static class AiPresetModelDefaults
{
    public static AiPresetModel? Find(
        IEnumerable<AiPresetModel> presets,
        string? channelId,
        string? modelId)
    {
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var normalizedModelId = modelId.Trim();
        return presets.FirstOrDefault(preset =>
            string.Equals(preset.ChannelId, channelId, StringComparison.Ordinal) &&
            string.Equals(preset.ModelId, normalizedModelId, StringComparison.Ordinal));
    }
}

public sealed record AiChatStarted(string InvocationId, string ConversationId, string AssistantMessageId);

public sealed record AiChatDelta(string InvocationId, string Delta);

public sealed record AiChatFinished(string InvocationId, string AssistantMessageId, string? ErrorCode, string? RequestId = null);

public sealed record AiContextEstimate(long EstimatedInputTokens, long SafeInputTokens, int ContextWindowTokens,
    int MaxOutputTokens, double UsageRatio, string Action, string? CompactedThroughMessageId);

public sealed record AiConversationSummaryRecord(string Id, string Content, string ThroughMessageId, string CreatedAt);

public sealed record AiMessagePage(string ConversationId, string? ActiveLeafMessageId, IReadOnlyList<AiChatMessage> Items,
    string? NextCursor, bool HasMore, AiConversationSummaryRecord? Summary);

public sealed record AiCompactInvocation(string InvocationId, string ConversationId);
public sealed record AiCompactStarted(string InvocationId, string ConversationId);
public sealed record AiCompactCompleted(string InvocationId, string ConversationId, string SummaryId, string ThroughMessageId);
public sealed record AiCompactFailed(string InvocationId, string ConversationId, string ErrorCode, string? RequestId = null);

public sealed record AiConsent(bool Granted, string? Endpoint);
