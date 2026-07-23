namespace Fowan.Ai.Shared.Models;

public static class AiProtocolContract
{
    public const string Version = "0.1";
    public const int ContractRevision = 1;
    public const int MaximumHeaderBytes = 8192;
    public const int MaximumFrameBytes = 8 * 1024 * 1024;

    public static IReadOnlyList<string> Capabilities { get; } = ["ai.config.v1", "ai.chat.v1", "ai.chat.context.v1", "ai.chat.branching.v1", "ai.report.v1"];
}

public static class AiProtocolMethods
{
    public const string EngineHandshake = "engine.handshake";
    public const string ChannelsList = "ai.channels.list";
    public const string ChannelsCreate = "ai.channels.create";
    public const string ChannelsUpdate = "ai.channels.update";
    public const string ChannelsDelete = "ai.channels.delete";
    public const string CredentialsList = "ai.credentials.list";
    public const string CredentialsUpsert = "ai.credentials.upsert";
    public const string CredentialsDelete = "ai.credentials.delete";
    public const string CredentialsTest = "ai.credentials.test";
    public const string ModelsList = "ai.models.list";
    public const string ModelsUpsert = "ai.models.upsert";
    public const string ModelsDelete = "ai.models.delete";
    public const string ModelsTest = "ai.models.test";
    public const string ModelsPresets = "ai.models.presets";
    public const string ToolFeaturesList = "ai.toolFeatures.list";
    public const string BindingsList = "ai.bindings.list";
    public const string BindingsUpsert = "ai.bindings.upsert";
    public const string BindingsDelete = "ai.bindings.delete";
    public const string ConsentsCheck = "ai.consents.check";
    public const string ConsentsGrant = "ai.consents.grant";
    public const string ConversationsList = "ai.conversations.list";
    public const string ConversationsCreate = "ai.conversations.create";
    public const string ConversationsGet = "ai.conversations.get";
    public const string ConversationsRename = "ai.conversations.rename";
    public const string ConversationsDelete = "ai.conversations.delete";
    public const string ConversationMessagesList = "ai.conversations.messages.list";
    public const string ConversationBranchSelect = "ai.conversations.branch.select";
    public const string ChatContextEstimate = "ai.chat.context.estimate";
    public const string ChatContextCompact = "ai.chat.context.compact";
    public const string ChatSend = "ai.chat.send";
    public const string ChatCancel = "ai.chat.cancel";
    public const string ChatRegenerate = "ai.chat.regenerate";
    public const string ReportGenerate = "ai.report.generate";
    public const string ReportCancel = "ai.report.cancel";

    public static IReadOnlyList<string> All { get; } =
    [
        EngineHandshake,
        ChannelsList, ChannelsCreate, ChannelsUpdate, ChannelsDelete,
        CredentialsList, CredentialsUpsert, CredentialsDelete, CredentialsTest,
        ModelsList, ModelsUpsert, ModelsDelete, ModelsTest, ModelsPresets,
        ToolFeaturesList, BindingsList, BindingsUpsert, BindingsDelete,
        ConsentsCheck, ConsentsGrant,
        ConversationsList, ConversationsCreate, ConversationsGet, ConversationsRename, ConversationsDelete,
        ConversationMessagesList, ConversationBranchSelect, ChatContextEstimate, ChatContextCompact,
        ChatSend, ChatCancel, ChatRegenerate, ReportGenerate, ReportCancel
    ];
}

public static class AiProtocolNotifications
{
    public const string ChatStarted = "ai.chat.started";
    public const string ChatDelta = "ai.chat.delta";
    public const string ChatCompleted = "ai.chat.completed";
    public const string ChatCancelled = "ai.chat.cancelled";
    public const string ChatFailed = "ai.chat.failed";
    public const string ContextCompactStarted = "ai.chat.context.compact.started";
    public const string ContextCompactCompleted = "ai.chat.context.compact.completed";
    public const string ContextCompactFailed = "ai.chat.context.compact.failed";
    public const string ReportStarted = "ai.report.started";
    public const string ReportCompleted = "ai.report.completed";
    public const string ReportCancelled = "ai.report.cancelled";
    public const string ReportFailed = "ai.report.failed";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ChatStarted, ChatDelta, ChatCompleted, ChatCancelled, ChatFailed,
        ContextCompactStarted, ContextCompactCompleted, ContextCompactFailed,
        ReportStarted, ReportCompleted, ReportCancelled, ReportFailed
    };
}

public static class AiProtocolErrors
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "invalid_argument", "not_found", "conflict", "protocol_mismatch", "handshake_required",
        "consent_required", "secret_store_unavailable", "secure_state_inconsistent",
        "protected_data_unavailable", "storage_unavailable", "provider_auth_failed",
        "provider_model_not_found", "provider_rate_limited", "provider_content_rejected",
        "provider_unavailable", "context_limit_exceeded", "context_compression_required",
        "message_too_large", "response_too_large", "timeout", "cancelled", "internal_error"
    };
}
