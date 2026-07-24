using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Fowan.Diary.Windows.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Coordination;

internal sealed class DiaryTagDialogCoordinator(
    Func<XamlRoot> xamlRoot,
    Func<DiaryData> data,
    DiaryThemePalette theme,
    Func<DiaryData, bool> save,
    Action rebuild,
    Func<string?> tagFilter,
    Action<string?> setTagFilter,
    Func<string, string, Task> showMessage)
{
    public async Task ShowTagPickerAsync(DiaryEntry entry)
    {
        var currentData = data();
        var selected = entry.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel { Spacing = 8 };
        foreach (var tag in currentData.TagCatalog.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var check = new CheckBox { Content = tag.Name, IsChecked = selected.Contains(tag.Name) };
            checks[tag.Name] = check;
            list.Children.Add(check);
        }
        var name = new TextBox { PlaceholderText = "新标签名称" };
        var color = BuildTagColorSelector();
        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                list.Children.Count == 0 ? Text("还没有标签。新建一个标签后即可使用。", 13, "TextSecondary") : new ScrollViewer { MaxHeight = 260, Content = list },
                new Border { BorderBrush = theme.Brush("InnerDivider"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0) },
                Field("新建标签", name),
                Field("配色", color)
            }
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "选择标签", Content = content,
            PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!string.IsNullOrWhiteSpace(name.Text))
        {
            try
            {
                var created = DiaryTags.Ensure(currentData, name.Text, (color.SelectedItem as ComboBoxItem)?.Tag?.ToString());
                selected.Add(created.Name);
            }
            catch (ArgumentException)
            {
                await showMessage("标签名称无效", "请输入非空的标签名称。");
                return;
            }
        }
        foreach (var pair in checks)
        {
            if (pair.Value.IsChecked == true) selected.Add(pair.Key); else selected.Remove(pair.Key);
        }
        var candidateEntry = currentData.Entries.First(value => value.Id == entry.Id);
        DiaryTags.Apply(currentData, candidateEntry, selected);
        candidateEntry.UpdatedAt = DateTimeOffset.Now;
        save(currentData);
        rebuild();
    }

    public async Task ShowCreateTagDialogAsync()
    {
        var name = new TextBox { PlaceholderText = "例如：阅读" };
        var color = BuildTagColorSelector();
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "新建标签",
            Content = new StackPanel { Spacing = 10, Children = { Field("名称", name), Field("配色（12 色）", color) } },
            PrimaryButtonText = "创建", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var currentData = data();
            DiaryTags.Ensure(currentData, name.Text, (color.SelectedItem as ComboBoxItem)?.Tag?.ToString());
            save(currentData);
        }
        catch (ArgumentException)
        {
            await showMessage("标签名称无效", "请输入非空的标签名称。");
            return;
        }
        rebuild();
    }

    public async Task ShowEditTagDialogAsync(DiaryTagDefinition tag)
    {
        var name = new TextBox { Text = tag.Name };
        var color = BuildTagColorSelector(tag.ColorId);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "编辑标签",
            Content = new StackPanel { Spacing = 10, Children = { Field("名称", name), Field("配色（12 色）", color) } },
            PrimaryButtonText = "保存", SecondaryButtonText = "删除标签", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                var currentData = data();
                if (!DiaryTags.Rename(currentData, tag.Id, name.Text))
                {
                    await showMessage("标签名称已存在", "请使用不同的标签名称。");
                    return;
                }
                var candidateTag = currentData.TagCatalog.First(value => value.Id == tag.Id);
                candidateTag.ColorId = DiaryMetadata.TagColor((color.SelectedItem as ComboBoxItem)?.Tag?.ToString()).Id;
                save(currentData);
                rebuild();
            }
            catch (ArgumentException)
            {
                await showMessage("标签名称无效", "请输入非空的标签名称。");
            }
            return;
        }
        if (result != ContentDialogResult.Secondary) return;
        var confirm = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = $"删除标签“{tag.Name}”？",
            Content = "标签定义将被移除；已有日记中的历史标签文字会保留。",
            PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        var deleteData = data();
        DiaryTags.RemoveDefinition(deleteData, tag.Id);
        if (string.Equals(tagFilter(), tag.Name, StringComparison.OrdinalIgnoreCase)) setTagFilter(null);
        save(deleteData);
        rebuild();
    }

    private ComboBox BuildTagColorSelector(string? selectedColorId = null)
    {
        var selector = new ComboBox { MinWidth = 240 };
        foreach (var color in DiaryMetadata.TagColors)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = theme.HexBrush(color.Hex), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(Text(color.Name, 14, "TextPrimary"));
            var item = new ComboBoxItem { Content = content, Tag = color.Id };
            selector.Items.Add(item);
            if (string.Equals(color.Id, selectedColorId ?? DiaryMetadata.DefaultTagColorId, StringComparison.OrdinalIgnoreCase)) selector.SelectedItem = item;
        }
        selector.SelectedIndex = selector.SelectedIndex < 0 ? 0 : selector.SelectedIndex;
        return selector;
    }

    private TextBlock Text(string value, double size, string brushKey) => new()
    {
        Text = value, FontSize = size, Foreground = theme.Brush(brushKey),
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement Field(string label, FrameworkElement input) => new StackPanel
    {
        Spacing = 5,
        Children = { new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, input }
    };
}
