using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using System.Collections.Immutable;

namespace Fowan.Ai.Shared.Application;

public enum AiChatLifecycleState { Idle, Estimating, Compacting, Generating, Cancelling, Refreshing }

public interface IAiChatCommands
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task RefreshConversationsAsync(CancellationToken cancellationToken = default);
    void StartNewConversation();
    void SelectConversation(string conversationId);
    void BeginGeneration();
    bool AcceptInvocation(AiChatStarted started);
    void AdoptInvocation(string invocationId);
    void AdoptInvocation(AiChatStarted started);
    string AppendDelta(string delta);
    void CompleteInvocation();
    bool FinishInvocation(string invocationId);
    Task CancelAsync(string invocationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default);
    Task<AiConversation> GetConversationAsync(string id, CancellationToken cancellationToken = default);
    Task RenameConversationAsync(string id, string title, CancellationToken cancellationToken = default);
    Task DeleteConversationAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> EnsureConsentAsync(string endpoint, Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default);
    Task<AiConsentExecution<AiChatStarted>> SendAsync(AiCredential credential, AiModelProfile model,
        string? conversationId, string text, Func<string, Task<bool>> confirmAsync, string? parentMessageId = null,
        CancellationToken cancellationToken = default);
    Task<AiConsentExecution<AiChatStarted>> RegenerateAsync(AiCredential credential, AiModelProfile model,
        string conversationId, Func<string, Task<bool>> confirmAsync, string? userMessageId = null,
        CancellationToken cancellationToken = default);
}

public sealed class AiChatSession(IAiCoreApi api, AiConsentCoordinator consent) : IAsyncDisposable, IAiChatCommands
{
    private AiCoreClient? _ownedClient;

    public AiChatSession(AiCoreClient client)
        : this(new AiCoreApi(client), new AiConsentCoordinator(client))
    {
        _ownedClient = client;
    }

    private ImmutableArray<AiChannel> _channels = [];
    private ImmutableArray<AiCredential> _credentials = [];
    private ImmutableArray<AiModelProfile> _models = [];
    private ImmutableArray<AiConversationSummary> _conversations = [];

    public string? CurrentConversationId { get; private set; }
    public string? ActiveInvocationId { get; private set; }
    public string StreamingContent { get; private set; } = string.Empty;
    public bool IsGenerating { get; private set; }
    public AiChatLifecycleState LifecycleState { get; private set; } = AiChatLifecycleState.Idle;
    private string? CompletedInvocationId { get; set; }

    public event EventHandler<AiChatSnapshot>? StateChanged;

    public Func<AiCoreNotificationEventArgs, CancellationToken, Task>? NotificationAsync
    {
        set
        {
            if (_ownedClient is null) throw new InvalidOperationException("This session does not own a Core connection.");
            _ownedClient.NotificationAsync = value;
        }
    }

