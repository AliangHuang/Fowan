using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Models;
using Fowan.Ai.Chat.Windows.Presentation;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Ai.Chat.Windows.Coordination;

internal sealed class ChatConversationCoordinator(
    AiChatSession session,
    ChatDialogPresenter dialogs,
    Func<AiConversationSummary?> selectedConversation,
    Func<Task> refreshConversations,
    Action startNewConversation,
    Action<Exception> showError,
    Func<string, string> localize)
{
    public async Task RenameAsync()
    {
        var selected = selectedConversation();
        if (selected is null) return;
        var title = new TextBox { Text = selected.Title, Header = localize("AI_ConversationTitle"), MinWidth = 380 };
        if (await dialogs.ShowEditorAsync(localize("AI_RenameChat"), title) != ContentDialogResult.Primary) return;
        try
        {
            await session.RenameConversationAsync(selected.Id, title.Text);
            await refreshConversations();
        }
        catch (Exception exception)
        {
            showError(exception);
        }
    }

    public async Task DeleteAsync()
    {
        var selected = selectedConversation();
        if (selected is null) return;
        if (!await dialogs.ConfirmDeleteConversationAsync()) return;
        try
        {
            await session.DeleteConversationAsync(selected.Id);
            startNewConversation();
            await refreshConversations();
        }
        catch (Exception exception)
        {
            showError(exception);
        }
    }
}
