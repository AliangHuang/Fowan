using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Ai.Shared.Application.Ports;
using Json.Schema;
using System.Text.Json;
using System.Text;
using System.Threading.Channels;
using Xunit;

namespace Fowan.Ai.Shared.Tests;

public sealed class AiSharedTests
{
    private sealed class NoopProcessLauncher : IAiCoreProcessLauncher
    {
        public void Start(string executablePath) => throw new InvalidOperationException("Unexpected process start.");
    }

    private sealed class ScriptedTransportFactory(Func<JsonElement, IEnumerable<string>> responses)
        : IAiCoreTransportFactory
    {
        public IAiCoreTransport Create(string pipeName) => new ScriptedTransport(responses);
    }

    private sealed class ScriptedTransport(Func<JsonElement, IEnumerable<string>> responses)
        : IAiCoreTransport
    {
        private readonly MemoryStream _written = new();
        private readonly Channel<byte[]> _reads = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _currentOffset;

        public bool IsConnected { get; private set; }

        public Task ConnectAsync(int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_current is null || _currentOffset == _current.Length)
            {
                _current = await _reads.Reader.ReadAsync(cancellationToken);
                _currentOffset = 0;
            }
            var count = Math.Min(buffer.Length, _current.Length - _currentOffset);
            _current.AsMemory(_currentOffset, count).CopyTo(buffer);
            _currentOffset += count;
            return count;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            _written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            var bytes = _written.ToArray();
            _written.SetLength(0);
            var separator = Encoding.ASCII.GetBytes("\r\n\r\n");
            var bodyOffset = bytes.AsSpan().IndexOf(separator) + separator.Length;
            using var request = JsonDocument.Parse(bytes.AsMemory(bodyOffset));
            foreach (var response in responses(request.RootElement))
            {
                _reads.Writer.TryWrite(Frame(response));
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsConnected = false;
            _reads.Writer.TryComplete();
        }

        private static byte[] Frame(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            return [.. header, .. body];
        }
    }

    private sealed class ConsentInvoker : IAiCoreInvoker
    {
        private readonly HashSet<string> _granted = new(StringComparer.Ordinal);

        public int GrantCount { get; private set; }

        public void GrantInitially(string endpoint) => _granted.Add(endpoint);

        public Task<T> InvokeAsync<T>(
            string method,
            object parameters,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoint = JsonSerializer.SerializeToElement(parameters).GetProperty("endpoint").GetString()!;
            object result = method switch
            {
                "ai.consents.check" => new AiConsent(_granted.Contains(endpoint), endpoint),
                "ai.consents.grant" => Grant(endpoint),
                _ => throw new InvalidOperationException(method)
            };
            return Task.FromResult((T)result);
        }

        private AiConsent Grant(string endpoint)
        {
            GrantCount++;
            _granted.Add(endpoint);
            return new AiConsent(true, endpoint);
        }
    }

    [Fact]
    public void Credential_display_label_contains_only_name_and_masked_hint()
    {
        var credential = new AiCredential(
            "credential-id",
            "deepseek",
            "工作密钥",
            "https://api.deepseek.com",
            "sk-****1234",
            true,
            null,
            null,
            "2026-07-13T00:00:00Z",
            "2026-07-13T00:00:00Z");

        Assert.Equal("工作密钥  sk-****1234", credential.DisplayLabel);
    }

    [Fact]
    public void Launcher_prefers_explicit_chat_path()
    {
        var executable = Path.GetTempFileName();
        var previous = Environment.GetEnvironmentVariable("FOWAN_AI_CHAT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("FOWAN_AI_CHAT_PATH", executable);
            Assert.Equal(Path.GetFullPath(executable), AiApplicationPathResolver.ResolveExecutable(AiApplication.Chat));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOWAN_AI_CHAT_PATH", previous);
            File.Delete(executable);
        }
    }

    [Fact]
    public void Handshake_accepts_each_application_capability_subset()
    {
        using var document = JsonDocument.Parse(
            """{"engineVersion":"0.1.0","protocolVersion":"0.1","capabilities":["ai.chat.v1","ai.config.v1"]}""");

        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.chat.v1"]);
        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.config.v1"]);
    }

    [Fact]
    public void Handshake_rejects_missing_capability()
    {
        using var document = JsonDocument.Parse(
            """{"engineVersion":"0.1.0","protocolVersion":"0.1","capabilities":["ai.chat.v1"]}""");

        var error = Assert.Throws<AiCoreException>(() =>
            AiCoreClient.ValidateHandshake(document.RootElement, ["ai.config.v1"]));
        Assert.Equal("protocol_mismatch", error.Code);
    }

