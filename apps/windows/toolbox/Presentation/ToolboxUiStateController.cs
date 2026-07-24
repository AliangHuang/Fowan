using Fowan.Windows.Application;
using Fowan.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxUiStateController(
    IToolboxCommands commands,
    Func<ToolboxSnapshot> snapshot,
    LocalizationService localization,
    ToolboxPresentationState state,
    Func<Grid> root,
    Func<TextBox> searchBox,
    Action selectFirstTool,
    Action refreshTools,
    Action buildShell)
{
    public void ToggleSidebar()
    {
        commands.ToggleSidebar();
        buildShell();
    }

    public void ClearSearch()
    {
        state.SearchText = string.Empty;
        var input = searchBox();
        if (!string.IsNullOrEmpty(input.Text)) input.Text = string.Empty;
        else refreshTools();
        input.Focus(FocusState.Programmatic);
    }

    public void ChangeViewMode(int mode)
    {
        if ((int)state.ViewMode == mode) return;
        state.ViewMode = (ToolViewMode)mode;
        buildShell();
    }

    public void ChangeSortMode(int mode)
    {
        if ((int)state.SortMode == mode) return;
        state.SortMode = (ToolSortMode)mode;
        selectFirstTool();
        buildShell();
    }

    public void ApplySettings(ToolboxSettingsSelection selection)
    {
        commands.UpdateSettings(selection);
        localization.SetLanguage(snapshot().Language);
        buildShell();
    }

    public void RegisterKeyboardAccelerators()
    {
        var focusSearch = new KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.K,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control
        };
        focusSearch.Invoked += (_, args) =>
        {
            searchBox().Focus(FocusState.Programmatic);
            searchBox().SelectAll();
            args.Handled = true;
        };
        root().KeyboardAccelerators.Add(focusSearch);
        var clearSearch = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
        clearSearch.Invoked += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(state.SearchText)) return;
            ClearSearch();
            args.Handled = true;
        };
        root().KeyboardAccelerators.Add(clearSearch);
    }
}
