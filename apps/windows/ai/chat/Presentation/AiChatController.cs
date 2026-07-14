using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal sealed class AiChatController(IAiCoreApi api, AiConsentCoordinator consent)
{
    public List<AiChannel> Channels { get; } = [];
    public List<AiCredential> Credentials { get; } = [];
    public List<AiModelProfile> Models { get; } = [];
    public List<AiConversationSummary> Conversations { get; } = [];

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Channels.ReplaceWith(await api.ListChannelsAsync(cancellationToken));
        Credentials.ReplaceWith(await api.ListCredentialsAsync(cancellationToken));
        Models.ReplaceWith(await api.ListModelsAsync(cancellationToken));
        await RefreshConversationsAsync(cancellationToken);
    }

    public async Task RefreshConversationsAsync(CancellationToken cancellationToken = default) =>
        Conversations.ReplaceWith(await api.ListConversationsAsync(cancellationToken));

    public Task<IReadOnlyList<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default) =>
        ListBindingsCoreAsync(cancellationToken);

    public Task<AiConversation> GetConversationAsync(string id, CancellationToken cancellationToken = default) =>
        api.GetConversationAsync(id, cancellationToken);

    public Task RenameConversationAsync(string id, string title, CancellationToken cancellationToken = default) =>
        api.RenameConversationAsync(id, title, cancellationToken);

    public Task DeleteConversationAsync(string id, CancellationToken cancellationToken = default) =>
        api.DeleteConversationAsync(id, cancellationToken);

    public Task CancelAsync(string invocationId, CancellationToken cancellationToken = default) =>
        api.CancelChatAsync(invocationId, cancellationToken);

    public Task<bool> EnsureConsentAsync(
        string endpoint,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default) =>
        consent.EnsureGrantedAsync(endpoint, confirmAsync, cancellationToken);

    public Task<AiConsentExecution<AiChatStarted>> SendAsync(
        AiCredential credential,
        AiModelProfile model,
        string? conversationId,
        string text,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default) =>
        consent.TryExecuteAsync(
            credential.BaseUrl,
            confirmAsync,
            token => api.SendChatAsync(conversationId, credential.Id, model.Id, text, token),
            cancellationToken);

    public Task<AiConsentExecution<AiChatStarted>> RegenerateAsync(
        AiCredential credential,
        AiModelProfile model,
        string conversationId,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default) =>
        consent.TryExecuteAsync(
            credential.BaseUrl,
            confirmAsync,
            token => api.RegenerateChatAsync(conversationId, credential.Id, model.Id, token),
            cancellationToken);

    private async Task<IReadOnlyList<AiBinding>> ListBindingsCoreAsync(CancellationToken cancellationToken) =>
        await api.ListBindingsAsync(cancellationToken);
}

internal static class ListReplacementExtensions
{
    public static void ReplaceWith<T>(this List<T> target, IEnumerable<T> values)
    {
        target.Clear();
        target.AddRange(values);
    }
}
