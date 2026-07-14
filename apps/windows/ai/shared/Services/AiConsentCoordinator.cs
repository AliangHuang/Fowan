using Fowan.Ai.Shared.Models;

namespace Fowan.Ai.Shared.Services;

public readonly record struct AiConsentExecution<T>(bool Executed, T? Value);

public sealed class AiConsentCoordinator(IAiCoreInvoker client)
{
    public async Task<bool> EnsureGrantedAsync(
        string endpoint,
        Func<string, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default)
    {
        var consent = await client.InvokeAsync<AiConsent>(
            AiProtocolMethods.ConsentsCheck,
            new { endpoint },
            cancellationToken);
        if (consent.Granted)
        {
            return true;
        }

        endpoint = consent.Endpoint ?? endpoint;
        if (!await confirmAsync(endpoint))
        {
            return false;
        }

        await client.InvokeAsync<AiConsent>(
            AiProtocolMethods.ConsentsGrant,
            new { endpoint },
            cancellationToken);
        return true;
    }

    public async Task<AiConsentExecution<T>> TryExecuteAsync<T>(
        string endpoint,
        Func<string, Task<bool>> confirmAsync,
        Func<CancellationToken, Task<T>> operationAsync,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureGrantedAsync(endpoint, confirmAsync, cancellationToken))
        {
            return new(false, default);
        }

        try
        {
            return new(true, await operationAsync(cancellationToken));
        }
        catch (AiCoreException exception) when (
            exception.Code == "consent_required" &&
            !string.IsNullOrWhiteSpace(exception.Endpoint))
        {
            if (!await EnsureGrantedAsync(exception.Endpoint!, confirmAsync, cancellationToken))
            {
                return new(false, default);
            }
            return new(true, await operationAsync(cancellationToken));
        }
    }
}
