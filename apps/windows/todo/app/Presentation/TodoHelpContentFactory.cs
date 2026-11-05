using Fowan.Todo.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MuxFontWeights = Microsoft.UI.Text.FontWeights;

namespace Fowan.Todo.Windows.Presentation;

internal sealed record TodoHelpCard(string Glyph, string Title, string Description);

internal sealed record TodoHelpContentPalette(
    Brush Hero,
    Brush HeroBorder,
    Brush Accent,
    Brush AccentText,
    Brush Text,
    Brush SecondaryText,
    Brush Border,
    Brush Section,
    Brush Card,
    Brush IconBackground,
    Brush Tip,
    Brush TipBorder,
    Brush TipText);

internal sealed class TodoHelpContentFactory(TodoHelpContentPalette palette)
{
    public UIElement Hero() => new Border
    {
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18),
        Background = palette.Hero,
        BorderBrush = palette.HeroBorder,
        BorderThickness = new Thickness(1),
        Child = new Grid
        {
            ColumnSpacing = 14,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            Children =
            {
                new Border
                {
                    Width = 44, Height = 44, CornerRadius = new CornerRadius(12),
                    Background = palette.Accent,
                    Child = new FontIcon { Glyph = "\uE897", FontSize = 22, Foreground = palette.AccentText }
                },
                HeroText()
            }
        }
    };

    public UIElement Section(
        string mode,
        string title,
        string description,
        IReadOnlyList<TodoHelpCard> cards,
        bool useTwoColumns)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = mode, FontSize = 12, FontWeight = MuxFontWeights.SemiBold,
                    Foreground = palette.Accent
                },
                new TextBlock
                {
                    Text = title, FontSize = 18, FontWeight = MuxFontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap, Foreground = palette.Text
                },
                new TextBlock
                {
                    Text = description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                    Foreground = palette.SecondaryText
                }
            }
        });
        stack.Children.Add(CardGrid(cards, useTwoColumns));
        return new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = palette.Border, Background = palette.Section,
            Padding = new Thickness(16), Child = stack
        };
    }

    public UIElement TipBand() => new Border
    {
        CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12),
        Background = palette.Tip, BorderBrush = palette.TipBorder,
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = $"小提示：任务最多支持 {TodoQuery.MaxTaskTreeDepth} 层子任务；每个任务最多 {TodoQuery.MaxChildTasksPerTask} 个直接子任务。完成包含未完成子任务的父任务时，会先询问是否一起完成。",
            TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20,
            Foreground = palette.TipText
        }
    };

    private UIElement HeroText()
    {
        var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "Fowan 待办可以做什么？", FontSize = 18,
            FontWeight = MuxFontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Foreground = palette.Text
        });
        stack.Children.Add(new TextBlock
        {
            Text = "这里只介绍当前已经支持的功能和操作方法。需要完整整理任务时用主界面，需要贴在桌面随手处理时用便签模式。",
            TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20,
            Foreground = palette.SecondaryText
        });
        Grid.SetColumn(stack, 1);
        return stack;
    }

    private UIElement CardGrid(IReadOnlyList<TodoHelpCard> cards, bool useTwoColumns)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (useTwoColumns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var columns = useTwoColumns ? 2 : 1;
        for (var row = 0; row < (cards.Count + columns - 1) / columns; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < cards.Count; index++)
        {
            var card = Card(cards[index]);
            Grid.SetRow(card, index / columns);
            Grid.SetColumn(card, index % columns);
            grid.Children.Add(card);
        }
        return grid;
    }

    private FrameworkElement Card(TodoHelpCard card)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
            Background = palette.IconBackground,
            Child = new FontIcon { Glyph = card.Glyph, FontSize = 16, Foreground = palette.Accent }
        });
        var text = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = card.Title, FontSize = 14, FontWeight = MuxFontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap, Foreground = palette.Text
                },
                new TextBlock
                {
                    Text = card.Description, FontSize = 12, LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap, Foreground = palette.SecondaryText
                }
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12),
            Background = palette.Card, BorderBrush = palette.Border,
            BorderThickness = new Thickness(1), Child = grid
        };
    }
}
