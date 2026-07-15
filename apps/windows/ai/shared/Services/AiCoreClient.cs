using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Application.Ports;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fowan.Ai.Shared.Services;

public sealed class AiCoreException(string code, string message, JsonElement? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public JsonElement? Details { get; } = details;
    public string? Endpoint => Details is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty("endpoint", out var endpoint) &&
        endpoint.ValueKind == JsonValueKind.String
            ? endpoint.GetString()
            : null;
}

public sealed class AiCoreNotificationEventArgs(string method, JsonElement parameters) : EventArgs
{
    public string Method { get; } = method;
    public JsonElement Parameters { get; } = parameters;
}

public interface IAiCoreInvoker
{
    Task<T> InvokeAsync<T>(
        string method,
        object parameters,
        CancellationToken cancellationToken = default);
}

public sealed class AiCoreClient : IAsyncDisposable, IAiCoreInvoker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IAiCoreTransport? _pipe;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private int _nextRequestId;
    private readonly string _pipeName = AiCoreEndpointResolver.ResolvePipeName();
    private readonly HashSet<string> _validatedCapabilities = new(StringComparer.Ordinal);
    private readonly IAiCoreProcessLauncher _processLauncher;
    private readonly IAiCoreTransportFactory _transportFactory;

    public AiCoreClient(
        IAiCoreProcessLauncher processLauncher,
        IAiCoreTransportFactory? transportFactory = null)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        _processLauncher = processLauncher;
        _transportFactory = transportFactory ?? new NamedPipeAiCoreTransportFactory();
    }

    public event EventHandler<AiCoreNotificationEventArgs>? Notification;
    public Func<AiCoreNotificationEventArgs, CancellationToken, Task>? NotificationAsync { get; set; }

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(
        IEnumerable<string> requiredCapabilities,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var required = requiredCapabilities.Distinct(StringComparer.Ordinal).ToArray();
            if (IsConnected && required.All(_validatedCapabilities.Contains))
            {
                return;
            }

            if (!IsConnected && _readerTask is not null)
            {
                await ResetConnectionAsync();
            }

            if (!IsConnected && !await TryConnectAsync(250, cancellationToken))
            {
                StartCoreProcess();
                var connected = false;
                for (var attempt = 0; attempt < 30 && !connected; attempt++)
                {
                    await Task.Delay(100, cancellationToken);
                    connected = await TryConnectAsync(250, cancellationToken);
                }

                if (!connected)
                {
                    throw new AiCoreException("provider_unavailable", "Fowan Core could not be started.");
                }
            }

            if (_readerTask is null || _readerTask.IsCompleted)
            {
                _readerCts?.Dispose();
                _readerCts = new CancellationTokenSource();
                _readerTask = ReadLoopAsync(_readerCts.Token);
            }
            try
            {
                var handshake = await InvokeAsync<JsonElement>(AiProtocolMethods.EngineHandshake, new
                {
                    protocolVersion = AiProtocolContract.Version,
                    requiredCapabilities = required
                }, cancellationToken);
                ValidateHandshake(handshake, required);
                _validatedCapabilities.UnionWith(required);
            }
            catch
            {
                await ResetConnectionAsync();
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public static void ValidateHandshake(JsonElement handshake, IEnumerable<string> requiredCapabilities)
        => AiCoreProtocolValidator.ValidateHandshake(handshake, requiredCapabilities);

    public async Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
    {
        var pipe = _pipe;
        if (pipe?.IsConnected != true)
        {
            throw new AiCoreException("provider_unavailable", "Fowan Core is disconnected.");
        }

        var id = NextRequestId();
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new AiCoreException("internal_error", "Could not allocate a protocol request.");
        }

        try
        {
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = JsonSerializer.SerializeToNode(parameters, JsonOptions)
            };
            await WriteFrameAsync(request, cancellationToken);
            var result = await completion.Task.WaitAsync(cancellationToken);
            if (typeof(T) == typeof(JsonElement))
            {
                return (T)(object)result;
            }

            return result.Deserialize<T>(JsonOptions) ??
                throw new AiCoreException("invalid_response", "Fowan Core returned an empty response.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task<bool> TryConnectAsync(int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        _pipe?.Dispose();
        var pipe = _transportFactory.Create(_pipeName);
        try
        {
            await pipe.ConnectAsync(timeoutMilliseconds, cancellationToken);
            _pipe = pipe;
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            pipe.Dispose();
            return false;
        }
    }

    private void StartCoreProcess()
    {
        var path = ResolveCorePath();
        if (path is null)
        {
            throw new AiCoreException("provider_unavailable", "fowan-core.exe was not found.");
        }

        _processLauncher.Start(path);
    }

    public static string? ResolveCorePath() => AiCoreEndpointResolver.ResolveExecutablePath();

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        AiCoreException? terminalFailure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipe?.IsConnected == true)
            {
                var pipe = _pipe ?? throw new EndOfStreamException();
                var message = await ContentLengthFrameCodec.ReadAsync(pipe, cancellationToken);
                if (message is null)
                {
                    break;
                }
                ValidateIncomingEnvelope(message.Value);

                if (message.Value.TryGetProperty("id", out var idElement))
                {
                    if (!idElement.TryGetInt32(out var id) || id <= 0 || !_pending.TryGetValue(id, out var completion))
                    {
                        throw new InvalidDataException("Fowan Core returned an unexpected response identifier.");
                    }
                    if (message.Value.TryGetProperty("error", out var error))
                    {
                        var details = error.TryGetProperty("data", out var data) ? data.Clone() : (JsonElement?)null;
                        completion.TrySetException(new AiCoreException(
                            error.GetProperty("code").GetString() ?? "internal_error",
                            error.GetProperty("message").GetString() ?? "Fowan Core request failed.",
                            details));
                    }
                    else if (message.Value.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    continue;
                }

                if (message.Value.TryGetProperty("method", out var method) &&
                    message.Value.TryGetProperty("params", out var parameters))
                {
                    var eventArgs = new AiCoreNotificationEventArgs(
                        method.GetString() ?? string.Empty,
                        parameters.Clone());
                    if (NotificationAsync is { } asyncNotification)
                    {
                        try
                        {
                            await asyncNotification(eventArgs, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch
                        {
                            // A subscriber failure must not leave protocol requests pending.
                        }
                    }
                    if (Notification is { } notification)
                    {
                        foreach (EventHandler<AiCoreNotificationEventArgs> handler in notification.GetInvocationList())
                        {
                            try
                            {
                                handler(this, eventArgs);
                            }
                            catch
                            {
                                // A UI subscriber must not terminate the protocol reader.
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            terminalFailure = exception is JsonException or InvalidDataException
                ? new AiCoreException(
                    "invalid_response",
                    "Fowan Core returned an invalid protocol frame.")
                : new AiCoreException(
                    "provider_unavailable",
                    "Fowan Core disconnected unexpectedly.");
        }
        finally
        {
            _pipe?.Dispose();
            _validatedCapabilities.Clear();
            FailPending(terminalFailure ?? new AiCoreException(
                "provider_unavailable",
                "Fowan Core disconnected."));
        }
    }

    internal static void ValidateIncomingEnvelope(JsonElement message)
        => AiCoreProtocolValidator.ValidateIncomingEnvelope(message);

    private int NextRequestId()
    {
        while (true)
        {
            var current = Volatile.Read(ref _nextRequestId);
            var next = current >= int.MaxValue ? 1 : current + 1;
            if (Interlocked.CompareExchange(ref _nextRequestId, next, current) == current)
            {
                return next;
            }
        }
    }

    private async Task WriteFrameAsync(JsonNode message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var pipe = _pipe ?? throw new EndOfStreamException();
            await ContentLengthFrameCodec.WriteAsync(pipe, message, JsonOptions, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            await ResetConnectionAsync();
            _writeLock.Dispose();
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }

    private void FailPending(AiCoreException exception)
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(exception);
        }
    }

    private async Task ResetConnectionAsync()
    {
        var readerTask = _readerTask;
        try
        {
            _readerCts?.Cancel();
            _pipe?.Dispose();
            if (readerTask is not null)
            {
                try
                {
                    await readerTask;
                }
                catch (Exception)
                {
                    // Reader failures are surfaced through pending requests; cleanup must finish.
                }
            }
        }
        finally
        {
            _readerCts?.Dispose();
            _readerCts = null;
            _readerTask = null;
            _pipe = null;
            _validatedCapabilities.Clear();
            FailPending(new AiCoreException(
                "provider_unavailable",
                "Fowan Core is unavailable."));
        }
    }
}
