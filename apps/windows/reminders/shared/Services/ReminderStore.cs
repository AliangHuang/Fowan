using Fowan.Reminders.Models;
using System.Text;
using System.Text.Json;

namespace Fowan.Reminders.Services;

public interface IReminderRepository
{
    ReminderData Load();
    void Save(ReminderData data);
    bool Update(Func<ReminderData, bool> update);
}

public sealed class ReminderStore : IReminderRepository
{
    private const string StorageMutexName = @"Local\Fowan.Reminders.Storage";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ReminderStore(string? rootPath = null)
    {
        RootPath = Path.GetFullPath(rootPath ?? ResolveRoot());
        DataPath = Path.Combine(RootPath, "reminders.json");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }
    public string DataPath { get; }

    public ReminderData Load()
    {
        try
        {
            var data = Read();
            Normalize(data);
            return data;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            BackupUnreadable();
            throw new ReminderStorageUnavailableException("提醒数据无法读取，已保留故障文件备份。", error);
        }
    }

    public void Save(ReminderData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        WithLock(() => Write(data));
    }

    public bool Update(Func<ReminderData, bool> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var changed = false;
        WithLock(() =>
        {
            var data = Read();
            Normalize(data);
            if (!update(data)) return;
            Write(data);
            changed = true;
        });
        return changed;
    }

    public static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 17, prefix.Length + 1 + 32)];

    private ReminderData Read()
    {
        if (!File.Exists(DataPath)) return new ReminderData();
        return JsonSerializer.Deserialize<ReminderData>(File.ReadAllText(DataPath), JsonOptions) ?? new ReminderData();
    }

    private void Write(ReminderData data)
    {
        Normalize(data);
        Directory.CreateDirectory(RootPath);
        var temporaryPath = $"{DataPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, DataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void BackupUnreadable()
    {
        if (!File.Exists(DataPath)) return;
        try
        {
            File.Copy(DataPath, $"{DataPath}.unreadable-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bak", overwrite: false);
        }
        catch { }
    }

    private static void Normalize(ReminderData data)
    {
        data.SchemaVersion = 1;
        data.Reminders ??= [];
        data.Settings ??= new ReminderSettings();
        data.Settings.StartDigestTime = NormalizeTime(data.Settings.StartDigestTime);
        data.Settings.DueDigestTime = NormalizeTime(data.Settings.DueDigestTime);
        foreach (var reminder in data.Reminders.ToList())
        {
            reminder.Id = string.IsNullOrWhiteSpace(reminder.Id) ? NewId("reminder") : reminder.Id.Trim();
            reminder.Title = reminder.Title?.Trim() ?? string.Empty;
            if (reminder.Title.Length == 0) { data.Reminders.Remove(reminder); continue; }
            reminder.Notes = reminder.Notes?.Trim() ?? string.Empty;
            reminder.TodoTaskId = string.IsNullOrWhiteSpace(reminder.TodoTaskId) ? null : reminder.TodoTaskId.Trim();
            reminder.FirstOccursAt = reminder.FirstOccursAt == default ? DateTimeOffset.Now : reminder.FirstOccursAt;
            reminder.Recurrence = ReminderRecurrenceRules.Normalize(reminder.Recurrence);
            reminder.PendingMissedCount = Math.Max(0, reminder.PendingMissedCount);
            if (reminder.CreatedAt == default) reminder.CreatedAt = DateTimeOffset.Now;
            if (reminder.UpdatedAt == default) reminder.UpdatedAt = reminder.CreatedAt;
        }
    }

    private static TimeSpan NormalizeTime(TimeSpan value) => value < TimeSpan.Zero || value >= TimeSpan.FromDays(1)
        ? new TimeSpan(9, 30, 0) : value;

    private static void WithLock(Action action)
    {
        using var mutex = new Mutex(false, StorageMutexName);
        var taken = false;
        try
        {
            try { taken = mutex.WaitOne(LockTimeout); }
            catch (AbandonedMutexException) { taken = true; }
            if (!taken) throw new TimeoutException("等待提醒数据锁超时。");
            action();
        }
        finally
        {
            if (taken) mutex.ReleaseMutex();
        }
    }

    private static string ResolveRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("FOWAN_REMINDER_DATA_ROOT");
        return !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "Reminders");
    }
}

public sealed class ReminderStorageUnavailableException(string message, Exception innerException) : InvalidOperationException(message, innerException);
