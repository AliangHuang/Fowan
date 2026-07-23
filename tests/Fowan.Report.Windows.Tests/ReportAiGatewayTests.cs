using Fowan.Ai.Shared.Application.Ports;
using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Fowan.Report.Shared;
using Fowan.Todo.Shared.Models;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class ReportAiGatewayTests
{
    [Fact]
    public void TextCompletionProducesAnEditableRichBlockDocument()
    {
        using var document = JsonDocument.Parse("""{"invocationId":"report-1","output":{"document":{"format":"text","blocks":[{"kind":"heading1","text":"本周汇报","bold":true,"italic":false,"link":null,"isChecked":false,"table":null}],"sheets":[]}}}""");
        var notification = new AiCoreNotificationEventArgs(
            AiProtocolNotifications.ReportCompleted,
            document.RootElement.Clone());

        var output = ReportWindow.ToReportGenerationOutput(
            notification.DeserializeParameters<AiReportCompleted>());

        Assert.Equal(ReportTextBlockKind.Heading1, Assert.Single(output.TextDocument!.Blocks).Kind);
        Assert.Equal("本周汇报", output.TextDocument.Blocks[0].Text);
        Assert.Null(output.FileDocument);
    }

    [Fact]
    public void TextRequestSendsCompleteContentTreeWithoutLocalBlockIds()
    {
        var input = Input() with
        {
            Template = new ReportTemplateContext(
                ReportTemplateMode.Text,
                string.Empty,
                string.Empty,
                TextDocument: new ReportTextDocument(ReportTextDocument.CurrentVersion,
                    [new("local-only-id", ReportTextBlockKind.Heading1, "本周汇报", Bold: true)]),
                ExampleTextDocument: new ReportTextDocument(ReportTextDocument.CurrentVersion,
                    [new("example-local-id", ReportTextBlockKind.Quote, "填写示例")]))
        };

        var request = ReportAiGateway.ToRequest(input);

        Assert.Equal("text", request.TemplateMode);
        Assert.Equal("text", request.Template.Format);
        Assert.Equal("heading1", Assert.Single(request.Template.Blocks).Kind);
        Assert.Equal("本周汇报", request.Template.Blocks[0].Text);
        Assert.Equal("填写示例", Assert.Single(request.Example!.Blocks).Text);
        Assert.DoesNotContain("local-only-id", JsonSerializer.Serialize(request));
        Assert.Null(request.Candidate);
        Assert.Equal(1, request.Attempt);
    }

    [Fact]
    public void FileRequestSendsOnlyCompleteFileContentTree()
    {
        var fileDocument = new ReportFileContentDocument(
            "docx",
            [new("paragraph", "模板标题"), new("table", Table: new ReportFileTable(
                [[new("事项"), new("状态")], [new(string.Empty), new(string.Empty)]], true))],
            []);
        var input = Input() with
        {
            Template = new ReportTemplateContext(
                ReportTemplateMode.File,
                string.Empty,
                string.Empty,
                "template.docx",
                FileDocument: fileDocument)
        };

        var request = ReportAiGateway.ToRequest(input);

        Assert.Equal("file", request.TemplateMode);
        Assert.Equal("docx", request.Template.Format);
        Assert.Equal(2, request.Template.Blocks.Count);
        var serialized = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("targetId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scalarValues", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rowValues", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepairRequestIncludesCandidateAndSafeDiagnostic()
    {
        var candidate = new AiReportContentDocument("text", [], []);

        var request = ReportAiGateway.ToRequest(Input(), 2, candidate, "表格列数与模板不一致，请保留原列数。");

        Assert.Equal(2, request.Attempt);
        Assert.Same(candidate, request.Candidate);
        Assert.Equal("表格列数与模板不一致，请保留原列数。", request.ValidationFeedback);
    }

    [Fact]
    public async Task GenerateReconnectsBeforeItReadsReportBindings()
    {
        var transport = new ScriptedTransportFactory(request => request.GetProperty("method").GetString() switch
        {
            AiProtocolMethods.EngineHandshake => HandshakeResponse(request),
            AiProtocolMethods.BindingsList => Response(request, "[]"),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        var client = new AiCoreClient(new NoopProcessLauncher(), transport);
        await using var gateway = new ReportAiGateway(client);

        var error = await Assert.ThrowsAsync<AiCoreException>(() => gateway.GenerateAsync(
            Input(),
            _ => Task.FromResult(true)));

        Assert.Equal("not_found", error.Code);
        Assert.Equal([AiProtocolMethods.EngineHandshake, AiProtocolMethods.BindingsList], transport.Methods);
    }

    private static ReportGenerationInput Input() => new(
        new ReportRange(ReportRangeKind.ThisWeek, new DateTime(2026, 7, 20), new DateTime(2026, 7, 26)),
        TodoFilterCriteria.Default,
        ReportStyle.Professional,
        string.Empty,
        new ReportTemplateContext(ReportTemplateMode.Text, string.Empty, string.Empty),
        new ReportTaskPreview([], []));

    private static string Response(JsonElement request, string result) =>
        $$"""{"jsonrpc":"2.0","id":{{request.GetProperty("id").GetInt32()}},"result":{{result}}}""";

    private static string HandshakeResponse(JsonElement request)
    {
        var required = request.GetProperty("params").GetProperty("requiredCapabilities")
            .EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        Assert.Equal(["ai.report.v1"], required);
        return Response(request, """{"engineVersion":"0.1.0","protocolVersion":"0.1","contractRevision":1,"capabilities":["ai.report.v1"]}""");
    }

    private sealed class NoopProcessLauncher : IAiCoreProcessLauncher
    {
        public void Start(string executablePath) => throw new InvalidOperationException("The scripted transport must connect before Core is started.");
    }

    private sealed class ScriptedTransportFactory(Func<JsonElement, string> response) : IAiCoreTransportFactory
    {
        private readonly Func<JsonElement, string> _response = response;

        public List<string> Methods { get; } = [];

        public IAiCoreTransport Create(string pipeName) => new ScriptedTransport(_response, Methods);
    }

    private sealed class ScriptedTransport(Func<JsonElement, string> response, List<string> methods) : IAiCoreTransport
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
            methods.Add(request.RootElement.GetProperty("method").GetString()!);
            _reads.Writer.TryWrite(Frame(response(request.RootElement)));
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
}
