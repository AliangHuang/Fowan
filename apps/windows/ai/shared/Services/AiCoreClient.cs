using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Application.Ports;
using System.Collections.Concurrent;
using System.Text;
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
    private readonly string _pipeName = ResolvePipeName();
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
    {
        if (handshake.ValueKind != JsonValueKind.Object ||
            handshake.EnumerateObject().Count() != 3 ||
            !handshake.TryGetProperty("engineVersion", out var engineVersion) ||
            engineVersion.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(engineVersion.GetString()) ||
            !handshake.TryGetProperty("protocolVersion", out var protocolVersion) ||
            protocolVersion.ValueKind != JsonValueKind.String ||
            protocolVersion.GetString() != AiProtocolContract.Version ||
            !handshake.TryGetProperty("capabilities", out var capabilityList) ||
            capabilityList.ValueKind != JsonValueKind.Array)
        {
            throw new AiCoreException("protocol_mismatch", "Fowan Core protocol version is not supported.");
        }

        var capabilityValues = capabilityList.EnumerateArray().ToArray();
        if (capabilityValues.Any(item =>
                item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString())))
        {
            throw new AiCoreException(
                "protocol_mismatch",
                "Fowan Core returned an invalid capability list.");
        }
        var capabilities = capabilityValues
            .Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        if (capabilities.Count != capabilityValues.Length ||
            capabilities.Any(capability => !AiProtocolContract.Capabilities.Contains(
                capability,
                StringComparer.Ordinal)))
        {
            throw new AiCoreException(
                "protocol_mismatch",
                "Fowan Core returned an invalid capability list.");
        }
        var missing = requiredCapabilities
            .Where(capability => !capabilities.Contains(capability))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new AiCoreException(
                "protocol_mismatch",
                $"Fowan Core is missing required capabilities: {string.Join(", ", missing)}.");
        }
    }

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

    public static string? ResolveCorePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("FOWAN_CORE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, "Core", "fowan-core.exe"),
            Path.Combine(baseDirectory, "fowan-core.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "Core", "fowan-core.exe"))
        };
        var directory = new DirectoryInfo(baseDirectory);
        for (var level = 0; level < 7 && directory is not null; level++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "out", "core", "windows", "win-x64", "debug", "fowan-core.exe"));
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "out", "core", "windows", "win-x64", "release", "fowan-core.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ResolvePipeName()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fowan",
            "Core");
        Directory.CreateDirectory(root);
        var tokenPath = Path.Combine(root, "pipe-token");
        string token;
        try
        {
            token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : string.Empty;
            if (token.Length != 32 || token.Any(character => !Uri.IsHexDigit(character)))
            {
                token = Guid.NewGuid().ToString("N");
                File.WriteAllText(tokenPath, token);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AiCoreException("secret_store_unavailable", "The per-user Core endpoint could not be initialized.");
        }

        return $"fowan-core-v1-{token}";
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        AiCoreException? terminalFailure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipe?.IsConnected == true)
            {
                var message = await ReadFrameAsync(cancellationToken);
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

    private async Task<JsonElement?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw new EndOfStreamException();
        var header = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await pipe.ReadAsync(one, cancellationToken);
            if (read == 0)
            {
                return null;
            }

            header.Add(one[0]);
            if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n')
            {
                break;
            }

            if (header.Count > AiProtocolContract.MaximumHeaderBytes)
            {
                throw new InvalidDataException("Protocol header is too large.");
            }
        }

        var headerText = Encoding.ASCII.GetString([.. header]);
        var headerLines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (headerLines.Length != 1 ||
            !headerLines[0].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(headerLines[0]["Content-Length:".Length..].Trim(), out var length) ||
            length is < 0 or > AiProtocolContract.MaximumFrameBytes)
        {
            throw new InvalidDataException("Protocol content length is invalid.");
        }

        var body = new byte[length];
        await ReadExactlyAsync(pipe, body, cancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    internal static void ValidateIncomingEnvelope(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("jsonrpc", out var version) ||
            version.ValueKind != JsonValueKind.String ||
            version.GetString() != "2.0")
        {
            throw new InvalidDataException("Fowan Core returned an invalid JSON-RPC envelope.");
        }

        var propertyCount = message.EnumerateObject().Count();
        if (message.TryGetProperty("id", out var id))
        {
            var validId = id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var requestId) && requestId > 0;
            var hasResult = message.TryGetProperty("result", out _);
            var hasError = message.TryGetProperty("error", out var error);
            if (!validId || hasResult == hasError || propertyCount != 3 ||
                (hasError && (error.ValueKind != JsonValueKind.Object ||
                              error.EnumerateObject().Count() is < 2 or > 3 ||
                              !error.TryGetProperty("code", out var code) ||
                              code.ValueKind != JsonValueKind.String ||
                              !AiProtocolErrors.All.Contains(code.GetString() ?? string.Empty) ||
                              !error.TryGetProperty("message", out var errorMessage) ||
                              errorMessage.ValueKind != JsonValueKind.String ||
                              (error.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Object))))
            {
                throw new InvalidDataException("Fowan Core returned an invalid JSON-RPC response.");
            }
            return;
        }

        if (propertyCount != 3 ||
            !message.TryGetProperty("method", out var method) ||
            method.ValueKind != JsonValueKind.String ||
            !AiProtocolNotifications.All.Contains(method.GetString() ?? string.Empty) ||
            !message.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Fowan Core returned an invalid JSON-RPC notification.");
        }
    }

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
        var body = Encoding.UTF8.GetBytes(message.ToJsonString(JsonOptions));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var pipe = _pipe ?? throw new EndOfStreamException();
            await pipe.WriteAsync(header, cancellationToken);
            await pipe.WriteAsync(body, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
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

    private static async Task ReadExactlyAsync(
        IAiCoreTransport transport,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await transport.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
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
