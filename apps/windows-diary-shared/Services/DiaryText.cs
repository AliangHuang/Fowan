using Fowan.Diary.Shared.Models;

namespace Fowan.Diary.Shared.Services;

public sealed record DiaryTemplate(string Id, string Name, string Body);

public static class DiaryText
{
    public static IReadOnlyList<DiaryTemplate> Templates { get; } =
    [
        new DiaryTemplate("daily-review", "每日回顾", "今天发生了什么？\n\n我学到了什么？\n\n明天最重要的一件事："),
        new DiaryTemplate("idea", "灵感速记", "灵感：\n\n为什么值得记录：\n\n下一步："),
        new DiaryTemplate("gratitude", "感恩记录", "今天让我感恩的三件事：\n\n1. \n2. \n3. ")
    ];

    public static string InferTitle(string body)
    {
        var line = body.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        return string.IsNullOrWhiteSpace(line)
            ? "未命名日记"
            : line.Length <= 32 ? line : $"{line[..32]}…";
    }

    public static IReadOnlyList<DiaryEntry> Search(DiaryData data, string query)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var needle = query.Trim();
        return data.Entries
            .Where(entry => entry.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Body.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Tags.Any(tag => tag.Contains(needle, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(entry => entry.UpdatedAt)
            .ToList();
    }
}
