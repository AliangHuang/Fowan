using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using System.Collections.Immutable;

namespace Fowan.Ai.Shared.Application;

public interface IAiConfigCommands
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task CreateChannelAsync(string name, string endpoint, CancellationToken cancellationToken = default);
    Task DeleteChannelAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertCredentialAsync(string? id, string channelId, string label, string endpoint, string? secret,
        CancellationToken cancellationToken = default);
    Task DeleteCredentialAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertModelAsync(string? id, string credentialId, string modelId, string displayName, string source,
        CancellationToken cancellationToken = default);
    Task DeleteModelAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertBindingAsync(string modelProfileId, CancellationToken cancellationToken = default);
    Task DeleteBindingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default);
    Task<AiConsentExecution<bool>> TestCredentialAsync(AiCredential credential, Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default);
    Task<AiConsentExecution<bool>> TestModelAsync(AiCredential credential, AiModelProfile model,
        Func<string, Task<bool>> confirmAsync, CancellationToken cancellationToken = default);
}

public sealed class AiConfigSession(IAiCoreApi api, AiConsentCoordinator consent) : IAsyncDisposable, IAiConfigCommands
{
    private AiCoreClient? _ownedClient;

    public AiConfigSession(AiCoreClient client)
        : this(new AiCoreApi(client), new AiConsentCoordinator(client))
    {
        _ownedClient = client;
    }

    private ImmutableArray<AiChannel> _channels = [];
    private ImmutableArray<AiCredential> _credentials = [];
    private ImmutableArray<AiModelProfile> _models = [];
    private ImmutableArray<AiPresetModel> _presets = [];

    public event EventHandler<AiConfigSnapshot>? StateChanged;
    public event EventHandler<AiConfigMutation>? MutationCompleted;

    public AiConfigSnapshot State => new(_channels, _credentials, _models, _presets);

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        OwnedClient().ConnectAsync(["ai.config.v1"], cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_ownedClient is not null) await _ownedClient.DisposeAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var channels = (await api.ListChannelsAsync(cancellationToken)).ToImmutableArray();
        var credentials = (await api.ListCredentialsAsync(cancellationToken)).ToImmutableArray();
        var models = (await api.ListModelsAsync(cancellationToken)).ToImmutableArray();
        var presets = (await api.ListPresetModelsAsync(cancellationToken)).ToImmutableArray();
        _channels = channels;
        _credentials = credentials;
        _models = models;
        _presets = presets;
        StateChanged?.Invoke(this, State);
    }

    public async Task<IReadOnlyList<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default) =>
        await api.ListBindingsAsync(cancellationToken);

    public Task CreateChannelAsync(string name, string endpoint, CancellationToken cancellationToken = default) =>
        MutateAsync("channel", "create", null,
            () => api.CreateChannelAsync(name, endpoint, true, cancellationToken));

    public Task DeleteChannelAsync(string id, CancellationToken cancellationToken = default) =>
        MutateAsync("channel", "delete", id, () => api.DeleteChannelAsync(id, cancellationToken));

    public Task UpsertCredentialAsync(
        string? id,
        string channelId,
        string label,
        string endpoint,
        string? secret,
        CancellationToken cancellationToken = default) =>
        MutateAsync("credential", "upsert", id,
            () => api.UpsertCredentialAsync(id, channelId, label, endpoint, secret, true, cancellationToken));

    public Task DeleteCredentialAsync(string id, CancellationToken cancellationToken = default) =>
        MutateAsync("credential", "delete", id, () => api.DeleteCredentialAsync(id, cancellationToken));

    public Task UpsertModelAsync(
        string? id,
        string credentialId,
        string modelId,
        string displayName,
        string source,
        CancellationToken cancellationToken = default) =>
        MutateAsync("model", "upsert", id,
            () => api.UpsertModelAsync(id, credentialId, modelId, displayName, source, true, cancellationToken));

    public Task DeleteModelAsync(string id, CancellationToken cancellationToken = default) =>
        MutateAsync("model", "delete", id, () => api.DeleteModelAsync(id, cancellationToken));

    public Task UpsertBindingAsync(string modelProfileId, CancellationToken cancellationToken = default) =>
        MutateAsync("binding", "upsert", "ai.chat",
            () => api.UpsertBindingAsync("ai.chat", modelProfileId, cancellationToken));

    public Task DeleteBindingAsync(CancellationToken cancellationToken = default) =>
        MutateAsync("binding", "delete", "ai.chat",
            () => api.DeleteBindingAsync("ai.chat", cancellationToken));

    public Task<AiConsentExecution<bool>> TestCredentialAsync(
        AiCredential credential,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default) =>
        consent.TryExecuteAsync(
            credential.BaseUrl,
            confirmAsync,
            token => ExecuteAsync(api.TestCredentialAsync(credential.Id, token)),
            cancellationToken);

    public Task<AiConsentExecution<bool>> TestModelAsync(
        AiCredential credential,
        AiModelProfile model,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default) =>
        consent.TryExecuteAsync(
            credential.BaseUrl,
            confirmAsync,
            token => ExecuteAsync(api.TestModelAsync(model.Id, token)),
            cancellationToken);

    private static async Task<bool> ExecuteAsync(Task operation)
    {
        await operation;
        return true;
    }

    private async Task MutateAsync(string component, string action, string? id, Func<Task> operation)
    {
        await operation();
        MutationCompleted?.Invoke(this, new AiConfigMutation(component, action, id));
    }

    private AiCoreClient OwnedClient() => _ownedClient ??
        throw new InvalidOperationException("This session does not own a Core connection.");
}

public sealed record AiConfigSnapshot(
    ImmutableArray<AiChannel> Channels,
    ImmutableArray<AiCredential> Credentials,
    ImmutableArray<AiModelProfile> Models,
    ImmutableArray<AiPresetModel> Presets);

public sealed record AiConfigMutation(string Component, string Action, string? Id);
