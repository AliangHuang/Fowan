using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxQuickCaptureDialog(
    Func<XamlRoot> xamlRoot,
    Func<string, string> localize,
    Func<string, Brush> themeBrush)
{
    public async Task<string?> ShowAsync()
    {
        var input = new TextBox
        {
            PlaceholderText = localize("QuickCapture_Placeholder"), AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 130
        };
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(input);
        stack.Children.Add(new TextBlock
        {
            Text = localize("QuickCapture_Destination"),
            Foreground = themeBrush("TextFillColorSecondaryBrush")
        });
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = localize("QuickCapture_Title"), Content = stack,
            PrimaryButtonText = localize("Action_Capture"), CloseButtonText = localize("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text)
            ? input.Text.Trim()
            : null;
    }
}
