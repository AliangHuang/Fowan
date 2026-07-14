using Fowan.Ai.Shared.Models;
using System.Text.Json;

namespace Fowan.Ai.Shared.Services;

public interface IAiCoreApi
{
    Task<List<AiChannel>> ListChannelsAsync(CancellationToken cancellationToken = default);
    Task CreateChannelAsync(string displayName, string defaultBaseUrl, bool enabled, CancellationToken cancellationToken = default);
    Task DeleteChannelAsync(string id, CancellationToken cancellationToken = default);
    Task<List<AiCredential>> ListCredentialsAsync(CancellationToken cancellationToken = default);
    Task UpsertCredentialAsync(string? id, string channelId, string label, string? baseUrl, string? secret, bool enabled, CancellationToken cancellationToken = default);
    Task DeleteCredentialAsync(string id, CancellationToken cancellationToken = default);
    Task TestCredentialAsync(string credentialId, CancellationToken cancellationToken = default);
    Task<List<AiModelProfile>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<List<AiPresetModel>> ListPresetModelsAsync(CancellationToken cancellationToken = default);
    Task UpsertModelAsync(string? id, string credentialId, string modelId, string displayName, string source, bool enabled, CancellationToken cancellationToken = default);
    Task DeleteModelAsync(string id, CancellationToken cancellationToken = default);
    Task TestModelAsync(string modelProfileId, CancellationToken cancellationToken = default);
    Task<List<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default);
    Task UpsertBindingAsync(string featureId, string modelProfileId, CancellationToken cancellationToken = default);
    Task DeleteBindingAsync(string featureId, CancellationToken cancellationToken = default);
    Task<List<AiConversationSummary>> ListConversationsAsync(CancellationToken cancellationToken = default);
    Task<AiConversation> GetConversationAsync(string id, CancellationToken cancellationToken = default);
    Task RenameConversationAsync(string id, string title, CancellationToken cancellationToken = default);
    Task DeleteConversationAsync(string id, CancellationToken cancellationToken = default);
    Task<AiChatStarted> SendChatAsync(string? conversationId, string credentialId, string modelProfileId, string text, CancellationToken cancellationToken = default);
    Task<AiChatStarted> RegenerateChatAsync(string conversationId, string credentialId, string modelProfileId, CancellationToken cancellationToken = default);
    Task CancelChatAsync(string invocationId, CancellationToken cancellationToken = default);
}

public sealed class AiCoreApi(IAiCoreInvoker client) : IAiCoreApi
{
    public Task<List<AiChannel>> ListChannelsAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync<List<AiChannel>>(AiProtocolMethods.ChannelsList, new { }, cancellationToken);

    public Task CreateChannelAsync(string displayName, string defaultBaseUrl, bool enabled, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ChannelsCreate, new { displayName, defaultBaseUrl, enabled }, cancellationToken);

    public Task DeleteChannelAsync(string id, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ChannelsDelete, new { id }, cancellationToken);

    public Task<List<AiCredential>> ListCredentialsAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync<List<AiCredential>>(AiProtocolMethods.CredentialsList, new { }, cancellationToken);

    public Task UpsertCredentialAsync(string? id, string channelId, string label, string? baseUrl, string? secret, bool enabled, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.CredentialsUpsert, new { id, channelId, label, baseUrl, secret, enabled }, cancellationToken);

    public Task DeleteCredentialAsync(string id, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.CredentialsDelete, new { id }, cancellationToken);

    public Task TestCredentialAsync(string credentialId, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.CredentialsTest, new { credentialId }, cancellationToken);

    public Task<List<AiModelProfile>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync<List<AiModelProfile>>(AiProtocolMethods.ModelsList, new { }, cancellationToken);

    public Task<List<AiPresetModel>> ListPresetModelsAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync<List<AiPresetModel>>(AiProtocolMethods.ModelsPresets, new { }, cancellationToken);

    public Task UpsertModelAsync(string? id, string credentialId, string modelId, string displayName, string source, bool enabled, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ModelsUpsert, new { id, credentialId, modelId, displayName, source, enabled }, cancellationToken);

    public Task DeleteModelAsync(string id, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ModelsDelete, new { id }, cancellationToken);

    public Task TestModelAsync(string modelProfileId, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ModelsTest, new { modelProfileId }, cancellationToken);

    public Task<List<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync<List<AiBinding>>(AiProtocolMethods.BindingsList, new { }, cancellationToken);

    public Task UpsertBindingAsync(string featureId, string modelProfileId, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.BindingsUpsert, new { featureId, modelProfileId }, cancellationToken);

    public Task DeleteBindingAsync(string featureId, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.BindingsDelete, new { featureId }, cancellationToken);

    public Task<List<AiConversationSummary>> ListConversationsAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync<List<AiConversationSummary>>(AiProtocolMethods.ConversationsList, new { }, cancellationToken);

    public Task<AiConversation> GetConversationAsync(string id, CancellationToken cancellationToken = default) =>
        client.InvokeAsync<AiConversation>(AiProtocolMethods.ConversationsGet, new { id }, cancellationToken);

    public Task RenameConversationAsync(string id, string title, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ConversationsRename, new { id, title }, cancellationToken);

    public Task DeleteConversationAsync(string id, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ConversationsDelete, new { id }, cancellationToken);

    public Task<AiChatStarted> SendChatAsync(string? conversationId, string credentialId, string modelProfileId, string text, CancellationToken cancellationToken = default) =>
        client.InvokeAsync<AiChatStarted>(AiProtocolMethods.ChatSend, new { conversationId, credentialId, modelProfileId, text }, cancellationToken);

    public Task<AiChatStarted> RegenerateChatAsync(string conversationId, string credentialId, string modelProfileId, CancellationToken cancellationToken = default) =>
        client.InvokeAsync<AiChatStarted>(AiProtocolMethods.ChatRegenerate, new { conversationId, credentialId, modelProfileId }, cancellationToken);

    public Task CancelChatAsync(string invocationId, CancellationToken cancellationToken = default) =>
        InvokeCommandAsync(AiProtocolMethods.ChatCancel, new { invocationId }, cancellationToken);

    private async Task InvokeCommandAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        _ = await client.InvokeAsync<JsonElement>(method, parameters, cancellationToken);
    }
}
