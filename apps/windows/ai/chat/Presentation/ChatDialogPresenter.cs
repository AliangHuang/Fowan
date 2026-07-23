using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal sealed class ChatDialogPresenter(Func<string, string> localize, Func<XamlRoot> xamlRoot)
{
    public async Task<ContentDialogResult> ShowEditorAsync(string title, UIElement content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = title, Content = content,
            PrimaryButtonText = localize("Action_Save"), CloseButtonText = localize("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmCloudConsentAsync(string endpoint)
    {
        var content = new TextBlock
        {
            Text = string.Format(localize("AI_CloudConsentDescription"), endpoint),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 520
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = localize("AI_CloudConsentTitle"), Content = content,
            PrimaryButtonText = localize("AI_CloudConsentAllow"), CloseButtonText = localize("Action_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<bool> ConfirmCompressionAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "压缩较早对话",
            Content = new TextBlock { Text = "将额外调用一次当前模型并产生费用；较早轮次会保存为本地加密摘要，近期原文保持不变。", TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            PrimaryButtonText = "压缩并继续发送", CloseButtonText = localize("Action_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public Task<bool> ConfirmStopGenerationAsync() => ConfirmAsync("停止当前生成？", "将先停止生成并保存已收到的部分内容，然后再继续此操作。", "停止并继续");
    public Task<bool> ConfirmDeleteConversationAsync() => ConfirmAsync("删除对话？", "此操作将永久删除本机保存的对话和分支。", "删除");
    public Task<bool> ConfirmRegenerateAfterMessageAsync() => ConfirmAsync("重新生成此回复？", "此回复之后的后续消息将被永久删除，且无法恢复。是否继续？", "删除后重新生成");

    private async Task<bool> ConfirmAsync(string title, string description, string primary)
    {
        var dialog = new ContentDialog { XamlRoot = xamlRoot(), Title = title,
            Content = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            PrimaryButtonText = primary, CloseButtonText = localize("Action_Cancel"), DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
