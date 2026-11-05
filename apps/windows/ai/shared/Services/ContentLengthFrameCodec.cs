using Fowan.Ai.Shared.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fowan.Ai.Shared.Services;

internal static class ContentLengthFrameCodec
{
    public static async Task<JsonElement?> ReadAsync(
        IAiCoreTransport transport,
        CancellationToken cancellationToken)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await transport.ReadAsync(one, cancellationToken);
            if (read == 0)
            {
                return null;
            }

            header.Add(one[0]);
            if (header.Count >= 4 &&
                header[^4] == '\r' && header[^3] == '\n' &&
                header[^2] == '\r' && header[^1] == '\n')
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
        await ReadExactlyAsync(transport, body, cancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    public static async Task WriteAsync(
        IAiCoreTransport transport,
        JsonNode message,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString(options));
        if (body.Length > AiProtocolContract.MaximumFrameBytes)
        {
            throw new InvalidDataException("Protocol frame is too large.");
        }
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await transport.WriteAsync(header, cancellationToken);
        await transport.WriteAsync(body, cancellationToken);
        await transport.FlushAsync(cancellationToken);
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
}
