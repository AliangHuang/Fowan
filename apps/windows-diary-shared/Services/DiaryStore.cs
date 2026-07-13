using Fowan.Diary.Shared.Models;
using System.Text.Json;

namespace Fowan.Diary.Shared.Services;

public sealed class DiaryStore
{
    public const string DefaultNotebookId = "inbox";
    public const string DefaultNotebookName = "收集箱";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string RootPath { get; }
    public string DataPath { get; }
    public string AttachmentsPath => Path.Combine(RootPath, "attachments");

    public DiaryStore()
        : this(ResolveRootPath())
    {
    }

    public DiaryStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
        DataPath = Path.Combine(RootPath, "diary-data.json");
    }

    public static string ResolveRootPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("FOWAN_DIARY_DATA_ROOT");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "Diary")
            : Path.GetFullPath(overridePath);
    }

    public DiaryData LoadData()
    {
        Directory.CreateDirectory(RootPath);
        if (!File.Exists(DataPath))
        {
            var created = new DiaryData();
            Normalize(created);
            SaveData(created);
            return created;
        }

        try
        {
            var data = JsonSerializer.Deserialize<DiaryData>(File.ReadAllText(DataPath), JsonOptions)
                ?? new DiaryData();
            Normalize(data);
            return data;
        }
        catch (JsonException)
        {
            BackupMalformedData();
            var recovered = new DiaryData();
            Normalize(recovered);
            SaveData(recovered);
            return recovered;
        }
    }

    public void SaveData(DiaryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(RootPath);
        Normalize(data);

        var temporaryPath = $"{DataPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temporaryPath, DataPath, overwrite: true);
    }

    public static string NewId(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 17, prefix.Length + 33)];
    }

    public DiaryAttachment ImportAttachment(string entryId, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Attachment source file was not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!IsSupportedImageExtension(extension))
        {
            throw new InvalidDataException("Only image attachments are supported.");
        }

        var attachmentId = NewId("attachment");
        var fileName = $"{attachmentId}{extension.ToLowerInvariant()}";
        var directory = GetAttachmentDirectory(entryId);
        Directory.CreateDirectory(directory);
        File.Copy(sourcePath, Path.Combine(directory, fileName), overwrite: false);
        return new DiaryAttachment
        {
            Id = attachmentId,
            FileName = Path.GetFileName(sourcePath),
            RelativePath = Path.Combine("attachments", entryId, fileName).Replace('\\', '/'),
            ContentType = ContentTypeForExtension(extension)
        };
    }

    public void DeleteAttachment(DiaryAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (string.IsNullOrWhiteSpace(attachment.RelativePath))
        {
            return;
        }

        var path = ResolveAttachmentPath(attachment.RelativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteAttachmentDirectory(string entryId)
    {
        var directory = GetAttachmentDirectory(entryId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public string ResolveAttachmentPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var root = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Attachment path escapes the diary data directory.");
        }
        return path;
    }

    private string GetAttachmentDirectory(string entryId)
    {
        var safeId = entryId.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray();
        if (safeId.Length == 0)
        {
            throw new InvalidDataException("The diary entry id is not valid for attachment storage.");
        }
        return Path.Combine(AttachmentsPath, new string(safeId));
    }

    private void BackupMalformedData()
    {
        var backupPath = $"{DataPath}.invalid-{DateTimeOffset.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json";
        File.Copy(DataPath, backupPath, overwrite: false);
    }

    private static void Normalize(DiaryData data)
    {
        data.SchemaVersion = DiaryData.CurrentSchemaVersion;
        data.Notebooks ??= [];
        data.TagCatalog ??= [];
        data.Entries ??= [];

        NormalizeNotebooks(data);
        var knownNotebookIds = data.Notebooks.Select(notebook => notebook.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in data.Entries.ToList())
        {
            NormalizeEntry(data, entry, knownNotebookIds);
        }
        NormalizeTagCatalog(data);
    }

    private static void NormalizeNotebooks(DiaryData data)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var notebook in data.Notebooks.ToList())
        {
            notebook.Id = string.IsNullOrWhiteSpace(notebook.Id) || !seen.Add(notebook.Id.Trim())
                ? NewId("notebook")
                : notebook.Id.Trim();
            seen.Add(notebook.Id);
            notebook.Name = string.IsNullOrWhiteSpace(notebook.Name) ? "未命名日记本" : notebook.Name.Trim();
            notebook.AccentColor = IsHexColor(notebook.AccentColor) ? notebook.AccentColor : "#2F80FF";
        }

        if (data.Notebooks.Count == 0)
        {
            data.Notebooks.Add(new DiaryNotebook
            {
                Id = DefaultNotebookId,
                Name = DefaultNotebookName,
                AccentColor = "#2F80FF"
            });
        }
    }

    private static void NormalizeEntry(DiaryData data, DiaryEntry entry, ISet<string> knownNotebookIds)
    {
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? NewId("entry") : entry.Id.Trim();
        entry.Title = string.IsNullOrWhiteSpace(entry.Title) ? "未命名日记" : entry.Title.Trim();
        entry.Body ??= string.Empty;
        entry.NotebookId = string.IsNullOrWhiteSpace(entry.NotebookId) || !knownNotebookIds.Contains(entry.NotebookId)
            ? data.Notebooks[0].Id
            : entry.NotebookId.Trim();
        entry.Mood = string.IsNullOrWhiteSpace(entry.Mood) ? "平静" : entry.Mood.Trim();
        entry.Weather = string.IsNullOrWhiteSpace(entry.Weather) ? "待补充" : entry.Weather.Trim();
        entry.Location = string.IsNullOrWhiteSpace(entry.Location) ? "待补充" : entry.Location.Trim();
        entry.Tags = entry.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        if (entry.LocationDetails is not null &&
            (double.IsNaN(entry.LocationDetails.Latitude) || double.IsInfinity(entry.LocationDetails.Latitude) ||
             double.IsNaN(entry.LocationDetails.Longitude) || double.IsInfinity(entry.LocationDetails.Longitude)))
        {
            entry.LocationDetails = null;
        }
        if (entry.WeatherDetails is not null &&
            (double.IsNaN(entry.WeatherDetails.TemperatureCelsius) || double.IsInfinity(entry.WeatherDetails.TemperatureCelsius) ||
             double.IsNaN(entry.WeatherDetails.Latitude) || double.IsInfinity(entry.WeatherDetails.Latitude) ||
             double.IsNaN(entry.WeatherDetails.Longitude) || double.IsInfinity(entry.WeatherDetails.Longitude)))
        {
            entry.WeatherDetails = null;
        }
        entry.Attachments = entry.Attachments?
            .Where(attachment => !string.IsNullOrWhiteSpace(attachment.Id) && !string.IsNullOrWhiteSpace(attachment.RelativePath))
            .GroupBy(attachment => attachment.Id.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var attachment = group.First();
                attachment.Id = group.Key;
                attachment.FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? "图片" : attachment.FileName.Trim();
                attachment.RelativePath = attachment.RelativePath.Replace('\\', '/').TrimStart('/');
                attachment.ContentType = string.IsNullOrWhiteSpace(attachment.ContentType) ? "image/*" : attachment.ContentType.Trim();
                return attachment;
            })
            .ToList() ?? [];
        entry.TodoLinks = entry.TodoLinks?
            .Where(link => !string.IsNullOrWhiteSpace(link.TaskId))
            .GroupBy(link => link.TaskId.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var link = group.First();
                link.TaskId = group.Key;
                link.TitleSnapshot = string.IsNullOrWhiteSpace(link.TitleSnapshot) ? "待办" : link.TitleSnapshot.Trim();
                link.ListNameSnapshot = string.IsNullOrWhiteSpace(link.ListNameSnapshot) ? "待办" : link.ListNameSnapshot.Trim();
                link.StartDate = link.StartDate == default ? DateTime.Today : link.StartDate.Date;
                return link;
            })
            .ToList() ?? [];
        entry.CreatedAt = entry.CreatedAt == default ? DateTimeOffset.Now : entry.CreatedAt;
        entry.UpdatedAt = entry.UpdatedAt == default ? entry.CreatedAt : entry.UpdatedAt;
    }

    private static void NormalizeTagCatalog(DiaryData data)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<DiaryTagDefinition>();
        foreach (var tag in data.TagCatalog)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }
            var name = tag.Name.Trim();
            if (!seenNames.Add(name))
            {
                continue;
            }
            tag.Id = string.IsNullOrWhiteSpace(tag.Id) || !seenIds.Add(tag.Id.Trim()) ? NewId("tag") : tag.Id.Trim();
            seenIds.Add(tag.Id);
            tag.Name = name;
            tag.ColorId = DiaryMetadata.TagColor(tag.ColorId).Id;
            normalized.Add(tag);
        }
        data.TagCatalog = normalized;
        foreach (var name in data.Entries.SelectMany(entry => entry.Tags))
        {
            if (string.IsNullOrWhiteSpace(name) || data.TagCatalog.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            data.TagCatalog.Add(new DiaryTagDefinition
            {
                Id = NewId("tag"),
                Name = name.Trim(),
                ColorId = DiaryMetadata.DefaultTagColorId
            });
        }
    }

    private static bool IsHexColor(string? value)
    {
        return value is { Length: 7 } && value[0] == '#' &&
            uint.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out _);
    }

    private static bool IsSupportedImageExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string ContentTypeForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/*"
        };
    }
}
