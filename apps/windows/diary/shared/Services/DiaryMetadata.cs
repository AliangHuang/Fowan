using Fowan.Diary.Shared.Models;

namespace Fowan.Diary.Shared.Services;

public sealed record DiaryTagColor(string Id, string Name, string Hex);

public static class DiaryMetadata
{
    public const string DefaultTagColorId = "blue";

    public static IReadOnlyList<string> MoodOptions { get; } =
    [
        "愉快", "平静", "专注", "满足", "放松", "疲惫", "低落", "兴奋"
    ];

    public static IReadOnlyList<string> WeatherOptions { get; } =
    [
        "晴", "多云", "阴", "小雨", "中雨", "大雨", "冻雨", "雷雨", "雪", "雾", "待补充"
    ];

    public static IReadOnlyList<DiaryTagColor> TagColors { get; } =
    [
        new("blue", "蓝", "#2F80FF"),
        new("sky", "天蓝", "#38BDF8"),
        new("cyan", "青", "#14B8A6"),
        new("green", "绿", "#35B779"),
        new("lime", "青柠", "#84CC16"),
        new("yellow", "黄", "#EAB308"),
        new("orange", "橙", "#F59E0B"),
        new("red", "红", "#E5484D"),
        new("rose", "玫红", "#F43F5E"),
        new("pink", "粉", "#EC4899"),
        new("purple", "紫", "#9D6DF2"),
        new("slate", "石板灰", "#64748B")
    ];

    public static DiaryTagColor TagColor(string? colorId) => TagColors.FirstOrDefault(color =>
        string.Equals(color.Id, colorId, StringComparison.OrdinalIgnoreCase)) ?? TagColors[0];
}

public static class DiaryTags
{
    public static IReadOnlyList<string> Names(DiaryData data) => data.TagCatalog
        .Select(tag => tag.Name)
        .Concat(data.Entries.SelectMany(entry => entry.Tags))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public static DiaryTagDefinition? Find(DiaryData data, string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : data.TagCatalog.FirstOrDefault(tag => string.Equals(tag.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static DiaryTagDefinition Ensure(DiaryData data, string name, string? colorId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var normalizedName = NormalizeName(name);
        var existing = Find(data, normalizedName);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(colorId))
            {
                existing.ColorId = DiaryMetadata.TagColor(colorId).Id;
            }
            return existing;
        }

        var created = new DiaryTagDefinition
        {
            Id = DiaryStore.NewId("tag"),
            Name = normalizedName,
            ColorId = DiaryMetadata.TagColor(colorId).Id
        };
        data.TagCatalog.Add(created);
        return created;
    }

    public static void Apply(DiaryData data, DiaryEntry entry, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entry);
        entry.Tags = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var name in entry.Tags)
        {
            Ensure(data, name);
        }
    }

    public static bool Rename(DiaryData data, string tagId, string newName)
    {
        ArgumentNullException.ThrowIfNull(data);
        var tag = data.TagCatalog.FirstOrDefault(candidate => string.Equals(candidate.Id, tagId, StringComparison.Ordinal));
        if (tag is null)
        {
            return false;
        }
        var normalizedName = NormalizeName(newName);
        if (data.TagCatalog.Any(candidate => candidate.Id != tagId && string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        var previousName = tag.Name;
        tag.Name = normalizedName;
        foreach (var entry in data.Entries)
        {
            entry.Tags = entry.Tags.Select(name => string.Equals(name, previousName, StringComparison.OrdinalIgnoreCase) ? normalizedName : name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        return true;
    }

    public static bool RemoveDefinition(DiaryData data, string tagId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.TagCatalog.RemoveAll(tag => string.Equals(tag.Id, tagId, StringComparison.Ordinal)) > 0;
    }

    public static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
