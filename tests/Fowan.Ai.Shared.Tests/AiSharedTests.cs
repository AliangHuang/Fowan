using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Ai.Shared.Application;
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

    private sealed class SessionInvoker : IAiCoreInvoker
    {
        public List<string> Methods { get; } = [];
        public List<AiCredential> Credentials { get; } = [];
        public List<AiModelProfile> Models { get; } = [];

        public Task<T> InvokeAsync<T>(
            string method,
            object parameters,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Methods.Add(method);
            object result = method switch
            {
                AiProtocolMethods.ChannelsList => new List<AiChannel>(),
                AiProtocolMethods.CredentialsList => Credentials,
                AiProtocolMethods.ModelsList => Models,
                AiProtocolMethods.ModelsPresets => new List<AiPresetModel>(),
                AiProtocolMethods.ToolFeaturesList => new List<AiToolFeature>
                {
                    new("ai.chat", "ai-chat", "AI Chat", ["ai.chat.v1"]),
                    new("ai.report", "report", "Report", ["ai.report.v1"])
                },
                AiProtocolMethods.BindingsList => new List<AiBinding>(),
                AiProtocolMethods.ConversationsList => new List<AiConversationSummary>(),
                _ => JsonSerializer.SerializeToElement(new { })
            };
            return Task.FromResult((T)result);
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
    public void Channel_display_label_marks_unavailable_channels()
    {
        var available = new AiChannel("deepseek", "deepseek", "DeepSeek", "https://api.deepseek.com", true, true);
        var unavailable = new AiChannel("zhipu", "zhipu", "智谱 AI", "https://open.bigmodel.cn/api/paas/v4", true, false);

        Assert.Equal("DeepSeek", available.DisplayLabel);
        Assert.Equal("智谱 AI（暂不支持）", unavailable.DisplayLabel);
    }

    [Fact]
    public void Model_profiles_deserialize_with_web_camel_case_options()
    {
        const string payload = """[{"id":"model-1","credentialId":"credential-1","modelId":"deepseek-v4-pro","displayName":"DeepSeek V4 Pro","source":"preset","enabled":true,"thinkingEnabled":true,"thinkingEffortOptions":["high","max"],"contextWindowTokens":1000000,"maxOutputTokens":384000,"limitsConfigured":true,"lastTestStatus":null,"lastTestAt":null,"createdAt":"2026-07-20T00:00:00Z","updatedAt":"2026-07-20T00:00:00Z"}]""";

        var models = JsonSerializer.Deserialize<List<AiModelProfile>>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var model = Assert.Single(models!);
        Assert.True(model.LimitsConfigured);
        Assert.Equal(1_000_000, model.ContextWindowTokens);
        Assert.Equal(384_000, model.MaxOutputTokens);
        Assert.Equal(["high", "max"], model.ThinkingEffortOptions);
    }

    [Fact]
    public void Preset_model_defaults_match_channel_and_exact_model_id()
    {
        var presets = new[]
        {
            new AiPresetModel("deepseek", "deepseek-v4-flash", "DeepSeek V4 Flash", 1_000_000, 384_000)
        };

        var match = AiPresetModelDefaults.Find(presets, "deepseek", " deepseek-v4-flash ");

        Assert.NotNull(match);
        Assert.Equal(1_000_000, match.ContextWindowTokens);
        Assert.Equal(384_000, match.MaxOutputTokens);
        Assert.Null(AiPresetModelDefaults.Find(presets, "zhipu", "deepseek-v4-flash"));
        Assert.Null(AiPresetModelDefaults.Find(presets, "deepseek", "DeepSeek-V4-Flash"));
        Assert.Null(AiPresetModelDefaults.Find(presets, "deepseek", "unknown-model"));
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
    public void Core_resolver_finds_the_unified_core_from_the_report_tool_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "Fowan-Ai-Shared-Tests", Guid.NewGuid().ToString("N"));
        var reportDirectory = Path.Combine(root, "app", "Tools", "Report");
        var corePath = Path.Combine(root, "app", "Core", AiCoreEndpointResolver.ExecutableName);
        var previous = Environment.GetEnvironmentVariable("FOWAN_CORE_PATH");
        try
        {
            Directory.CreateDirectory(reportDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(corePath)!);
            File.WriteAllText(corePath, string.Empty);
            Environment.SetEnvironmentVariable("FOWAN_CORE_PATH", null);

            Assert.Equal(
                Path.GetFullPath(corePath),
                AiCoreEndpointResolver.ResolveExecutablePath(reportDirectory));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOWAN_CORE_PATH", previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Handshake_accepts_each_application_capability_subset()
    {
        using var document = JsonDocument.Parse(
            """{"engineVersion":"0.1.0","protocolVersion":"0.1","contractRevision":1,"capabilities":["ai.chat.v1","ai.config.v1","ai.chat.context.v1","ai.chat.branching.v1","ai.report.v1"]}""");

        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.chat.v1"]);
        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.config.v1"]);
        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.report.v1"]);
    }

    [Fact]
    public void Handshake_rejects_missing_capability()
    {
        using var document = JsonDocument.Parse(
            """{"engineVersion":"0.1.0","protocolVersion":"0.1","contractRevision":1,"capabilities":["ai.chat.v1","ai.chat.context.v1","ai.chat.branching.v1"]}""");

        var error = Assert.Throws<AiCoreException>(() =>
            AiCoreClient.ValidateHandshake(document.RootElement, ["ai.config.v1"]));
        Assert.Equal("protocol_mismatch", error.Code);
    }

    [Fact]
    public void Handshake_rejects_a_non_initial_contract_revision()
    {
        using var document = JsonDocument.Parse(
            """{"engineVersion":"0.1.0","protocolVersion":"0.1","contractRevision":2,"capabilities":["ai.chat.v1"]}""");

        var error = Assert.Throws<AiCoreException>(() =>
            AiCoreClient.ValidateHandshake(document.RootElement, ["ai.chat.v1"]));
        Assert.Equal("protocol_mismatch", error.Code);
    }

    [Fact]
    public void Chat_session_publishes_immutable_lifecycle_snapshots()
    {
        var invoker = new SessionInvoker();
        var session = new AiChatSession(new AiCoreApi(invoker), new AiConsentCoordinator(invoker));
        var states = new List<AiChatSnapshot>();
        session.StateChanged += (_, state) => states.Add(state);

        session.BeginGeneration();
        session.AdoptInvocation("invocation-1");
        Assert.Equal("hello", session.AppendDelta("hello"));
        var snapshot = session.State;
        session.CompleteInvocation();

        Assert.True(states.Count >= 4);
        Assert.True(snapshot.IsGenerating);
        Assert.Equal("invocation-1", snapshot.ActiveInvocationId);
        Assert.Equal("hello", snapshot.StreamingContent);
        Assert.False(session.State.IsGenerating);
    }

    [Fact]
    public void Chat_session_keeps_a_completion_that_arrives_before_the_send_response()
    {
        var invoker = new SessionInvoker();
        var session = new AiChatSession(new AiCoreApi(invoker), new AiConsentCoordinator(invoker));

        session.BeginGeneration();
        session.AdoptInvocation("invocation-1");
        Assert.True(session.FinishInvocation("invocation-1"));

        var completedBeforeResponse = session.AcceptInvocation(new AiChatStarted(
            "invocation-1", "conversation-1", "assistant-message-1"));

        Assert.True(completedBeforeResponse);
        Assert.Equal("conversation-1", session.State.CurrentConversationId);
        Assert.Null(session.State.ActiveInvocationId);
        Assert.False(session.State.IsGenerating);
    }

    [Fact]
    public void Chat_session_adopts_the_conversation_from_a_started_notification()
    {
        var invoker = new SessionInvoker();
        var session = new AiChatSession(new AiCoreApi(invoker), new AiConsentCoordinator(invoker));

        session.BeginGeneration();
        session.AdoptInvocation(new AiChatStarted("invocation-1", "conversation-1", "assistant-message-1"));

        Assert.Equal("conversation-1", session.State.CurrentConversationId);
        Assert.Equal("invocation-1", session.State.ActiveInvocationId);
        Assert.True(session.State.IsGenerating);
    }

    [Fact]
    public void Camel_case_chat_notifications_drive_the_streaming_lifecycle()
    {
        var invoker = new SessionInvoker();
        var session = new AiChatSession(new AiCoreApi(invoker), new AiConsentCoordinator(invoker));
        session.BeginGeneration();
        session.AcceptInvocation(new AiChatStarted(
            "invocation-1", "conversation-1", "assistant-message-1"));

        using var startedDocument = JsonDocument.Parse(
            """{"invocationId":"invocation-1","conversationId":"conversation-1","assistantMessageId":"assistant-message-1"}""");
        var started = new AiCoreNotificationEventArgs(
            AiProtocolNotifications.ChatStarted,
            startedDocument.RootElement.Clone()).DeserializeParameters<AiChatStarted>();
        session.AdoptInvocation(started);

        foreach (var payload in new[]
                 {
                     """{"invocationId":"invocation-1","delta":"hello "}""",
                     """{"invocationId":"invocation-1","delta":"world"}"""
                 })
        {
            using var deltaDocument = JsonDocument.Parse(payload);
            var delta = new AiCoreNotificationEventArgs(
                AiProtocolNotifications.ChatDelta,
                deltaDocument.RootElement.Clone()).DeserializeParameters<AiChatDelta>();
            Assert.Equal(session.State.ActiveInvocationId, delta.InvocationId);
            session.AppendDelta(delta.Delta);
        }

        using var completedDocument = JsonDocument.Parse(
            """{"invocationId":"invocation-1","assistantMessageId":"assistant-message-1","errorCode":null}""");
        var completed = new AiCoreNotificationEventArgs(
            AiProtocolNotifications.ChatCompleted,
            completedDocument.RootElement.Clone()).DeserializeParameters<AiChatFinished>();

        Assert.Equal("hello world", session.State.StreamingContent);
        Assert.True(session.FinishInvocation(completed.InvocationId));
        Assert.False(session.State.IsGenerating);
        Assert.Null(session.State.ActiveInvocationId);
    }

    [Fact]
    public void Chat_notification_missing_required_fields_is_an_invalid_response()
    {
        using var document = JsonDocument.Parse("""{"delta":"hello"}""");
        var notification = new AiCoreNotificationEventArgs(
            AiProtocolNotifications.ChatDelta,
            document.RootElement.Clone());

        var error = Assert.Throws<AiCoreException>(
            () => notification.DeserializeParameters<AiChatDelta>());

        Assert.Equal("invalid_response", error.Code);
    }

    [Fact]
    public async Task Config_session_publishes_refresh_state_and_successful_crud_results()
    {
        var invoker = new SessionInvoker();
        var session = new AiConfigSession(new AiCoreApi(invoker), new AiConsentCoordinator(invoker));
        AiConfigSnapshot? state = null;
        AiConfigMutation? mutation = null;
        session.StateChanged += (_, value) => state = value;
        session.MutationCompleted += (_, value) => mutation = value;

        await session.RefreshAsync();
        await session.CreateChannelAsync("Custom", "https://api.example.com/v1");

        Assert.NotNull(state);
        Assert.Empty(state.Channels);
        Assert.Equal(new AiConfigMutation("channel", "create", null), mutation);
        Assert.Contains(AiProtocolMethods.ChannelsCreate, invoker.Methods);
    }

    [Fact]
    public async Task Config_session_requires_an_enabled_model_to_test_a_credential()
    {
        var invoker = new SessionInvoker();
        invoker.Models.Add(new AiModelProfile(
            "model-1", "credential-1", "test-model", "Test model", "custom", false, false,
            null, null, false, null, null, "2026-07-16T00:00:00Z", "2026-07-16T00:00:00Z"));
        var session = new AiConfigSession(new AiCoreApi(invoker), new AiConsentCoordinator(invoker));

        await session.RefreshAsync();

        Assert.False(session.HasEnabledModelForCredential("credential-1"));

        invoker.Models[0] = invoker.Models[0] with { Enabled = true };
        await session.RefreshAsync();

        Assert.True(session.HasEnabledModelForCredential("credential-1"));
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
                ? [$"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"engineVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"contractRevision\":1,\"capabilities\":[\"ai.chat.v1\",\"ai.chat.context.v1\",\"ai.chat.branching.v1\"]}}}}"]
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
