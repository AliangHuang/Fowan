
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fowan.Ai.Shared.Services;

public sealed class AiCoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AiCoreNotificationEventArgs(string method, JsonElement parameters) : EventArgs
{
    public string Method { get; } = method;
    public JsonElement Parameters { get; } = parameters;
}

public sealed class AiCoreClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private int _nextRequestId;
    private readonly string _pipeName = ResolvePipeName();

    public event EventHandler<AiCoreNotificationEventArgs>? Notification;

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(
        IEnumerable<string> requiredCapabilities,
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        if (!await TryConnectAsync(250, cancellationToken))
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

        _readerCts = new CancellationTokenSource();
        _readerTask = ReadLoopAsync(_readerCts.Token);
        var handshake = await InvokeAsync<JsonElement>("engine.handshake", new { }, cancellationToken);
        ValidateHandshake(handshake, requiredCapabilities);
    }

    public static void ValidateHandshake(JsonElement handshake, IEnumerable<string> requiredCapabilities)
    {
        if (handshake.GetProperty("protocolVersion").GetString() != "0.1")
        {
            throw new AiCoreException("protocol_mismatch", "Fowan Core protocol version is not supported.");
        }

        var capabilities = handshake.GetProperty("capabilities")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
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

        var id = Interlocked.Increment(ref _nextRequestId);
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
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
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

    private static void StartCoreProcess()
    {
        var path = ResolveCorePath();
        if (path is null)
        {
            throw new AiCoreException("provider_unavailable", "fowan-core.exe was not found.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
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
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "target", "debug", "fowan-core.exe"));
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "target", "release", "fowan-core.exe"));
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
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipe?.IsConnected == true)
            {
                var message = await ReadFrameAsync(cancellationToken);
                if (message is null)
                {
                    break;
                }

                if (message.Value.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id) &&
                    _pending.TryGetValue(id, out var completion))
                {
                    if (message.Value.TryGetProperty("error", out var error))
                    {
                        completion.TrySetException(new AiCoreException(
                            error.GetProperty("code").GetString() ?? "internal_error",
                            error.GetProperty("message").GetString() ?? "Fowan Core request failed."));
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
                    Notification?.Invoke(this, new AiCoreNotificationEventArgs(
                        method.GetString() ?? string.Empty,
                        parameters.Clone()));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or JsonException)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new AiCoreException("provider_unavailable", "Fowan Core disconnected."));
            }
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

            if (header.Count > 8192)
            {
                throw new InvalidDataException("Protocol header is too large.");
            }
        }

        var headerText = Encoding.ASCII.GetString([.. header]);
        var contentLengthLine = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        if (contentLengthLine is null || !int.TryParse(contentLengthLine["Content-Length:".Length..].Trim(), out var length) ||
            length is < 0 or > 8 * 1024 * 1024)
        {
            throw new InvalidDataException("Protocol content length is invalid.");
        }

        var body = new byte[length];
        await pipe.ReadExactlyAsync(body, cancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
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
        _readerCts?.Cancel();
        if (_readerTask is not null)
        {
            try
            {
                await _readerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _readerCts?.Dispose();
        _pipe?.Dispose();
        _writeLock.Dispose();
    }
}