    [Theory]
    [InlineData("{\"engineVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"capabilities\":[\"ai.chat.v1\",\"ai.chat.v1\"]}")]
    [InlineData("{\"engineVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"capabilities\":[\"\"]}")]
    [InlineData("{\"engineVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"capabilities\":[\"unknown\"]}")]
    [InlineData("{\"engineVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"capabilities\":[],\"extra\":true}")]
    public void Handshake_rejects_non_contract_capability_results(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        var error = Assert.Throws<AiCoreException>(() =>
            AiCoreClient.ValidateHandshake(document.RootElement, []));

        Assert.Equal("protocol_mismatch", error.Code);
    }

    [Fact]
    public void Consent_error_exposes_only_the_normalized_endpoint_detail()
    {
        using var document = JsonDocument.Parse("""{"endpoint":"https://api.example.com/v1"}""");
        var error = new AiCoreException("consent_required", "Consent required", document.RootElement.Clone());

        Assert.Equal("https://api.example.com/v1", error.Endpoint);
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"1.0\",\"id\":1,\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{},\"error\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":0,\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":-1,\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":2147483648,\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1.5,\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"\",\"params\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"ai.chat.unknown\",\"params\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":\"provider_raw_error\",\"message\":\"x\"}}")]
    public void Invalid_core_envelopes_are_rejected(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        Assert.Throws<InvalidDataException>(() =>
            AiCoreClient.ValidateIncomingEnvelope(document.RootElement));
    }

    [Fact]
    public void Valid_core_response_and_notification_envelopes_are_accepted()
    {
        using var response = JsonDocument.Parse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        using var notification = JsonDocument.Parse(
            """{"jsonrpc":"2.0","method":"ai.chat.delta","params":{"delta":"ok"}}""");

        AiCoreClient.ValidateIncomingEnvelope(response.RootElement);
        AiCoreClient.ValidateIncomingEnvelope(notification.RootElement);
    }

    [Fact]
    public async Task Consent_coordinator_retries_a_core_rejection_only_once()
    {
        var invoker = new ConsentInvoker();
        invoker.GrantInitially("https://api.example.com/v1");
        var coordinator = new AiConsentCoordinator(invoker);
        var attempts = 0;

        var execution = await coordinator.TryExecuteAsync(
            "https://api.example.com/v1",
            _ => Task.FromResult(true),
            _ =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<string>(new AiCoreException(
                        "consent_required",
                        "Consent required",
                        JsonSerializer.SerializeToElement(new { endpoint = "https://api.changed.example/v1" })))
                    : Task.FromResult("ok");
            });

        Assert.True(execution.Executed);
        Assert.Equal("ok", execution.Value);
        Assert.Equal(2, attempts);
        Assert.Equal(1, invoker.GrantCount);
    }

    [Fact]
    public void Protocol_contract_and_all_vectors_are_valid_json()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "protocol");
        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contract.json")));
        Assert.Equal("0.1", contract.RootElement.GetProperty("protocolVersion").GetString());
        Assert.Contains(
            contract.RootElement.GetProperty("errors").EnumerateArray(),
            item => item.GetString() == "consent_required");
        Assert.Contains(
            contract.RootElement.GetProperty("methods").EnumerateArray(),
            item => item.GetString() == "engine.handshake");

        using var validHandshake = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "examples", "valid", "handshake-request.json")));
        var validParams = validHandshake.RootElement.GetProperty("params");
        Assert.Equal("0.1", validParams.GetProperty("protocolVersion").GetString());
        Assert.Equal(JsonValueKind.Array, validParams.GetProperty("requiredCapabilities").ValueKind);

        using var invalidHandshake = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "examples", "invalid", "handshake-missing-version.json")));
        Assert.False(invalidHandshake.RootElement.GetProperty("params").TryGetProperty("protocolVersion", out _));
    }

    [Fact]
    public void Protocol_vectors_match_their_declared_json_schema_expectation()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "protocol");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "examples", "manifest.json")));

        foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var relativePath = vector.GetProperty("path").GetString()!;
            var schemaPath = Path.Combine(root, vector.GetProperty("schemaRef").GetString()!);
            var schema = JsonSchema.FromFile(
                schemaPath,
                new BuildOptions { SchemaRegistry = new SchemaRegistry() },
                new Uri(Path.GetFullPath(schemaPath)));
            using var instance = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, relativePath)));
            var evaluation = schema.Evaluate(instance.RootElement);
            Assert.Equal(vector.GetProperty("valid").GetBoolean(), evaluation.IsValid);
        }

        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contract.json")));
        var validVectors = manifest.RootElement.GetProperty("vectors").EnumerateArray()
            .Where(vector => vector.GetProperty("valid").GetBoolean())
            .ToArray();
        Assert.Equal(
            contract.RootElement.GetProperty("methods").GetArrayLength(),
            validVectors.Count(vector => vector.GetProperty("kind").GetString() == "request"));
        Assert.Equal(
            contract.RootElement.GetProperty("methods").GetArrayLength(),
            validVectors.Count(vector => vector.GetProperty("kind").GetString() == "response"));
        Assert.Equal(
            contract.RootElement.GetProperty("notifications").GetArrayLength(),
            validVectors.Count(vector => vector.GetProperty("kind").GetString() == "notification"));
        Assert.Equal(
            contract.RootElement.GetProperty("errors").GetArrayLength(),
            validVectors.Count(vector => vector.GetProperty("kind").GetString() == "error"));
    }

    [Fact]
    public void Protocol_contract_schema_and_client_constants_match_exactly()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "protocol");
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contract.json")));
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schema", "methods.schema.json")));
        var definitions = schema.RootElement.GetProperty("$defs");

        Assert.Equal(AiProtocolContract.Version, contract.RootElement.GetProperty("protocolVersion").GetString());
        Assert.Equal(
            AiProtocolContract.MaximumHeaderBytes,
            contract.RootElement.GetProperty("limits").GetProperty("maximumHeaderBytes").GetInt32());
        Assert.Equal(
            AiProtocolContract.MaximumFrameBytes,
            contract.RootElement.GetProperty("limits").GetProperty("maximumFrameBytes").GetInt32());
        Assert.Equal(
            AiProtocolContract.Capabilities.Order(StringComparer.Ordinal),
            contract.RootElement.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(
            AiProtocolMethods.All.Order(StringComparer.Ordinal),
            contract.RootElement.GetProperty("methods").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(
            AiProtocolNotifications.All.Order(StringComparer.Ordinal),
            contract.RootElement.GetProperty("notifications").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(
            AiProtocolErrors.All.Order(StringComparer.Ordinal),
            contract.RootElement.GetProperty("errors").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal));

        AssertSameStrings(
            contract.RootElement.GetProperty("methods"),
            definitions.GetProperty("request").GetProperty("properties").GetProperty("method").GetProperty("enum"));
        AssertSameStrings(
            contract.RootElement.GetProperty("notifications"),
            definitions.GetProperty("notification").GetProperty("properties").GetProperty("method").GetProperty("enum"));
        AssertSameStrings(
            contract.RootElement.GetProperty("errors"),
            definitions.GetProperty("errorResponse").GetProperty("properties").GetProperty("error")
                .GetProperty("properties").GetProperty("code").GetProperty("enum"));
    }

    private static void AssertSameStrings(JsonElement left, JsonElement right) =>
        Assert.Equal(
            left.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal),
            right.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Invalid_reader_frame_does_not_prevent_connection_cleanup()
    {
        var factory = new ScriptedTransportFactory(_ =>
            ["{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{},\"extra\":true}"]);
        var client = new AiCoreClient(new NoopProcessLauncher(), factory);

        var error = await Assert.ThrowsAsync<AiCoreException>(
            () => client.ConnectAsync(["ai.chat.v1"]));
        Assert.Equal("invalid_response", error.Code);
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Unexpected_response_identifier_fails_pending_with_the_protocol_error()
    {
        var factory = new ScriptedTransportFactory(request =>
        {
            var unexpectedId = request.GetProperty("id").GetInt32() + 1;
            return [$"{{\"jsonrpc\":\"2.0\",\"id\":{unexpectedId},\"result\":{{}}}}"];
        });
        var client = new AiCoreClient(new NoopProcessLauncher(), factory);

        var error = await Assert.ThrowsAsync<AiCoreException>(
            () => client.ConnectAsync(["ai.chat.v1"]));

        Assert.Equal("invalid_response", error.Code);
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Async_notification_failure_does_not_leave_following_request_pending()
    {
        var factory = new ScriptedTransportFactory(request =>
        {
            var id = request.GetProperty("id").GetInt32();
            return request.GetProperty("method").GetString() == "engine.handshake"
                ? [$"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"engineVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"capabilities\":[\"ai.chat.v1\"]}}}}"]
                : [
                    "{\"jsonrpc\":\"2.0\",\"method\":\"ai.chat.delta\",\"params\":{\"invocationId\":\"test\",\"delta\":\"x\"}}",
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":[]}}"
                ];
        });
        var client = new AiCoreClient(new NoopProcessLauncher(), factory)
        {
            NotificationAsync = (_, _) => Task.FromException(new InvalidOperationException("subscriber"))
        };

        await client.ConnectAsync(["ai.chat.v1"]);
        var result = await client.InvokeAsync<List<AiConversationSummary>>("ai.conversations.list", new { });

        Assert.Empty(result);
        await client.DisposeAsync();
    }
}
