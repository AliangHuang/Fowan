using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Fowan.Diary.Windows.Presentation;
using Fowan.Windows.Platform.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Diary.Windows.Coordination;

internal sealed class DiaryEntryInteractionCoordinator(
    Func<XamlRoot> xamlRoot,
    Func<DiaryData> data,
    DiaryWorkspace workspace,
    IFileDialogService filePicker,
    DiaryThemePalette theme,
    Func<DiaryEntry> ensureDraft,
    Func<DiaryData, bool> save,
    Action rebuild,
    Func<bool> isTimelineView,
    Action<DiaryEntry> navigateTimelineToEntry,
    Func<DiaryEntry?> selectedEntry,
    Action<DiaryEntry?> setSelectedEntry,
    Func<DiaryEntry?> draftEntry,
    Action<DiaryEntry?> setDraftEntry,
    Func<IEnumerable<DiaryEntry>> filteredEntries,
    Func<string, string, Task> showMessage)
{
    private const int MaximumBodyLength = 5000;

    public async Task ShowEntryEditorAsync(DiaryEntry entry)
    {
        var currentData = data();
        var titleBox = new TextBox { Text = entry.Title == "未命名日记" ? string.Empty : entry.Title, PlaceholderText = "标题" };
        var bodyBox = new TextBox { Text = entry.Body, PlaceholderText = "正文", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxLength = MaximumBodyLength, MinHeight = 150 };
        var notebookBox = new ComboBox { MinWidth = 260 };
        foreach (var notebook in currentData.Notebooks)
        {
            var item = new ComboBoxItem { Content = notebook.Name, Tag = notebook.Id };
            notebookBox.Items.Add(item);
            if (notebook.Id == entry.NotebookId) notebookBox.SelectedItem = item;
        }
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var tag in entry.Tags) tags.Children.Add(TagPill(tag));
        var content = new ScrollViewer
        {
            MaxHeight = 560,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Field("标题", titleBox), Field("正文", bodyBox), Field("日记本", notebookBox),
                    Field("标签", new StackPanel { Spacing = 8, Children = { tags, Text("请从日记详情或快速记录工具栏管理标签。", 12, "TextMuted") } })
                }
            }
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "编辑日记", Content = content,
            PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var candidateEntry = currentData.Entries.First(value => value.Id == entry.Id);
        if (notebookBox.SelectedItem is ComboBoxItem selectedNotebook) candidateEntry.NotebookId = selectedNotebook.Tag?.ToString() ?? candidateEntry.NotebookId;
        candidateEntry.Body = bodyBox.Text;
        candidateEntry.Title = string.IsNullOrWhiteSpace(titleBox.Text) ? DiaryText.InferTitle(candidateEntry.Body) : titleBox.Text.Trim();
        candidateEntry.UpdatedAt = DateTimeOffset.Now;
        save(currentData);
        rebuild();
    }

    public async Task AddImageAttachmentAsync()
    {
        try
        {
            var path = await filePicker.PickOpenFileAsync(new FileOpenRequest([".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"]));
            if (string.IsNullOrWhiteSpace(path)) return;
            var draft = ensureDraft();
            var attachment = workspace.ImportAttachment(draft.Id, path);
            var currentData = data();
            var candidateDraft = currentData.Entries.First(value => value.Id == draft.Id);
            candidateDraft.Attachments.Add(attachment);
            candidateDraft.UpdatedAt = DateTimeOffset.Now;
            if (!save(currentData))
            {
                workspace.DeleteAttachment(attachment);
            }
            rebuild();
        }
        catch
        {
            await showMessage("添加图片失败", "请选择受支持的图片文件，并确认日记数据目录可写。");
        }
    }

    public void ShowTemplateMenu(Button anchor)
    {
        var flyout = new MenuFlyout();
        foreach (var template in DiaryText.Templates)
        {
            var item = new MenuFlyoutItem { Text = template.Name };
            item.Click += (_, _) => ApplyTemplate(template);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(anchor);
    }

    public void ApplyTemplate(DiaryTemplate template)
    {
        var draft = ensureDraft();
        var currentData = data();
        var candidateDraft = currentData.Entries.First(value => value.Id == draft.Id);
        candidateDraft.Body = string.IsNullOrWhiteSpace(candidateDraft.Body) ? template.Body : $"{candidateDraft.Body.TrimEnd()}\n\n{template.Body}";
        candidateDraft.Title = DiaryText.InferTitle(candidateDraft.Body);
        candidateDraft.UpdatedAt = DateTimeOffset.Now;
        save(currentData);
        rebuild();
    }

    public async Task ShowSearchDialogAsync(string initialQuery = "")
    {
        var queryBox = new TextBox { Text = initialQuery, PlaceholderText = "搜索标题、正文或标签" };
        var results = new StackPanel { Spacing = 6 };
        ContentDialog? dialog = null;
        void Refresh()
        {
            results.Children.Clear();
            foreach (var entry in DiaryText.Search(data(), queryBox.Text).Take(8))
            {
                var button = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new StackPanel
                    {
                        Spacing = 2,
                        Children = { Text(entry.Title, 15, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold), Text(Snippet(entry.Body), 13, "TextSecondary") }
                    }
                };
                button.Click += (_, _) =>
                {
                    dialog?.Hide();
                    if (isTimelineView()) navigateTimelineToEntry(entry);
                    else
                    {
                        setSelectedEntry(entry);
                        rebuild();
                    }
                };
                results.Children.Add(button);
            }
        }
        queryBox.TextChanged += (_, _) => Refresh();
        dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "搜索日记",
            Content = new StackPanel { Spacing = 10, Children = { queryBox, new ScrollViewer { MaxHeight = 330, Content = results } } },
            CloseButtonText = "关闭"
        };
        Refresh();
        await dialog.ShowAsync();
    }

    public async Task ShowTodoPickerAsync(DiaryEntry entry)
    {
        var candidates = TodoCandidateReader.LoadOpenCandidates(50);
        var selected = entry.TodoLinks.Select(link => link.TaskId).ToHashSet(StringComparer.Ordinal);
        var checks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        var list = new StackPanel { Spacing = 8 };
        foreach (var candidate in candidates)
        {
            var check = new CheckBox { Content = $"{candidate.Title} · {candidate.ListName}", IsChecked = selected.Contains(candidate.Id) };
            checks[candidate.Id] = check;
            list.Children.Add(check);
        }
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "关联待办",
            Content = candidates.Count == 0 ? Text("没有可关联的未完成待办。", 14, "TextSecondary") : new ScrollViewer { MaxHeight = 360, Content = list },
            PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var currentData = data();
        var candidateEntry = currentData.Entries.First(value => value.Id == entry.Id);
        candidateEntry.TodoLinks = candidates.Where(candidate => checks[candidate.Id].IsChecked == true)
            .Select(candidate => new DiaryTodoLink
            {
                TaskId = candidate.Id, TitleSnapshot = candidate.Title,
                ListNameSnapshot = candidate.ListName, StartDate = candidate.StartDate
            }).ToList();
        candidateEntry.UpdatedAt = DateTimeOffset.Now;
        save(currentData);
        rebuild();
    }

    public async Task DeleteEntryAsync(DiaryEntry entry)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = "删除这篇日记？", Content = "删除后无法恢复。",
            PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var currentData = data();
        currentData.Entries.RemoveAll(candidate => candidate.Id == entry.Id);
        if (save(currentData)) workspace.DeleteAttachmentDirectory(entry.Id);
        if (draftEntry()?.Id == entry.Id) setDraftEntry(null);
        setSelectedEntry(filteredEntries().FirstOrDefault());
        rebuild();
    }

    public async Task ExportEntryAsync(DiaryEntry entry)
    {
        try
        {
            await filePicker.SaveTextFileAsync(new TextFileSaveRequest(SafeFileName(entry.Title), ToMarkdown(entry), "Markdown 文档", ".md"));
        }
        catch
        {
            await showMessage("导出失败", "请检查目标位置是否可写。");
        }
    }

    public void ShowHeaderMenu(Button anchor)
    {
        var flyout = new MenuFlyout();
        var search = new MenuFlyoutItem { Text = "搜索日记" };
        search.Click += async (_, _) => await ShowSearchDialogAsync();
        flyout.Items.Add(search);
        var attachments = new MenuFlyoutItem { Text = "查看当前附件" };
        attachments.Click += async (_, _) => await ShowAttachmentsDialogAsync(selectedEntry());
        flyout.Items.Add(attachments);
        flyout.ShowAt(anchor);
    }

    public void ShowEntryMenu(Button anchor, DiaryEntry entry)
    {
        var flyout = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "编辑日记" };
        edit.Click += async (_, _) => await ShowEntryEditorAsync(entry);
        flyout.Items.Add(edit);
        var attachments = new MenuFlyoutItem { Text = "管理图片" };
        attachments.Click += async (_, _) => await ShowAttachmentsDialogAsync(entry);
        flyout.Items.Add(attachments);
        var delete = new MenuFlyoutItem { Text = "删除", Foreground = theme.Brush("Danger") };
        delete.Click += async (_, _) => await DeleteEntryAsync(entry);
        flyout.Items.Add(delete);
        flyout.ShowAt(anchor);
    }

    public async Task ShowAttachmentsDialogAsync(DiaryEntry? entry)
    {
        if (entry is null)
        {
            await showMessage("图片附件", "请选择或新建一篇日记后再管理图片。");
            return;
        }
        var list = new StackPanel { Spacing = 8 };
        if (entry.Attachments.Count == 0) list.Children.Add(Text("当前日记没有图片附件。", 14, "TextSecondary"));
        foreach (var attachment in entry.Attachments.ToList())
        {
            var remove = SecondaryButton($"移除 {attachment.FileName}");
            remove.Click += (_, _) =>
            {
                var currentData = data();
                var candidateEntry = currentData.Entries.First(value => value.Id == entry.Id);
                candidateEntry.Attachments.RemoveAll(value => value.Id == attachment.Id);
                if (save(currentData)) workspace.DeleteAttachment(attachment);
            };
            list.Children.Add(remove);
        }
        await new ContentDialog { XamlRoot = xamlRoot(), Title = "图片附件", Content = list, CloseButtonText = "关闭" }.ShowAsync();
        rebuild();
    }

    private Border TagPill(string tag) => new()
    {
        CornerRadius = new CornerRadius(6), Background = theme.TagBackground(tag),
        Padding = new Thickness(9, 5, 9, 5), Child = Text(tag, 12, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold)
    };

    private Button SecondaryButton(string value) => new()
    {
        Height = 36, Padding = new Thickness(14, 0, 14, 0), BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(7), Background = theme.Brush("ControlBackground"),
        Content = Text(value, 14, "TextPrimary", Microsoft.UI.Text.FontWeights.SemiBold)
    };

    private TextBlock Text(string value, double size, string brushKey, global::Windows.UI.Text.FontWeight? weight = null) => new()
    {
        Text = value, FontSize = size, Foreground = theme.Brush(brushKey),
        FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement Field(string label, FrameworkElement input) => new StackPanel
    {
        Spacing = 5,
        Children = { new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, input }
    };

    private static string Snippet(string body)
    {
        var line = body.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(line) ? "还没有正文内容。" : line.Length <= 52 ? line : $"{line[..52]}...";
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "日记" : safe;
    }

    private static string ToMarkdown(DiaryEntry entry)
    {
        var lines = new List<string>
        {
            $"# {entry.Title}", string.Empty, $"- 创建时间：{entry.CreatedAt:yyyy-MM-dd HH:mm}",
            $"- 更新时间：{entry.UpdatedAt:yyyy-MM-dd HH:mm}", $"- 心情：{entry.Mood}",
            $"- 天气：{entry.Weather}", $"- 地点：{entry.Location}",
            $"- 标签：{(entry.Tags.Count == 0 ? "无" : string.Join("、", entry.Tags))}",
            string.Empty, "## 正文", string.Empty, entry.Body
        };
        if (entry.TodoLinks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## 关联待办");
            lines.Add(string.Empty);
            lines.AddRange(entry.TodoLinks.Select(link => $"- {link.TitleSnapshot}（{link.ListNameSnapshot}）"));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
