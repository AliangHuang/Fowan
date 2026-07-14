using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;

namespace Fowan.Ai.Config.Windows.Presentation;

internal sealed class AiConfigController(IAiCoreApi api, AiConsentCoordinator consent)
{
    public List<AiChannel> Channels { get; } = [];
    public List<AiCredential> Credentials { get; } = [];
    public List<AiModelProfile> Models { get; } = [];
    public List<AiPresetModel> Presets { get; } = [];

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Replace(Channels, await api.ListChannelsAsync(cancellationToken));
        Replace(Credentials, await api.ListCredentialsAsync(cancellationToken));
        Replace(Models, await api.ListModelsAsync(cancellationToken));
        Replace(Presets, await api.ListPresetModelsAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<AiBinding>> ListBindingsAsync(CancellationToken cancellationToken = default) =>
        await api.ListBindingsAsync(cancellationToken);

    public Task CreateChannelAsync(string name, string endpoint, CancellationToken cancellationToken = default) =>
        api.CreateChannelAsync(name, endpoint, true, cancellationToken);

    public Task DeleteChannelAsync(string id, CancellationToken cancellationToken = default) =>
        api.DeleteChannelAsync(id, cancellationToken);

    public Task UpsertCredentialAsync(string? id, string channelId, string label, string endpoint, string? secret, CancellationToken cancellationToken = default) =>
        api.UpsertCredentialAsync(id, channelId, label, endpoint, secret, true, cancellationToken);

    public Task DeleteCredentialAsync(string id, CancellationToken cancellationToken = default) =>
        api.DeleteCredentialAsync(id, cancellationToken);

    public Task UpsertModelAsync(string? id, string credentialId, string modelId, string displayName, string source, CancellationToken cancellationToken = default) =>
        api.UpsertModelAsync(id, credentialId, modelId, displayName, source, true, cancellationToken);

    public Task DeleteModelAsync(string id, CancellationToken cancellationToken = default) =>
        api.DeleteModelAsync(id, cancellationToken);

    public Task UpsertBindingAsync(string modelProfileId, CancellationToken cancellationToken = default) =>
        api.UpsertBindingAsync("ai.chat", modelProfileId, cancellationToken);

    public Task DeleteBindingAsync(CancellationToken cancellationToken = default) =>
        api.DeleteBindingAsync("ai.chat", cancellationToken);

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

    private static void Replace<T>(List<T> target, IEnumerable<T> values)
    {
        target.Clear();
        target.AddRange(values);
    }

    private static async Task<bool> ExecuteAsync(Task operation)
    {
        await operation;
        return true;
    }
}
