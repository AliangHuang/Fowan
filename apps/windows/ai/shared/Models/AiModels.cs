
using System.Globalization;
using System.Text.Json.Serialization;

namespace Fowan.Ai.Shared.Models;

public sealed record AiChannel(
    string Id,
    string Kind,
    string DisplayName,
    string DefaultBaseUrl,
    bool BuiltIn,
    bool Enabled);

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
}
public sealed record AiModelProfile(
    string Id,
    string CredentialId,
    string ModelId,
    string DisplayName,
    string Source,
    bool Enabled,
    string? LastTestStatus,
    string? LastTestAt,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore]
    public string DisplayLabel => DisplayName == ModelId ? ModelId : $"{DisplayName}  ({ModelId})";
}

public sealed record AiBinding(string FeatureId, string CredentialId, string ModelProfileId, string UpdatedAt);

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

public sealed record AiPresetModel(string ChannelId, string ModelId, string DisplayName);

public sealed record AiChatStarted(string InvocationId, string ConversationId, string AssistantMessageId);

public sealed record AiChatDelta(string InvocationId, string Delta);

public sealed record AiChatFinished(string InvocationId, string AssistantMessageId, string? ErrorCode);

public sealed record AiConsent(bool Granted, string? Endpoint);
