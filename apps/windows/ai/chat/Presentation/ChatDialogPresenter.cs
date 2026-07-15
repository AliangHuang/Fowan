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
}
