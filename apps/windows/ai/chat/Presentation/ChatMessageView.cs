using Fowan.Ai.Shared.Models;
using Fowan.Windows.Platform.Contracts;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal sealed class ChatMessageView(Func<string, string> text, IClipboardService copyService)
{
    public Border Bubble(AiChatMessage message)
    {
        var metadata = message.Role == "assistant" && message.ModelId is not null
            ? $"{message.ChannelName} · {message.ModelId}"
            : null;
        return Bubble(message.Role, message.Content, metadata, message.CreatedAt);
    }

    public Border Bubble(string role, string content, string? metadata) =>
        Bubble(role, content, metadata, DateTimeOffset.Now.ToString("O"));

    private Border Bubble(string role, string content, string? metadata, string createdAt)
    {
        var isUser = string.Equals(role, "user", StringComparison.Ordinal);
        var streamingText = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 15,
            LineHeight = 24
        };
        UIElement body = string.IsNullOrEmpty(content) ? streamingText : Markdown(content);
        var contentStack = new StackPanel { Spacing = 10 };
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!isUser && !string.IsNullOrWhiteSpace(metadata))
        {
            var separator = metadata.IndexOf(" · ", StringComparison.Ordinal);
            var channel = separator >= 0 ? metadata[..separator] : metadata;
            var model = separator >= 0 ? metadata[(separator + 3)..] : null;
            var metadataPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            metadataPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 234, 243, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = channel,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 11, 103, 246))
                }
            });
            if (!string.IsNullOrWhiteSpace(model))
            {
                metadataPanel.Children.Add(new TextBlock
                {
                    Text = $"· {model}",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 138, 147, 162)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            heading.Children.Add(metadataPanel);
        }
        var time = new TextBlock
        {
            Text = DisplayTime(createdAt),
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 138, 147, 162)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(time, 1);
        heading.Children.Add(time);
        contentStack.Children.Add(heading);
        contentStack.Children.Add(body);
        if (!isUser)
        {
            contentStack.Children.Add(ActionBar(content));
        }

        var card = new Border
        {
            Background = isUser
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 244, 249, 255))
                : new SolidColorBrush(Colors.White),
            BorderBrush = isUser
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 186, 214, 255))
                : new SolidColorBrush(ColorHelper.FromArgb(255, 217, 225, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13, 16, 12)
        };
        var cardContent = new Grid { ColumnSpacing = 12 };
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardContent.Children.Add(Avatar(isUser));
        Grid.SetColumn(contentStack, 1);
        cardContent.Children.Add(contentStack);
        card.Child = cardContent;
        return new Border { Tag = streamingText, Child = card, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    public UIElement VariantGroup(IReadOnlyList<AiChatMessage> variants)
    {
        if (variants.Count == 1) return Bubble(variants[0]);
        var host = new Grid();
        foreach (var variant in variants) host.Children.Add(Bubble(variant));
        var selectedIndex = variants.Count - 1;
        var previous = new Button { Content = "‹", Padding = new Thickness(8, 2, 8, 2) };
        var indicator = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var next = new Button { Content = "›", Padding = new Thickness(8, 2, 8, 2) };
        void Refresh()
        {
            selectedIndex = Math.Clamp(selectedIndex, 0, variants.Count - 1);
            indicator.Text = $"{selectedIndex + 1} / {variants.Count}";
            previous.IsEnabled = selectedIndex > 0;
            next.IsEnabled = selectedIndex + 1 < variants.Count;
            for (var index = 0; index < host.Children.Count; index++)
                host.Children[index].Visibility = index == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
        previous.Click += (_, _) => { selectedIndex--; Refresh(); };
        next.Click += (_, _) => { selectedIndex++; Refresh(); };
        Refresh();
        return new StackPanel
        {
            Spacing = 5,
            Children = { host, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { previous, indicator, next } } }
        };
    }

    private UIElement Avatar(bool isUser) => new Border
    {
        Width = 36,
        Height = 36,
        CornerRadius = new CornerRadius(18),
        VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(isUser ? ColorHelper.FromArgb(255, 11, 103, 246) : ColorHelper.FromArgb(255, 97, 110, 255)),
        Child = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = isUser ? "\uE77B" : "\uE8BD",
            FontSize = 17,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private StackPanel ActionBar(string content)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var copy = new Button { Padding = new Thickness(5), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(copy, text("AI_Copy"));
        copy.Content = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = "\uE8C8", FontSize = 16 };
        copy.Click += (_, _) => copyService.SetText(content);
        actions.Children.Add(copy);
        actions.Children.Add(ActionIcon("\uE19F", "赞"));
        actions.Children.Add(ActionIcon("\uE19E", "踩"));
        return actions;
    }

    private static Button ActionIcon(string glyph, string label)
    {
        var button = new Button
        {
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = glyph, FontSize = 16 }
        };
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private StackPanel Markdown(string content)
    {
        var root = new StackPanel { Spacing = 7 };
        var paragraph = new List<string>();
        var code = new List<string>();
        var codeLanguage = string.Empty;
        var inCode = false;
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            root.Children.Add(new TextBlock { Text = string.Join(Environment.NewLine, paragraph), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, LineHeight = 22 });
            paragraph.Clear();
        }
        void FlushCode()
        {
            var value = string.Join(Environment.NewLine, code);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(codeLanguage) ? "代码" : codeLanguage, FontSize = 13, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 203, 213, 225)) });
            var copy = new Button
            {
                Content = text("AI_CopyCode"),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 12,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 226, 232, 240)),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            copy.Click += (_, _) => copyService.SetText(value);
            Grid.SetColumn(copy, 1);
            header.Children.Add(copy);
            var codeBlock = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                LineHeight = 21,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 226, 232, 240))
            };
            root.Children.Add(new Border { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 35, 43)), CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 10, 12, 12), Child = new StackPanel { Children = { header, codeBlock } } });
            code.Clear();
            codeLanguage = string.Empty;
        }
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) FlushCode(); else { FlushParagraph(); codeLanguage = line[3..].Trim(); }
                inCode = !inCode;
            }
            else if (inCode) code.Add(line);
            else if (line.StartsWith("# ", StringComparison.Ordinal)) AddHeading(line[2..], 21);
            else if (line.StartsWith("## ", StringComparison.Ordinal)) AddHeading(line[3..], 18);
            else if (line.StartsWith("- ", StringComparison.Ordinal)) paragraph.Add($"• {line[2..]}");
            else if (string.IsNullOrWhiteSpace(line)) FlushParagraph();
            else paragraph.Add(line);
        }
        if (inCode) FlushCode();
        FlushParagraph();
        return root;

        void AddHeading(string value, double fontSize)
        {
            FlushParagraph();
            root.Children.Add(new TextBlock { Text = value, FontSize = fontSize, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }
    }

    private static string DisplayTime(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.LocalDateTime.ToString("HH:mm") : string.Empty;
}
