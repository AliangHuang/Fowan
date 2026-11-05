using Fowan.Ai.Shared.Models;
using Fowan.Ai.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal enum ChatMessageSeverity
{
    Success,
    Warning,
    Error
}

internal sealed class ChatStatusPresenter(
    Func<string, string> localize,
    Func<IReadOnlyList<AiChannel>> channels,
    Func<Border> statusBar,
    Func<TextBlock> statusText)
{
    public string ChannelName(string id) => channels().FirstOrDefault(item => item.Id == id)?.DisplayName ?? id;

    public void ShowError(Exception exception)
    {
        var message = exception is AiCoreException core ? ErrorText(core.Code) : exception.Message;
        ShowMessage(message, ChatMessageSeverity.Error);
    }

    public string ErrorText(string? code) => code switch
    {
        "provider_auth_failed" => localize("AI_Error_Auth"),
        "provider_model_not_found" => localize("AI_Error_Model"),
        "provider_rate_limited" => localize("AI_Error_RateLimit"),
        "provider_content_rejected" => localize("AI_Error_Content"),
        "context_limit_exceeded" => localize("AI_Error_Context"),
        "timeout" => localize("AI_Error_Timeout"),
        "conflict" => localize("AI_Error_Conflict"),
        "secret_store_unavailable" => localize("AI_Error_SecretStore"),
        _ => localize("AI_Error_Unavailable")
    };

    public void ShowMessage(string message, ChatMessageSeverity severity)
    {
        statusText().Text = message;
        statusBar().Background = severity switch
        {
            ChatMessageSeverity.Success => new SolidColorBrush(Color.FromArgb(255, 220, 252, 231)),
            ChatMessageSeverity.Warning => new SolidColorBrush(Color.FromArgb(255, 254, 243, 199)),
            _ => new SolidColorBrush(Color.FromArgb(255, 254, 226, 226))
        };
        statusBar().Visibility = Visibility.Visible;
    }
}
