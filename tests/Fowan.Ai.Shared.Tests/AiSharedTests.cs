using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using System.Text.Json;
using Xunit;

namespace Fowan.Ai.Shared.Tests;

public sealed class AiSharedTests
{
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
            Assert.Equal(Path.GetFullPath(executable), AiApplicationLauncher.ResolveExecutable(AiApplication.Chat));
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
            """{"protocolVersion":"0.1","capabilities":["ai.chat.v1","ai.config.v1"]}""");

        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.chat.v1"]);
        AiCoreClient.ValidateHandshake(document.RootElement, ["ai.config.v1"]);
    }

    [Fact]
    public void Handshake_rejects_missing_capability()
    {
        using var document = JsonDocument.Parse(
            """{"protocolVersion":"0.1","capabilities":["ai.chat.v1"]}""");

        var error = Assert.Throws<AiCoreException>(() =>
            AiCoreClient.ValidateHandshake(document.RootElement, ["ai.config.v1"]));
        Assert.Equal("protocol_mismatch", error.Code);
    }
}