    public AiChatSnapshot State => new(
        _channels, _credentials, _models, _conversations,
        CurrentConversationId, ActiveInvocationId, StreamingContent, IsGenerating, LifecycleState);

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        OwnedClient().ConnectAsync(["ai.chat.v1", "ai.chat.context.v1", "ai.chat.branching.v1"], cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_ownedClient is null) return;
        _ownedClient.NotificationAsync = null;
        await _ownedClient.DisposeAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        LifecycleState = AiChatLifecycleState.Refreshing;
        Publish();
        try
        {
            var channels = (await api.ListChannelsAsync(cancellationToken)).ToImmutableArray();
            var credentials = (await api.ListCredentialsAsync(cancellationToken)).ToImmutableArray();
            var models = (await api.ListModelsAsync(cancellationToken)).ToImmutableArray();
            var conversations = (await api.ListConversationsAsync(cancellationToken)).ToImmutableArray();
            _channels = channels;
            _credentials = credentials;
            _models = models;
            _conversations = conversations;
        }
        finally
        {
            LifecycleState = AiChatLifecycleState.Idle;
            Publish();
        }
    }

    public async Task RefreshConversationsAsync(CancellationToken cancellationToken = default)
    {
        LifecycleState = AiChatLifecycleState.Refreshing;
        Publish();
        try { _conversations = (await api.ListConversationsAsync(cancellationToken)).ToImmutableArray(); }
        finally
        {
            LifecycleState = AiChatLifecycleState.Idle;
            Publish();
        }
    }

    public void StartNewConversation()
    {
        CurrentConversationId = null;
        CompleteInvocation();
    }

    public void SelectConversation(string conversationId)
    {
        CurrentConversationId = conversationId;
        CompleteInvocation();
    }

    public void BeginGeneration()
    {
        StreamingContent = string.Empty;
        ActiveInvocationId = null;
        CompletedInvocationId = null;
        IsGenerating = true;
        LifecycleState = AiChatLifecycleState.Generating;
        Publish();
    }

    public bool AcceptInvocation(AiChatStarted started)
    {
        CurrentConversationId = started.ConversationId;
        var completedBeforeResponse = string.Equals(CompletedInvocationId, started.InvocationId, StringComparison.Ordinal);
        if (completedBeforeResponse)
        {
            ActiveInvocationId = null;
            StreamingContent = string.Empty;
            IsGenerating = false;
            LifecycleState = AiChatLifecycleState.Idle;
        }
        else
        {
            ActiveInvocationId = started.InvocationId;
            IsGenerating = true;
            LifecycleState = AiChatLifecycleState.Generating;
        }
        Publish();
        return completedBeforeResponse;
    }

    public void AdoptInvocation(string invocationId)
    {
        if (string.Equals(CompletedInvocationId, invocationId, StringComparison.Ordinal))
        {
            return;
        }
        ActiveInvocationId ??= invocationId;
        Publish();
    }

    public void AdoptInvocation(AiChatStarted started)
    {
        ArgumentNullException.ThrowIfNull(started);
        CurrentConversationId = started.ConversationId;
        AdoptInvocation(started.InvocationId);
    }

    public string AppendDelta(string delta)
    {
        StreamingContent += delta;
        Publish();
        return StreamingContent;
    }

    public void CompleteInvocation()
    {
        ActiveInvocationId = null;
        StreamingContent = string.Empty;
        IsGenerating = false;
        LifecycleState = AiChatLifecycleState.Idle;
        CompletedInvocationId = null;
        Publish();
    }

    public bool FinishInvocation(string invocationId)
    {
        if (!IsGenerating || (ActiveInvocationId is not null &&
            !string.Equals(ActiveInvocationId, invocationId, StringComparison.Ordinal)))
        {
            return false;
        }

        ActiveInvocationId = null;
        StreamingContent = string.Empty;
        IsGenerating = false;
        LifecycleState = AiChatLifecycleState.Idle;
        CompletedInvocationId = invocationId;
        Publish();
        return true;
    }

    private void Publish() => StateChanged?.Invoke(this, State);

    private AiCoreClient OwnedClient() => _ownedClient ??
        throw new InvalidOperationException("This session does not own a Core connection.");

    public async Task<IReadOnlyList<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default) =>
        await api.ListBindingsAsync(cancellationToken);

    public Task<AiConversation> GetConversationAsync(string id, CancellationToken cancellationToken = default) =>
        api.GetConversationAsync(id, cancellationToken);

    public async Task<AiMessagePage> ListMessagesAsync(string conversationId, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        LifecycleState = AiChatLifecycleState.Refreshing;
        Publish();
        try { return await api.ListMessagesAsync(conversationId, cursor: cursor, cancellationToken: cancellationToken); }
        finally
        {
            LifecycleState = AiChatLifecycleState.Idle;
            Publish();
        }
    }

    public Task RenameConversationAsync(string id, string title, CancellationToken cancellationToken = default) =>
        api.RenameConversationAsync(id, title, cancellationToken);

    public Task DeleteConversationAsync(string id, CancellationToken cancellationToken = default) =>
        api.DeleteConversationAsync(id, cancellationToken);

    public Task SelectBranchAsync(string conversationId, string leafMessageId, CancellationToken cancellationToken = default) =>
        api.SelectBranchAsync(conversationId, leafMessageId, cancellationToken);

    public async Task CancelAsync(string invocationId, CancellationToken cancellationToken = default)
    {
        var previousState = LifecycleState;
        LifecycleState = AiChatLifecycleState.Cancelling;
        Publish();
        try { await api.CancelChatAsync(invocationId, cancellationToken); }
        catch
        {
            LifecycleState = previousState;
            Publish();
            throw;
        }
    }

    public async Task<AiContextEstimate> EstimateContextAsync(AiModelProfile model, string draft, string? branchLeafMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!model.LimitsConfigured) throw new AiCoreException("conflict", "模型限制待配置，补全后才能发送。");
        if (LifecycleState != AiChatLifecycleState.Idle)
            throw new InvalidOperationException("当前对话正忙，暂时不能重新估算上下文。");
        LifecycleState = AiChatLifecycleState.Estimating;
        Publish();
        try { return await api.EstimateContextAsync(CurrentConversationId, model.Id, branchLeafMessageId, draft, cancellationToken); }
        finally { if (LifecycleState == AiChatLifecycleState.Estimating) { LifecycleState = AiChatLifecycleState.Idle; Publish(); } }
    }

    public Task<bool> EnsureConsentAsync(
        string endpoint,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default) =>
        consent.EnsureGrantedAsync(endpoint, confirmAsync, cancellationToken);

    public async Task<AiCompactInvocation> CompactAsync(AiCredential credential, AiModelProfile model,
        string conversationId, string? branchLeafMessageId = null, CancellationToken cancellationToken = default)
    {
        LifecycleState = AiChatLifecycleState.Compacting;
        Publish();
        try
        {
            var result = await api.CompactContextAsync(conversationId, credential.Id, model.Id, branchLeafMessageId, cancellationToken);
            ActiveInvocationId = result.InvocationId;
            Publish();
            return result;
        }
        catch
        {
            LifecycleState = AiChatLifecycleState.Idle;
            Publish();
            throw;
        }
    }

    public Task<AiConsentExecution<AiChatStarted>> SendAsync(
        AiCredential credential,
        AiModelProfile model,
        string? conversationId,
        string text,
        Func<string, Task<bool>> confirmAsync,
        string? parentMessageId = null,
        CancellationToken cancellationToken = default) =>
        consent.TryExecuteAsync(
            credential.BaseUrl,
            confirmAsync,
            token => api.SendChatAsync(conversationId, credential.Id, model.Id, text, parentMessageId, token),
            cancellationToken);

    public Task<AiConsentExecution<AiChatStarted>> RegenerateAsync(
        AiCredential credential,
        AiModelProfile model,
        string conversationId,
        Func<string, Task<bool>> confirmAsync,
        string? userMessageId = null,
        CancellationToken cancellationToken = default) =>
        consent.TryExecuteAsync(
            credential.BaseUrl,
            confirmAsync,
            token => api.RegenerateChatAsync(conversationId, credential.Id, model.Id, userMessageId, token),
            cancellationToken);
}

public sealed record AiChatSnapshot(
    ImmutableArray<AiChannel> Channels,
    ImmutableArray<AiCredential> Credentials,
    ImmutableArray<AiModelProfile> Models,
    ImmutableArray<AiConversationSummary> Conversations,
    string? CurrentConversationId,
    string? ActiveInvocationId,
    string StreamingContent,
    bool IsGenerating,
    AiChatLifecycleState LifecycleState);
