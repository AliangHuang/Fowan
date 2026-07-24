using Fowan.Ai.Shared.Models;
using Fowan.Windows.Platform.Contracts;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Windows.UI;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal sealed class ChatMessageView(
    Func<string, string> text,
    IClipboardService copyService,
    Func<AiChatMessage, Task>? regenerate = null,
    Func<string, Task>? selectBranch = null,
    Action<Button>? registerRegenerateButton = null,
    ImageSource? userAvatar = null)
{
    public Border Bubble(AiChatMessage message)
    {
        var metadata = message.Role == "assistant" && message.ModelId is not null
            ? $"{message.ChannelName} · {message.ModelId}"
            : null;
        return Bubble(message.Role, message.Content, metadata, message.CreatedAt, message);
    }

    public Border Bubble(string role, string content, string? metadata) =>
        Bubble(role, content, metadata, DateTimeOffset.Now.ToString("O"), null);

    private Border Bubble(string role, string content, string? metadata, string createdAt, AiChatMessage? message)
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
            contentStack.Children.Add(ActionBar(content, message));
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

    public UIElement VariantGroup(IReadOnlyList<AiChatMessage> variants, string? selectedMessageId = null)
    {
        if (variants.Count == 1) return Bubble(variants[0]);
        var host = new Grid();
        foreach (var variant in variants) host.Children.Add(Bubble(variant));
        var selectedIndex = selectedMessageId is null
            ? variants.Count - 1
            : Math.Max(0, variants.ToList().FindIndex(item => item.Id == selectedMessageId));
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
        previous.Click += async (_, _) => { selectedIndex--; Refresh(); if (selectBranch is not null) await selectBranch(variants[selectedIndex].Id); };
        next.Click += async (_, _) => { selectedIndex++; Refresh(); if (selectBranch is not null) await selectBranch(variants[selectedIndex].Id); };
        Refresh();
        return new StackPanel
        {
            Spacing = 5,
            Children = { host, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { previous, indicator, next } } }
        };
    }

    private UIElement Avatar(bool isUser)
    {
        var avatar = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            VerticalAlignment = VerticalAlignment.Top
        };
        if (isUser && userAvatar is not null)
        {
            avatar.Background = new ImageBrush { ImageSource = userAvatar, Stretch = Stretch.UniformToFill };
            return avatar;
        }

        avatar.Background = new SolidColorBrush(
            isUser ? ColorHelper.FromArgb(255, 11, 103, 246) : ColorHelper.FromArgb(255, 97, 110, 255));
        avatar.Child = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = isUser ? "\uE77B" : "\uE8BD",
            FontSize = 17,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return avatar;
    }

    private StackPanel ActionBar(string content, AiChatMessage? message)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (message is { Role: "assistant", ParentMessageId: not null } && regenerate is not null)
        {
            var regenerateButton = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = "\uE72C", FontSize = 16 }
            };
            ToolTipService.SetToolTip(regenerateButton, "重新生成");
            AutomationProperties.SetName(regenerateButton, "重新生成");
            regenerateButton.Click += async (_, _) => await regenerate(message);
            registerRegenerateButton?.Invoke(regenerateButton);
            actions.Children.Add(regenerateButton);
        }
        var copy = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(copy, text("AI_Copy"));
        copy.Content = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = "\uE8C8", FontSize = 16 };
        copy.Click += (_, _) => copyService.SetText(content);
        actions.Children.Add(copy);
        return actions;
    }

    private StackPanel Markdown(string content)
    {
        var root = new StackPanel { Spacing = 7 };
        var pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
        var document = Markdig.Markdown.Parse(content, pipeline);
        RenderBlocks(document, root, 0);
        return root;

        void RenderBlocks(ContainerBlock blocks, StackPanel target, int quoteDepth)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        target.Children.Add(InlineText(heading.Inline, Math.Max(17, 25 - heading.Level * 2), FontWeights.SemiBold));
                        break;
                    case ParagraphBlock paragraph:
                        target.Children.Add(InlineText(paragraph.Inline, 15, FontWeights.Normal));
                        break;
                    case FencedCodeBlock fenced:
                        target.Children.Add(CodeBlock(fenced.Lines.ToString(), fenced.Info?.ToString() ?? string.Empty));
                        break;
                    case CodeBlock code:
                        target.Children.Add(CodeBlock(code.Lines.ToString(), string.Empty));
                        break;
                    case QuoteBlock quote:
                        var quoteStack = new StackPanel { Spacing = 6 };
                        RenderBlocks(quote, quoteStack, quoteDepth + 1);
                        target.Children.Add(new Border { BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 148, 163, 184)), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2), Child = quoteStack });
                        break;
                    case ListBlock list:
                        var index = 1;
                        foreach (var item in list.OfType<ListItemBlock>())
                        {
                            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                            row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{index++}." : "•", FontWeight = FontWeights.SemiBold });
                            var itemStack = new StackPanel { Spacing = 4 };
                            RenderBlocks(item, itemStack, quoteDepth);
                            row.Children.Add(itemStack);
                            target.Children.Add(row);
                        }
                        break;
                    case HtmlBlock:
                        break;
                    case ContainerBlock container:
                        RenderBlocks(container, target, quoteDepth);
                        break;
                }
            }
        }

        RichTextBlock InlineText(ContainerInline? inline, double fontSize, global::Windows.UI.Text.FontWeight weight)
        {
            var result = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = fontSize, FontWeight = weight, LineHeight = fontSize + 8 };
            var paragraph = new Paragraph();
            if (inline is not null) RenderInlines(inline, paragraph.Inlines);
            result.Blocks.Add(paragraph);
            return result;
        }

        void RenderInlines(ContainerInline container, InlineCollection output)
        {
            for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                switch (inline)
                {
                    case LiteralInline literal: output.Add(new Run { Text = literal.Content.ToString() }); break;
                    case CodeInline code: output.Add(new Run { Text = code.Content, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 175, 55, 75)) }); break;
                    case LineBreakInline: output.Add(new LineBreak()); break;
                    case EmphasisInline emphasis:
                        var span = emphasis.DelimiterCount >= 2 ? new Bold() as Span : new Italic();
                        RenderInlines(emphasis, span.Inlines);
                        output.Add(span);
                        break;
                    case LinkInline link when !link.IsImage:
                        var linkSpan = new Span { Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 11, 103, 246)) };
                        RenderInlines(link, linkSpan.Inlines);
                        output.Add(linkSpan);
                        break;
                    case LinkInline image when image.IsImage:
                        RenderInlines(image, output);
                        break;
                    case HtmlInline:
                        break;
                    case ContainerInline nested:
                        RenderInlines(nested, output);
                        break;
                }
            }
        }

        Border CodeBlock(string value, string codeLanguage)
        {
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
            return new Border { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 35, 43)), CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 10, 12, 12), Child = new StackPanel { Children = { header, codeBlock } } };
        }
    }

    private static string DisplayTime(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.LocalDateTime.ToString("HH:mm") : string.Empty;
}
