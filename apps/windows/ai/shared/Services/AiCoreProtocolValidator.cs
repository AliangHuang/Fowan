using Fowan.Ai.Shared.Models;
using System.Text.Json;

namespace Fowan.Ai.Shared.Services;

internal static class AiCoreProtocolValidator
{
    public static void ValidateHandshake(
        JsonElement handshake,
        IEnumerable<string> requiredCapabilities)
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
            throw new AiCoreException(
                "protocol_mismatch",
                "Fowan Core protocol version is not supported.");
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

    public static void ValidateIncomingEnvelope(JsonElement message)
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
            var validId = id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var requestId) && requestId > 0;
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
                              (error.TryGetProperty("data", out var data) &&
                               data.ValueKind != JsonValueKind.Object))))
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
}
