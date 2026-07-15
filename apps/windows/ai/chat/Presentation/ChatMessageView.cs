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
        var metadata = message.ModelId is null
            ? null
            : $"{message.ChannelName} · {message.CredentialName} · {message.ModelId} · {message.Status}";
        return Bubble(message.Role, message.Content, metadata);
    }

    public Border Bubble(string role, string content, string? metadata)
    {
        var isUser = role == "user";
        var stack = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            stack.Children.Add(new TextBlock
            {
                Text = metadata,
                FontSize = 13,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 105, 115, 134))
            });
        }
        var streamingText = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 15,
            LineHeight = 24
        };
        stack.Children.Add(string.IsNullOrEmpty(content) ? streamingText : Markdown(content));
        var copy = new Button
        {
            Content = text("AI_Copy"),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        copy.Click += (_, _) => copyService.SetText(streamingText.Text);
        stack.Children.Add(copy);
        return new Border
        {
            Tag = streamingText,
            Child = stack,
            Background = isUser
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 244, 249, 255))
                : new SolidColorBrush(Colors.White),
            BorderBrush = isUser
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 186, 214, 255))
                : new SolidColorBrush(ColorHelper.FromArgb(255, 217, 225, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    public UIElement VariantGroup(IReadOnlyList<AiChatMessage> variants)
    {
        if (variants.Count == 1)
        {
            return Bubble(variants[0]);
        }
        var host = new Grid();
        foreach (var variant in variants)
        {
            host.Children.Add(Bubble(variant));
        }
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
            {
                host.Children[index].Visibility = index == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        previous.Click += (_, _) => { selectedIndex--; Refresh(); };
        next.Click += (_, _) => { selectedIndex++; Refresh(); };
        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { previous, indicator, next }
        };
        Refresh();
        return new StackPanel { Spacing = 5, Children = { host, navigation } };
    }

    private StackPanel Markdown(string content)
    {
        var root = new StackPanel { Spacing = 7 };
        var paragraph = new List<string>();
        var code = new List<string>();
        var inCode = false;
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            root.Children.Add(new TextBlock
            {
                Text = string.Join(Environment.NewLine, paragraph),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                LineHeight = 22
            });
            paragraph.Clear();
        }
        void FlushCode()
        {
            var value = string.Join(Environment.NewLine, code);
            var codeBox = new TextBox
            {
                Text = value,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 226, 232, 240)),
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 35, 43)),
                BorderThickness = new Thickness(0),
                MinHeight = 42,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var copyCode = new Button
            {
                Content = text("AI_CopyCode"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(8, 3, 8, 3)
            };
            copyCode.Click += (_, _) => copyService.SetText(value);
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 35, 43)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10),
                Child = new StackPanel { Spacing = 6, Children = { codeBox, copyCode } }
            });
            code.Clear();
        }
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) FlushCode(); else FlushParagraph();
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
            root.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });
        }
    }
}
