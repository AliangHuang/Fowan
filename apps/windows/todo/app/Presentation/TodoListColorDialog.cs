using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoListColorChoice(
    string Id,
    string Label,
    Brush Primary,
    Brush Soft,
    bool Selected);

internal sealed class TodoListColorDialog(
    Func<XamlRoot?> xamlRoot,
    Func<ElementTheme> theme,
    Func<ContentDialog, Task<ContentDialogResult>> showModal,
    Func<Brush> cardBorder)
{
    public async Task<string?> ShowAsync(string listName, IReadOnlyList<TodoListColorChoice> choices)
    {
        var palette = new Grid { MinWidth = 392, ColumnSpacing = 12, RowSpacing = 12 };
        palette.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        palette.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (choices.Count + 1) / 2; row++)
        {
            palette.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), RequestedTheme = theme(), Title = $"{listName} 的配色",
            Content = palette, CloseButtonText = "完成"
        };
        string? selectedId = null;
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var card = CreateCard(choice);
            card.Click += (_, _) => { selectedId = choice.Id; dialog.Hide(); };
            Grid.SetRow(card, index / 2);
            Grid.SetColumn(card, index % 2);
            palette.Children.Add(card);
        }
        await showModal(dialog);
        return selectedId;
    }

    private Button CreateCard(TodoListColorChoice choice)
    {
        var border = choice.Selected ? choice.Primary : cardBorder();
        var thickness = new Thickness(choice.Selected ? 2 : 1);
        var card = new Button
        {
            Height = 56, Padding = new Thickness(14, 0, 12, 0),
            CornerRadius = new CornerRadius(10), BorderThickness = thickness,
            BorderBrush = border, Background = choice.Soft,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        card.Resources["ButtonBackground"] = choice.Soft;
        card.Resources["ButtonBackgroundPointerOver"] = choice.Soft;
        card.Resources["ButtonBackgroundPressed"] = choice.Soft;
        card.Resources["ButtonBorderBrush"] = border;
        card.Resources["ButtonBorderBrushPointerOver"] = choice.Primary;
        card.Resources["ButtonBorderBrushPressed"] = choice.Primary;
        card.Resources["ButtonBorderThickness"] = thickness;
        card.Resources["ButtonBorderThicknessPointerOver"] = thickness;
        card.Resources["ButtonBorderThicknessPressed"] = thickness;
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 18, Height = 18, Fill = choice.Primary,
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = choice.Label, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = choice.Primary, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        if (choice.Selected)
        {
            var selected = new FontIcon
            {
                Glyph = "\uE73E", FontSize = 16, Foreground = choice.Primary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(selected, 2);
            content.Children.Add(selected);
        }
        card.Content = content;
        return card;
    }
}
